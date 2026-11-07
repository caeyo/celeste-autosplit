use asr::{
    settings::Map,
    time::Duration,
    timer::{self, TimerState},
    watcher::{Pair, Watcher},
};

use crate::{
    memory::Values,
    settings::{Kind, Settings, Timing, TimingMethod, Trigger, NO_CHAPTER},
    TICKS_PER_SECOND,
};

const SUMMIT: i64 = 7;

pub struct Splitter {
    settings: Settings,
    loaded: String,
    ticks_since_load: u32,
    timer_state: Watcher<TimerState>,
    starting_new_file: Watcher<bool>,
    chapter: Watcher<i32>,
    mode: Watcher<i32>,
    room: Watcher<String>,
    sid: Watcher<String>,
    chapter_started: Watcher<bool>,
    chapter_complete: Watcher<bool>,
    chapter_heart: Watcher<bool>,
    chapter_cassette: Watcher<bool>,
    file_clock: Watcher<i64>,
    chapter_clock: Watcher<i64>,
    last_chapter_time: Option<i64>,
    clocks_at_start: Clocks,
    started_by_module: bool,
    started_by_host: bool,
    start_armed: Option<Option<u64>>,
    split_armed: Option<Option<u64>>,
    logged_no_sid: bool,
}

#[derive(Clone, Copy, Default)]
struct Clocks {
    file: i64,
    chapter: i64,
}

struct Tick<'a> {
    new_file: bool,
    chapter: Pair<i32>,
    mode: Pair<i32>,
    room: &'a Pair<String>,
    sid: Option<&'a Pair<String>>,
    started: Pair<bool>,
    resuming: bool,
    complete: Pair<bool>,
    heart: Pair<bool>,
    cassette: Pair<bool>,
}

impl Splitter {
    pub fn new() -> Self {
        let mut splitter = Self {
            settings: Settings::default(),
            loaded: String::new(),
            ticks_since_load: 0,
            timer_state: Watcher::new(),
            starting_new_file: Watcher::new(),
            chapter: Watcher::new(),
            mode: Watcher::new(),
            room: Watcher::new(),
            sid: Watcher::new(),
            chapter_started: Watcher::new(),
            chapter_complete: Watcher::new(),
            chapter_heart: Watcher::new(),
            chapter_cassette: Watcher::new(),
            file_clock: Watcher::new(),
            chapter_clock: Watcher::new(),
            last_chapter_time: None,
            clocks_at_start: Clocks::default(),
            started_by_module: false,
            started_by_host: false,
            start_armed: None,
            split_armed: None,
            logged_no_sid: false,
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
        let states = *self.timer_state.update_infallible(state);
        let reset = states.changed_to(&TimerState::NotRunning);
        if states.changed_from(&TimerState::NotRunning) && !self.started_by_module {
            self.started_by_host = true;
        }
        self.started_by_module = false;
        self.ticks_since_load += 1;
        if reset || self.ticks_since_load >= TICKS_PER_SECOND {
            self.load();
        }
        let slot = timer::current_split_index();
        for armed in [&mut self.start_armed, &mut self.split_armed] {
            if armed.is_some_and(|armed| armed != slot) {
                *armed = None;
            }
        }
        let Some(values) = values else {
            return;
        };
        let new_file = values.starting_new_file.is_some_and(|starting| {
            let pair = self.starting_new_file.update_infallible(starting);
            pair.check(|&starting| starting)
        });
        let room = held(&self.room, values.level.as_ref());
        let everest = values.everest.as_ref();
        let sid = everest.map(|everest| held(&self.sid, everest.chapter_sid.as_ref()));
        if sid.is_none() && self.settings.uses_sid() && !self.logged_no_sid {
            asr::print_message("The game isn't running Everest, so triggers with a sid don't fire");
            self.logged_no_sid = true;
        }
        let started = *self
            .chapter_started
            .update_infallible(values.chapter_started);
        let chapter_time = values.chapter_time;
        let resuming = started.check(|&started| started)
            && chapter_time > 0
            && self
                .last_chapter_time
                .is_some_and(|last| chapter_time >= last);
        if i64::from(values.chapter) != NO_CHAPTER {
            self.last_chapter_time = Some(chapter_time);
        }
        let file_clock = *self.file_clock.update_infallible(values.file_time);
        let chapter_clock = *self
            .chapter_clock
            .update_infallible(self.last_chapter_time.unwrap_or_default());
        let clocks = Clocks {
            file: file_clock.current,
            chapter: chapter_clock.current,
        };
        if self.started_by_host {
            self.clocks_at_start = clocks;
            self.started_by_host = false;
        }
        let method = self.settings.timing_method;
        let clock = match method {
            TimingMethod::File | TimingMethod::FileSinceStart => file_clock,
            TimingMethod::Chapter | TimingMethod::ChapterSinceStart => chapter_clock,
        };
        let tick = Tick {
            new_file,
            chapter: *self.chapter.update_infallible(values.chapter),
            mode: *self.mode.update_infallible(values.mode),
            room: self.room.update_infallible(room),
            sid: match sid {
                Some(sid) => Some(self.sid.update_infallible(sid)),
                None => None,
            },
            started,
            resuming,
            complete: *self
                .chapter_complete
                .update_infallible(values.chapter_complete),
            heart: *self.chapter_heart.update_infallible(values.chapter_heart),
            cassette: *self
                .chapter_cassette
                .update_infallible(values.chapter_cassette),
        };
        let settings = &self.settings;
        match state {
            TimerState::NotRunning => {
                if fires(&settings.start, slot, &tick, &mut self.start_armed) {
                    self.clocks_at_start = clocks;
                    self.started_by_module = true;
                    start(clock, offset(method, clocks));
                }
            }
            TimerState::Running | TimerState::Paused | TimerState::Ended => {
                if settings.auto_reset && fires(&settings.start, slot, &tick, &mut self.start_armed)
                {
                    timer::reset();
                    self.split_armed = None;
                    self.clocks_at_start = clocks;
                    start(clock, offset(method, clocks));
                    return;
                }
                if state == TimerState::Ended {
                    return;
                }
                set_game_time(clock, offset(method, self.clocks_at_start));
                let index = slot.and_then(|index| usize::try_from(index).ok());
                let trigger = index.and_then(|index| settings.splits.get(index));
                if trigger.is_some_and(|trigger| fires(trigger, slot, &tick, &mut self.split_armed))
                {
                    timer::split();
                }
            }
            _ => {}
        }
    }
}

fn held(watcher: &Watcher<String>, value: Option<&String>) -> String {
    let last = watcher.pair.as_ref().map(|pair| &pair.current);
    value.or(last).cloned().unwrap_or_default()
}

fn offset(method: TimingMethod, at_start: Clocks) -> i64 {
    match method {
        TimingMethod::File | TimingMethod::Chapter => 0,
        TimingMethod::FileSinceStart => at_start.file,
        TimingMethod::ChapterSinceStart => at_start.chapter,
    }
}

fn start(clock: Pair<i64>, offset: i64) {
    timer::start();
    timer::set_game_time(game_time(clock.current, offset));
    pause_unless_changed(clock);
}

fn set_game_time(clock: Pair<i64>, offset: i64) {
    if clock.current > 0 || clock.unchanged() {
        timer::set_game_time(game_time(clock.current, offset));
    }
    pause_unless_changed(clock);
}

fn game_time(clock: i64, offset: i64) -> Duration {
    Duration::nanoseconds(clock.saturating_sub(offset).saturating_mul(100))
}

fn pause_unless_changed(clock: Pair<i64>) {
    if clock.changed() {
        timer::resume_game_time();
    } else {
        timer::pause_game_time();
    }
}

#[derive(Clone, Copy)]
enum When {
    Before,
    Now,
}

fn at<T>(pair: &Pair<T>, when: When) -> &T {
    match when {
        When::Before => &pair.old,
        When::Now => &pair.current,
    }
}

fn fires(
    trigger: &Trigger,
    slot: Option<u64>,
    tick: &Tick,
    armed: &mut Option<Option<u64>>,
) -> bool {
    let entered = tick.chapter.changed();
    match trigger.kind {
        Kind::Manual | Kind::Unknown => false,
        Kind::NewFile => tick.new_file,
        Kind::ChapterStart => {
            tick.started.check(|&started| started) && !tick.resuming && tick.matches(trigger)
        }
        Kind::ChapterEnter => entered && tick.changed_to(trigger, When::Now),
        Kind::ChapterExit => entered && tick.changed_to(trigger, When::Before),
        Kind::ChapterComplete => chapter_complete(trigger, slot, tick, armed),
        Kind::Checkpoint => {
            !tick.at_checkpoint(trigger, When::Before) && tick.at_checkpoint(trigger, When::Now)
        }
        Kind::Heart => tick.heart.check(|&heart| heart) && !entered && tick.matches(trigger),
        Kind::Cassette => {
            tick.cassette.check(|&cassette| cassette) && !entered && tick.matches(trigger)
        }
        Kind::RoomEnter => {
            tick.room_changed()
                && tick.room.current == trigger.room
                && !tick.resuming
                && tick.matches(trigger)
        }
        Kind::RoomExit => {
            tick.room_changed() && tick.room.old == trigger.room && tick.matches(trigger)
        }
    }
}

impl Tick<'_> {
    fn in_chapter(&self, trigger: &Trigger, when: When) -> bool {
        match &trigger.sid {
            Some(sid) => self.sid.is_some_and(|pair| at(pair, when) == sid),
            None => trigger
                .chapter
                .is_none_or(|chapter| chapter == i64::from(*at(&self.chapter, when))),
        }
    }

    fn on_side(&self, trigger: &Trigger, when: When) -> bool {
        trigger
            .side
            .is_none_or(|side| side == i64::from(*at(&self.mode, when)))
    }

    fn matches(&self, trigger: &Trigger) -> bool {
        self.in_chapter(trigger, When::Now) && self.on_side(trigger, When::Now)
    }

    fn changed_to(&self, trigger: &Trigger, when: When) -> bool {
        self.in_chapter(trigger, when)
            && (trigger.side.is_none() || self.mode.changed() && self.on_side(trigger, when))
    }

    fn room_changed(&self) -> bool {
        i64::from(self.chapter.current) != NO_CHAPTER && self.room.changed()
    }

    fn at_checkpoint(&self, trigger: &Trigger, when: When) -> bool {
        let mode = usize::try_from(*at(&self.mode, when)).ok();
        let room = mode.and_then(|mode| trigger.checkpoint_rooms.get(mode));
        trigger.chapter == Some(i64::from(*at(&self.chapter, when)))
            && self.on_side(trigger, when)
            && room.is_some_and(|room| room == at(self.room, when))
    }
}

fn chapter_complete(
    trigger: &Trigger,
    slot: Option<u64>,
    tick: &Tick,
    armed: &mut Option<Option<u64>>,
) -> bool {
    let named = trigger.chapter.is_some() || trigger.sid.is_some();
    let rose = tick.complete.check(|&complete| complete)
        && tick.in_chapter(trigger, When::Now)
        && !(named && i64::from(tick.chapter.current) == SUMMIT && in_credits(&tick.room.current));
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
