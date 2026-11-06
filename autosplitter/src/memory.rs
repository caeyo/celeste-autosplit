use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};

use crate::{
    everest,
    vanilla::{Anchor, Layout, CLR32, MONO64},
};

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
    pub starting_new_file: bool,
    pub file_active: bool,
}

#[expect(
    clippy::large_enum_variant,
    reason = "only one exists per attach, so its size doesn't matter"
)]
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
    ptr_size: PointerSize,
    string_len: u64,
    string_chars: u64,
    chapter_time: DeepPointer<3>,
    file_time: DeepPointer<3>,
    level: DeepPointer<3>,
    chapter: DeepPointer<3>,
    mode: DeepPointer<3>,
    chapter_strawberries: DeepPointer<3>,
    file_strawberries: DeepPointer<3>,
    file_cassettes: DeepPointer<3>,
    file_hearts: DeepPointer<3>,
    timer_active: DeepPointer<3>,
    chapter_started: DeepPointer<3>,
    chapter_complete: DeepPointer<3>,
    chapter_cassette: DeepPointer<3>,
    chapter_heart: DeepPointer<3>,
    total_deaths: DeepPointer<2>,
}

impl Vanilla {
    fn read(&self, process: &Process) -> Option<Values> {
        let flag = |pointer: &DeepPointer<3>| Some(pointer.deref::<u8>(process).ok()? != 0);
        let level = self.level.deref_offsets(process).ok()?;
        let level = process.read_pointer(level, self.ptr_size).ok()?.value();
        Some(Values {
            chapter: self.chapter.deref(process).ok()?,
            mode: self.mode.deref(process).ok()?,
            level: read_dotnet_string(process, level, self.string_len, self.string_chars),
            timer_active: flag(&self.timer_active)?,
            chapter_started: flag(&self.chapter_started)?,
            chapter_complete: flag(&self.chapter_complete)?,
            chapter_time: self.chapter_time.deref(process).ok()?,
            chapter_strawberries: self.chapter_strawberries.deref(process).ok()?,
            chapter_cassette: flag(&self.chapter_cassette)?,
            chapter_heart: flag(&self.chapter_heart)?,
            file_time: self.file_time.deref(process).ok()?,
            file_strawberries: self.file_strawberries.deref(process).ok()?,
            file_cassettes: self.file_cassettes.deref(process).ok()?,
            file_hearts: self.file_hearts.deref(process).ok()?,
            deaths: self.total_deaths.deref(process).ok(),
            everest: None,
        })
    }
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
            everest: Some(EverestValues {
                file_golden_strawberries: info.file_golden_strawberries,
                grabbed_golden: chapter(everest::GRABBED_GOLDEN),
                level_set: string(info.level_set_str_ptr),
                chapter_sid: string(info.chapter_sid_str_ptr),
                is_debug: file(everest::IS_DEBUG),
                assist_mode: file(everest::ASSIST_MODE),
                variants_mode: file(everest::VARIANTS_MODE),
                starting_new_file: file(everest::STARTING_NEW_FILE),
                file_active: file(everest::FILE_ACTIVE),
            }),
        })
    }
}

fn read_dotnet_string(
    process: &Process,
    string_obj: u64,
    string_len: u64,
    string_chars: u64,
) -> Option<String> {
    if string_obj == 0 {
        return None;
    }
    let len: i32 = process.read(string_obj + string_len).ok()?;
    if !(0..=512).contains(&len) {
        return None;
    }
    if len == 0 {
        return Some(String::new());
    }
    let mut buf = vec![0u16; len as usize];
    process
        .read_into_slice(string_obj + string_chars, &mut buf)
        .ok()?;
    String::from_utf16(&buf).ok()
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

#[derive(Clone, Copy)]
struct Slot {
    address: Address,
    ptr_size: PointerSize,
}

impl Slot {
    fn field(self, field: u64) -> DeepPointer<2> {
        DeepPointer::new(self.address, self.ptr_size, &[0, field])
    }

    fn info_field(self, info: u64, field: u64) -> DeepPointer<3> {
        DeepPointer::new(self.address, self.ptr_size, &[0, info, field])
    }
}

fn find_instance(process: &Process, runtime: &Runtime) -> Option<(Slot, u64)> {
    let layout = runtime.layout;
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (runtime.scan_instance)(process, (start, size))
            .into_iter()
            .find_map(|code| {
                let slot =
                    layout
                        .celeste_instance
                        .read_slot(process, layout.pointer_size, code, end)?;
                let info = layout.read_info_store(process, code, start)?;
                layout.has_info(process, slot, info).then_some((slot, info))
            })
    })
}

fn find_save_data(process: &Process, runtime: &Runtime) -> Option<Slot> {
    let layout = runtime.layout;
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (runtime.scan_save_data)(process, (start, size))
            .into_iter()
            .find_map(|code| {
                layout
                    .save_data_instance
                    .read_slot(process, layout.pointer_size, code, end)
            })
    })
}

impl Anchor {
    fn read_slot(
        &self,
        process: &Process,
        ptr_size: PointerSize,
        code: Address,
        end: u64,
    ) -> Option<Slot> {
        let len = self.reference_window.min(end - code.value());
        let code = process.read_vec::<u8>(code, len as usize).ok()?;
        let address = (0..code.len()).find_map(|start| {
            self.references
                .iter()
                .find_map(|reference| match_pattern(reference, &code[start..]))
        })?;
        Some(Slot { address, ptr_size })
    }
}

impl Layout {
    fn read_info_store(&self, process: &Process, code: Address, range: Address) -> Option<u64> {
        let signature = code.value() - pattern_len(self.celeste_instance.signature) as u64;
        let len = self
            .auto_splitter_info_store_window
            .min(signature - range.value());
        let code = process
            .read_vec::<u8>(Address::new(signature - len), len as usize)
            .ok()?;
        (0..code.len())
            .rev()
            .find_map(|start| match_pattern(self.auto_splitter_info_store, &code[start..]))
            .map(|offset| offset.value())
    }

    fn has_info(&self, process: &Process, slot: Slot, info: u64) -> bool {
        let Ok(field) = slot.field(info).deref_offsets(process) else {
            return false;
        };
        process
            .read_pointer(field, self.pointer_size)
            .is_ok_and(|object| !object.is_null())
    }

    fn state(&self, slot: Slot, info: u64, save: Slot) -> GameState {
        GameState::Vanilla(Vanilla {
            ptr_size: slot.ptr_size,
            string_len: self.string_length,
            string_chars: self.string_chars,
            chapter_time: slot.info_field(info, self.chapter_time),
            file_time: slot.info_field(info, self.file_time),
            level: slot.info_field(info, self.level),
            chapter: slot.info_field(info, self.chapter),
            mode: slot.info_field(info, self.mode),
            chapter_strawberries: slot.info_field(info, self.chapter_strawberries),
            file_strawberries: slot.info_field(info, self.file_strawberries),
            file_cassettes: slot.info_field(info, self.file_cassettes),
            file_hearts: slot.info_field(info, self.file_hearts),
            timer_active: slot.info_field(info, self.timer_active),
            chapter_started: slot.info_field(info, self.chapter_started),
            chapter_complete: slot.info_field(info, self.chapter_complete),
            chapter_cassette: slot.info_field(info, self.chapter_cassette),
            chapter_heart: slot.info_field(info, self.chapter_heart),
            total_deaths: save.field(self.total_deaths),
        })
    }
}

fn match_pattern(pattern: &str, code: &[u8]) -> Option<Address> {
    let mut value = 0;
    let mut value_bytes = 0;
    for (i, byte) in pattern.split(' ').enumerate() {
        let actual = *code.get(i)?;
        match byte {
            "SS" => {
                value |= u64::from(actual) << (8 * value_bytes);
                value_bytes += 1;
            }
            "??" => {}
            _ => {
                if u8::from_str_radix(byte, 16).ok()? != actual {
                    return None;
                }
            }
        }
    }
    Some(Address::new(value))
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
    Some(runtime.layout.state(slot, info, save))
}

pub fn init(process: &Process) -> Result<GameState, Failure> {
    init_everest(process).or_else(|failure| init_vanilla(process).ok_or(failure))
}
