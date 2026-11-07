# traces

Recordings of the real game's `AutoSplitterInfo` and its save's `TotalDeaths`
and `Time`, update by update, while CelesteTAS plays a TAS through it: what the
auto splitter reads. Each `<name>.jsonl` is recorded from the
`<name>.tas` next to it. They hold values only, nothing from the game's files.
The harness replays every trace through the real game, on every vanilla
flavour, in the Live check, and then its [scenarios](#scenarios). To record one,
see [Maintaining](../docs/maintaining.md#recording-a-trace).

| Trace | What it covers |
|---|---|
| `prologue` | A new file through the Prologue: from the title screen, through starting a new file and the Prologue, to the overworld. |
| `forsaken-city` | A new file through the Prologue, then Forsaken City as the community's All Hearts route plays it: the A-side through its first checkpoint to its crystal heart, then a Save and Quit back into it, through its second checkpoint to its cassette, then back to the map; the B-side to its end; a restart of the B-side; then back to the map, through the menus to a new file in the second slot, and into its Prologue. A checkpoint's side can't be told wrong here: the A-side's checkpoint rooms, `6` and `9b`, never appear on the B-side, nor the B-side's, `04` and `08`, on the A-side. |
| `summit` | The Summit from its last checkpoint, loaded with `console load`: its end, the credits, and the start of the Epilogue. |

## TAS files

Each TAS file plays from the title screen, with fresh saves. Its inputs come from
the community's [TAS files](https://github.com/VampireFlower/CelesteTAS), which it
`Read`s from `community/`: unchanged copies, from commit
[`eb4550a`](https://github.com/VampireFlower/CelesteTAS/tree/eb4550a253be97fd4942a5e609fe2a60e980f7ca),
the one CelesteTAS's own sync check plays for the release `record.sh` pins. To read
another, copy it from the same commit. A TAS file can also have inputs of its own,
for what the community's don't play. A TAS file:

- ends with `FileTime:`, which CelesteTAS fills in once the TAS reaches its end.
  Commit it with the trace: a time that changes on a later recording means the
  TAS played differently;
- never fast-forwards or pauses: no `***` breakpoints. CelesteTAS doesn't update
  the auto splitter's values while it fast-forwards;
- starts with `Unsafe` if it leaves a level without `StartFullGameFile`, whose
  `SelectCampaign` sets it: otherwise CelesteTAS stops it outside a level.

`summit` loads the point the community's `7A.tas` names for its last checkpoint,
`3000M`, rather than playing the whole chapter. `console load` plays on
CelesteTAS's own save slot, which it starts with a `Time` of 0, so `new_file`
fires there. Community files that save and quit, such as `1HC`, play only on a
real file.

## The recorder

[`tools/trace-recorder/`](../tools/trace-recorder/) is an Everest code mod, and
`record.sh`, which records with it. Each update, right after the game updates its
`AutoSplitterInfo`, Everest raises `AutoSplitter.OnUpdateInfo`, from its patch of
`AutoSplitterInfo.Update`. The recorder writes the game's values into the trace
then, for every update while the TAS plays.

- It builds against the reference assemblies of the Everest release that
  `TraceRecorder.csproj` pins, fetched into `tools/trace-recorder/.deps/`. CI
  builds it on Linux, so that a change that breaks it shows before the next
  recording.
- It records in an install of its own, so its versions are pinned, it has no
  other mods, and it can't touch the player's saves.
- The game runs in a window, at its own speed. CelesteTAS doesn't update the
  auto splitter's values in a sync check, or while it fast-forwards. And
  headless, Everest runs updates back to back, so anything the game waits for in
  real time, such as a save, would last thousands of updates.
- CelesteTAS starts the TAS at the end of the title screen's first update. So
  the recording fails if no TAS is running after 60 updates in the overworld.
- It exits the game the way the game exits itself, after the update: exiting the
  process from within an update can hang it. Once Everest has shut the game down,
  it exits the process with 0, so the recorder exits it in `OnShutdown`, just
  before, with its own code.
- Everest's installer links `orig/Content` and `orig/Saves` to the game's own.
  On Windows, that needs admin rights, unless they exist already, so `record.sh`
  creates them first.
- Each recording starts with fresh saves, but for the settings `record.sh`
  writes. Without them, the game would start fullscreen and play its intro,
  Everest's first-run screens would take the TAS's inputs, and Everest would
  colour its log.

The recorder exits the game, and logs why, when the TAS doesn't start, as when a
`Read` fails; when an update isn't recorded, as when the TAS fast-forwards; when
the TAS pauses, at a breakpoint or at the end of a draft; and when the TAS doesn't
reach its end. `record.sh` clears the TAS file's `FileTime:` first, and the time
CelesteTAS fills in, the time the file has gained since the TAS's first level
began, has to be the trace's. Recording again gives the same trace, on Windows or
Linux, except for how many updates the game waits for a save to finish, which
takes real time.

## Format

JSON, one object per line, with lines ending in `\n`. The first line holds the
versions the trace was recorded with:

```json
{"celeste":"1.4.0.0","everest":"1.6623.0-azure-47d6a-stable","celesteTas":"3.47.1"}
```

Then there's a line for each update of the game while the TAS plays, from the state
before its first frame to the state after its last. Each holds the values the game
sets at the start of that update, from the state the previous update left, which an
auto splitter reads until the next. It has:

- `frame`: the update's number, from 0.
- `roots`: every field of the game's `AutoSplitterInfo`, and `TotalDeaths` and
  `Time` from the save in `SaveData.Instance`, by root and path as the
  [probe's host](../docs/how-it-works.md#the-probe) takes them: strings, numbers
  and booleans. A root holding null is `null`.

Each line only holds the values that changed since the previous line, and the first
holds them all. A line where a root stops holding null holds all of that root's
values. A line without
changes has only its `frame`. In `prologue`, the new file's save appears in frame
265, and the timers start running in frame 432:

```json
{"frame":264}
{"frame":265,"roots":{"Celeste.SaveData::Instance":{"TotalDeaths":0,"Time":0}}}
{"frame":266}
```

```json
{"frame":430}
{"frame":431,"roots":{"Celeste.Celeste::Instance":{"AutoSplitterInfo.Chapter":0,"AutoSplitterInfo.Mode":0,"AutoSplitterInfo.Level":"0","AutoSplitterInfo.TimerActive":true,"AutoSplitterInfo.ChapterStarted":true}}}
{"frame":432,"roots":{"Celeste.Celeste::Instance":{"AutoSplitterInfo.FileTime":170000},"Celeste.SaveData::Instance":{"Time":170000}}}
```

Times are in ticks of 100 ns. The game adds 17 ms, 170,000 ticks, to them each
update that its timers run.

A trace has more lines than its TAS has input frames. CelesteTAS doesn't count the
updates in which the game is loading or saving, but the game still updates, and so
does what the auto splitter reads.

## Scenarios

A scenario, `<trace>.<scenario>.json` next to its trace, holds a settings map and
the timer events the auto splitter must give with it while the trace is
replayed, as [How it works](../docs/how-it-works.md#what-the-tests-prove)
describes. Its events are written by hand from reading the trace, never from the
auto splitter's output. It's an object with these keys:

- `settings`, the whole settings map: objects are maps, arrays lists, and numbers
  `i64`. `null` isn't allowed.
- `segments`, optional, how many segments the timer has, 1 by default. A split
  of the last ends the run.
- `events`, every start, split and reset, in order, each an object with the
  `frame` of the trace it happens in, the `event`, `start`, `split` or `reset`,
  and the `game_time` in ticks of 100 ns, as the trace's times are, or `null`
  for none, as for every reset. Frame 0 also holds what happens while the auto
  splitter attaches.

All of `prologue`'s start a new file and split on completing chapter 0, unless
the table says otherwise:

| Scenario | Checks |
|---|---|
| `chapter-complete` | `new_file` starts in frame 265, where the save appears, at a file time of 0, and `chapter_complete` splits in frame 1942, where `ChapterComplete` rises, at that frame's `FileTime`. |
| `chapter-exit` | `timing: "chapter_exit"` and `side: 0` split in frame 1974, where `ChapterComplete` falls and `Mode` becomes -1, so the side is the previous tick's. |
| `other-chapter` | `chapter: 1` doesn't split. |
| `empty` | An empty `cfg` neither starts nor splits. |
| `invalid-value` | `timing: "chapter_end"`, a value not allowed, makes the split manual, and `start` still applies. |
| `invalid-type` | `chapter: "0"`, a string, makes the split manual. |
| `invalid-kind` | `kind: "Chapter_Complete"`, a kind that differs only in case, makes the split manual. |
| `invalid-reset` | A `reset` of `chapter_exit` with `chapter: "0"`, a string, is manual, so the run the split ends doesn't reset on returning to the map, in frame 1974. |
| `invalid-chapter` | `chapter_enter` with `chapter: -1`, which isn't a chapter, makes the split manual, so it doesn't split on returning to the map, in frame 1974. |
| `newer` | `schema_version` 2 neither starts nor splits. |
| `sid-everest-off` | A `sid` of chapter 1, `Celeste/1-ForsakenCity`, with `everest` off, is ignored, so the split is any chapter's, in frame 1942. |
| `sid-without-everest` | A `sid` of chapter 0, `Celeste/0-Intro`, with `everest` on, doesn't split without Everest. |

`forsaken-city`'s start a new file in frame 265, and split at the frame's
`FileTime`, unless the table says otherwise. Chapter 1's only completion is the
B-side's, in frame 11501:

| Scenario | Checks |
|---|---|
| `b-side` | `chapter_complete` of chapter 1 with `side: 1` splits in frame 11501. |
| `other-side` | The same with `side: 0` doesn't split. |
| `file-time-drop` | `new_file`, which editors offer only for starts and resets, splits in frame 12032, where the second file's save appears and `FileTime` drops to 0, at the previous frame's `FileTime`. No other trigger fires on that frame. |
| `checkpoint` | Two splits on chapter 1's first checkpoint, without a side, split where the room becomes the A-side's, `6`, in frame 3586, not again in the next frame, still in it, and then the B-side's, `04`, in frame 8483. |
| `checkpoint-side` | Chapter 1's second checkpoint with `side: 1` splits in the B-side's `08`, in frame 9524, not the A-side's `9b`, in frame 6174. |
| `heart` | Two splits on chapter 1's heart split where `ChapterHeart` rises, in frames 4776 and 11501, not in frame 4777, where it's still set, nor in frame 4983, where it comes back set on resuming. |
| `cassette` | Chapter 1's cassette splits where `ChapterCassette` rises, in frame 6676, not where the heart's flag does. |
| `save-and-quit` | `chapter_exit` without a `chapter` splits on returning to the map after the Prologue, in frame 1974, `chapter_exit` of chapter 1 on the Save and Quit, in frame 4810, and `chapter_enter` of chapter 1 on resuming, in frame 4983. |
| `chapter-side` | `chapter_enter` of chapter 1 with `side: 1` splits where the B-side is entered, in frame 7440, not the A-side, and `chapter_exit` with `side: 1` where it's left, in frame 11534, the side then being -1. |
| `room-exit` | `room_exit` of `s1` doesn't split on the Save and Quit from it, in frame 4810, but on leaving it for `s0`, in frame 5113, and `room_enter` of `00` splits on entering the B-side in it, from no room, in frame 7440. |
| `room-resume` | `chapter_exit` of chapter 1 splits on the Save and Quit, in frame 4810, and `room_enter` of `s1` then doesn't split on resuming in it, in frame 4983. |
| `any-chapter` | `chapter_exit` without a `chapter` doesn't split on entering the Prologue, in frame 431, which leaves no chapter, but on leaving it, in frame 1974. `chapter_enter` without one then splits on entering chapter 1, in frame 2652, and again, not on the Save and Quit, in frame 4810, which enters no chapter, but on resuming, in frame 4983. `chapter_exit` with only `side: 1` then doesn't split on leaving the A-side, in frame 7030, but on leaving the B-side, in frame 11534. |
| `il` | The individual-level preset, `chapter_start` as start, `chapter_exit` as reset and `timing_method: "chapter"`, starts where the Prologue starts, in frame 431, and resets on leaving it, in frame 1974. It starts where chapter 1's A-side starts, in frame 2652, and resets on the Save and Quit, in frame 4810. Resuming, in frame 4983, where `ChapterStarted` rises with `ChapterTime` past its last value, doesn't start it again, so leaving again, in frame 7030, finds no run to reset. It starts where the B-side starts, in frame 7440. The split, on completing it in frame 11501, ends the run, which still resets on the restart, in frame 11534, where the chapter ID is -1 until the B-side starts again, in frame 11587. That attempt resets on returning to the map, in frame 11740, and the second file's Prologue starts in frame 12193. Starts are at a game time of 0, `ChapterTime`, and the split at that frame's `ChapterTime`. |
| `il-save-and-quit` | The individual-level preset with Save and Quit, `chapter_start` as start and reset and `timing_method: "chapter"`, starts where the Prologue starts, in frame 431, then resets and starts again where chapter 1's A-side, B-side and restart and the second file's Prologue start, in frames 2652, 7440, 11587 and 12193, but not on resuming, in frame 4983. The split, on completing the B-side in frame 11501, ends the run, which still resets. Starts are at a game time of 0, `ChapterTime`, and the split at that frame's `ChapterTime`. |
| `full-game` | The full-game preset, `new_file` as start and reset and `timing_method: "file"`, resets and starts again where the second file's save appears, in frame 12032, at a game time of 0, though `FileTime` drops to 0 there. The split, `new_file`, which fires on that frame too, doesn't split, since the reset takes precedence. |
| `reset-other-kind` | `chapter_enter` without a `chapter` as start, `new_file` as reset, and `chapter_exit` without one as the split. Nothing happens where the save appears, in frame 265. It starts on entering the Prologue, in frame 431, at a `FileTime` of 0, and the split, on leaving it, in frame 1974, ends the run, which entering chapter 1, in frame 2652, doesn't start again. It resets where the second file's save appears, in frame 12032, and starts on entering its Prologue, in frame 12193, at a `FileTime` of 0 again. |
| `chapter-time` | `chapter_start` of chapter 1 with `side: 1` starts where the B-side is entered, in frame 7440, and `timing_method: "chapter"` holds the restarted B-side's last `ChapterTime`, from frame 11739, until the split, `new_file`, in frame 12032. |
| `file-since-start` | `chapter_start` of chapter 1 starts where its A-side is entered, in frame 2652, not the Prologue, and `timing_method: "file_since_start"` subtracts that frame's `FileTime` from the split's, in frame 11501. |
| `chapter-since-start` | `checkpoint` starts where chapter 1's first checkpoint is entered, in frame 3586, and `timing_method: "chapter_since_start"` subtracts that frame's `ChapterTime` from the split's, on the heart, in frame 4776. |

`summit`'s `credits` scenarios start on completing chapter 7, in frame 7960,
where `ChapterComplete` rises in room `g-03`, at that frame's `FileTime`. It
rises again in frame 8376, in `credits-summit`. With one timer segment, only a
start can skip `g-03`'s completion, so they start with `chapter_complete`, which
editors offer only for splits:

| Scenario | Checks |
|---|---|
| `credits` | `chapter_complete` with `chapter: 7` doesn't split in the credits. |
| `credits-any-chapter` | `chapter_complete` without a `chapter` splits in frame 8376, at that frame's `FileTime`. |

The others have no split:

| Scenario | Checks |
|---|---|
| `chapter-start` | `chapter_start` as start and reset starts in frame 2, two frames after attaching, doesn't reset in the credits level, in frame 8375, where `ChapterStarted` rises with the Summit's `ChapterTime`, and resets and starts in the Epilogue, in frame 8399, at that frame's `FileTime`. |
| `checkpoint-spawn` | The Summit's last checkpoint starts in frame 2, where the TAS spawns into its room, `g-00`. |
| `room-case` | `room_enter` of `G-00B` doesn't start in `g-00b`, in frame 715. |
