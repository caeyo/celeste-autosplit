use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};

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
    },
    Everest {
        info: Address,
    },
}

const EVEREST_MAGIC: [u8; 20] = *b"EVERESTAUTOSPLIT\xf0\xf1\xf2\xf3";
const EVEREST_INFO_MIN_VERSION: u8 = 3;
const EVEREST_INFO_VERSION_OFF: u64 = 0x17;
const EVEREST_LEVEL_SET_OFF: u64 = 0x20;
const EVEREST_CHAPTER_SID_OFF: u64 = 0x28;
const EVEREST_CHAPTER_OFF: u64 = 0x30;
const EVEREST_MODE_OFF: u64 = 0x34;
const EVEREST_LEVEL_OFF: u64 = 0x38;
const EVEREST_CHAPTER_TIME_OFF: u64 = 0x40;
const EVEREST_CHAPTER_STRAWBERRIES_OFF: u64 = 0x48;
const EVEREST_CHAPTER_FLAGS_OFF: u64 = 0x4c;
const EVEREST_FILE_TIME_OFF: u64 = 0x50;
const EVEREST_FILE_STRAWBERRIES_OFF: u64 = 0x58;
const EVEREST_FILE_GOLDENS_OFF: u64 = 0x5c;
const EVEREST_FILE_CASSETTES_OFF: u64 = 0x60;
const EVEREST_FILE_HEARTS_OFF: u64 = 0x64;
const EVEREST_FILE_FLAGS_OFF: u64 = 0x68;
const EVEREST_CHAPTER_STARTED: u32 = 1 << 0;
const EVEREST_CHAPTER_COMPLETE: u32 = 1 << 1;
const EVEREST_CHAPTER_CASSETTE: u32 = 1 << 2;
const EVEREST_CHAPTER_HEART: u32 = 1 << 3;
const EVEREST_GRABBED_GOLDEN: u32 = 1 << 4;
const EVEREST_TIMER_ACTIVE: u32 = 1 << 31;
const EVEREST_IS_DEBUG: u32 = 1 << 0;
const EVEREST_ASSIST_MODE: u32 = 1 << 1;
const EVEREST_VARIANTS_MODE: u32 = 1 << 2;
const EVEREST_STARTING_NEW_FILE: u32 = 1 << 30;
const EVEREST_FILE_ACTIVE: u32 = 1 << 31;

impl GameState {
    pub fn chapter_time(&self, process: &Process) -> Option<i64> {
        match self {
            Self::Vanilla { chapter_time, .. } => chapter_time.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_CHAPTER_TIME_OFF).ok(),
        }
    }

    pub fn file_time(&self, process: &Process) -> Option<i64> {
        match self {
            Self::Vanilla { file_time, .. } => file_time.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_FILE_TIME_OFF).ok(),
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
                let ptr = process.read(*info + EVEREST_LEVEL_OFF).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn chapter(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { chapter, .. } => chapter.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_CHAPTER_OFF).ok(),
        }
    }

    pub fn mode(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { mode, .. } => mode.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_MODE_OFF).ok(),
        }
    }

    pub fn chapter_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla {
                chapter_strawberries,
                ..
            } => chapter_strawberries.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_CHAPTER_STRAWBERRIES_OFF).ok(),
        }
    }

    pub fn file_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla {
                file_strawberries, ..
            } => file_strawberries.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_FILE_STRAWBERRIES_OFF).ok(),
        }
    }

    pub fn file_cassettes(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { file_cassettes, .. } => file_cassettes.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_FILE_CASSETTES_OFF).ok(),
        }
    }

    pub fn file_hearts(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { file_hearts, .. } => file_hearts.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_FILE_HEARTS_OFF).ok(),
        }
    }

    pub fn timer_active(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { timer_active, .. } => {
                Some(timer_active.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_TIMER_ACTIVE != 0)
            }
        }
    }

    pub fn chapter_started(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_started, ..
            } => Some(chapter_started.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_STARTED != 0)
            }
        }
    }

    pub fn chapter_complete(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_complete, ..
            } => Some(chapter_complete.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_COMPLETE != 0)
            }
        }
    }

    pub fn chapter_cassette(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla {
                chapter_cassette, ..
            } => Some(chapter_cassette.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_CASSETTE != 0)
            }
        }
    }

    pub fn chapter_heart(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { chapter_heart, .. } => {
                Some(chapter_heart.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_HEART != 0)
            }
        }
    }

    pub fn file_golden_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => process.read(*info + EVEREST_FILE_GOLDENS_OFF).ok(),
        }
    }

    pub fn grabbed_golden(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_GRABBED_GOLDEN != 0)
            }
        }
    }

    pub fn level_set(&self, process: &Process) -> Option<String> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let ptr = process.read(*info + EVEREST_LEVEL_SET_OFF).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn chapter_sid(&self, process: &Process) -> Option<String> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let ptr = process.read(*info + EVEREST_CHAPTER_SID_OFF).ok()?;
                read_everest_string(process, ptr)
            }
        }
    }

    pub fn is_debug(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_FILE_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_IS_DEBUG != 0)
            }
        }
    }

    pub fn assist_mode(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_FILE_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_ASSIST_MODE != 0)
            }
        }
    }

    pub fn variants_mode(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_FILE_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_VARIANTS_MODE != 0)
            }
        }
    }

    pub fn starting_new_file(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_FILE_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_STARTING_NEW_FILE != 0)
            }
        }
    }

    pub fn file_active(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { .. } => None,
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_FILE_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_FILE_ACTIVE != 0)
            }
        }
    }
}

/// Where a runtime puts a string's length and characters, and each field of `AutoSplitterInfo`,
/// from the start of the object.
struct Layout {
    ptr_size: PointerSize,
    string_len: u64,
    string_chars: u64,
    chapter_time: u64,
    file_time: u64,
    level: u64,
    chapter: u64,
    mode: u64,
    chapter_strawberries: u64,
    file_strawberries: u64,
    file_cassettes: u64,
    file_hearts: u64,
    timer_active: u64,
    chapter_started: u64,
    chapter_complete: u64,
    chapter_cassette: u64,
    chapter_heart: u64,
}

/// 32-bit .NET Framework, on Windows.
const CLR32: Layout = Layout {
    ptr_size: PointerSize::Bit32,
    string_len: 4,
    string_chars: 8,
    chapter_time: 0x04,
    file_time: 0x0c,
    level: 0x14,
    chapter: 0x18,
    mode: 0x1c,
    chapter_strawberries: 0x20,
    file_strawberries: 0x24,
    file_cassettes: 0x28,
    file_hearts: 0x2c,
    timer_active: 0x30,
    chapter_started: 0x31,
    chapter_complete: 0x32,
    chapter_cassette: 0x33,
    chapter_heart: 0x34,
};

/// 64-bit Mono, on Linux and macOS: a 16-byte object header, then the fields that hold
/// references, then the others in declaration order, each aligned to its size.
const MONO64: Layout = Layout {
    ptr_size: PointerSize::Bit64,
    string_len: 0x10,
    string_chars: 0x14,
    chapter_time: 0x28,
    file_time: 0x38,
    level: 0x10,
    chapter: 0x18,
    mode: 0x1c,
    chapter_strawberries: 0x30,
    file_strawberries: 0x40,
    file_cassettes: 0x44,
    file_hearts: 0x48,
    timer_active: 0x20,
    chapter_started: 0x21,
    chapter_complete: 0x22,
    chapter_cassette: 0x34,
    chapter_heart: 0x35,
};

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

/// A vanilla flavour of the game: how to find `Celeste.Instance`, and where its fields are.
struct Flavour {
    /// As `asr::get_os` names it.
    os: &'static str,
    /// Where each match of the flavour's signature ends, in an executable range.
    signature: fn(&Process, (Address, u64)) -> Vec<Address>,
    /// Patterns for the store into `Celeste.Instance` after the signature: hex bytes, with `??`
    /// for any byte, and `SS` for the static slot's address, 4 or 8 of them in a row.
    stores: &'static [&'static str],
    layout: &'static Layout,
    /// `Engine.Title`, which holds "Celeste".
    title: u64,
    /// `Celeste.AutoSplitterInfo`.
    auto_splitter_info: u64,
}

// Celeste's constructor passes the game and window sizes (1920x1080, 960x540) to Engine's,
// then stores itself in Celeste.Instance. The signatures match the size arguments: pushed on
// Windows, in registers on Mono. Then the store:
// - On Windows, the slot's address goes to the GC's write barrier: lea edx, [slot]; call.
// - On Mono, it's loaded into rax and stored through: mov eax or rax, slot; mov [rax], r15.
//   The 4-byte form is used when the address fits, which on Linux depends on where the heap
//   is, so both forms are listed. Where the heap is also changes the length of other
//   instructions between the signature and the store, so the store is searched for rather
//   than expected at a fixed distance.
// The layout probe checks the same signatures and store patterns against the game's compiled
// code, from its own copy in tools/layout-probe/Manifest.cs.

static CLR32_SIGNATURE: Signature<17> =
    Signature::new("68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35");

const CLR32_STORES: &[&str] = &["8d 15 SS SS SS SS e8"];

static MONO64_SIGNATURE: Signature<21> =
    Signature::new("be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00");

const MONO64_STORES: &[&str] = &[
    "b8 SS SS SS SS 4c 89 38",
    "48 b8 SS SS SS SS SS SS SS SS 4c 89 38",
];

fn clr32_signature(process: &Process, range: (Address, u64)) -> Vec<Address> {
    signature_ends(&CLR32_SIGNATURE, process, range)
}

fn mono64_signature(process: &Process, range: (Address, u64)) -> Vec<Address> {
    signature_ends(&MONO64_SIGNATURE, process, range)
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

/// In the order they're tried. Both Windows flavours have the same signature and store, so
/// only their title offsets tell them apart: XNA's and FNA's `Game` classes, which `Engine`
/// derives from, have different fields.
const FLAVOURS: &[Flavour] = &[
    // windows-xna
    Flavour {
        os: "windows",
        signature: clr32_signature,
        stores: CLR32_STORES,
        layout: &CLR32,
        title: 0x90,
        auto_splitter_info: 0xac,
    },
    // windows-fna
    Flavour {
        os: "windows",
        signature: clr32_signature,
        stores: CLR32_STORES,
        layout: &CLR32,
        title: 0x84,
        auto_splitter_info: 0xa0,
    },
    // linux
    Flavour {
        os: "linux",
        signature: mono64_signature,
        stores: MONO64_STORES,
        layout: &MONO64,
        title: 0xe8,
        auto_splitter_info: 0x118,
    },
    // macos
    Flavour {
        os: "macos",
        signature: mono64_signature,
        stores: MONO64_STORES,
        layout: &MONO64,
        title: 0xe8,
        auto_splitter_info: 0x118,
    },
];

/// How much of the code after a signature is searched for the store. Today's stores start 80
/// to 144 bytes after their signatures, and the layout probe checks that they end within this.
const STORE_WINDOW: u64 = 256;

impl Flavour {
    /// Finds the static slot of `Celeste.Instance`. Each match of the signature is tried in
    /// turn, and its slot is only accepted if the object in it has the title "Celeste".
    fn find_slot(&self, process: &Process) -> Option<Address> {
        scan_executable(process, |(start, size)| {
            let end = start.value() + size;
            (self.signature)(process, (start, size))
                .into_iter()
                .find_map(|code| {
                    // Only within the range, which is all mapped.
                    let len = STORE_WINDOW.min(end - code.value());
                    let slot = self.read_store(process, code, len)?;
                    self.has_title(process, slot).then_some(slot)
                })
        })
    }

    /// The slot's address, from the earliest match of any store pattern in the code. Earliest,
    /// not the first pattern listed, because a 4-byte pattern can also match inside an 8-byte
    /// store, when the address's fourth byte happens to be the 4-byte form's opcode.
    fn read_store(&self, process: &Process, code: Address, len: u64) -> Option<Address> {
        let mut buf = [0; STORE_WINDOW as usize];
        let code_bytes = &mut buf[..len as usize];
        process.read_into_buf(code, code_bytes).ok()?;
        (0..code_bytes.len()).find_map(|start| {
            self.stores
                .iter()
                .find_map(|store| match_store(store, &code_bytes[start..]))
        })
    }

    fn has_title(&self, process: &Process, slot: Address) -> bool {
        let layout = self.layout;
        let Ok(instance) = process.read_pointer(slot, layout.ptr_size) else {
            return false;
        };
        if instance.is_null() {
            return false;
        }
        let Ok(title) = process.read_pointer(instance + self.title, layout.ptr_size) else {
            return false;
        };
        read_dotnet_string(
            process,
            title.value(),
            layout.string_len,
            layout.string_chars,
        )
        .is_some_and(|title| title == "Celeste")
    }
}

/// The slot's address in the store, if the store pattern matches at the start of the code.
fn match_store(store: &str, code: &[u8]) -> Option<Address> {
    let mut slot = 0;
    let mut slot_bytes = 0;
    for (i, byte) in store.split(' ').enumerate() {
        let actual = *code.get(i)?;
        match byte {
            "SS" => {
                slot |= u64::from(actual) << (8 * slot_bytes);
                slot_bytes += 1;
            }
            "??" => {}
            _ => {
                if u8::from_str_radix(byte, 16).ok()? != actual {
                    return None;
                }
            }
        }
    }
    Some(Address::new(slot))
}

fn scan_executable(
    process: &Process,
    mut scan: impl FnMut((Address, u64)) -> Option<Address>,
) -> Option<Address> {
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
        let mut magic = [0u8; 20];
        if process.read_into_slice(base, &mut magic).is_err() {
            continue;
        }
        if magic != EVEREST_MAGIC {
            continue;
        }
        let Ok(version) = process.read::<u8>(base + EVEREST_INFO_VERSION_OFF) else {
            continue;
        };
        if version < EVEREST_INFO_MIN_VERSION {
            continue;
        }
        return Some(GameState::Everest { info: base });
    }
    None
}

fn init_vanilla(process: &Process, flavour: &Flavour) -> Option<GameState> {
    let static_slot = flavour.find_slot(process)?;
    let layout = flavour.layout;
    let field = |offset| {
        DeepPointer::new(
            static_slot,
            layout.ptr_size,
            &[0, flavour.auto_splitter_info, offset],
        )
    };
    Some(GameState::Vanilla {
        ptr_size: layout.ptr_size,
        string_len: layout.string_len,
        string_chars: layout.string_chars,
        chapter_time: field(layout.chapter_time),
        file_time: field(layout.file_time),
        level: field(layout.level),
        chapter: field(layout.chapter),
        mode: field(layout.mode),
        chapter_strawberries: field(layout.chapter_strawberries),
        file_strawberries: field(layout.file_strawberries),
        file_cassettes: field(layout.file_cassettes),
        file_hearts: field(layout.file_hearts),
        timer_active: field(layout.timer_active),
        chapter_started: field(layout.chapter_started),
        chapter_complete: field(layout.chapter_complete),
        chapter_cassette: field(layout.chapter_cassette),
        chapter_heart: field(layout.chapter_heart),
    })
}

pub fn init(process: &Process) -> Option<GameState> {
    if let Some(state) = init_everest(process) {
        return Some(state);
    }
    let os = asr::get_os().ok()?;
    FLAVOURS
        .iter()
        .filter(|flavour| flavour.os == os.as_str())
        .find_map(|flavour| init_vanilla(process, flavour))
}
