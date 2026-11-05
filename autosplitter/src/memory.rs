use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};

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

struct Anchor {
    signature: &'static str,
    scan: fn(&Process, (Address, u64)) -> Vec<Address>,
    references: &'static [&'static str],
    reference_window: u64,
}

macro_rules! anchor {
    ($root:ident) => {
        Anchor {
            signature: $root::SIGNATURE,
            scan: |process, range| {
                static SIGNATURE: Signature<{ pattern_len($root::SIGNATURE) }> =
                    Signature::new($root::SIGNATURE);
                signature_ends(&SIGNATURE, process, range)
            },
            references: $root::REFERENCES,
            reference_window: $root::REFERENCE_WINDOW,
        }
    };
}

struct Runtime {
    oses: &'static [&'static str],
    ptr_size: PointerSize,
    instance: Anchor,
    info_store: &'static str,
    info_store_window: u64,
    save_data: Anchor,
    state: fn(Slot, u64, Slot) -> GameState,
}

macro_rules! runtime {
    ($layout:ident, $oses:expr) => {{
        use $layout::{celeste_instance as instance, save_data_instance as save_data};
        Runtime {
            oses: $oses,
            ptr_size: pointer_size($layout::POINTER_SIZE),
            instance: anchor!(instance),
            info_store: instance::AUTO_SPLITTER_INFO_STORE,
            info_store_window: instance::AUTO_SPLITTER_INFO_STORE_WINDOW,
            save_data: anchor!(save_data),
            state: |slot, info, save| GameState::Vanilla {
                ptr_size: slot.ptr_size,
                string_len: $layout::STRING_LENGTH,
                string_chars: $layout::STRING_CHARS,
                chapter_time: slot.info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_TIME),
                file_time: slot.info_field(info, instance::AUTO_SPLITTER_INFO_FILE_TIME),
                level: slot.info_field(info, instance::AUTO_SPLITTER_INFO_LEVEL),
                chapter: slot.info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER),
                mode: slot.info_field(info, instance::AUTO_SPLITTER_INFO_MODE),
                chapter_strawberries: slot
                    .info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_STRAWBERRIES),
                file_strawberries: slot
                    .info_field(info, instance::AUTO_SPLITTER_INFO_FILE_STRAWBERRIES),
                file_cassettes: slot.info_field(info, instance::AUTO_SPLITTER_INFO_FILE_CASSETTES),
                file_hearts: slot.info_field(info, instance::AUTO_SPLITTER_INFO_FILE_HEARTS),
                timer_active: slot.info_field(info, instance::AUTO_SPLITTER_INFO_TIMER_ACTIVE),
                chapter_started: slot
                    .info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_STARTED),
                chapter_complete: slot
                    .info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_COMPLETE),
                chapter_cassette: slot
                    .info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_CASSETTE),
                chapter_heart: slot.info_field(info, instance::AUTO_SPLITTER_INFO_CHAPTER_HEART),
                total_deaths: save.field(save_data::TOTAL_DEATHS),
            },
        }
    }};
}

const RUNTIMES: &[Runtime] = &[
    runtime!(clr32, &["windows"]),
    runtime!(mono64, &["linux", "macos"]),
];

const fn pointer_size(bytes: u64) -> PointerSize {
    match bytes {
        4 => PointerSize::Bit32,
        8 => PointerSize::Bit64,
        _ => panic!("pointers take 4 or 8 bytes"),
    }
}

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
    let anchor = &runtime.instance;
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (anchor.scan)(process, (start, size))
            .into_iter()
            .find_map(|code| {
                let slot = anchor.read_slot(process, runtime.ptr_size, code, end)?;
                let info = runtime.read_info_store(process, code, start)?;
                runtime
                    .has_info(process, slot, info)
                    .then_some((slot, info))
            })
    })
}

fn find_save_data(process: &Process, runtime: &Runtime) -> Option<Slot> {
    let anchor = &runtime.save_data;
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (anchor.scan)(process, (start, size))
            .into_iter()
            .find_map(|code| anchor.read_slot(process, runtime.ptr_size, code, end))
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

impl Runtime {
    fn read_info_store(&self, process: &Process, code: Address, range: Address) -> Option<u64> {
        let signature = code.value() - pattern_len(self.instance.signature) as u64;
        let len = self.info_store_window.min(signature - range.value());
        let code = process
            .read_vec::<u8>(Address::new(signature - len), len as usize)
            .ok()?;
        (0..code.len())
            .rev()
            .find_map(|start| match_pattern(self.info_store, &code[start..]))
            .map(|offset| offset.value())
    }

    fn has_info(&self, process: &Process, slot: Slot, info: u64) -> bool {
        let Ok(field) = slot.field(info).deref_offsets(process) else {
            return false;
        };
        process
            .read_pointer(field, self.ptr_size)
            .is_ok_and(|object| !object.is_null())
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
    Some((runtime.state)(slot, info, save))
}
