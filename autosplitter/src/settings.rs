use asr::settings::{Map, Value};

#[allow(dead_code)]
mod schema {
    include!(concat!(env!("OUT_DIR"), "/schema.rs"));
}

use schema::{Field, TimingMethod, CHAPTERS, NO_CHAPTER, SCHEMA_VERSION, SIDE_VALUES};
pub use schema::{Kind, Timing};

pub struct Trigger {
    pub kind: Kind,
    pub chapter: Option<i64>,
    pub side: Option<i64>,
    pub timing: Timing,
}

impl Trigger {
    fn of(kind: Kind) -> Self {
        Self {
            kind,
            chapter: None,
            side: None,
            timing: Timing::default(),
        }
    }
}

pub struct Settings {
    pub start: Trigger,
    pub splits: Vec<Trigger>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            start: Trigger::of(schema::START),
            splits: Vec::new(),
        }
    }
}

impl Settings {
    pub fn parse(map: &Map) -> Self {
        let mut settings = Self::default();
        if let Some(version) = map.get("schema_version") {
            match version.get_i64() {
                Some(SCHEMA_VERSION) => {}
                Some(newer) if newer > SCHEMA_VERSION => {
                    asr::print_message(&format!(
                        "schema_version {newer} is newer than {SCHEMA_VERSION}, so cfg is ignored"
                    ));
                    return settings;
                }
                Some(older) => asr::print_message(&format!(
                    "schema_version {older} is older than {SCHEMA_VERSION}, so it's read as {SCHEMA_VERSION}"
                )),
                None => asr::print_message(&format!(
                    "schema_version {version:?} isn't a number, so it's read as {SCHEMA_VERSION}"
                )),
            }
        }
        let Some(cfg) = map.get("cfg") else {
            return settings;
        };
        let Some(cfg) = cfg.get_map() else {
            asr::print_message("cfg isn't a map, so it's ignored");
            return settings;
        };
        let everest = flag(&cfg, "everest", schema::EVEREST);
        if flag(&cfg, "auto_reset", schema::AUTO_RESET) {
            asr::print_message("cfg.auto_reset isn't supported yet, so it's off");
        }
        timing_method(&cfg);
        if let Some(start) = cfg.get("start") {
            settings.start = trigger(&start, everest, "start");
        }
        if let Some(splits) = cfg.get("splits") {
            match splits.get_list() {
                Some(splits) => {
                    settings.splits = splits
                        .iter()
                        .enumerate()
                        .map(|(i, split)| trigger(&split, everest, &format!("splits[{i}]")))
                        .collect();
                }
                None => asr::print_message("cfg.splits isn't a list, so there are none"),
            }
        }
        settings
    }
}

fn flag(cfg: &Map, key: &str, default: bool) -> bool {
    let Some(value) = cfg.get(key) else {
        return default;
    };
    value.get_bool().unwrap_or_else(|| {
        asr::print_message(&format!(
            "cfg.{key} {value:?} isn't a bool, so it's {default}"
        ));
        default
    })
}

fn timing_method(cfg: &Map) {
    let Some(value) = cfg.get("timing_method") else {
        return;
    };
    let file = TimingMethod::default().name();
    match value
        .get_string()
        .and_then(|name| TimingMethod::from_name(&name))
    {
        Some(TimingMethod::File) => {}
        Some(method) => asr::print_message(&format!(
            "cfg.timing_method {} isn't supported yet, so it's {file}",
            method.name()
        )),
        None => asr::print_message(&format!(
            "cfg.timing_method {value:?} isn't valid, so it's {file}"
        )),
    }
}

fn trigger(value: &Value, everest: bool, place: &str) -> Trigger {
    match parse_trigger(value, everest) {
        Ok(trigger) => trigger,
        Err(problem) => {
            asr::print_message(&format!("cfg.{place} {problem}, so it's manual"));
            Trigger::of(Kind::Manual)
        }
    }
}

fn parse_trigger(value: &Value, everest: bool) -> Result<Trigger, String> {
    let map = value.get_map().ok_or("isn't a map")?;
    let kind = map.get("kind").and_then(|kind| kind.get_string());
    let kind = kind.ok_or("has no kind")?;
    let kind = Kind::from_name(&kind).ok_or_else(|| format!("has the unknown kind {kind:?}"))?;
    let mut trigger = Trigger::of(kind);
    let mut present = Vec::new();
    let mut checkpoint = None;
    for &field in kind.required().iter().chain(kind.optional()) {
        if field.everest() && !everest {
            continue;
        }
        let Some(value) = map.get(field.name()) else {
            continue;
        };
        let invalid = || format!("has an invalid {}, {value:?}", field.name());
        match field {
            Field::Chapter => {
                let valid = |&id: &i64| {
                    CHAPTERS.iter().any(|chapter| chapter.id == id)
                        || (kind.allow_no_chapter() && id == NO_CHAPTER)
                };
                trigger.chapter = Some(value.get_i64().filter(valid).ok_or_else(invalid)?);
            }
            Field::Side => {
                let valid = |side: &i64| SIDE_VALUES.contains(side);
                trigger.side = Some(value.get_i64().filter(valid).ok_or_else(invalid)?);
            }
            Field::Timing => {
                let timing = value.get_string().and_then(|name| Timing::from_name(&name));
                trigger.timing = timing.ok_or_else(invalid)?;
            }
            Field::Checkpoint => checkpoint = Some(value.get_i64().ok_or_else(invalid)?),
            Field::Room | Field::Sid | Field::Raw => {
                value.get_string().ok_or_else(invalid)?;
            }
        }
        present.push(field);
    }
    let stands_in = |field| {
        present
            .iter()
            .any(|&other| other == field || other.replaces() == Some(field))
    };
    if let Some(missing) = kind.required().iter().find(|&&field| !stands_in(field)) {
        return Err(format!("has no {}", missing.name()));
    }
    if let Some(checkpoint) = checkpoint {
        let chapter = CHAPTERS
            .iter()
            .find(|chapter| Some(chapter.id) == trigger.chapter);
        let count = chapter.map_or(0, |chapter| chapter.checkpoints.len());
        if !(1..=count as i64).contains(&checkpoint) {
            return Err(format!("has an invalid checkpoint, {checkpoint}"));
        }
    }
    if !matches!(
        kind,
        Kind::Manual | Kind::Unknown | Kind::NewFile | Kind::ChapterComplete
    ) {
        return Err(format!("is {}, which isn't supported yet", kind.name()));
    }
    if present.contains(&Field::Sid) {
        return Err("has a sid, which isn't supported yet".to_owned());
    }
    Ok(trigger)
}
