mod probe;
mod timer;

use std::{
    collections::BTreeMap,
    env, fs, mem,
    path::{Path, PathBuf},
    process::ExitCode,
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result, bail};
use livesplit_auto_splitting::{AutoSplitter, CompiledAutoSplitter, Config, Runtime};
use serde_json::Value;

use crate::{probe::Probe, timer::HarnessTimer};

const USAGE: &str = "usage: harness [TRACE...] -- COMMAND...";

const TIMEOUT: Duration = Duration::from_secs(120);

const HARNESS_DIR: &str = env!("CARGO_MANIFEST_DIR");

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
    ("TotalDeaths", "Deaths"),
];

struct Options {
    wasm: PathBuf,
    traces: Vec<PathBuf>,
    command: Vec<String>,
}

type Trace = Vec<BTreeMap<String, Value>>;

fn main() -> ExitCode {
    let args = env::args().skip(1).collect::<Vec<_>>();
    let options = match parse(&args) {
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
    let wasm = Path::new(HARNESS_DIR).parent().unwrap().join(
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
    let mut traces = Vec::new();
    let mut args = args.iter();
    for arg in args.by_ref() {
        match arg.as_str() {
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
        traces,
        command,
    })
}

fn run(options: &Options) -> Result<bool> {
    let wasm =
        fs::read(&options.wasm).with_context(|| format!("reading {}", options.wasm.display()))?;
    let traces = options
        .traces
        .iter()
        .map(|file| read_trace(file).with_context(|| format!("reading {}", file.display())))
        .collect::<Result<Vec<_>>>()?;
    let compiled = Runtime::new(Config::default())?.compile(&wasm)?;

    let mut probe = Probe::start(&options.command)?;
    let checked = check(&mut probe, &compiled, &traces, options);
    let wait = match checked {
        Ok(_) => TIMEOUT,
        Err(_) => Duration::from_secs(5),
    };
    let exited = probe.finish(wait);
    let ok = checked?;
    if let Err(problem) = exited {
        return Err(probe.failure(&problem));
    }
    Ok(ok)
}

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
    let module = Module::new(compiled)?;

    let deadline = Instant::now() + TIMEOUT;
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

fn replay_frame(
    probe: &mut Probe,
    module: &Module,
    held: &mut BTreeMap<String, Value>,
    changes: &BTreeMap<String, Value>,
) -> Result<Vec<String>> {
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

struct Module {
    auto_splitter: AutoSplitter<HarnessTimer>,
    timer: HarnessTimer,
}

impl Module {
    fn new(compiled: &CompiledAutoSplitter) -> Result<Self> {
        let timer = HarnessTimer::new();
        let auto_splitter = compiled.instantiate(timer.clone(), None, None)?;
        Ok(Self {
            auto_splitter,
            timer,
        })
    }

    fn update(&self) -> Result<BTreeMap<String, String>, String> {
        self.timer.clear_variables();
        match self.auto_splitter.lock().update() {
            Ok(()) => Ok(self.timer.variables()),
            Err(e) => Err(format!(
                "the auto splitter failed: {:#}",
                anyhow::Error::from(e)
            )),
        }
    }
}

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

fn read_frame(line: &str, number: usize) -> Result<BTreeMap<String, Value>> {
    let line: serde_json::Map<String, Value> = serde_json::from_str(line)?;
    if line.get("frame") != Some(&Value::from(number)) {
        bail!("expected frame {number}");
    }
    let mut changes = BTreeMap::new();
    for (key, value) in &line {
        match key.as_str() {
            "frame" => {}
            "roots" => changes = by_path(value)?,
            _ => bail!("unexpected {key}"),
        }
    }
    Ok(changes)
}

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

fn expected(held: &BTreeMap<String, Value>) -> Result<BTreeMap<String, String>> {
    let mut expected = BTreeMap::new();
    for &(path, variable) in REPORTED {
        let text = match held
            .get(path)
            .with_context(|| format!("the probe holds no {path}"))?
        {
            Value::Bool(flag) => u8::from(*flag).to_string(),
            Value::String(text) => text.clone(),
            number => number.to_string(),
        };
        expected.insert(variable.to_owned(), text);
    }
    Ok(expected)
}

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
