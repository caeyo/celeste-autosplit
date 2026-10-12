//! The reading test. For each layout usable on this OS, the auto splitter reads a fake game built
//! from it, one update per frame. Each frame writes known values into the fake game, and the debug
//! variables the auto splitter reports must be exactly those values.
//!
//! The fake game's offsets come from the layouts, and the auto splitter's from its own
//! hand-written `memory.rs`, so the test checks one against the other.

use std::{
    collections::{BTreeMap, BTreeSet},
    fs,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result, bail};
use livesplit_auto_splitting::{Config, Runtime, settings};
use serde_json::{Value, json};

use crate::{
    game::Game,
    layout::{self, EVEREST_FILE, Everest, Flavour},
    timer::HarnessTimer,
};

/// The vanilla paths the auto splitter reports, and the debug variable it reports each one as.
const VANILLA: &[(&str, &str)] = &[
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

/// Everest's fields and flags that the auto splitter reports, and the debug variable it reports
/// each one as.
const EVEREST: &[(&str, &str)] = &[
    ("LevelSetStrPtr", "Level Set"),
    ("ChapterSIDStrPtr", "Chapter SID"),
    ("ChapterID", "Chapter"),
    ("ChapterMode", "Mode"),
    ("RoomNameStrPtr", "Level"),
    ("ChapterTime", "Chapter Time"),
    ("ChapterStrawberries", "Chapter Strawberries"),
    ("ChapterFlags.ChapterStarted", "Chapter Started"),
    ("ChapterFlags.ChapterComplete", "Chapter Complete"),
    ("ChapterFlags.ChapterCassette", "Chapter Cassette"),
    ("ChapterFlags.ChapterHeart", "Chapter Heart"),
    ("ChapterFlags.GrabbedGolden", "Grabbed Golden"),
    ("ChapterFlags.TimerActive", "Timer Active"),
    ("FileTime", "File Time"),
    ("FileStrawberries", "Strawberries"),
    ("FileGoldenStrawberries", "Golden Strawberries"),
    ("FileCassettes", "Cassettes"),
    ("FileHearts", "Hearts"),
    ("FileFlags.IsDebug", "Debug"),
    ("FileFlags.AssistMode", "Assist Mode"),
    ("FileFlags.VariantsMode", "Variants Mode"),
    ("FileFlags.StartingNewFile", "Starting New File"),
    ("FileFlags.FileActive", "File Active"),
];

/// One fake game for the auto splitter to read.
pub struct Case {
    /// The flavour, and the store's width when the flavour has more than one.
    pub name: String,
    layout: PathBuf,
    store_width: Option<usize>,
    values: Vec<Reported>,
    /// What the auto splitter reports for a null string: nothing for a .NET string, and an
    /// empty one for Everest's.
    null_string: Option<&'static str>,
}

/// A value that the auto splitter reports.
#[derive(Clone)]
struct Reported {
    /// Its path or field in the layout.
    name: &'static str,
    variable: &'static str,
    kind: Kind,
}

#[derive(Copy, Clone)]
enum Kind {
    I32,
    I64,
    Bool,
    String,
}

/// Every layout in the folder for this OS, plus Everest's. A flavour with 4- and 8-byte stores
/// is tested with each.
pub fn cases(layouts: &Path) -> Result<Vec<Case>> {
    let os = std::env::consts::OS;
    let mut files = fs::read_dir(layouts)
        .with_context(|| format!("listing {}", layouts.display()))?
        .map(|entry| Ok(entry?.path()))
        .collect::<Result<Vec<_>>>()?;
    files.sort();
    let mut cases = Vec::new();
    for file in files {
        if file.extension().is_none_or(|extension| extension != "json")
            || file.file_name().is_some_and(|name| name == EVEREST_FILE)
        {
            continue;
        }
        let flavour: Flavour = layout::read(&file)?;
        if flavour.os != os {
            continue;
        }
        let values = vanilla_values(&flavour)?;
        let widths = flavour
            .roots
            .values()
            .flat_map(|root| root.store.iter().map(|store| layout::slot_width(store)))
            .collect::<BTreeSet<_>>();
        for &width in &widths {
            // A 64-bit macOS process can't map anything below 4 GB, and the game on macOS
            // always has the 8-byte store.
            if width == 4 && os == "macos" {
                continue;
            }
            cases.push(Case {
                name: if widths.len() > 1 {
                    format!("{}, {width}-byte store", flavour.flavour)
                } else {
                    flavour.flavour.clone()
                },
                layout: file.clone(),
                store_width: Some(width),
                values: values.clone(),
                null_string: None,
            });
        }
    }
    if cases.is_empty() {
        bail!("{} has no layout for {os}", layouts.display());
    }
    let file = layouts.join(EVEREST_FILE);
    cases.push(Case {
        name: "everest".to_owned(),
        values: everest_values(&layout::read(&file)?)?,
        layout: file,
        store_width: None,
        null_string: Some(""),
    });
    Ok(cases)
}

fn vanilla_values(flavour: &Flavour) -> Result<Vec<Reported>> {
    VANILLA
        .iter()
        .map(|&(name, variable)| {
            let path = flavour
                .roots
                .values()
                .find_map(|root| root.paths.get(name))
                .with_context(|| format!("{} has no path {name}", flavour.flavour))?;
            let kind = match path.types.last().map(String::as_str) {
                Some("System.Int32") => Kind::I32,
                Some("System.Int64") => Kind::I64,
                Some("System.Boolean") => Kind::Bool,
                Some("System.String") => Kind::String,
                other => bail!("{name} is a {other:?}, which the reading test has no values for"),
            };
            Ok(Reported {
                name,
                variable,
                kind,
            })
        })
        .collect()
}

fn everest_values(everest: &Everest) -> Result<Vec<Reported>> {
    EVEREST
        .iter()
        .map(|&(name, variable)| {
            let kind = match name.split_once('.') {
                Some((field, flag)) => {
                    everest
                        .fields
                        .get(field)
                        .and_then(|field| field.flags.get(flag))
                        .with_context(|| format!("Everest has no flag {name}"))?;
                    Kind::Bool
                }
                None => {
                    let field = everest
                        .fields
                        .get(name)
                        .with_context(|| format!("Everest has no field {name}"))?;
                    match field.ty.as_str() {
                        "i32" => Kind::I32,
                        "i64" => Kind::I64,
                        "utf8_ptr" => Kind::String,
                        other => {
                            bail!("{name} is a {other}, which the reading test has no values for")
                        }
                    }
                }
            };
            Ok(Reported {
                name,
                variable,
                kind,
            })
        })
        .collect()
}

/// The values for each frame, in the order of the case's values, with a name for each frame.
/// Each number differs from its neighbours' in every byte, so that a wrong offset can't read
/// back the right value, and each flag is seen set and clear, alone and with the others.
fn frames(values: &[Reported]) -> Vec<(String, Vec<Value>)> {
    // The n-th value, counting from 1.
    let frame = |name: &str, value: &dyn Fn(usize, Kind) -> Value| {
        let values = values
            .iter()
            .enumerate()
            .map(|(i, reported)| value(i + 1, reported.kind))
            .collect();
        (name.to_owned(), values)
    };
    let mut frames = vec![
        frame("all zero, strings null", &|_, kind| match kind {
            Kind::I32 | Kind::I64 => json!(0),
            Kind::Bool => json!(false),
            Kind::String => Value::Null,
        }),
        frame("distinct values", &|n, kind| match kind {
            Kind::I32 => json!(n as i64 * 0x0101_0101),
            Kind::I64 => json!(n as i64 * 0x0101_0101_0101_0101),
            Kind::Bool => json!(n % 2 == 1),
            Kind::String => json!(format!("room-{n}")),
        }),
        frame("distinct values, negated", &|n, kind| match kind {
            Kind::I32 => json!(-(n as i64) * 0x0101_0101),
            Kind::I64 => json!(-(n as i64) * 0x0101_0101_0101_0101),
            Kind::Bool => json!(n % 2 == 0),
            Kind::String => json!(format!("🍓 {n}")),
        }),
        frame("-1, strings empty", &|_, kind| match kind {
            Kind::I32 | Kind::I64 => json!(-1),
            Kind::Bool => json!(true),
            Kind::String => json!(""),
        }),
        frame("minimums", &|_, kind| match kind {
            Kind::I32 => json!(i32::MIN),
            Kind::I64 => json!(i64::MIN),
            Kind::Bool => json!(false),
            Kind::String => json!("a"),
        }),
        frame("maximums", &|_, kind| match kind {
            Kind::I32 => json!(i32::MAX),
            Kind::I64 => json!(i64::MAX),
            Kind::Bool => json!(true),
            Kind::String => json!("Celeste"),
        }),
    ];
    for (i, flag) in values
        .iter()
        .enumerate()
        .filter(|(_, reported)| matches!(reported.kind, Kind::Bool))
    {
        frames.push(frame(
            &format!("only {} set", flag.name),
            &|n, kind| match kind {
                Kind::I32 | Kind::I64 => json!(0),
                Kind::Bool => json!(n == i + 1),
                Kind::String => Value::Null,
            },
        ));
    }
    frames
}

/// The variables the auto splitter should report for a frame's values.
fn expected(case: &Case, values: &[Value]) -> BTreeMap<String, String> {
    case.values
        .iter()
        .zip(values)
        .filter_map(|(reported, value)| {
            let text = match value {
                Value::Null => case.null_string?.to_owned(),
                Value::Bool(flag) => u8::from(*flag).to_string(),
                Value::String(text) => text.clone(),
                number => number.to_string(),
            };
            Some((reported.variable.to_owned(), text))
        })
        .collect()
}

/// How the variables read differ from those expected.
fn differences_from(
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

/// Runs a case in this process, which has to run as the game: the fake game, and the auto
/// splitter with a timer of its own. Returns each frame's differences, prefixed with the frame's
/// name. Fails if the case takes longer than the timeout, or the auto splitter stops.
pub fn run(
    case: &Case,
    wasm: &[u8],
    settings: Option<&settings::Map>,
    timeout: Duration,
    timer: &HarnessTimer,
) -> Result<Vec<String>> {
    let deadline = Instant::now() + timeout;
    let mut game = Game::build(&case.layout, case.store_width)?;

    // Only now that the game is mapped: the runtime lists the processes when it's created, and
    // each process's memory ranges when they're first asked for, then only once a second.
    let auto_splitter = Runtime::new(Config::default())?
        .compile(wasm)?
        .instantiate(timer.clone(), settings.cloned(), None)?;
    // A runtime stepped by hand has no watchdog of its own, so this interrupts the auto splitter
    // if it's still running at the deadline.
    let interrupt = auto_splitter.interrupt_handle();
    thread::spawn(move || {
        thread::sleep(deadline.saturating_duration_since(Instant::now()));
        interrupt.interrupt();
    });

    let mut differences = Vec::new();
    for (name, values) in frames(&case.values) {
        for (reported, value) in case.values.iter().zip(&values) {
            game.set(reported.name, value)
                .with_context(|| format!("setting {} to {value}", reported.name))?;
        }
        timer.clear_variables();
        if let Err(e) = auto_splitter.lock().update() {
            if Instant::now() >= deadline {
                bail!("timed out in frame {name}");
            }
            let e = anyhow::Error::from(e);
            return Err(e.context(format!("the auto splitter failed in frame {name}")));
        }
        let read = timer.variables();
        if read.is_empty() {
            differences.push(format!("frame {name}: the auto splitter reported nothing"));
            continue;
        }
        differences.extend(
            differences_from(&expected(case, &values), &read)
                .into_iter()
                .map(|difference| format!("frame {name}: {difference}")),
        );
    }
    Ok(differences)
}
