//! The probe, started as the game in its replay mode, and the lines it exchanges with the harness.
//! Once every check has passed, the probe prints `probe: ok`. It writes distinctive values into
//! the game, and prints `probe: values` and, as JSON, its flavour, the values its paths hold,
//! keyed by root and then path, and the roots it set up, since the game left them null. Then it
//! reads sets of lines, with an empty line at the end of each: `root=null` or `root=object`, which
//! make a root hold null or its object again, and `path=value`, with a string as the rest of the
//! line. It writes each set into the game and answers `probe: written`, until its input ends. It
//! prints `probe: failed` if a check or the replay fails. Its other lines, and what it prints on
//! its standard error, are its log, which is shown if anything goes wrong with it.

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

use crate::{Held, roots};

pub struct Probe {
    child: Child,
    /// Closed to end the replay.
    input: Option<ChildStdin>,
    /// Each line it prints, as it prints it.
    lines: Receiver<String>,
    /// The lines it has printed so far, other than its answers.
    log: Vec<String>,
    /// How long it has for each answer.
    timeout: Duration,
}

impl Probe {
    /// Starts the probe with the command, which the harness adds `--replay` to. On Linux and macOS,
    /// the command runs the probe through `xvfb-run` or `sudo`, so killing it leaves a probe that
    /// hangs running, until CI ends the job. Its standard error goes into its log too, so that what
    /// it leaves running doesn't hold the harness's own output open.
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

    /// Waits for the probe to pass its checks, then returns its flavour, what the game holds for
    /// each root, and the roots it set up.
    pub fn values(&mut self) -> Result<(String, BTreeMap<String, Held>, Vec<String>)> {
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
        let held = roots(&values["roots"])
            .context("the probe's values")?
            .into_iter()
            .map(|(root, paths)| {
                let held = Held {
                    null: paths.is_none(),
                    paths: paths.unwrap_or_default(),
                };
                (root, held)
            })
            .collect();
        let set_up = serde_json::from_value(values["setUp"].clone())
            .context("the probe's values have no roots it set up")?;
        Ok((flavour.to_owned(), held, set_up))
    }

    /// Has the probe make each root given hold null, if it's true, or its object again, then write
    /// each value into the game, by path, and waits until it has.
    pub fn write(
        &mut self,
        roots: &BTreeMap<String, bool>,
        values: &BTreeMap<String, Value>,
    ) -> Result<()> {
        let mut set = String::new();
        for (root, null) in roots {
            set += &format!("{root}={}\n", if *null { "null" } else { "object" });
        }
        for (path, value) in values {
            let text = match value {
                Value::Bool(flag) => flag.to_string(),
                Value::Number(number) => number.to_string(),
                // A string ends with its line.
                Value::String(text) if !text.contains(['\r', '\n']) => text.clone(),
                _ => bail!("can't send the probe {value} for {path}"),
            };
            set += &format!("{path}={text}\n");
        }
        set.push('\n');
        let input = self.input.as_mut().context("the probe's input is closed")?;
        if let Err(e) = input.write_all(set.as_bytes()).and_then(|()| input.flush()) {
            // Most likely it has exited, and what it printed last says why.
            return Err(self.failure(&format!("writing to the probe failed: {e}")));
        }
        self.expect("written")
    }

    /// Ends the probe's input, which ends its replay, and waits for it to exit, for at most as long
    /// as given, before killing it. Returns what went wrong, without the probe's log.
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

    /// Waits for the answer given.
    fn expect(&mut self, expected: &str) -> Result<()> {
        let answer = self.answer()?;
        if answer != expected {
            return Err(self.failure(&format!(
                "expected the probe to answer {expected}, not {answer}"
            )));
        }
        Ok(())
    }

    /// The probe's next answer: the rest of its next line that starts with `probe: `. Its failure,
    /// its exit or its silence fails.
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

    /// The problem, followed by the probe's log.
    pub fn failure(&mut self, problem: &str) -> Error {
        // Including what it prints last, such as why it fails, if it does, for up to a second.
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

/// Sends each line of the stream as it's printed, until it ends.
fn forward(stream: impl Read + Send + 'static, lines: Sender<String>) {
    thread::spawn(move || {
        let mut stream = BufReader::new(stream);
        let mut line = Vec::new();
        // Its log isn't always UTF-8: on Windows, paths are in the console's code page.
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
