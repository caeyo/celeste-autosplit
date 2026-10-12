//! Runs the auto splitter, `celeste_autosplit.wasm`, on livesplit-core's own timer, against
//! `fake-celeste`, a stand-in for the game built from the layouts in `layouts/`.
//!
//! Usage: harness read [OPTIONS]
//!
//! `read` runs the reading test (see `reading.rs`). Options:
//!   --wasm FILE         the auto splitter (default: the release build in autosplitter/).
//!   --layouts DIR       the layouts (default: layouts/).
//!   --settings FILE     settings for the auto splitter, as a JSON object (default: none).
//!   --timeout SECONDS   how long each case may take (default: 60).
//!
//! It exits with 1 if a case fails, or with 2 for bad arguments.

mod fake;
mod reading;
mod timer;

use std::{
    path::{Path, PathBuf},
    process::ExitCode,
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

struct Options {
    wasm: PathBuf,
    layouts: PathBuf,
    settings: Option<PathBuf>,
    timeout: Duration,
}

fn main() -> ExitCode {
    let options = match parse(std::env::args().skip(1)) {
        Ok(options) => options,
        Err(e) => {
            eprintln!("harness: {e:#}\n{USAGE}");
            return ExitCode::from(2);
        }
    };
    match reading_test(&options) {
        Ok(true) => ExitCode::SUCCESS,
        Ok(false) => ExitCode::FAILURE,
        Err(e) => {
            eprintln!("harness: {e:#}");
            ExitCode::FAILURE
        }
    }
}

fn parse(mut args: impl Iterator<Item = String>) -> Result<Options> {
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
    match args.next().as_deref() {
        Some("read") => {}
        Some(other) => bail!("unknown command {other}"),
        None => bail!("no command"),
    }
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

/// The reading test. Whether every case passed.
fn reading_test(options: &Options) -> Result<bool> {
    let wasm = std::fs::read(&options.wasm)
        .with_context(|| format!("reading {}", options.wasm.display()))?;
    let settings = options.settings.as_deref().map(read_settings).transpose()?;
    let cases = reading::cases(&options.layouts)?;
    let fake = fake::executable()?;

    println!(
        "reading test: {} against {}",
        options.wasm.display(),
        options.layouts.display()
    );
    let mut failed = 0;
    for case in &cases {
        let timer = HarnessTimer::new(1);
        let problems = match reading::run(
            case,
            &fake,
            &wasm,
            settings.as_ref(),
            options.timeout,
            &timer,
        ) {
            Ok(differences) if differences.is_empty() => {
                println!("{}: ok", case.name);
                continue;
            }
            Ok(differences) => differences,
            Err(e) => vec![format!("{e:#}")],
        };
        failed += 1;
        println!("{}: FAILED", case.name);
        for problem in problems {
            println!("  {problem}");
        }
        println!("  log:");
        for line in timer.log() {
            println!("    {line}");
        }
    }
    if failed == 0 {
        println!("reading test: ok");
    } else {
        println!("reading test: {failed} of {} cases failed", cases.len());
    }
    Ok(failed == 0)
}

fn read_settings(file: &Path) -> Result<settings::Map> {
    let text =
        std::fs::read_to_string(file).with_context(|| format!("reading {}", file.display()))?;
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
