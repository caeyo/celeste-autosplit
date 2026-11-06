use asr::{future::next_tick, Process};

mod everest;
mod memory;
mod vanilla;

asr::async_main!(stable);

fn set_debug_info(values: &memory::Values) {
    asr::timer::set_variable_int("Strawberries", values.file_strawberries);
    asr::timer::set_variable_int("Chapter Strawberries", values.chapter_strawberries);
    asr::timer::set_variable_int("Cassettes", values.file_cassettes);
    asr::timer::set_variable_int("Hearts", values.file_hearts);
    asr::timer::set_variable_int("Chapter", values.chapter);
    asr::timer::set_variable_int("Mode", values.mode);
    if let Some(level) = &values.level {
        asr::timer::set_variable("Level", level);
    }
    asr::timer::set_variable("Chapter Time", &values.chapter_time.to_string());
    asr::timer::set_variable("File Time", &values.file_time.to_string());
    asr::timer::set_variable_int("Timer Active", values.timer_active as i32);
    asr::timer::set_variable_int("Chapter Started", values.chapter_started as i32);
    asr::timer::set_variable_int("Chapter Complete", values.chapter_complete as i32);
    asr::timer::set_variable_int("Chapter Cassette", values.chapter_cassette as i32);
    asr::timer::set_variable_int("Chapter Heart", values.chapter_heart as i32);
    if let Some(everest) = &values.everest {
        asr::timer::set_variable_int("Golden Strawberries", everest.file_golden_strawberries);
        asr::timer::set_variable_int("Grabbed Golden", everest.grabbed_golden as i32);
        if let Some(level_set) = &everest.level_set {
            asr::timer::set_variable("Level Set", level_set);
        }
        if let Some(sid) = &everest.chapter_sid {
            asr::timer::set_variable("Chapter SID", sid);
        }
        asr::timer::set_variable_int("Debug", everest.is_debug as i32);
        asr::timer::set_variable_int("Assist Mode", everest.assist_mode as i32);
        asr::timer::set_variable_int("Variants Mode", everest.variants_mode as i32);
        asr::timer::set_variable_int("Starting New File", everest.starting_new_file as i32);
        asr::timer::set_variable_int("File Active", everest.file_active as i32);
    }
    if let Some(deaths) = values.deaths {
        asr::timer::set_variable_int("Deaths", deaths);
    }
}

const TICKS_PER_SECOND: u32 = 120;

const INIT_ATTEMPTS: u32 = 10;

async fn init(process: &Process) -> Option<memory::GameState> {
    let mut logged = Vec::new();
    for _ in 0..INIT_ATTEMPTS {
        let failure = match memory::init(process) {
            Ok(state) => return Some(state),
            Err(failure) => failure,
        };
        if !logged.contains(&failure) {
            match failure {
                memory::Failure::NotFound => asr::print_message("Init failed, retrying"),
                memory::Failure::EverestVersion(version) => asr::print_message(&format!(
                    "Everest's auto splitter info version {version} isn't supported"
                )),
            }
            logged.push(failure);
        }
        for _ in 0..TICKS_PER_SECOND {
            next_tick().await;
        }
    }
    None
}

async fn main() {
    loop {
        asr::print_message("Waiting for Celeste");
        let process = memory::wait_attach().await;
        asr::print_message("Attached to Celeste");

        process
            .until_closes(async {
                let Some(state) = init(&process).await else {
                    asr::print_message("Init failed, detaching");
                    return;
                };
                asr::print_message("Init OK");

                loop {
                    if let Some(values) = state.read(&process) {
                        set_debug_info(&values);
                    }
                    next_tick().await;
                }
            })
            .await;
    }
}
