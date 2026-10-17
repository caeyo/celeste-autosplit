use asr::{
    deep_pointer::DeepPointer, future::retry, signature::Signature, Address, MemoryRangeFlags,
    PointerSize, Process,
};

// The memory layouts, in layouts/ at the repository's root: each vanilla flavour's is generated
// by the probe, and Everest's is transcribed from its source. rustfmt leaves them as they're
// written, and they can hold more than this reads.
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/everest.rs"]
mod everest;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/linux.rs"]
mod linux;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/macos.rs"]
mod macos;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/windows-fna.rs"]
mod windows_fna;
#[rustfmt::skip]
#[allow(dead_code)]
#[path = "../../layouts/windows-xna.rs"]
mod windows_xna;

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
        save: DeepPointer<3>,
        save_time: DeepPointer<3>,
        save_debug_mode: DeepPointer<3>,
    },
    Everest {
        info: Address,
    },
}

impl GameState {
    pub fn save_time(&self, process: &Process) -> Option<Option<i64>> {
        match self {
            Self::Vanilla {
                save, save_time, ..
            } => {
                if save.deref_offsets(process).ok()?.is_null() {
                    return Some(None);
                }
                Some(Some(save_time.deref(process).ok()?))
            }
            Self::Everest { .. } => None,
        }
    }

    pub fn save_debug_mode(&self, process: &Process) -> Option<Option<bool>> {
        match self {
            Self::Vanilla {
                save,
                save_debug_mode,
                ..
            } => {
                if save.deref_offsets(process).ok()?.is_null() {
                    return Some(None);
                }
                Some(Some(save_debug_mode.deref::<u8>(process).ok()? != 0))
            }
            Self::Everest { .. } => None,
        }
    }

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

/// A vanilla flavour of the game, from its layout: how to find each root's static slot, and how
/// to read the game through them.
struct Flavour {
    /// As `asr::get_os` names it.
    os: &'static str,
    ptr_size: PointerSize,
    string_len: u64,
    string_chars: u64,
    /// `Celeste.Instance`, found from `Celeste`'s constructor. Flavours can share its signature.
    instance: Anchor,
    /// The offsets of `Engine.Title`, which holds "Celeste", from the object in the slot.
    title: [u64; 1],
    save_data: Anchor,
    /// The game's state, read through the slots of `Celeste.Instance` and `SaveData.Instance`.
    state: fn(Slot, Slot) -> GameState,
}

struct Anchor {
    /// Hex bytes, with `??` for any byte.
    signature: &'static str,
    /// Where each match of the signature ends, in an executable range.
    scan: fn(&Process, (Address, u64)) -> Vec<Address>,
    /// Patterns for a reference to the slot after the signature: hex bytes, with `??` for any
    /// byte, and `SS` for the slot's address, 4 or 8 of them in a row.
    references: &'static [&'static str],
    /// How many bytes after the signature are searched for the reference.
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

/// A vanilla flavour, from its layout's module.
macro_rules! flavour {
    ($layout:ident) => {{
        use $layout::{celeste_instance as instance, save_data_instance as save_data};
        Flavour {
            os: $layout::OS,
            ptr_size: pointer_size($layout::POINTER_SIZE),
            string_len: $layout::STRING_LENGTH,
            string_chars: $layout::STRING_CHARS,
            instance: anchor!(instance),
            title: instance::TITLE,
            save_data: anchor!(save_data),
            state: |slot, save_slot| GameState::Vanilla {
                ptr_size: slot.ptr_size,
                string_len: $layout::STRING_LENGTH,
                string_chars: $layout::STRING_CHARS,
                chapter_time: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_TIME),
                file_time: slot.field(instance::AUTO_SPLITTER_INFO_FILE_TIME),
                level: slot.field(instance::AUTO_SPLITTER_INFO_LEVEL),
                chapter: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER),
                mode: slot.field(instance::AUTO_SPLITTER_INFO_MODE),
                chapter_strawberries: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_STRAWBERRIES),
                file_strawberries: slot.field(instance::AUTO_SPLITTER_INFO_FILE_STRAWBERRIES),
                file_cassettes: slot.field(instance::AUTO_SPLITTER_INFO_FILE_CASSETTES),
                file_hearts: slot.field(instance::AUTO_SPLITTER_INFO_FILE_HEARTS),
                timer_active: slot.field(instance::AUTO_SPLITTER_INFO_TIMER_ACTIVE),
                chapter_started: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_STARTED),
                chapter_complete: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_COMPLETE),
                chapter_cassette: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_CASSETTE),
                chapter_heart: slot.field(instance::AUTO_SPLITTER_INFO_CHAPTER_HEART),
                save: save_slot.field([0]),
                save_time: save_slot.field(save_data::TIME),
                save_debug_mode: save_slot.field(save_data::DEBUG_MODE),
            },
        }
    }};
}

/// In the order they're tried. Both Windows flavours have the same anchors, so only their title
/// offsets tell them apart: XNA's and FNA's `Game` classes, which `Engine` derives from, have
/// different fields.
const FLAVOURS: &[Flavour] = &[
    flavour!(windows_xna),
    flavour!(windows_fna),
    flavour!(linux),
    flavour!(macos),
];

const fn pointer_size(bytes: u64) -> PointerSize {
    match bytes {
        4 => PointerSize::Bit32,
        8 => PointerSize::Bit64,
        _ => panic!("pointers take 4 or 8 bytes"),
    }
}

/// How many bytes a signature has: two hex digits each, separated by spaces.
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

/// A root's static slot, which holds a reference to the object that each of its paths starts
/// from.
#[derive(Clone, Copy)]
struct Slot {
    address: Address,
    ptr_size: PointerSize,
}

impl Slot {
    /// A field's pointer path, from the slot through the field's offsets. The build fails unless
    /// the `DeepPointer` has room for them all, since it would silently drop the rest.
    /// `cargo check` and clippy don't evaluate this.
    fn field<const N: usize, const CAP: usize>(self, offsets: [u64; N]) -> DeepPointer<CAP> {
        const { assert!(N < CAP, "a path is too long for its DeepPointer") };
        // The first offset, 0, reads the object's address from the slot.
        let mut path = [0; CAP];
        path[1..=N].copy_from_slice(&offsets);
        DeepPointer::new(self.address, self.ptr_size, &path[..=N])
    }
}

/// Finds the static slot of `Celeste.Instance`, and which of the flavours the game is. They must
/// all have the same signature, and there must be at least one. Each match of the signature is
/// tried in turn, with each flavour's reference patterns, and a flavour's slot is only accepted if
/// the object in it has the title "Celeste" at the flavour's offsets.
fn find_instance(
    process: &Process,
    flavours: &[&'static Flavour],
) -> Option<(&'static Flavour, Slot)> {
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (flavours[0].instance.scan)(process, (start, size))
            .into_iter()
            .find_map(|code| {
                flavours.iter().find_map(|&flavour| {
                    let slot = flavour.instance.read_slot(process, flavour, code, end)?;
                    flavour.has_title(process, slot).then_some((flavour, slot))
                })
            })
    })
}

impl Anchor {
    /// The slot, from the earliest match of any reference pattern in the code. Earliest, not the
    /// first pattern listed, because a pattern with a 4-byte address can also match inside one with
    /// an 8-byte address, when the address's fourth byte happens to be the 4-byte form's opcode.
    fn read_slot(
        &self,
        process: &Process,
        flavour: &Flavour,
        code: Address,
        end: u64,
    ) -> Option<Slot> {
        // Only within the range, which is all mapped.
        let len = self.reference_window.min(end - code.value());
        let code = process.read_vec::<u8>(code, len as usize).ok()?;
        let address = (0..code.len()).find_map(|start| {
            self.references
                .iter()
                .find_map(|reference| match_reference(reference, &code[start..]))
        })?;
        Some(Slot {
            address,
            ptr_size: flavour.ptr_size,
        })
    }
}

impl Flavour {
    fn has_title(&self, process: &Process, slot: Slot) -> bool {
        let title: DeepPointer<2> = slot.field(self.title);
        let Ok(title) = title.deref_offsets(process) else {
            return false;
        };
        let Ok(title) = process.read_pointer(title, self.ptr_size) else {
            return false;
        };
        read_dotnet_string(process, title.value(), self.string_len, self.string_chars)
            .is_some_and(|title| title == "Celeste")
    }
}

fn find_save_data(process: &Process, flavour: &Flavour) -> Option<Slot> {
    let anchor = &flavour.save_data;
    scan_executable(process, |(start, size)| {
        let end = start.value() + size;
        (anchor.scan)(process, (start, size))
            .into_iter()
            .find_map(|code| anchor.read_slot(process, flavour, code, end))
    })
}

/// The slot's address in the reference, if the reference pattern matches at the start of the code.
fn match_reference(reference: &str, code: &[u8]) -> Option<Address> {
    let mut slot = 0;
    let mut slot_bytes = 0;
    for (i, byte) in reference.split(' ').enumerate() {
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
    // Flavours with the same signature, like XNA and FNA, share a scan.
    let mut groups: Vec<Vec<&'static Flavour>> = Vec::new();
    for flavour in FLAVOURS.iter().filter(|flavour| flavour.os == os.as_str()) {
        match groups
            .iter_mut()
            .find(|group| group[0].instance.signature == flavour.instance.signature)
        {
            Some(group) => group.push(flavour),
            None => groups.push(vec![flavour]),
        }
    }
    let (flavour, slot) = groups
        .iter()
        .find_map(|group| find_instance(process, group))?;
    Some((flavour.state)(slot, find_save_data(process, flavour)?))
}
