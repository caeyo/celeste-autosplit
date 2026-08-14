use asr::future::{next_tick, retry};

mod memory;

asr::async_main!(stable);

async fn main() {
    loop {
        asr::print_message("Waiting for Celeste");
        let process = memory::wait_attach().await;
        asr::print_message("Attached to Celeste");

        process
            .until_closes(async {
                let state = retry(|| memory::init(&process)).await;
                asr::print_message("Init OK");

                loop {
                    if let Some(count) = state.strawberries(&process) {
                        asr::timer::set_variable_int("Strawberries", count);
                    }
                    next_tick().await;
                }
            })
            .await;
    }
}
