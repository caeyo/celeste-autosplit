//! The live check and the replay: the auto splitter, `celeste_autosplit.wasm`, against the real
//! game. The harness starts the probe as the game, in its replay mode. The probe constructs the
//! game in the game's own runtime, writes distinctive values into it through .NET's reflection,
//! and sends the harness those values. The auto splitter runs on livesplit-core's own timer,
//! attached to the probe's process, and after each update, its debug variables must be exactly
//! the values the game holds:
//!
//! - the live check: the probe's values, then the same values with every number negated and every
//!   flag flipped, then each flag set alone, which the probe writes for the harness. So reading the
//!   wrong field, or the wrong width, shows in every field;
//! - the replay: then, for each line of each trace given, the probe writes the line's values into
//!   the game, and the harness updates the auto splitter once.
//!
//! Usage: harness replay [--wasm FILE] [--timeout SECONDS] [TRACE...] -- COMMAND...
//!
//!   --wasm FILE         the auto splitter (default: the release build in autosplitter/).
//!   --timeout SECONDS   how long to wait for the probe's checks, then for the auto splitter to
//!                       report, then for each answer from the probe and each update of the auto
//!                       splitter, and at the end, for the probe to exit, which after an error
//!                       gets at most 5 s (default: 120).
//!   TRACE               a trace to replay, from traces/. Without any, only the live check runs.
//!   COMMAND             starts the probe as the game, the way players start the game. The
//!                       harness adds --replay to it.
//!
//! The auto splitter finds the probe's process by the game's name.
//!
//! It exits with 1 if the check fails, or with 2 for bad arguments.

mod probe;
mod timer;

use std::{
    collections::BTreeMap,
    env, fs, mem,
    path::{Path, PathBuf},
    process::ExitCode,
    sync::mpsc::{self, RecvTimeoutError},
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result, bail};
use livesplit_auto_splitting::{AutoSplitter, CompiledAutoSplitter, Config, Runtime};
use serde_json::Value;

use crate::{probe::Probe, timer::HarnessTimer};

const USAGE: &str =
    "usage: harness replay [--wasm FILE] [--timeout SECONDS] [TRACE...] -- COMMAND...";

/// The harness's folder in the repository, which the default for `--wasm` is relative to.
const HARNESS_DIR: &str = env!("CARGO_MANIFEST_DIR");

/// The paths the auto splitter reports, and the debug variable it reports each one as.
const REPORTED: &[(&str, &str)] = &[
    ("AutoSplitterInfo.Chapter", "Chapter"),
    ("AutoSplitterInfo.Mode", "Mode"),
    ("AutoSplitterInfo.Level", "Level"),
    ("AutoSplitterInfo.TimerActive", "Timer Active"),
    ("AutoSplitterInfo.ChapterStarted", "Chapter Started"),
    ("AutoSplitterInfo.ChapterComplete", "Chapter Complete"),
    ("AutoSplitterInfo.ChapterTime", "Chapter Time"),
    (
        "AutoSplitterInfo.ChapterStrawberries",
        "Chapter Strawberries",
    ),
    ("AutoSplitterInfo.ChapterCassette", "Chapter Cassette"),
    ("AutoSplitterInfo.ChapterHeart", "Chapter Heart"),
    ("AutoSplitterInfo.FileTime", "File Time"),
    ("AutoSplitterInfo.FileStrawberries", "Strawberries"),
    ("AutoSplitterInfo.FileCassettes", "Cassettes"),
    ("AutoSplitterInfo.FileHearts", "Hearts"),
];

struct Options {
    wasm: PathBuf,
    timeout: Duration,
    traces: Vec<PathBuf>,
    command: Vec<String>,
}

/// A trace's frames: each line after its header, as its changes to the game's values, by path.
type Trace = Vec<BTreeMap<String, Value>>;

fn main() -> ExitCode {
    let args = env::args().skip(1).collect::<Vec<_>>();
    let options = match args.split_first() {
        Some((command, options)) if command == "replay" => parse(options),
        Some((command, _)) => Err(anyhow::anyhow!("unknown command {command}")),
        None => Err(anyhow::anyhow!("no command")),
    };
    let options = match options {
        Ok(options) => options,
        Err(e) => {
            eprintln!("harness: {e:#}\n{USAGE}");
            return ExitCode::from(2);
        }
    };
    match run(&options) {
        Ok(true) => ExitCode::SUCCESS,
        Ok(false) => ExitCode::FAILURE,
        Err(e) => {
            eprintln!("harness: {e:#}");
            ExitCode::FAILURE
        }
    }
}

fn parse(args: &[String]) -> Result<Options> {
    let mut wasm = Path::new(HARNESS_DIR).parent().unwrap().join(
        [
            "autosplitter",
            "target",
            "wasm32-unknown-unknown",
            "release",
            "celeste_autosplit.wasm",
        ]
        .iter()
        .collect::<PathBuf>(),
    );
    let mut timeout = Duration::from_secs(120);
    let mut traces = Vec::new();
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        let mut value = || args.next().with_context(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--wasm" => wasm = value()?.into(),
            "--timeout" => {
                timeout =
                    Duration::from_secs(value()?.parse().context("--timeout takes whole seconds")?)
            }
            "--" => break,
            _ if arg.starts_with("--") => bail!("unknown option {arg}"),
            _ => traces.push(arg.into()),
        }
    }
    let command = args.cloned().collect::<Vec<_>>();
    if command.is_empty() {
        bail!("no command after -- to start the probe with");
    }
    Ok(Options {
        wasm,
        timeout,
        traces,
        command,
    })
}

/// Whether the auto splitter reports what the game holds, in the live check and the replay.
fn run(options: &Options) -> Result<bool> {
    let wasm =
        fs::read(&options.wasm).with_context(|| format!("reading {}", options.wasm.display()))?;
    let traces = options
        .traces
        .iter()
        .map(|file| read_trace(file).with_context(|| format!("reading {}", file.display())))
        .collect::<Result<Vec<_>>>()?;
    let compiled = Runtime::new(Config::default())?.compile(&wasm)?;

    let mut probe = Probe::start(&options.command, options.timeout)?;
    let checked = check(&mut probe, &compiled, &traces, options);
    // However the check ended, the probe's input ends, and it has to exit. After an error, which
    // can be the probe's, it only gets a moment.
    let wait = match checked {
        Ok(_) => options.timeout,
        Err(_) => options.timeout.min(Duration::from_secs(5)),
    };
    let exited = probe.finish(wait);
    let ok = checked?;
    if let Err(problem) = exited {
        return Err(probe.failure(&problem));
    }
    Ok(ok)
}

/// The live check: once the auto splitter has found the game, it must report the probe's values,
/// then the same values with every number negated and every flag flipped, then each flag set
/// alone. Then the replay of each trace, one line at a time.
fn check(
    probe: &mut Probe,
    compiled: &CompiledAutoSplitter,
    traces: &[Trace],
    options: &Options,
) -> Result<bool> {
    let (flavour, mut held) = probe.values()?;
    println!(
        "live check: {} against the {flavour} probe",
        options.wasm.display()
    );
    let module = Module::new(compiled, options.timeout)?;

    // It reports nothing until it has found the game, which can take more than one update.
    let deadline = Instant::now() + options.timeout;
    let read = loop {
        let read = match module.update() {
            Ok(read) => read,
            Err(problem) => return failed("live check: FAILED", &[problem], &module),
        };
        if !read.is_empty() {
            break read;
        }
        if Instant::now() >= deadline {
            let problem = "the auto splitter reported nothing".to_owned();
            return failed("live check: FAILED", &[problem], &module);
        }
        thread::sleep(Duration::from_millis(10));
    };
    let problems = differences(&expected(&held)?, &read);
    if !problems.is_empty() {
        return failed("live check: FAILED", &problems, &module);
    }
    // Negating shows a number read with the wrong sign. A flag has only two values, so the probe's
    // can't tell them all apart, even flipped: setting each alone does, and shows each both ways.
    let mut frames = vec![("the values flipped".to_owned(), flipped(&held)?)];
    let flags = held
        .iter()
        .filter(|(_, value)| value.is_boolean())
        .map(|(path, _)| path.clone())
        .collect::<Vec<_>>();
    for flag in &flags {
        let alone = flags
            .iter()
            .map(|other| (other.clone(), Value::Bool(other == flag)));
        frames.push((format!("only {flag} set"), alone.collect()));
    }
    for (frame, changes) in &frames {
        let problems = replay_frame(probe, &module, &mut held, changes)
            .with_context(|| format!("the live check, with {frame}"))?;
        if !problems.is_empty() {
            return failed(
                &format!("live check: FAILED, with {frame}"),
                &problems,
                &module,
            );
        }
    }
    println!(
        "live check: ok, the auto splitter reports the {} values the probe holds, flipped, and each flag alone",
        read.len()
    );

    // The game keeps each value until the next line that changes it.
    for (trace, file) in traces.iter().zip(&options.traces) {
        for (frame, changes) in trace.iter().enumerate() {
            let problems = replay_frame(probe, &module, &mut held, changes)
                .with_context(|| format!("frame {frame} of {}", file.display()))?;
            if !problems.is_empty() {
                let heading = format!("replay: FAILED at frame {frame} of {}", file.display());
                return failed(&heading, &problems, &module);
            }
        }
        println!(
            "replay: ok, the auto splitter reports each of the {} frames of {}",
            trace.len(),
            file.display()
        );
    }
    Ok(true)
}

/// Has the probe write the changes into the game, which then holds them, updates the auto splitter
/// once, and returns how what it reports differs from every value the game holds.
fn replay_frame(
    probe: &mut Probe,
    module: &Module,
    held: &mut BTreeMap<String, Value>,
    changes: &BTreeMap<String, Value>,
) -> Result<Vec<String>> {
    // The probe would take any value for a string: it's the rest of the line.
    for (path, value) in changes {
        match held.get(path) {
            Some(old) if mem::discriminant(old) == mem::discriminant(value) => {}
            Some(old) => bail!("{path} holds {old}, which {value} can't replace"),
            None => bail!("the probe has no path {path}"),
        }
    }
    probe.write(changes)?;
    held.extend(changes.clone());
    Ok(match module.update() {
        Ok(read) => differences(&expected(held)?, &read),
        Err(problem) => vec![problem],
    })
}

/// The values, with every number negated and every flag flipped. Strings are left out: the probe's
/// can't be matched by chance.
fn flipped(held: &BTreeMap<String, Value>) -> Result<BTreeMap<String, Value>> {
    let mut flipped = BTreeMap::new();
    for (path, value) in held {
        let value = match value {
            Value::String(_) => continue,
            Value::Bool(flag) => Value::Bool(!flag),
            Value::Number(number) => number
                .as_i64()
                .and_then(i64::checked_neg)
                .with_context(|| format!("can't negate {path}'s {number}"))?
                .into(),
            _ => bail!("can't flip {path}'s {value}"),
        };
        flipped.insert(path.clone(), value);
    }
    Ok(flipped)
}

/// The auto splitter, on livesplit-core's timer, updated by hand. A runtime updated by hand has
/// no watchdog of its own, so a thread interrupts any update that runs longer than the timeout.
struct Module {
    auto_splitter: AutoSplitter<HarnessTimer>,
    timer: HarnessTimer,
    /// When the update that's running has to end by, for the watchdog.
    deadlines: mpsc::Sender<Option<Instant>>,
    timeout: Duration,
}

impl Module {
    fn new(compiled: &CompiledAutoSplitter, timeout: Duration) -> Result<Self> {
        let timer = HarnessTimer::new(1);
        let auto_splitter = compiled.instantiate(timer.clone(), None, None)?;
        let interrupt = auto_splitter.interrupt_handle();
        let (deadlines, updates) = mpsc::channel();
        thread::spawn(move || {
            let mut deadline: Option<Instant> = None;
            loop {
                let next = match deadline {
                    Some(deadline) => {
                        updates.recv_timeout(deadline.saturating_duration_since(Instant::now()))
                    }
                    None => updates.recv().map_err(|_| RecvTimeoutError::Disconnected),
                };
                match next {
                    Ok(next) => deadline = next,
                    Err(RecvTimeoutError::Timeout) => {
                        interrupt.interrupt();
                        deadline = None;
                    }
                    Err(RecvTimeoutError::Disconnected) => return,
                }
            }
        });
        Ok(Self {
            auto_splitter,
            timer,
            deadlines,
            timeout,
        })
    }

    /// Runs one update, and returns the debug variables the auto splitter set in it, or why it
    /// failed.
    fn update(&self) -> Result<BTreeMap<String, String>, String> {
        self.timer.clear_variables();
        let start = Instant::now();
        // The watchdog only stops once the sender is dropped, so sending can't fail.
        let _ = self.deadlines.send(Some(start + self.timeout));
        let updated = self.auto_splitter.lock().update();
        let _ = self.deadlines.send(None);
        match updated {
            Ok(()) => Ok(self.timer.variables()),
            Err(_) if start.elapsed() >= self.timeout => Err(format!(
                "an update ran for more than {} s",
                self.timeout.as_secs()
            )),
            Err(e) => Err(format!(
                "the auto splitter failed: {:#}",
                anyhow::Error::from(e)
            )),
        }
    }
}

/// A trace's frames. Its first line, the header, holds the versions it was recorded with, and
/// nothing replays Everest's values.
fn read_trace(file: &Path) -> Result<Trace> {
    let text = fs::read_to_string(file)?;
    let mut lines = text.lines().enumerate();
    lines.next().context("no header")?;
    let trace = lines
        .map(|(i, line)| read_frame(line, i - 1).with_context(|| format!("line {}", i + 1)))
        .collect::<Result<Trace>>()?;
    if trace.is_empty() {
        bail!("no frames");
    }
    Ok(trace)
}

/// A frame's changes to the game's values, from its line, which has to hold the frame's number.
fn read_frame(line: &str, number: usize) -> Result<BTreeMap<String, Value>> {
    let line: serde_json::Map<String, Value> = serde_json::from_str(line)?;
    if line.get("frame") != Some(&Value::from(number)) {
        bail!("expected frame {number}");
    }
    let mut changes = BTreeMap::new();
    for (key, value) in &line {
        match key.as_str() {
            "frame" | "everest" => {}
            "roots" => changes = by_path(value)?,
            _ => bail!("unexpected {key}"),
        }
    }
    Ok(changes)
}

/// Every root's values, by path, from JSON keyed by root and then path, as the probe sends them
/// and the traces hold them.
fn by_path(roots: &Value) -> Result<BTreeMap<String, Value>> {
    let mut values = BTreeMap::new();
    for paths in roots.as_object().context("no roots")?.values() {
        for (path, value) in paths.as_object().context("a root with no paths")? {
            if values.insert(path.clone(), value.clone()).is_some() {
                bail!("two roots have the path {path}");
            }
        }
    }
    Ok(values)
}

/// The debug variables the auto splitter should report for the values the game holds.
fn expected(held: &BTreeMap<String, Value>) -> Result<BTreeMap<String, String>> {
    let mut expected = BTreeMap::new();
    for &(path, variable) in REPORTED {
        let text = match held
            .get(path)
            .with_context(|| format!("the probe holds no {path}"))?
        {
            // The auto splitter doesn't report a null .NET string.
            Value::Null => continue,
            Value::Bool(flag) => u8::from(*flag).to_string(),
            Value::String(text) => text.clone(),
            number => number.to_string(),
        };
        expected.insert(variable.to_owned(), text);
    }
    Ok(expected)
}

/// How the variables read differ from those expected.
fn differences(
    expected: &BTreeMap<String, String>,
    read: &BTreeMap<String, String>,
) -> Vec<String> {
    let mut differences = Vec::new();
    for (variable, value) in expected {
        match read.get(variable) {
            None => differences.push(format!("{variable}: expected {value:?}, but it's missing")),
            Some(read) if read != value => {
                differences.push(format!("{variable}: expected {value:?}, read {read:?}"))
            }
            Some(_) => {}
        }
    }
    for (variable, value) in read {
        if !expected.contains_key(variable) {
            differences.push(format!("{variable}: not expected, read {value:?}"));
        }
    }
    differences
}

/// Prints why a check failed, and what the auto splitter logged.
fn failed(heading: &str, problems: &[String], module: &Module) -> Result<bool> {
    println!("{heading}");
    for problem in problems {
        println!("  {problem}");
    }
    println!("  log:");
    for line in module.timer.log() {
        println!("    {line}");
    }
    Ok(false)
}
