use asr::{
    future::{next_tick, retry},
    Process,
};

mod memory;

asr::async_main!(stable);

fn set_debug_info(state: &memory::GameState, process: &Process) {
    if let Some(count) = state.file_strawberries(process) {
        asr::timer::set_variable_int("Strawberries", count);
    }
    if let Some(count) = state.chapter_strawberries(process) {
        asr::timer::set_variable_int("Chapter Strawberries", count);
    }
    if let Some(count) = state.file_cassettes(process) {
        asr::timer::set_variable_int("Cassettes", count);
    }
    if let Some(count) = state.file_hearts(process) {
        asr::timer::set_variable_int("Hearts", count);
    }
    if let Some(chapter) = state.chapter(process) {
        asr::timer::set_variable_int("Chapter", chapter);
    }
    if let Some(mode) = state.mode(process) {
        asr::timer::set_variable_int("Mode", mode);
    }
    if let Some(level) = state.level(process) {
        asr::timer::set_variable("Level", &level);
    }
    if let Some(time) = state.chapter_time(process) {
        asr::timer::set_variable("Chapter Time", &time.to_string());
    }
    if let Some(time) = state.file_time(process) {
        asr::timer::set_variable("File Time", &time.to_string());
    }
    if let Some(v) = state.timer_active(process) {
        asr::timer::set_variable_int("Timer Active", v as i32);
    }
    if let Some(v) = state.chapter_started(process) {
        asr::timer::set_variable_int("Chapter Started", v as i32);
    }
    if let Some(v) = state.chapter_complete(process) {
        asr::timer::set_variable_int("Chapter Complete", v as i32);
    }
    if let Some(v) = state.chapter_cassette(process) {
        asr::timer::set_variable_int("Chapter Cassette", v as i32);
    }
    if let Some(v) = state.chapter_heart(process) {
        asr::timer::set_variable_int("Chapter Heart", v as i32);
    }
    if let Some(goldens) = state.file_golden_strawberries(process) {
        asr::timer::set_variable_int("Golden Strawberries", goldens);
    }
    if let Some(grabbed) = state.grabbed_golden(process) {
        asr::timer::set_variable_int("Grabbed Golden", grabbed as i32);
    }
    if let Some(level_set) = state.level_set(process) {
        asr::timer::set_variable("Level Set", &level_set);
    }
    if let Some(sid) = state.chapter_sid(process) {
        asr::timer::set_variable("Chapter SID", &sid);
    }
    if let Some(v) = state.is_debug(process) {
        asr::timer::set_variable_int("Debug", v as i32);
    }
    if let Some(v) = state.assist_mode(process) {
        asr::timer::set_variable_int("Assist Mode", v as i32);
    }
    if let Some(v) = state.variants_mode(process) {
        asr::timer::set_variable_int("Variants Mode", v as i32);
    }
    if let Some(v) = state.starting_new_file(process) {
        asr::timer::set_variable_int("Starting New File", v as i32);
    }
    if let Some(v) = state.file_active(process) {
        asr::timer::set_variable_int("File Active", v as i32);
    }
    if let Some(Some(save)) = state.save(process) {
        asr::timer::set_variable("Save Time", &save.time.to_string());
        asr::timer::set_variable_int("Save Debug Mode", save.debug_mode as i32);
    }
}

async fn main() {
    loop {
        asr::print_message("Waiting for Celeste");
        let process = memory::wait_attach().await;
        asr::print_message("Attached to Celeste");

        process
            .until_closes(async {
                let mut state = retry(|| memory::init(&process)).await;
                asr::print_message("Init OK");

                loop {
                    state.update(&process);
                    set_debug_info(&state, &process);
                    next_tick().await;
                }
            })
            .await;
    }
}
