use asr::{
    deep_pointer::DeepPointer,
    future::{next_tick, retry},
    Address, MemoryRangeFlags, Process,
    signature::Signature,
};

asr::async_main!(stable);

static OPENGL14: Signature<17> =
    Signature::new("68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35");

const OPENGL14_DISP: u64 = 99;
const OPENGL14_FIELD_OFFS: u32 = 0x84;

struct GameState {
    strawberries: DeepPointer<3>,
}

async fn main() {
    loop {
        let process = Process::wait_attach("Celeste.exe").await;
        asr::print_message("Attached to Celeste.exe");

        process
            .until_closes(async {
                let match_addr = retry(|| find_signature(&process)).await;
                let state = retry(|| init_from_match(&process, match_addr)).await;
                asr::print_message("OpenGL14 init OK");

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

/// First hit in JIT `PAGE_EXECUTE` regions
fn find_signature(process: &Process) -> Option<Address> {
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
        if let Some(found) = OPENGL14.scan_process_range(process, range) {
            return Some(found);
        }
    }
    None
}

/// Upstream: `AutoDeref.Single` then wait until `Celeste.Instance` is non-null.
fn init_from_match(process: &Process, match_addr: Address) -> Option<GameState> {
    let ptr: u32 = process.read(match_addr + OPENGL14_DISP).ok()?;
    let instance: u32 = process.read(ptr).ok()?;
    if instance == 0 {
        return None;
    }
    let field_offs = celeste_field_offs(process, instance).unwrap_or(OPENGL14_FIELD_OFFS);
    Some(GameState {
        strawberries: DeepPointer::new_32bit(ptr, &[0, field_offs as u64 + 0x1c, 0x24]),
    })
}

fn celeste_field_offs(process: &Process, instance: u32) -> Option<u32> {
    let method_table: u32 = process.read(instance).ok()?;
    let type_size: u32 = process.read(method_table + 4).ok()?;
    let mut offs = type_size.saturating_sub(4);
    loop {
        if read_field_string(process, instance, offs).as_deref() == Some("Celeste") {
            return Some(offs);
        }
        if offs < 4 {
            break;
        }
        offs -= 4;
    }
    None
}

fn read_field_string(process: &Process, instance: u32, field_offs: u32) -> Option<String> {
    let string_obj: u32 = process.read(instance + field_offs).ok()?;
    read_dotnet_string(process, string_obj)
}

fn read_dotnet_string(process: &Process, string_obj: u32) -> Option<String> {
    if string_obj == 0 {
        return None;
    }
    let len: i32 = process.read(string_obj + 4).ok()?;
    if !(1..=512).contains(&len) {
        return None;
    }
    let mut buf = vec![0u16; len as usize];
    process.read_into_slice(string_obj + 8, &mut buf).ok()?;
    String::from_utf16(&buf).ok()
}
