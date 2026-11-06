use asr::{
    settings::Map,
    time::Duration,
    timer::{self, TimerState},
    watcher::{Pair, Watcher},
};

use crate::{
    memory::Values,
    settings::{Kind, Settings, Timing, Trigger},
    TICKS_PER_SECOND,
};

const SUMMIT: i64 = 7;

pub struct Splitter {
    settings: Settings,
    loaded: String,
    ticks_since_load: u32,
    timer_state: Watcher<TimerState>,
    starting_new_file: Watcher<bool>,
    chapter_complete: Watcher<bool>,
    mode: Watcher<i32>,
    file_time: Watcher<i64>,
    room: String,
    armed: Option<Option<u64>>,
}

struct Tick<'a> {
    new_file: bool,
    complete: Pair<bool>,
    chapter: i32,
    mode: Pair<i32>,
    room: &'a str,
}

impl Splitter {
    pub fn new() -> Self {
        let mut splitter = Self {
            settings: Settings::default(),
            loaded: String::new(),
            ticks_since_load: 0,
            timer_state: Watcher::new(),
            starting_new_file: Watcher::new(),
            chapter_complete: Watcher::new(),
            mode: Watcher::new(),
            file_time: Watcher::new(),
            room: String::new(),
            armed: None,
        };
        splitter.load();
        splitter
    }

    fn load(&mut self) {
        self.ticks_since_load = 0;
        let map = Map::load();
        let loaded = format!("{:?} {:?}", map.get("schema_version"), map.get("cfg"));
        if loaded != self.loaded {
            self.settings = Settings::parse(&map);
            self.loaded = loaded;
        }
    }

    pub fn update(&mut self, values: Option<&Values>) {
        let state = timer::state();
        let reset = self
            .timer_state
            .update_infallible(state)
            .changed_to(&TimerState::NotRunning);
        self.ticks_since_load += 1;
        if reset || self.ticks_since_load >= TICKS_PER_SECOND {
            self.load();
        }
        let Some(values) = values else {
            return;
        };
        let new_file = values.starting_new_file.is_some_and(|starting| {
            let pair = self.starting_new_file.update_infallible(starting);
            pair.check(|&starting| starting)
        });
        if let Some(level) = &values.level {
            self.room.clone_from(level);
        }
        let file_time = *self.file_time.update_infallible(values.file_time);
        let tick = Tick {
            new_file,
            complete: *self
                .chapter_complete
                .update_infallible(values.chapter_complete),
            chapter: values.chapter,
            mode: *self.mode.update_infallible(values.mode),
            room: &self.room,
        };
        match state {
            TimerState::NotRunning => {
                if fires(&self.settings.start, None, &tick, &mut self.armed) {
                    timer::start();
                    set_game_time(file_time);
                }
            }
            TimerState::Running | TimerState::Paused => {
                set_game_time(file_time);
                let slot = timer::current_split_index();
                let index = slot.and_then(|index| usize::try_from(index).ok());
                let trigger = index.and_then(|index| self.settings.splits.get(index));
                if trigger.is_some_and(|trigger| fires(trigger, slot, &tick, &mut self.armed)) {
                    timer::split();
                }
            }
            _ => {}
        }
    }
}

fn set_game_time(file_time: Pair<i64>) {
    timer::set_game_time(Duration::nanoseconds(file_time.current.saturating_mul(100)));
    if file_time.changed() {
        timer::resume_game_time();
    } else {
        timer::pause_game_time();
    }
}

fn fires(
    trigger: &Trigger,
    slot: Option<u64>,
    tick: &Tick,
    armed: &mut Option<Option<u64>>,
) -> bool {
    if armed.is_some_and(|armed| armed != slot) {
        *armed = None;
    }
    match trigger.kind {
        Kind::NewFile => tick.new_file,
        Kind::ChapterComplete => chapter_complete(trigger, slot, tick, armed),
        _ => false,
    }
}

fn chapter_complete(
    trigger: &Trigger,
    slot: Option<u64>,
    tick: &Tick,
    armed: &mut Option<Option<u64>>,
) -> bool {
    let rose = tick.complete.check(|&complete| complete)
        && trigger
            .chapter
            .is_none_or(|chapter| chapter == i64::from(tick.chapter))
        && (trigger.chapter != Some(SUMMIT) || !in_credits(tick.room));
    if rose {
        *armed = Some(slot);
    }
    if armed.is_none() {
        return false;
    }
    let fired = match trigger.timing {
        Timing::ChapterComplete => rose,
        Timing::ChapterExit => tick.complete.check(|&complete| !complete),
    };
    if !fired {
        return false;
    }
    *armed = None;
    trigger
        .side
        .is_none_or(|side| side == i64::from(tick.mode.old))
}

fn in_credits(room: &str) -> bool {
    room.is_empty()
        || room
            .get(..7)
            .is_some_and(|start| start.eq_ignore_ascii_case("credits"))
}
