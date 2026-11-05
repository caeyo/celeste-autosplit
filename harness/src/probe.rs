use std::{
    collections::BTreeMap,
    io::{BufRead, BufReader, Read, Write},
    process::{Child, ChildStdin, Command, Stdio},
    sync::mpsc::{self, Receiver, RecvTimeoutError, Sender},
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Error, Result, anyhow, bail};
use serde_json::Value;

use crate::by_path;

pub struct Probe {
    child: Child,
    input: Option<ChildStdin>,
    lines: Receiver<String>,
    log: Vec<String>,
    timeout: Duration,
}

impl Probe {
    pub fn start(command: &[String], timeout: Duration) -> Result<Self> {
        let mut child = Command::new(&command[0])
            .args(&command[1..])
            .arg("--replay")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .with_context(|| format!("starting {}", command.join(" ")))?;
        let (sender, lines) = mpsc::channel();
        forward(child.stdout.take().context("no output")?, sender.clone());
        forward(child.stderr.take().context("no error output")?, sender);
        Ok(Self {
            input: child.stdin.take(),
            child,
            lines,
            log: Vec::new(),
            timeout,
        })
    }

    pub fn values(&mut self) -> Result<(String, BTreeMap<String, Value>)> {
        self.expect("ok")?;
        let answer = self.answer()?;
        let Some(values) = answer
            .strip_prefix("values ")
            .and_then(|json| serde_json::from_str::<Value>(json).ok())
        else {
            return Err(self.failure(&format!("expected the probe's values, not {answer}")));
        };
        let flavour = values["flavour"]
            .as_str()
            .context("the probe's values have no flavour")?;
        let held = by_path(&values["roots"]).context("the probe's values")?;
        Ok((flavour.to_owned(), held))
    }

    pub fn write(&mut self, values: &BTreeMap<String, Value>) -> Result<()> {
        let mut set = String::new();
        for (path, value) in values {
            let text = match value {
                Value::Bool(flag) => flag.to_string(),
                Value::Number(number) => number.to_string(),
                Value::String(text) if !text.contains(['\r', '\n']) => text.clone(),
                _ => bail!("can't send the probe {value} for {path}"),
            };
            set += &format!("{path}={text}\n");
        }
        set.push('\n');
        let input = self.input.as_mut().context("the probe's input is closed")?;
        if let Err(e) = input.write_all(set.as_bytes()).and_then(|()| input.flush()) {
            return Err(self.failure(&format!("writing to the probe failed: {e}")));
        }
        self.expect("written")
    }

    pub fn finish(&mut self, wait: Duration) -> Result<(), String> {
        self.input = None;
        let deadline = Instant::now() + wait;
        let status = loop {
            match self.child.try_wait() {
                Ok(Some(status)) => break status,
                Ok(None) => {}
                Err(e) => return Err(format!("waiting for the probe failed: {e}")),
            }
            if Instant::now() >= deadline {
                let _ = self.child.kill();
                let _ = self.child.wait();
                return Err(format!("the probe didn't exit within {} s", wait.as_secs()));
            }
            thread::sleep(Duration::from_millis(10));
        };
        if !status.success() {
            return Err(format!("the probe exited with {status}"));
        }
        Ok(())
    }

    fn expect(&mut self, expected: &str) -> Result<()> {
        let answer = self.answer()?;
        if answer != expected {
            return Err(self.failure(&format!(
                "expected the probe to answer {expected}, not {answer}"
            )));
        }
        Ok(())
    }

    fn answer(&mut self) -> Result<String> {
        let deadline = Instant::now() + self.timeout;
        loop {
            let problem = match self
                .lines
                .recv_timeout(deadline.saturating_duration_since(Instant::now()))
            {
                Ok(line) => match line.strip_prefix("probe: ") {
                    Some("failed") => "the probe failed".to_owned(),
                    Some(answer) => return Ok(answer.to_owned()),
                    None => {
                        self.log.push(line);
                        continue;
                    }
                },
                Err(RecvTimeoutError::Timeout) => format!(
                    "the probe didn't answer within {} s",
                    self.timeout.as_secs()
                ),
                Err(RecvTimeoutError::Disconnected) => "the probe exited".to_owned(),
            };
            return Err(self.failure(&problem));
        }
    }

    pub fn failure(&mut self, problem: &str) -> Error {
        let deadline = Instant::now() + Duration::from_secs(1);
        while let Ok(line) = self
            .lines
            .recv_timeout(deadline.saturating_duration_since(Instant::now()))
        {
            if !line.starts_with("probe: ") {
                self.log.push(line);
            }
        }
        let log = self.log.iter().map(|line| format!("\n  {line}"));
        anyhow!("{problem}. Its log:{}", log.collect::<String>())
    }
}

fn forward(stream: impl Read + Send + 'static, lines: Sender<String>) {
    thread::spawn(move || {
        let mut stream = BufReader::new(stream);
        let mut line = Vec::new();
        while stream
            .read_until(b'\n', &mut line)
            .is_ok_and(|read| read > 0)
        {
            let text = String::from_utf8_lossy(&line);
            if lines
                .send(text.trim_end_matches(['\r', '\n']).to_owned())
                .is_err()
            {
                return;
            }
            line.clear();
        }
    });
}
