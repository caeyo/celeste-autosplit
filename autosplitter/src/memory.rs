use core::{iter, slice};

use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};
use bytemuck::AnyBitPattern;

use crate::{
    everest,
    vanilla::{Layout, Pattern, CLR32, MONO64, WINDOW},
};

pub const NO_CHAPTER: i32 = -1;

pub struct Values {
    pub chapter: i32,
    pub mode: i32,
    pub level: Option<String>,
    pub timer_active: bool,
    pub chapter_started: bool,
    pub chapter_complete: bool,
    pub chapter_time: i64,
    pub chapter_strawberries: i32,
    pub chapter_cassette: bool,
    pub chapter_heart: bool,
    pub file_time: i64,
    pub file_strawberries: i32,
    pub file_cassettes: i32,
    pub file_hearts: i32,
    pub deaths: Option<i32>,
    pub starting_new_file: Option<bool>,
    pub everest: Option<EverestValues>,
}

pub struct EverestValues {
    pub file_golden_strawberries: i32,
    pub grabbed_golden: bool,
    pub level_set: Option<String>,
    pub chapter_sid: Option<String>,
    pub is_debug: bool,
    pub assist_mode: bool,
    pub variants_mode: bool,
    pub file_active: bool,
}

pub enum GameState {
    Vanilla(Vanilla),
    Everest(Everest),
}

impl GameState {
    pub fn read(&self, process: &Process) -> Option<Values> {
        match self {
            Self::Vanilla(vanilla) => vanilla.read(process),
            Self::Everest(everest) => everest.read(process),
        }
    }
}

pub struct Vanilla {
    layout: &'static Layout,
    instance: Address,
    info: u64,
    save_data: Address,
}

impl Vanilla {
    fn read(&self, process: &Process) -> Option<Values> {
        let layout = self.layout;
        let info = process
            .read_pointer(self.instance, layout.pointer_size)
            .and_then(|celeste| process.read_pointer(celeste + self.info, layout.pointer_size))
            .ok()
            .and_then(|info| self.object(process, info, layout.auto_splitter_info_size))?;
        let save = process
            .read_pointer(self.save_data, layout.pointer_size)
            .ok()
            .and_then(|save| {
                if save.is_null() {
                    Some(None)
                } else {
                    self.object(process, save, layout.save_data_size).map(Some)
                }
            });
        Some(Values {
            chapter: value(&info, layout.chapter)?,
            mode: value(&info, layout.mode)?,
            level: self.string(process, pointer(&info, layout.level, layout.pointer_size)?),
            timer_active: flag(&info, layout.timer_active)?,
            chapter_started: flag(&info, layout.chapter_started)?,
            chapter_complete: flag(&info, layout.chapter_complete)?,
            chapter_time: value(&info, layout.chapter_time)?,
            chapter_strawberries: value(&info, layout.chapter_strawberries)?,
            chapter_cassette: flag(&info, layout.chapter_cassette)?,
            chapter_heart: flag(&info, layout.chapter_heart)?,
            file_time: value(&info, layout.file_time)?,
            file_strawberries: value(&info, layout.file_strawberries)?,
            file_cassettes: value(&info, layout.file_cassettes)?,
            file_hearts: value(&info, layout.file_hearts)?,
            deaths: save
                .as_ref()
                .and_then(Option::as_ref)
                .and_then(|save| value(save, layout.total_deaths)),
            starting_new_file: save.as_ref().and_then(|save| match save {
                None => Some(false),
                Some(save) => value::<i64>(save, layout.save_time).map(|time| time == 0),
            }),
            everest: None,
        })
    }

    fn object(&self, process: &Process, object: Address, size: u32) -> Option<Vec<u8>> {
        let layout = self.layout;
        let len = size - layout.size_before_object;
        let bytes = process.read_vec::<u8>(object, len as usize).ok()?;
        let type_pointer = pointer(&bytes, 0, layout.pointer_size)?;
        let type_size: u32 = process
            .read_pointer_path(type_pointer, layout.pointer_size, layout.type_size_path)
            .ok()?;
        (type_size == size).then_some(bytes)
    }

    fn string(&self, process: &Process, string: Address) -> Option<String> {
        let layout = self.layout;
        let header = self.object(process, string, layout.string_size)?;
        let len: i32 = value(&header, layout.string_length)?;
        if !(0..=512).contains(&len) {
            return None;
        }
        if len == 0 {
            return Some(String::new());
        }
        let mut buf = vec![0u16; len as usize];
        process
            .read_into_slice(string + layout.string_chars, &mut buf)
            .ok()?;
        String::from_utf16(&buf).ok()
    }
}

fn value<T: AnyBitPattern>(object: &[u8], offset: u64) -> Option<T> {
    let start = offset as usize;
    object
        .get(start..start + size_of::<T>())
        .map(bytemuck::pod_read_unaligned)
}

fn flag(object: &[u8], offset: u64) -> Option<bool> {
    Some(value::<u8>(object, offset)? != 0)
}

fn pointer(object: &[u8], offset: u64, size: PointerSize) -> Option<Address> {
    let start = offset as usize;
    object.get(start..start + size as usize).map(address)
}

fn address(bytes: &[u8]) -> Address {
    let mut value = [0; 8];
    value[..bytes.len()].copy_from_slice(bytes);
    Address::new(u64::from_le_bytes(value))
}

pub struct Everest {
    info: Address,
}

impl Everest {
    fn read(&self, process: &Process) -> Option<Values> {
        let info: everest::CoreAutoSplitterInfo = process.read(self.info).ok()?;
        let version: u8 = process.read(self.info + everest::INFO_VERSION).ok()?;
        if info.info_version == everest::UPDATING_VERSION || version == everest::UPDATING_VERSION {
            return None;
        }
        let string = |ptr| read_everest_string(process, ptr);
        let chapter = |flag| info.chapter_flags & flag != 0;
        let file = |flag| info.file_flags & flag != 0;
        Some(Values {
            chapter: info.chapter_id,
            mode: info.chapter_mode,
            level: string(info.room_name_str_ptr),
            timer_active: chapter(everest::TIMER_ACTIVE),
            chapter_started: chapter(everest::CHAPTER_STARTED),
            chapter_complete: chapter(everest::CHAPTER_COMPLETE),
            chapter_time: info.chapter_time,
            chapter_strawberries: info.chapter_strawberries,
            chapter_cassette: chapter(everest::CHAPTER_CASSETTE),
            chapter_heart: chapter(everest::CHAPTER_HEART),
            file_time: info.file_time,
            file_strawberries: info.file_strawberries,
            file_cassettes: info.file_cassettes,
            file_hearts: info.file_hearts,
            deaths: None,
            starting_new_file: Some(file(everest::STARTING_NEW_FILE)),
            everest: Some(EverestValues {
                file_golden_strawberries: info.file_golden_strawberries,
                grabbed_golden: chapter(everest::GRABBED_GOLDEN),
                level_set: string(info.level_set_str_ptr),
                chapter_sid: string(info.chapter_sid_str_ptr),
                is_debug: file(everest::IS_DEBUG),
                assist_mode: file(everest::ASSIST_MODE),
                variants_mode: file(everest::VARIANTS_MODE),
                file_active: file(everest::FILE_ACTIVE),
            }),
        })
    }
}

fn read_everest_string(process: &Process, ptr: u64) -> Option<String> {
    if ptr == 0 {
        return Some(String::new());
    }
    let len: u16 = process.read(ptr - 2).ok()?;
    if len == 0 {
        return Some(String::new());
    }
    if len > 512 {
        return None;
    }
    let mut buf = vec![0u8; len as usize];
    process.read_into_slice(ptr, &mut buf).ok()?;
    String::from_utf8(buf).ok()
}

type Scan = fn(&Process, (Address, u64)) -> Vec<Address>;

macro_rules! scan {
    ($anchor:expr) => {
        |process, range| {
            static SIGNATURE: Signature<{ pattern_len($anchor.signature) }> =
                Signature::new($anchor.signature);
            signature_ends(&SIGNATURE, process, range)
        }
    };
}

struct Runtime {
    oses: &'static [&'static str],
    layout: &'static Layout,
    scan_instance: Scan,
    scan_save_data: Scan,
}

const RUNTIMES: &[Runtime] = &[
    Runtime {
        oses: &["windows"],
        layout: &CLR32,
        scan_instance: scan!(CLR32.celeste_instance),
        scan_save_data: scan!(CLR32.save_data_instance),
    },
    Runtime {
        oses: &["linux", "macos"],
        layout: &MONO64,
        scan_instance: scan!(MONO64.celeste_instance),
        scan_save_data: scan!(MONO64.save_data_instance),
    },
];

const fn pattern_len(signature: &str) -> usize {
    (signature.len() + 1) / 3
}

fn signature_ends<const N: usize>(
    signature: &Signature<N>,
    process: &Process,
    range: (Address, u64),
) -> Vec<Address> {
    signature
        .scan_iter(process, range)
        .map(|start| start + N as u64)
        .collect()
}

fn find_instance(process: &Process, runtime: &Runtime) -> Option<(Address, u64)> {
    let layout = runtime.layout;
    let signature_len = pattern_len(layout.celeste_instance.signature) as u64;
    let references = layout.celeste_instance.references;
    let store = slice::from_ref(&layout.auto_splitter_info_store);
    scan_executable(process, |region| {
        (runtime.scan_instance)(process, region)
            .into_iter()
            .find_map(|code| {
                let slot = search(process, region, code, Side::After, references)?;
                let signature = Address::new(code.value() - signature_len);
                let info = search(process, region, signature, Side::Before, store)?.value();
                layout.has_info(process, slot, info).then_some((slot, info))
            })
    })
}

fn find_save_data(process: &Process, runtime: &Runtime) -> Option<Address> {
    let references = runtime.layout.save_data_instance.references;
    scan_executable(process, |region| {
        (runtime.scan_save_data)(process, region)
            .into_iter()
            .find_map(|code| search(process, region, code, Side::After, references))
    })
}

#[derive(Clone, Copy)]
enum Side {
    After,
    Before,
}

fn search(
    process: &Process,
    (region, size): (Address, u64),
    edge: Address,
    side: Side,
    patterns: &[Pattern],
) -> Option<Address> {
    let (start, len) = match side {
        Side::After => (edge, WINDOW.min(region.value() + size - edge.value())),
        Side::Before => {
            let len = WINDOW.min(edge.value() - region.value());
            (Address::new(edge.value() - len), len)
        }
    };
    let code = process.read_vec::<u8>(start, len as usize).ok()?;
    let found = |at: usize| {
        patterns
            .iter()
            .find_map(|pattern| match_pattern(pattern, &code[at..]))
    };
    match side {
        Side::After => (0..code.len()).find_map(found),
        Side::Before => (0..code.len()).rev().find_map(found),
    }
}

impl Layout {
    fn field(&self, slot: Address, field: u64) -> DeepPointer<2> {
        DeepPointer::new(slot, self.pointer_size, &[0, field])
    }

    fn has_info(&self, process: &Process, slot: Address, info: u64) -> bool {
        let Ok(field) = self.field(slot, info).deref_offsets(process) else {
            return false;
        };
        process
            .read_pointer(field, self.pointer_size)
            .is_ok_and(|object| !object.is_null())
    }
}

fn match_pattern(pattern: &Pattern, code: &[u8]) -> Option<Address> {
    let (before, code) = code.split_at_checked(pattern.before.len())?;
    let (value, after) = code.split_at_checked(pattern.width)?;
    let matches = |expected: &[Option<u8>], actual: &[u8]| {
        actual.len() >= expected.len()
            && iter::zip(expected, actual)
                .all(|(expected, actual)| expected.is_none_or(|expected| expected == *actual))
    };
    if !matches(pattern.before, before) || !matches(pattern.after, after) {
        return None;
    }
    Some(address(value))
}

fn scan_executable<T>(
    process: &Process,
    mut scan: impl FnMut((Address, u64)) -> Option<T>,
) -> Option<T> {
    for region in process.memory_ranges() {
        let Ok(flags) = region.flags() else {
            continue;
        };
        if !flags.contains(MemoryRangeFlags::EXECUTE) {
            continue;
        }
        let Ok(range) = region.range() else {
            continue;
        };
        if let Some(found) = scan(range) {
            return Some(found);
        }
    }
    None
}

const PROCESS_NAMES: &[&str] = &[
    "Celeste.bin.x86_64",
    "Celeste.bin.x86",
    "Celeste.exe",
    "Celeste",
];

pub async fn wait_attach() -> Process {
    retry(|| PROCESS_NAMES.iter().find_map(|&name| Process::attach(name))).await
}

#[derive(Clone, Copy, PartialEq)]
pub enum Failure {
    NotFound,
    EverestVersion(u8),
}

fn init_everest(process: &Process) -> Result<GameState, Failure> {
    let mut failure = Failure::NotFound;
    for region in process.memory_ranges() {
        let Ok(base) = region.address() else {
            continue;
        };
        let Ok(info) = process.read::<everest::CoreAutoSplitterInfo>(base) else {
            continue;
        };
        if info.magic != everest::MAGIC_BYTES {
            continue;
        }
        match info.info_version {
            everest::CURRENT_VERSION => return Ok(GameState::Everest(Everest { info: base })),
            everest::UPDATING_VERSION => {}
            version => failure = Failure::EverestVersion(version),
        }
    }
    Err(failure)
}

fn init_vanilla(process: &Process) -> Option<GameState> {
    let os = asr::get_os().ok()?;
    let runtime = RUNTIMES
        .iter()
        .find(|runtime| runtime.oses.contains(&os.as_str()))?;
    let (slot, info) = find_instance(process, runtime)?;
    let save = find_save_data(process, runtime)?;
    Some(GameState::Vanilla(Vanilla {
        layout: runtime.layout,
        instance: slot,
        info,
        save_data: save,
    }))
}

pub fn init(process: &Process) -> Result<GameState, Failure> {
    init_everest(process).or_else(|failure| init_vanilla(process).ok_or(failure))
}
