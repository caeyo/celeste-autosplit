use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};

use crate::vanilla::{Anchor, Layout, CLR32, MONO64};

#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/everest.rs"]
mod everest;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/clr32.rs"]
mod clr32;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/mono64.rs"]
mod mono64;

const fn same_str(a: &str, b: &str) -> bool {
    let (a, b) = (a.as_bytes(), b.as_bytes());
    if a.len() != b.len() {
        return false;
    }
    let mut i = 0;
    while i < a.len() {
        if a[i] != b[i] {
            return false;
        }
        i += 1;
    }
    true
}

const fn same_strs(a: &[&str], b: &[&str]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut i = 0;
    while i < a.len() {
        if !same_str(a[i], b[i]) {
            return false;
        }
        i += 1;
    }
    true
}

macro_rules! assert_same_layout {
    ($new:expr, $old:ident) => {
        const _: () = assert!($new.pointer_size as u64 == $old::POINTER_SIZE);
        const _: () = assert!($new.string_length == $old::STRING_LENGTH);
        const _: () = assert!($new.string_chars == $old::STRING_CHARS);
        const _: () = assert!(same_str(
            $new.celeste_instance.signature,
            $old::celeste_instance::SIGNATURE
        ));
        const _: () = assert!(same_strs(
            $new.celeste_instance.references,
            $old::celeste_instance::REFERENCES
        ));
        const _: () = assert!(
            $new.celeste_instance.reference_window == $old::celeste_instance::REFERENCE_WINDOW
        );
        const _: () = assert!(same_str(
            $new.auto_splitter_info_store,
            $old::celeste_instance::AUTO_SPLITTER_INFO_STORE
        ));
        const _: () = assert!(
            $new.auto_splitter_info_store_window
                == $old::celeste_instance::AUTO_SPLITTER_INFO_STORE_WINDOW
        );
        const _: () = assert!(same_str(
            $new.save_data_instance.signature,
            $old::save_data_instance::SIGNATURE
        ));
        const _: () = assert!(same_strs(
            $new.save_data_instance.references,
            $old::save_data_instance::REFERENCES
        ));
        const _: () = assert!(
            $new.save_data_instance.reference_window == $old::save_data_instance::REFERENCE_WINDOW
        );
        const _: () = assert!($new.chapter == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER);
        const _: () = assert!($new.mode == $old::celeste_instance::AUTO_SPLITTER_INFO_MODE);
        const _: () = assert!($new.level == $old::celeste_instance::AUTO_SPLITTER_INFO_LEVEL);
        const _: () =
            assert!($new.timer_active == $old::celeste_instance::AUTO_SPLITTER_INFO_TIMER_ACTIVE);
        const _: () = assert!(
            $new.chapter_started == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_STARTED
        );
        const _: () = assert!(
            $new.chapter_complete == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_COMPLETE
        );
        const _: () =
            assert!($new.chapter_time == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_TIME);
        const _: () = assert!(
            $new.chapter_strawberries
                == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_STRAWBERRIES
        );
        const _: () = assert!(
            $new.chapter_cassette == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_CASSETTE
        );
        const _: () =
            assert!($new.chapter_heart == $old::celeste_instance::AUTO_SPLITTER_INFO_CHAPTER_HEART);
        const _: () =
            assert!($new.file_time == $old::celeste_instance::AUTO_SPLITTER_INFO_FILE_TIME);
        const _: () = assert!(
            $new.file_strawberries == $old::celeste_instance::AUTO_SPLITTER_INFO_FILE_STRAWBERRIES
        );
        const _: () = assert!(
            $new.file_cassettes == $old::celeste_instance::AUTO_SPLITTER_INFO_FILE_CASSETTES
        );
        const _: () =
            assert!($new.file_hearts == $old::celeste_instance::AUTO_SPLITTER_INFO_FILE_HEARTS);
        const _: () = assert!($new.total_deaths == $old::save_data_instance::TOTAL_DEATHS);
    };
}

assert_same_layout!(CLR32, clr32);
assert_same_layout!(MONO64, mono64);

#[expect(
    clippy::large_enum_variant,
    reason = "only one exists per attach, so its size doesn't matter"
)]
pub enum GameState {
    Vanilla {
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
    },
    Everest {
        info: Address,
    },
}

impl GameState {
    pub fn chapter_time(&self, process: &Process) -> Option<i64> {
        match self {
            Self::Vanilla { chapter_time, .. } => chapter_time.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::CHAPTER_TIME).ok(),
        }
    }

    pub fn file_time(&self, process: &Process) -> Option<i64> {
        match self {
            Self::Vanilla { file_time, .. } => file_time.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::FILE_TIME).ok(),
        }
    }

    pub fn level(&self, process: &Process) -> Option<String> {
        match self {
            Self::Vanilla {
                ptr_size,
                string_len,
                string_chars,
                level,
                ..
            } => {
                let addr = level.deref_offsets(process).ok()?;
                let obj = process.read_pointer(addr, *ptr_size).ok()?.value();
                read_dotnet_string(process, obj, *string_len, *string_chars)
            }
            Self::Everest { info } => {
                let ptr = process.read(*info + everest::ROOM_NAME_STR_PTR).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn chapter(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { chapter, .. } => chapter.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::CHAPTER_ID).ok(),
        }
    }

    pub fn mode(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { mode, .. } => mode.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::CHAPTER_MODE).ok(),
        }
    }

    pub fn chapter_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla {
                chapter_strawberries,
                ..
            } => chapter_strawberries.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::CHAPTER_STRAWBERRIES).ok(),
        }
    }

    pub fn file_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla {
                file_strawberries, ..
            } => file_strawberries.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::FILE_STRAWBERRIES).ok(),
        }
    }

    pub fn file_cassettes(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { file_cassettes, .. } => file_cassettes.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::FILE_CASSETTES).ok(),
        }
    }

    pub fn file_hearts(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { file_hearts, .. } => file_hearts.deref(process).ok(),
            Self::Everest { info } => process.read(*info + everest::FILE_HEARTS).ok(),
        }
    }

    pub fn timer_active(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { timer_active, .. } => {
                Some(timer_active.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::TIMER_ACTIVE != 0)
            }
        }
    }

    pub fn chapter_started(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_started, ..
            } => Some(chapter_started.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::CHAPTER_STARTED != 0)
            }
        }
    }

    pub fn chapter_complete(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_complete, ..
            } => Some(chapter_complete.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::CHAPTER_COMPLETE != 0)
            }
        }
    }

    pub fn chapter_cassette(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_cassette, ..
            } => Some(chapter_cassette.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::CHAPTER_CASSETTE != 0)
            }
        }
    }

    pub fn chapter_heart(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { chapter_heart, .. } => {
                Some(chapter_heart.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::CHAPTER_HEART != 0)
            }
        }
    }

    pub fn total_deaths(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { total_deaths, .. } => total_deaths.deref(process).ok(),
            Self::Everest { .. } => None,
        }
    }

    pub fn file_golden_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => process.read(*info + everest::FILE_GOLDEN_STRAWBERRIES).ok(),
        }
    }

    pub fn grabbed_golden(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::CHAPTER_FLAGS).ok()?;
                Some(flags & everest::GRABBED_GOLDEN != 0)
            }
        }
    }

    pub fn level_set(&self, process: &Process) -> Option<String> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let ptr = process.read(*info + everest::LEVEL_SET_STR_PTR).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn chapter_sid(&self, process: &Process) -> Option<String> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let ptr = process.read(*info + everest::CHAPTER_SID_STR_PTR).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn is_debug(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::FILE_FLAGS).ok()?;
                Some(flags & everest::IS_DEBUG != 0)
            }
        }
    }

    pub fn assist_mode(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::FILE_FLAGS).ok()?;
                Some(flags & everest::ASSIST_MODE != 0)
            }
        }
    }

    pub fn variants_mode(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::FILE_FLAGS).ok()?;
                Some(flags & everest::VARIANTS_MODE != 0)
            }
        }
    }

    pub fn starting_new_file(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::FILE_FLAGS).ok()?;
                Some(flags & everest::STARTING_NEW_FILE != 0)
            }
        }
    }

    pub fn file_active(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + everest::FILE_FLAGS).ok()?;
                Some(flags & everest::FILE_ACTIVE != 0)
            }
        }
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
        GameState::Vanilla {
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
        }
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

fn init_everest(process: &Process) -> Option<GameState> {
    for region in process.memory_ranges() {
        let Ok(base) = region.address() else {
            continue;
        };
        let mut magic = [0u8; everest::MAGIC_BYTES.len()];
        if process.read_into_slice(base, &mut magic).is_err() {
            continue;
        }
        if magic != everest::MAGIC_BYTES {
            continue;
        }
        let Ok(version) = process.read::<u8>(base + everest::INFO_VERSION) else {
            continue;
        };
        if version < everest::CURRENT_VERSION {
            continue;
        }
        return Some(GameState::Everest { info: base });
    }
    None
}

pub fn init(process: &Process) -> Option<GameState> {
    if let Some(state) = init_everest(process) {
        return Some(state);
    }
    let os = asr::get_os().ok()?;
    let runtime = RUNTIMES
        .iter()
        .find(|runtime| runtime.oses.contains(&os.as_str()))?;
    let (slot, info) = find_instance(process, runtime)?;
    let save = find_save_data(process, runtime)?;
    Some(runtime.layout.state(slot, info, save))
}
