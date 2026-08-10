use asr::{
    deep_pointer::DeepPointer,
    future::{next_tick, retry},
    signature::Signature,
    Address, MemoryRangeFlags, PointerSize, Process,
};

asr::async_main!(stable);

#[derive(Copy, Clone)]
enum Platform {
    WindowsClr32,
    LinuxMono64,
    MacMono64,
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
    file_strawberries: u64,
    instance_size: InstanceSize,
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

struct GameState {
    strawberries: DeepPointer<3>,
}

static WINDOWS_CLR32_SIG: Signature<17> =
    Signature::new("68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35");

static MONO64_SIG: Signature<21> =
    Signature::new("be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00");

const PROCESS_NAMES: &[&str] = &[
    "Celeste.bin.x86_64",
    "Celeste.bin.x86",
    "Celeste.exe",
    "Celeste",
];

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

const WINDOWS_CLR32: Layout = Layout {
    ptr_size: PointerSize::Bit32,
    field_step: 4,
    string_len: 4,
    string_chars: 8,
    autosplitter_from_title: 0x1c,
    file_strawberries: 0x24,
    instance_size: InstanceSize::MethodTablePlus4,
};

const MONO64: Layout = Layout {
    ptr_size: PointerSize::Bit64,
    field_step: 8,
    string_len: 0x10,
    string_chars: 0x14,
    autosplitter_from_title: 0x30,
    file_strawberries: 0x40,
    instance_size: InstanceSize::KlassPlus1c,
};

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
            if self.read_string_at(process, instance, offs).as_deref() == Some("Celeste") {
                return Some(offs);
            }
            if offs < self.field_step {
                break;
            }
            offs -= self.field_step;
        }
        None
    }

    fn strawberries_ptr(&self, static_slot: u64, title_offs: u64) -> Option<DeepPointer<3>> {
        let path = [
            0,
            title_offs + self.autosplitter_from_title,
            self.file_strawberries,
        ];
        Some(DeepPointer::new(static_slot, self.ptr_size, &path))
    }

    fn read_string_at(&self, process: &Process, instance: u64, field_offs: u64) -> Option<String> {
        let string_obj = self.read_ptr(process, instance + field_offs)?;
        if string_obj == 0 {
            return None;
        }
        let len: i32 = process.read(string_obj + self.string_len).ok()?;
        if !(1..=512).contains(&len) {
            return None;
        }
        let mut buf = vec![0u16; len as usize];
        process
            .read_into_slice(string_obj + self.string_chars, &mut buf)
            .ok()?;
        String::from_utf16(&buf).ok()
    }
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

fn detect_platform() -> Option<Platform> {
    match asr::get_os().ok()?.as_str() {
        "windows" => Some(Platform::WindowsClr32),
        "linux" => Some(Platform::LinuxMono64),
        "macos" => Some(Platform::MacMono64),
        _ => None,
    }
}

async fn wait_attach() -> Process {
    retry(|| PROCESS_NAMES.iter().find_map(|&name| Process::attach(name))).await
}

fn init(process: &Process, layout: &Layout, sig: SigMatch) -> Option<GameState> {
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
    let strawberries = layout.strawberries_ptr(static_slot, title_offs)?;
    Some(GameState { strawberries })
}

async fn main() {
    loop {
        asr::print_message("Waiting for Celeste");
        let process = wait_attach().await;
        asr::print_message("Attached to Celeste");

        process
            .until_closes(async {
                let platform = retry(detect_platform).await;
                let layout = platform.layout();
                let sig = retry(|| find_signature(&process, platform.signatures())).await;
                let state = retry(|| init(&process, layout, sig)).await;
                asr::print_message("Init OK");

                loop {
                    if let Ok(count) = state.strawberries.deref::<i32>(&process) {
                        asr::timer::set_variable_int("Strawberries", count);
                    }
                    next_tick().await;
                }
            })
            .await;
    }
}
