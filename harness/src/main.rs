//! The live check: the auto splitter, `celeste_autosplit.wasm`, against the real game. A layout
//! probe process holds the game it constructed in the game's own runtime, with values it set
//! through .NET's reflection, and writes the values to a file. Running on livesplit-core's own
//! timer, the auto splitter must report exactly those values in its debug variables.
//!
//! Usage: harness live --values FILE [--wasm FILE] [--timeout SECONDS]
//!
//!   --values FILE       the values the probe holds, from its own --values option.
//!   --wasm FILE         the auto splitter (default: the release build in autosplitter/).
//!   --timeout SECONDS   how long to wait in all, for the values and then for the auto
//!                       splitter to report them (default: 120).
//!
//! The probe runs as the game, so the auto splitter finds its process by the game's name.
//!
//! It exits with 1 if the check fails, or with 2 for bad arguments.

mod timer;

use std::{
    collections::BTreeMap,
    env, fs,
    path::{Path, PathBuf},
    process::ExitCode,
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result, bail};
use livesplit_auto_splitting::{Config, Runtime};
use serde_json::Value;

use crate::timer::HarnessTimer;

const USAGE: &str = "usage: harness live --values FILE [--wasm FILE] [--timeout SECONDS]";

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
    values: PathBuf,
    wasm: PathBuf,
    timeout: Duration,
}

fn main() -> ExitCode {
    let args = env::args().skip(1).collect::<Vec<_>>();
    let options = match args.split_first() {
        Some((command, options)) if command == "live" => parse(options),
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
    match live(&options) {
        Ok(true) => ExitCode::SUCCESS,
        Ok(false) => ExitCode::FAILURE,
        Err(e) => {
            eprintln!("harness: {e:#}");
            ExitCode::FAILURE
        }
    }
}

fn parse(args: &[String]) -> Result<Options> {
    let mut values = None;
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
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        let mut value = || args.next().with_context(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--values" => values = Some(value()?.into()),
            "--wasm" => wasm = value()?.into(),
            "--timeout" => {
                timeout =
                    Duration::from_secs(value()?.parse().context("--timeout takes whole seconds")?)
            }
            _ => bail!("unknown option {arg}"),
        }
    }
    Ok(Options {
        values: values.context("--values is required")?,
        wasm,
        timeout,
    })
}

/// Whether the auto splitter reports exactly the values the probe holds.
fn live(options: &Options) -> Result<bool> {
    let deadline = Instant::now() + options.timeout;
    let wasm =
        fs::read(&options.wasm).with_context(|| format!("reading {}", options.wasm.display()))?;
    let (flavour, held) = wait_for_values(&options.values, deadline)?;
    let expected = expected(&held)?;
    println!(
        "live check: {} against the {flavour} probe",
        options.wasm.display()
    );

    let timer = HarnessTimer::new(1);
    let auto_splitter = Runtime::new(Config::default())?
        .compile(&wasm)?
        .instantiate(timer.clone(), None, None)?;
    // A runtime stepped by hand has no watchdog of its own, so this interrupts the auto splitter
    // if it's still running at the deadline.
    let interrupt = auto_splitter.interrupt_handle();
    thread::spawn(move || {
        thread::sleep(deadline.saturating_duration_since(Instant::now()));
        interrupt.interrupt();
    });

    // It reports nothing until it has found the game, which can take more than one update.
    let read = loop {
        timer.clear_variables();
        if let Err(e) = auto_splitter.lock().update() {
            if Instant::now() >= deadline {
                return failed(&["timed out".to_owned()], &timer);
            }
            return Err(anyhow::Error::from(e).context("the auto splitter failed"));
        }
        let read = timer.variables();
        if !read.is_empty() {
            break read;
        }
        if Instant::now() >= deadline {
            return failed(&["the auto splitter reported nothing".to_owned()], &timer);
        }
        thread::sleep(Duration::from_millis(10));
    };

    let differences = differences(&expected, &read);
    if !differences.is_empty() {
        return failed(&differences, &timer);
    }
    println!(
        "live check: ok, the auto splitter reports the {} values the probe holds",
        expected.len()
    );
    Ok(true)
}

/// Waits for the probe to write its values, which it does once every check has passed, with
/// the values in place. Returns its flavour, and each path's value.
fn wait_for_values(file: &Path, deadline: Instant) -> Result<(String, BTreeMap<String, Value>)> {
    loop {
        // A file still being written doesn't parse yet.
        if let Ok(text) = fs::read_to_string(file)
            && let Ok(values) = serde_json::from_str::<Value>(&text)
        {
            let flavour = values["flavour"].as_str().context("no flavour")?.to_owned();
            let mut held = BTreeMap::new();
            let roots = values["roots"].as_object().context("no roots")?;
            for paths in roots.values() {
                for (path, value) in paths.as_object().context("a root with no paths")? {
                    if held.insert(path.clone(), value.clone()).is_some() {
                        bail!("two roots have the path {path}");
                    }
                }
            }
            return Ok((flavour, held));
        }
        if Instant::now() >= deadline {
            bail!("no values in {}: has the probe failed?", file.display());
        }
        thread::sleep(Duration::from_millis(100));
    }
}

/// The debug variables the auto splitter should report for the values the probe holds.
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

fn failed(problems: &[String], timer: &HarnessTimer) -> Result<bool> {
    println!("live check: FAILED");
    for problem in problems {
        println!("  {problem}");
    }
    println!("  log:");
    for line in timer.log() {
        println!("    {line}");
    }
    Ok(false)
}
