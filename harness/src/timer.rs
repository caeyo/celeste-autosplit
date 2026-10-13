//! The timer that the auto splitter controls: livesplit-core's own `Timer`, behind the runtime's
//! `Timer` trait, as in livesplit-core's own auto splitting. The variables the auto splitter
//! sets and everything it logs are also recorded, for the live check to compare and print.

use std::{
    collections::BTreeMap,
    fmt,
    sync::{Arc, Mutex, MutexGuard},
};

use livesplit_auto_splitting::{LogLevel, Timer as AutoSplitTimer, TimerState, time};
use livesplit_core::{Run, Segment, Timer, TimerPhase};

/// Shared by the runtime, which owns a clone, and the harness.
#[derive(Clone)]
pub struct HarnessTimer(Arc<Mutex<Recorded>>);

struct Recorded {
    timer: Timer,
    variables: BTreeMap<String, String>,
    log: Vec<String>,
}

impl HarnessTimer {
    /// A timer for a run with this many segments.
    pub fn new(segments: usize) -> Self {
        let mut run = Run::new();
        run.set_game_name("Celeste");
        run.set_category_name("Harness");
        for i in 1..=segments {
            run.push_segment(Segment::new(format!("Segment {i}")));
        }
        let timer = Timer::new(run).expect("a run with segments");
        Self(Arc::new(Mutex::new(Recorded {
            timer,
            variables: BTreeMap::new(),
            log: Vec::new(),
        })))
    }

    fn lock(&self) -> MutexGuard<'_, Recorded> {
        self.0.lock().unwrap()
    }

    pub fn clear_variables(&self) {
        self.lock().variables.clear();
    }

    /// Each variable the auto splitter set since they were cleared, with its last value.
    pub fn variables(&self) -> BTreeMap<String, String> {
        self.lock().variables.clone()
    }

    /// What the auto splitter and the runtime logged, in order.
    pub fn log(&self) -> Vec<String> {
        self.lock().log.clone()
    }
}

impl AutoSplitTimer for HarnessTimer {
    fn state(&self) -> TimerState {
        match self.lock().timer.current_phase() {
            TimerPhase::NotRunning => TimerState::NotRunning,
            TimerPhase::Running => TimerState::Running,
            TimerPhase::Paused => TimerState::Paused,
            TimerPhase::Ended => TimerState::Ended,
        }
    }

    fn current_split_index(&self) -> Option<usize> {
        self.lock().timer.current_split_index()
    }

    fn segment_splitted(&self, idx: usize) -> Option<bool> {
        let recorded = self.lock();
        let timer = &recorded.timer;
        if idx >= timer.current_split_index()? {
            return None;
        }
        Some(
            timer
                .run()
                .segments()
                .get(idx)?
                .split_time()
                .real_time
                .is_some(),
        )
    }

    // The timer's own errors, such as splitting with no attempt running, are what
    // livesplit-core's auto splitting ignores too.

    fn start(&mut self) {
        let _ = self.lock().timer.start();
    }

    fn split(&mut self) {
        let _ = self.lock().timer.split();
    }

    fn skip_split(&mut self) {
        let _ = self.lock().timer.skip_split();
    }

    fn undo_split(&mut self) {
        let _ = self.lock().timer.undo_split();
    }

    fn reset(&mut self) {
        let _ = self.lock().timer.reset(true);
    }

    fn set_game_time(&mut self, time: time::Duration) {
        let _ = self.lock().timer.set_game_time(time.into());
    }

    fn pause_game_time(&mut self) {
        let _ = self.lock().timer.pause_game_time();
    }

    fn resume_game_time(&mut self) {
        let _ = self.lock().timer.resume_game_time();
    }

    fn set_variable(&mut self, key: &str, value: &str) {
        let mut recorded = self.lock();
        recorded.timer.set_custom_variable(key, value);
        recorded.variables.insert(key.to_owned(), value.to_owned());
    }

    fn log_auto_splitter(&mut self, message: fmt::Arguments) {
        self.lock().log.push(message.to_string());
    }

    fn log_runtime(&mut self, message: fmt::Arguments, log_level: LogLevel) {
        let level = match log_level {
            LogLevel::Trace => "trace",
            LogLevel::Debug => "debug",
            LogLevel::Info => "info",
            LogLevel::Warning => "warning",
            LogLevel::Error => "error",
        };
        self.lock().log.push(format!("runtime {level}: {message}"));
    }
}
