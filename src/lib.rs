use asr::{
    deep_pointer::DeepPointer,
    file_format::{elf, pe},
    future::{next_tick, retry},
    Address, MemoryRangeFlags, PointerSize, Process,
    signature::Signature,
};

asr::async_main!(stable);

static OPENGL14: Signature<17> =
    Signature::new("68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35");

static LINUX: Signature<21> =
    Signature::new("be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00");

const PROCESS_NAMES: &[&str] = &[
    "Celeste.bin.x86_64",
    "Celeste.bin.x86",
    "Celeste.exe",
    "Celeste",
];

#[derive(Copy, Clone)]
enum Platform {
    WindowsClr32,
    LinuxMono64,
}

/// JIT signature and offset from the match to the `Celeste.Instance` static slot.
struct SignatureHook {
    static_slot_disp: u64,
    scan: fn(&Process, (Address, u64)) -> Option<Address>,
}

#[derive(Copy, Clone)]
struct SigMatch {
    address: Address,
    static_slot_disp: u64,
}

/// Runtime layout: strings, Title-relative field offsets, pointer width.
struct Layout {
    ptr: PointerSize,
    field_step: u64,
    string_len: u64,
    string_chars: u64,
    autosplitter_from_title: u64,
    file_strawberries: u64,
    instance_size: InstanceSize,
}

enum InstanceSize {
    MethodTablePlus4,
    KlassPlus1c,
}

const OPENGL14_HOOK: SignatureHook = SignatureHook {
    static_slot_disp: 99,
    scan: scan_opengl14,
};

const LINUX_HOOK: SignatureHook = SignatureHook {
    static_slot_disp: 132,
    scan: scan_linux,
};

const WINDOWS_SIGNATURES: &[SignatureHook] = &[OPENGL14_HOOK];

const LINUX_SIGNATURES: &[SignatureHook] = &[LINUX_HOOK];

const WINDOWS_CLR32: Layout = Layout {
    ptr: PointerSize::Bit32,
    field_step: 4,
    string_len: 4,
    string_chars: 8,
    autosplitter_from_title: 0x1c,
    file_strawberries: 0x24,
    instance_size: InstanceSize::MethodTablePlus4,
};

const LINUX_MONO64: Layout = Layout {
    ptr: PointerSize::Bit64,
    field_step: 8,
    string_len: 0x10,
    string_chars: 0x14,
    autosplitter_from_title: 0x30,
    file_strawberries: 0x40,
    instance_size: InstanceSize::KlassPlus1c,
};

struct GameState {
    strawberries: DeepPointer<3>,
}

impl Platform {
    const fn layout(self) -> &'static Layout {
        match self {
            Self::WindowsClr32 => &WINDOWS_CLR32,
            Self::LinuxMono64 => &LINUX_MONO64,
        }
    }

    const fn signatures(self) -> &'static [SignatureHook] {
        match self {
            Self::WindowsClr32 => WINDOWS_SIGNATURES,
            Self::LinuxMono64 => LINUX_SIGNATURES,
        }
    }
}

impl Layout {
    fn read_ptr(&self, process: &Process, address: u64) -> Option<u64> {
        match self.ptr {
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

    /// Scan backward for `Engine.Title == "Celeste"`, same strategy as upstream.
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
        Some(match self.ptr {
            PointerSize::Bit32 => DeepPointer::new_32bit(static_slot, &path),
            PointerSize::Bit64 => DeepPointer::new_64bit(static_slot, &path),
            PointerSize::Bit16 => return None,
        })
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

fn scan_opengl14(process: &Process, range: (Address, u64)) -> Option<Address> {
    OPENGL14.scan_process_range(process, range)
}

fn scan_linux(process: &Process, range: (Address, u64)) -> Option<Address> {
    LINUX.scan_process_range(process, range)
}

fn find_signature(process: &Process, hooks: &[SignatureHook]) -> Option<SigMatch> {
    for hook in hooks {
        if let Some(address) = scan_executable(process, |range| (hook.scan)(process, range)) {
            return Some(SigMatch {
                address,
                static_slot_disp: hook.static_slot_disp,
            });
        }
    }
    None
}

async fn wait_attach() -> Process {
    retry(|| PROCESS_NAMES.iter().find_map(|&name| Process::attach(name))).await
}

fn detect_platform(process: &Process) -> Option<Platform> {
    let (main_module, _) = process.get_main_module_range().ok()?;

    if elf::pointer_size(process, main_module) == Some(PointerSize::Bit64) {
        return Some(Platform::LinuxMono64);
    }

    if pe::MachineType::read(process, main_module).and_then(|m| m.pointer_size())
        == Some(PointerSize::Bit32)
    {
        return Some(Platform::WindowsClr32);
    }

    None
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

fn init(
    process: &Process,
    layout: &Layout,
    sig: SigMatch,
) -> Option<GameState> {
    let static_slot: u32 = process.read(sig.address + sig.static_slot_disp).ok()?;
    let static_slot = u64::from(static_slot);
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
                let platform = retry(|| detect_platform(&process)).await;
                let layout = platform.layout();
                let sig = retry(|| find_signature(&process, platform.signatures())).await;
                let state = retry(|| init(&process, layout, sig)).await;
                asr::print_message("Init OK");

                loop {
                    if let Ok(count) = state.strawberries.deref::<i32>(&process) {
                        asr::timer::set_variable("Strawberries", &count.to_string());
                    }
                    next_tick().await;
                }
            })
            .await;
    }
}
