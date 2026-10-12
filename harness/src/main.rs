//! Runs the auto splitter, `celeste_autosplit.wasm`, on livesplit-core's own timer, against a
//! fake game built from the layouts in `layouts/`, in the harness's own process.
//!
//! Usage: harness read [OPTIONS]
//!
//! `read` runs the reading test (see `reading.rs`). Options:
//!   --wasm FILE         the auto splitter (default: the release build in autosplitter/).
//!   --layouts DIR       the layouts (default: layouts/).
//!   --settings FILE     settings for the auto splitter, as a JSON object (default: none).
//!   --timeout SECONDS   how long each case may take (default: 60).
//!
//! The auto splitter finds the game by its process name, so each case runs in a copy of the
//! harness named like the game, as `case INDEX [OPTIONS]`, and reads the fake game from that
//! process's own memory. A process of its own also keeps each case's fake game from the others.
//!
//! It exits with 1 if a case fails, or with 2 for bad arguments.

mod game;
mod layout;
mod memory;
mod reading;
mod timer;

use std::{
    env, fs,
    path::{Path, PathBuf},
    process::{Command, ExitCode},
    time::Duration,
};

use anyhow::{Context, Result, bail};
use livesplit_auto_splitting::settings;
use serde_json::Value;

use crate::timer::HarnessTimer;

const USAGE: &str =
    "usage: harness read [--wasm FILE] [--layouts DIR] [--settings FILE] [--timeout SECONDS]";

/// The harness's folder in the repository, which the defaults are relative to.
const HARNESS_DIR: &str = env!("CARGO_MANIFEST_DIR");

/// What the game's process is called on this OS, which the auto splitter looks for.
const GAME_NAME: &str = if cfg!(windows) {
    "Celeste.exe"
} else if cfg!(target_os = "linux") {
    "Celeste.bin.x86_64"
} else {
    "Celeste"
};

struct Options {
    wasm: PathBuf,
    layouts: PathBuf,
    settings: Option<PathBuf>,
    timeout: Duration,
}

fn main() -> ExitCode {
    let args = env::args().skip(1).collect::<Vec<_>>();
    let result = match args.split_first() {
        Some((command, options)) if command == "read" => parse(options).map(|parsed| {
            // The cases get the same options.
            reading_test(&parsed, options)
        }),
        Some((command, rest)) if command == "case" => match rest.split_first() {
            Some((index, options)) => index
                .parse()
                .context("case takes the case's index")
                .and_then(|index| Ok((index, parse(options)?)))
                .map(|(index, parsed)| case(index, &parsed)),
            None => Err(anyhow::anyhow!("case needs the case's index")),
        },
        Some((command, _)) => Err(anyhow::anyhow!("unknown command {command}")),
        None => Err(anyhow::anyhow!("no command")),
    };
    match result {
        Err(e) => {
            eprintln!("harness: {e:#}\n{USAGE}");
            ExitCode::from(2)
        }
        Ok(Ok(true)) => ExitCode::SUCCESS,
        Ok(Ok(false)) => ExitCode::FAILURE,
        Ok(Err(e)) => {
            eprintln!("harness: {e:#}");
            ExitCode::FAILURE
        }
    }
}

fn parse(args: &[String]) -> Result<Options> {
    let repository = Path::new(HARNESS_DIR).parent().unwrap();
    let mut options = Options {
        wasm: repository.join(
            [
                "autosplitter",
                "target",
                "wasm32-unknown-unknown",
                "release",
                "celeste_autosplit.wasm",
            ]
            .iter()
            .collect::<PathBuf>(),
        ),
        layouts: repository.join("layouts"),
        settings: None,
        timeout: Duration::from_secs(60),
    };
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        let mut value = || args.next().with_context(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--wasm" => options.wasm = value()?.into(),
            "--layouts" => options.layouts = value()?.into(),
            "--settings" => options.settings = Some(value()?.into()),
            "--timeout" => {
                options.timeout =
                    Duration::from_secs(value()?.parse().context("--timeout takes whole seconds")?)
            }
            _ => bail!("unknown option {arg}"),
        }
    }
    Ok(options)
}

/// The reading test: runs each case in a copy of the harness named like the game, which prints
/// how the case went. Whether every case passed.
fn reading_test(options: &Options, args: &[String]) -> Result<bool> {
    let cases = reading::cases(&options.layouts)?;
    let harness = env::current_exe()?;
    let game = harness
        .parent()
        .context("the harness has no folder")?
        .join("game")
        .join(GAME_NAME);
    fs::create_dir_all(game.parent().unwrap())?;
    fs::copy(&harness, &game)
        .with_context(|| format!("copying {} to {}", harness.display(), game.display()))?;

    println!(
        "reading test: {} against {}",
        options.wasm.display(),
        options.layouts.display()
    );
    let mut failed = 0;
    for (index, case) in cases.iter().enumerate() {
        let status = Command::new(&game)
            .arg("case")
            .arg(index.to_string())
            .args(args)
            .status()
            .with_context(|| format!("starting {}", game.display()))?;
        if !status.success() {
            failed += 1;
            // With 1, the case has said what went wrong.
            if status.code() != Some(1) {
                println!("{}: FAILED ({status})", case.name);
            }
        }
    }
    if failed == 0 {
        println!("reading test: ok");
    } else {
        println!("reading test: {failed} of {} cases failed", cases.len());
    }
    Ok(failed == 0)
}

/// Runs one case of the reading test in this process, which must be named like the game.
fn case(index: usize, options: &Options) -> Result<bool> {
    let name = env::current_exe()?;
    if name.file_name().is_none_or(|name| name != GAME_NAME) {
        bail!("cases run in a copy of the harness named {GAME_NAME}: run harness read");
    }
    let cases = reading::cases(&options.layouts)?;
    let case = cases.get(index).context("no case with that index")?;
    let timer = HarnessTimer::new(1);
    let problems = match run_case(case, options, &timer) {
        Ok(differences) if differences.is_empty() => {
            println!("{}: ok", case.name);
            return Ok(true);
        }
        Ok(differences) => differences,
        Err(e) => vec![format!("{e:#}")],
    };
    println!("{}: FAILED", case.name);
    for problem in problems {
        println!("  {problem}");
    }
    println!("  log:");
    for line in timer.log() {
        println!("    {line}");
    }
    Ok(false)
}

fn run_case(case: &reading::Case, options: &Options, timer: &HarnessTimer) -> Result<Vec<String>> {
    let wasm =
        fs::read(&options.wasm).with_context(|| format!("reading {}", options.wasm.display()))?;
    let settings = options.settings.as_deref().map(read_settings).transpose()?;
    reading::run(case, &wasm, settings.as_ref(), options.timeout, timer)
}

fn read_settings(file: &Path) -> Result<settings::Map> {
    let text = fs::read_to_string(file).with_context(|| format!("reading {}", file.display()))?;
    match serde_json::from_str(&text).with_context(|| format!("parsing {}", file.display()))? {
        Value::Object(map) => settings_map(&map),
        _ => bail!("{} isn't a JSON object", file.display()),
    }
}

fn settings_map(json: &serde_json::Map<String, Value>) -> Result<settings::Map> {
    let mut map = settings::Map::new();
    for (key, value) in json {
        map.insert(
            key.as_str().into(),
            setting(value).with_context(|| format!("in {key}"))?,
        );
    }
    Ok(map)
}

fn setting(json: &Value) -> Result<settings::Value> {
    Ok(match json {
        Value::Bool(value) => settings::Value::Bool(*value),
        Value::Number(number) => match number.as_i64() {
            Some(value) => settings::Value::I64(value),
            None => settings::Value::F64(number.as_f64().context("a number out of range")?),
        },
        Value::String(value) => settings::Value::String(value.as_str().into()),
        Value::Array(items) => {
            let mut list = settings::List::new();
            for item in items {
                list.push(setting(item)?);
            }
            settings::Value::List(list)
        }
        Value::Object(map) => settings::Value::Map(settings_map(map)?),
        Value::Null => bail!("settings can't be null"),
    })
}
