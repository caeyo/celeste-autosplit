//! Running `fake-celeste` as the game.

use std::{
    env,
    ffi::OsStr,
    fs,
    io::{BufRead, BufReader, Write},
    path::{Path, PathBuf},
    process::{Child, ChildStdin, Command, Stdio},
    sync::mpsc::{self, Receiver, RecvTimeoutError},
    thread,
    time::Instant,
};

use anyhow::{Context, Result, bail};

/// What the game's process is called on this OS, which the auto splitter looks for.
const GAME_NAME: &str = if cfg!(windows) {
    "Celeste.exe"
} else if cfg!(target_os = "linux") {
    "Celeste.bin.x86_64"
} else {
    "Celeste"
};

/// Copies `fake-celeste`, built next to the harness, to `fake/` there, under the game's name.
pub fn executable() -> Result<PathBuf> {
    let harness = env::current_exe()?;
    let dir = harness.parent().context("the harness has no folder")?;
    let built = dir.join(format!("fake-celeste{}", env::consts::EXE_SUFFIX));
    if !built.exists() {
        bail!(
            "{} is missing: build every binary, with cargo build --release",
            built.display()
        );
    }
    let copy = dir.join("fake").join(GAME_NAME);
    fs::create_dir_all(copy.parent().unwrap())?;
    fs::copy(&built, &copy)
        .with_context(|| format!("copying {} to {}", built.display(), copy.display()))?;
    Ok(copy)
}

/// A running `fake-celeste`, killed when dropped.
pub struct Fake {
    child: Child,
    stdin: ChildStdin,
    lines: Receiver<String>,
}

impl Fake {
    /// Starts it as a child of the harness, since on Linux only a parent can read a process's
    /// memory, and waits until it's ready.
    pub fn start(executable: &Path, args: &[&OsStr], deadline: Instant) -> Result<Self> {
        let mut child = Command::new(executable)
            .args(args)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .spawn()
            .with_context(|| format!("starting {}", executable.display()))?;
        let stdin = child.stdin.take().unwrap();
        let stdout = child.stdout.take().unwrap();
        // Read on a thread of its own, so that waiting for a line can time out.
        let (sender, lines) = mpsc::channel();
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines() {
                let Ok(line) = line else { break };
                if sender.send(line).is_err() {
                    break;
                }
            }
        });
        let mut fake = Self {
            child,
            stdin,
            lines,
        };
        fake.expect("ready", deadline)?;
        Ok(fake)
    }

    /// Writes the values into the fake game, by path or field name.
    pub fn frame(&mut self, values: &serde_json::Value, deadline: Instant) -> Result<()> {
        writeln!(self.stdin, "frame {values}")
            .and_then(|()| self.stdin.flush())
            .context("sending a frame to fake-celeste")?;
        self.expect("ack", deadline)
    }

    fn expect(&mut self, expected: &str, deadline: Instant) -> Result<()> {
        match self
            .lines
            .recv_timeout(deadline.saturating_duration_since(Instant::now()))
        {
            Ok(line) if line == expected => Ok(()),
            Ok(line) => bail!("fake-celeste said {line:?} instead of {expected}"),
            Err(RecvTimeoutError::Timeout) => {
                bail!("timed out waiting for fake-celeste to say {expected}")
            }
            Err(RecvTimeoutError::Disconnected) => match self.child.try_wait()? {
                Some(status) => {
                    bail!("fake-celeste exited ({status}) instead of saying {expected}")
                }
                None => bail!("fake-celeste closed its output instead of saying {expected}"),
            },
        }
    }
}

impl Drop for Fake {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}
