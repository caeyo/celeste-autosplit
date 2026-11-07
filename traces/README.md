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
auto splitter's output. It's an object with two keys:

- `settings`, the whole settings map: objects are maps, arrays lists, and numbers
  `i64`. `null` isn't allowed.
- `events`, every start and split, in order, each an object with the `frame` of
  the trace it happens in, the `event`, `start` or `split`, and the `game_time`
  in ticks of 100 ns, as the trace's times are, or `null` for none. Frame 0 also
  holds what happens while the auto splitter attaches. The timer has one
  segment, so a split ends the run.

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
| `newer` | `schema_version` 2 neither starts nor splits. |

`forsaken-city`'s start a new file in frame 265, and split on completing chapter
1, whose only completion is the B-side's, in frame 11501, unless the table says
otherwise:

| Scenario | Checks |
|---|---|
| `b-side` | `side: 1` splits in frame 11501, at that frame's `FileTime`. |
| `other-side` | `side: 0` doesn't split. |
| `file-time-drop` | `new_file`, which editors offer only for starts, splits in frame 12032, where the second file's save appears and `FileTime` drops to 0, at the previous frame's `FileTime`. No other trigger fires on that frame. |

`summit`'s start on completing chapter 7, in frame 7960, where `ChapterComplete`
rises in room `g-03`, at that frame's `FileTime`. It rises again in frame 8376, in
`credits-summit`. With one timer segment, only a start can skip `g-03`'s
completion, so they start with `chapter_complete`, which editors offer only for
splits:

| Scenario | Checks |
|---|---|
| `credits` | `chapter_complete` with `chapter: 7` doesn't split in the credits. |
| `credits-any-chapter` | `chapter_complete` without a `chapter` splits in frame 8376, at that frame's `FileTime`. |
