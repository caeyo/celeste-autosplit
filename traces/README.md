# traces

Recordings of the real game's `AutoSplitterInfo` and its save's `TotalDeaths`
and `Time`, update by update, while CelesteTAS plays a TAS through it: what the
auto splitter reads. Each `<name>.jsonl` is recorded from the
`<name>.tas` next to it. They hold values only, nothing from the game's files.
The harness replays every trace through the real game, on every vanilla
flavour, in the Live check. To record one, see
[Maintaining](../docs/maintaining.md#recording-a-trace).

| Trace | What it covers |
|---|---|
| `prologue` | A new file through the Prologue: from the title screen, through starting a new file and the Prologue, to the overworld. |

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
  the auto splitter's values while it fast-forwards.

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
holds them all, as does a line where a root stops holding null. A line without
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
