mod probe;
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
use livesplit_auto_splitting::{AutoSplitter, CompiledAutoSplitter, Config, Runtime};
use serde_json::Value;

use crate::{probe::Probe, timer::HarnessTimer};

const USAGE: &str = "usage: harness [TRACE...] -- COMMAND...";

const TIMEOUT: Duration = Duration::from_secs(120);

const HARNESS_DIR: &str = env!("CARGO_MANIFEST_DIR");

const INSTANCE: &str = "Celeste.Celeste::Instance";
const SAVE_DATA: &str = "Celeste.SaveData::Instance";

#[derive(Clone, Copy, Debug)]
enum Kind {
    Bool,
    Int,
    Long,
    String,
}

#[rustfmt::skip]
const PATHS: &[(&str, &str, Kind, Option<&str>)] = &[
    (INSTANCE, "AutoSplitterInfo.Chapter", Kind::Int, Some("Chapter")),
    (INSTANCE, "AutoSplitterInfo.Mode", Kind::Int, Some("Mode")),
    (INSTANCE, "AutoSplitterInfo.Level", Kind::String, Some("Level")),
    (INSTANCE, "AutoSplitterInfo.TimerActive", Kind::Bool, Some("Timer Active")),
    (INSTANCE, "AutoSplitterInfo.ChapterStarted", Kind::Bool, Some("Chapter Started")),
    (INSTANCE, "AutoSplitterInfo.ChapterComplete", Kind::Bool, Some("Chapter Complete")),
    (INSTANCE, "AutoSplitterInfo.ChapterTime", Kind::Long, Some("Chapter Time")),
    (INSTANCE, "AutoSplitterInfo.ChapterStrawberries", Kind::Int, Some("Chapter Strawberries")),
    (INSTANCE, "AutoSplitterInfo.ChapterCassette", Kind::Bool, Some("Chapter Cassette")),
    (INSTANCE, "AutoSplitterInfo.ChapterHeart", Kind::Bool, Some("Chapter Heart")),
    (INSTANCE, "AutoSplitterInfo.FileTime", Kind::Long, Some("File Time")),
    (INSTANCE, "AutoSplitterInfo.FileStrawberries", Kind::Int, Some("Strawberries")),
    (INSTANCE, "AutoSplitterInfo.FileCassettes", Kind::Int, Some("Cassettes")),
    (INSTANCE, "AutoSplitterInfo.FileHearts", Kind::Int, Some("Hearts")),
    (SAVE_DATA, "TotalDeaths", Kind::Int, Some("Deaths")),
    (SAVE_DATA, "Time", Kind::Long, None),
];

impl Kind {
    fn sample(self, n: i64) -> Value {
        match self {
            Kind::Bool => Value::Bool(n % 2 == 1),
            Kind::Int => (n * 0x0101_0101).into(),
            Kind::Long => (n * 0x0101_0101_0101_0101).into(),
            Kind::String => format!("path {n}").into(),
        }
    }

    fn holds(self, value: &Value) -> bool {
        match self {
            Kind::Bool => value.is_boolean(),
            Kind::Int => value.as_i64().is_some_and(|n| i32::try_from(n).is_ok()),
            Kind::Long => value.is_i64(),
            Kind::String => value.is_string(),
        }
    }
}

struct Options {
    wasm: PathBuf,
    traces: Vec<PathBuf>,
    command: Vec<String>,
}

type Changes = BTreeMap<usize, Value>;

type Trace = Vec<Changes>;

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
    probe.ready()?;
    println!("live check: {}", options.wasm.display());
    let samples = PATHS
        .iter()
        .zip(1..)
        .map(|(&(_, _, kind, _), n)| kind.sample(n))
        .enumerate()
        .collect::<Changes>();
    probe.write(&samples)?;
    let mut held = samples.into_values().collect::<Vec<_>>();
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
    let problems = differences(&expected(&held), &read);
    if !problems.is_empty() {
        return failed("live check: FAILED", &problems, &module);
    }
    let mut frames = vec![("the values flipped".to_owned(), flipped(&held)?)];
    let flags = (0..PATHS.len())
        .filter(|&i| matches!(PATHS[i].2, Kind::Bool))
        .collect::<Vec<_>>();
    for &flag in &flags {
        let alone = flags
            .iter()
            .map(|&other| (other, Value::Bool(other == flag)));
        frames.push((format!("only {} set", PATHS[flag].1), alone.collect()));
    }
    let save = (0..PATHS.len()).filter(|&i| PATHS[i].0 == SAVE_DATA);
    let time = position(SAVE_DATA, "Time").unwrap();
    let no_save = save.clone().map(|i| (i, Value::Null)).collect();
    let time_zero = save
        .map(|i| {
            let value = if i == time {
                0.into()
            } else {
                PATHS[i].2.sample(i as i64 + 1)
            };
            (i, value)
        })
        .collect();
    let high_half = Changes::from([(time, (0x0101_0101_i64 << 32).into())]);
    frames.push(("no save".to_owned(), no_save));
    frames.push(("a save whose Time is 0".to_owned(), time_zero));
    frames.push(("only Time's high half set".to_owned(), high_half));
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
        "live check: ok, the auto splitter reports the {} values the harness wrote, flipped, each flag alone, no save, a save whose Time is 0, and only Time's high half set",
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
    held: &mut [Value],
    changes: &Changes,
) -> Result<Vec<String>> {
    for (&i, value) in changes {
        let (root, path, kind, _) = PATHS[i];
        if !value.is_null() && !kind.holds(value) {
            bail!("{root} {path} is {kind:?}, which can't hold {value}");
        }
        if (value.is_null() || held[i].is_null()) && !whole(changes, root, value.is_null()) {
            bail!("{root} becomes null, or an object again, with all its paths at once");
        }
    }
    probe.write(changes)?;
    for (&i, value) in changes {
        held[i] = value.clone();
    }
    Ok(match module.update() {
        Ok(read) => differences(&expected(held), &read),
        Err(problem) => vec![problem],
    })
}

fn whole(changes: &Changes, root: &str, null: bool) -> bool {
    (0..PATHS.len())
        .filter(|&i| PATHS[i].0 == root)
        .all(|i| changes.get(&i).is_some_and(|value| value.is_null() == null))
}

fn flipped(held: &[Value]) -> Result<Changes> {
    let mut flipped = Changes::new();
    for (i, value) in held.iter().enumerate() {
        let value = match value {
            Value::String(_) => continue,
            Value::Bool(flag) => Value::Bool(!flag),
            Value::Number(number) => number
                .as_i64()
                .and_then(i64::checked_neg)
                .with_context(|| format!("can't negate {}'s {number}", PATHS[i].1))?
                .into(),
            _ => bail!("can't flip {}'s {value}", PATHS[i].1),
        };
        flipped.insert(i, value);
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

fn read_frame(line: &str, number: usize) -> Result<Changes> {
    let line: serde_json::Map<String, Value> = serde_json::from_str(line)?;
    if line.get("frame") != Some(&Value::from(number)) {
        bail!("expected frame {number}");
    }
    let mut changes = Changes::new();
    for (key, value) in &line {
        match key.as_str() {
            "frame" => {}
            "roots" => changes = by_path(value)?,
            _ => bail!("unexpected {key}"),
        }
    }
    Ok(changes)
}

fn by_path(roots: &Value) -> Result<Changes> {
    let mut changes = Changes::new();
    for (root, paths) in roots.as_object().context("no roots")? {
        if paths.is_null() {
            if !PATHS.iter().any(|&(r, _, _, _)| r == root) {
                bail!("{root} isn't one of the harness's roots");
            }
            let nulls = (0..PATHS.len()).filter(|&i| PATHS[i].0 == root);
            changes.extend(nulls.map(|i| (i, Value::Null)));
            continue;
        }
        for (path, value) in paths.as_object().context("a root with no paths")? {
            if value.is_null() {
                bail!("{root} {path} is null: only a root can be");
            }
            let i = position(root, path)
                .with_context(|| format!("{root} {path} isn't one of the harness's paths"))?;
            changes.insert(i, value.clone());
        }
    }
    Ok(changes)
}

fn position(root: &str, path: &str) -> Option<usize> {
    PATHS
        .iter()
        .position(|&(r, p, _, _)| r == root && p == path)
}

fn expected(held: &[Value]) -> BTreeMap<String, String> {
    let mut expected = PATHS
        .iter()
        .zip(held)
        .filter_map(|(&(_, _, _, variable), value)| {
            let text = match value {
                Value::Null => return None,
                Value::Bool(flag) => u8::from(*flag).to_string(),
                Value::String(text) => text.clone(),
                number => number.to_string(),
            };
            Some((variable?.to_owned(), text))
        })
        .collect::<BTreeMap<_, _>>();
    let time = &held[position(SAVE_DATA, "Time").unwrap()];
    let new_file = u8::from(time.as_i64() == Some(0));
    expected.insert("Starting New File".to_owned(), new_file.to_string());
    expected
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
