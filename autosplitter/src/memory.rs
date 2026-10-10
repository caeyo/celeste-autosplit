use asr::{
    deep_pointer::DeepPointer,
    future::retry,
    signature::Signature,
    Address, MemoryRangeFlags, PointerSize, Process,
};

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
            Self::Vanilla { chapter_strawberries, .. } => chapter_strawberries.deref(process).ok(),
            Self::Everest { info } => process.read(*info + EVEREST_CHAPTER_STRAWBERRIES_OFF).ok(),
        }
    }

    pub fn file_strawberries(&self, process: &Process) -> Option<i32> {
        match self {
            Self::Vanilla { file_strawberries, .. } => file_strawberries.deref(process).ok(),
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
            Self::Vanilla { timer_active, .. } => Some(timer_active.deref::<u8>(process).ok()? != 0),
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_TIMER_ACTIVE != 0)
            }
        }
    }

    pub fn chapter_started(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { chapter_started, .. } => {
                Some(chapter_started.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_STARTED != 0)
            }
        }
    }

    pub fn chapter_complete(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { chapter_complete, .. } => {
                Some(chapter_complete.deref::<u8>(process).ok()? != 0)
            }
            Self::Everest { info } => {
                let flags: u32 = process.read(*info + EVEREST_CHAPTER_FLAGS_OFF).ok()?;
                Some(flags & EVEREST_CHAPTER_COMPLETE != 0)
            }
        }
    }

    pub fn chapter_cassette(&self, process: &Process) -> Option<bool> {
        match self {
            Self::Vanilla { chapter_cassette, .. } => {
                Some(chapter_cassette.deref::<u8>(process).ok()? != 0)
            }
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

enum InstanceSize {
    MethodTablePlus4,
    KlassPlus1c,
}

struct Layout {
    ptr_size: PointerSize,
    field_step: u64,
    string_len: u64,
    string_chars: u64,
    autosplitter_from_title: u64,
    instance_size: InstanceSize,
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

const WINDOWS_CLR32: Layout = Layout {
    ptr_size: PointerSize::Bit32,
    field_step: 4,
    string_len: 4,
    string_chars: 8,
    autosplitter_from_title: 0x1c,
    instance_size: InstanceSize::MethodTablePlus4,
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

/// 16-byte MonoObject header, then the same fields as Windows
/// in declaration order. Ints are 8-aligned rather than packed.
const MONO64: Layout = Layout {
    ptr_size: PointerSize::Bit64,
    field_step: 8,
    string_len: 0x10,
    string_chars: 0x14,
    autosplitter_from_title: 0x30,
    instance_size: InstanceSize::KlassPlus1c,
    chapter_time: 0x10,
    file_time: 0x18,
    level: 0x20,
    chapter: 0x28,
    mode: 0x30,
    chapter_strawberries: 0x38,
    file_strawberries: 0x40,
    file_cassettes: 0x48,
    file_hearts: 0x50,
    timer_active: 0x54,
    chapter_started: 0x55,
    chapter_complete: 0x56,
    chapter_cassette: 0x57,
    chapter_heart: 0x58,
};

impl Layout {
    fn read_ptr(&self, process: &Process, address: u64) -> Option<u64> {
        match self.ptr_size {
            PointerSize::Bit32 => process.read::<u32>(address).ok().map(u64::from),
            PointerSize::Bit64 => process.read(address).ok(),
            PointerSize::Bit16 => None,
        }
    }

    fn instance_size(&self, process: &Process, instance: u64) -> Option<u32> {
        let vtable = self.read_ptr(process, instance)?;
        match self.instance_size {
            InstanceSize::MethodTablePlus4 => process.read(vtable + 4).ok(),
            InstanceSize::KlassPlus1c => {
                let klass: u64 = process.read(vtable).ok()?;
                process.read(klass + 0x1c).ok()
            }
        }
    }

    fn title_offs(&self, process: &Process, instance: u64) -> Option<u64> {
        let type_size = self.instance_size(process, instance)?;
        if u64::from(type_size) < self.field_step {
            return None;
        }

        let mut offs = u64::from(type_size) - self.field_step;
        loop {
            if read_dotnet_string(
                process,
                self.read_ptr(process, instance + offs)?,
                self.string_len,
                self.string_chars,
            )
            .as_deref()
                == Some("Celeste")
            {
                return Some(offs);
            }
            if offs < self.field_step {
                break;
            }
            offs -= self.field_step;
        }
        None
    }

    fn field_ptr(&self, static_slot: u64, title_offs: u64, field: u64) -> DeepPointer<3> {
        DeepPointer::new(
            static_slot,
            self.ptr_size,
            &[0, title_offs + self.autosplitter_from_title, field],
        )
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

struct SignatureHook {
    static_slot_disp: u64,
    static_slot_u64: bool,
    scan: fn(&Process, (Address, u64)) -> Option<Address>,
}

#[derive(Copy, Clone)]
struct SigMatch {
    address: Address,
    static_slot_disp: u64,
    static_slot_u64: bool,
}

static WINDOWS_CLR32_SIG: Signature<17> =
    Signature::new("68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35");

static MONO64_SIG: Signature<21> =
    Signature::new("be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00");

fn scan_windows_clr32(process: &Process, range: (Address, u64)) -> Option<Address> {
    WINDOWS_CLR32_SIG.scan_process_range(process, range)
}

fn scan_mono64(process: &Process, range: (Address, u64)) -> Option<Address> {
    MONO64_SIG.scan_process_range(process, range)
}

const WINDOWS_CLR32_HOOK: SignatureHook = SignatureHook {
    static_slot_disp: 99,
    static_slot_u64: false,
    scan: scan_windows_clr32,
};

const LINUX_MONO64_HOOK: SignatureHook = SignatureHook {
    static_slot_disp: 132,
    static_slot_u64: false,
    scan: scan_mono64,
};

const MACOS_MONO64_HOOK: SignatureHook = SignatureHook {
    static_slot_disp: 167,
    static_slot_u64: true,
    scan: scan_mono64,
};

const WINDOWS_CLR32_SIGNATURES: &[SignatureHook] = &[WINDOWS_CLR32_HOOK];
const LINUX_MONO64_SIGNATURES: &[SignatureHook] = &[LINUX_MONO64_HOOK];
const MACOS_MONO64_SIGNATURES: &[SignatureHook] = &[MACOS_MONO64_HOOK];

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

fn find_signature(process: &Process, hooks: &[SignatureHook]) -> Option<SigMatch> {
    for hook in hooks {
        if let Some(address) = scan_executable(process, |range| (hook.scan)(process, range)) {
            return Some(SigMatch {
                address,
                static_slot_disp: hook.static_slot_disp,
                static_slot_u64: hook.static_slot_u64,
            });
        }
    }
    None
}

#[derive(Copy, Clone)]
enum Platform {
    WindowsClr32,
    LinuxMono64,
    MacMono64,
}

impl Platform {
    const fn layout(self) -> &'static Layout {
        match self {
            Self::WindowsClr32 => &WINDOWS_CLR32,
            Self::LinuxMono64 | Self::MacMono64 => &MONO64,
        }
    }

    const fn signatures(self) -> &'static [SignatureHook] {
        match self {
            Self::WindowsClr32 => WINDOWS_CLR32_SIGNATURES,
            Self::LinuxMono64 => LINUX_MONO64_SIGNATURES,
            Self::MacMono64 => MACOS_MONO64_SIGNATURES,
        }
    }
}

fn detect_platform() -> Option<Platform> {
    match asr::get_os().ok()?.as_str() {
        "windows" => Some(Platform::WindowsClr32),
        "linux" => Some(Platform::LinuxMono64),
        "macos" => Some(Platform::MacMono64),
        _ => None,
    }
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

fn init_vanilla(process: &Process, layout: &Layout, sig: SigMatch) -> Option<GameState> {
    let static_slot = if sig.static_slot_u64 {
        process.read(sig.address + sig.static_slot_disp).ok()?
    } else {
        u64::from(process.read::<u32>(sig.address + sig.static_slot_disp).ok()?)
    };
    let instance = layout.read_ptr(process, static_slot)?;
    if instance == 0 {
        return None;
    }
    let title_offs = layout.title_offs(process, instance)?;
    Some(GameState::Vanilla {
        ptr_size: layout.ptr_size,
        string_len: layout.string_len,
        string_chars: layout.string_chars,
        chapter_time: layout.field_ptr(static_slot, title_offs, layout.chapter_time),
        file_time: layout.field_ptr(static_slot, title_offs, layout.file_time),
        level: layout.field_ptr(static_slot, title_offs, layout.level),
        chapter: layout.field_ptr(static_slot, title_offs, layout.chapter),
        mode: layout.field_ptr(static_slot, title_offs, layout.mode),
        chapter_strawberries: layout.field_ptr(static_slot, title_offs, layout.chapter_strawberries),
        file_strawberries: layout.field_ptr(static_slot, title_offs, layout.file_strawberries),
        file_cassettes: layout.field_ptr(static_slot, title_offs, layout.file_cassettes),
        file_hearts: layout.field_ptr(static_slot, title_offs, layout.file_hearts),
        timer_active: layout.field_ptr(static_slot, title_offs, layout.timer_active),
        chapter_started: layout.field_ptr(static_slot, title_offs, layout.chapter_started),
        chapter_complete: layout.field_ptr(static_slot, title_offs, layout.chapter_complete),
        chapter_cassette: layout.field_ptr(static_slot, title_offs, layout.chapter_cassette),
        chapter_heart: layout.field_ptr(static_slot, title_offs, layout.chapter_heart),
    })
}

pub fn init(process: &Process) -> Option<GameState> {
    if let Some(state) = init_everest(process) {
        return Some(state);
    }
    let platform = detect_platform()?;
    let layout = platform.layout();
    let sig = find_signature(process, platform.signatures())?;
    init_vanilla(process, layout, sig)
}
