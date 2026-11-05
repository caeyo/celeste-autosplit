# traces

Traces of the real game's `AutoSplitterInfo`, update by update, while CelesteTAS
plays a TAS through it. That's what the auto splitter reads, but for `TotalDeaths`,
from `SaveData.Instance`, which they don't record. Each `<name>.jsonl` is recorded
from the `<name>.tas` next to it, by the [trace recorder](../tools/trace-recorder/).
They hold values only, nothing from the game's files. The [harness](../harness/)
replays every trace through the real game, on every vanilla flavour, in the Live
check workflow.

| Trace | What it covers |
|---|---|
| `prologue` | A new file through the Prologue: from the title screen, through starting a new file and the Prologue, to the overworld. |

## TAS files

Each TAS file plays from the title screen, with fresh saves. Its inputs come from
the community's [TAS files](https://github.com/VampireFlower/CelesteTAS), which it
`Read`s from `community/`: unchanged copies of the ones read here, from commit
[`eb4550a`](https://github.com/VampireFlower/CelesteTAS/tree/eb4550a253be97fd4942a5e609fe2a60e980f7ca),
the one CelesteTAS's own sync check plays for the release `record.sh` pins. To read
another, copy it from the same commit. A TAS file can also have inputs of its own,
for what the community's don't play.

A TAS file:

- ends with `FileTime:`, which CelesteTAS fills in once the TAS reaches its end.
  Commit the TAS file with the trace: a time that changes on a later recording
  means the TAS played differently;
- never fast-forwards or pauses: no `***` breakpoints. CelesteTAS doesn't update
  the auto splitter's values while it fast-forwards.

To record or re-record traces, see the
[trace recorder's README](../tools/trace-recorder/README.md#recording). Recording
again gives the same trace, except for how many updates the game waits for a save
to finish, which takes real time.

## The recorder

- CelesteTAS starts the TAS at the end of the title screen's first update. So the
  recording fails if no TAS is running after 60 updates, a second, in the
  overworld: it couldn't start.
- It exits the game the way the game exits itself, after the update: exiting the
  process from within an update can hang it. Once Everest has shut the game down,
  it exits the process with 0, so the recorder exits it in `OnShutdown`, just
  before, with its own code.
- Everest's installer links `orig/Content` and `orig/Saves` to the game's own, so
  that the vanilla game it keeps in `orig/` shares Everest's saves. On Windows,
  that needs admin rights, which it asks for, unless they exist already, so
  `record.sh` creates them first.
- Each recording starts with fresh saves, except for the settings `record.sh`
  writes. Without them, the game would start fullscreen and play its intro first,
  Everest's first-run screens would take the TAS's inputs, and Everest would
  colour its log.

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
- `roots`: the game's `AutoSplitterInfo`, every field of it, by root and path as
  the [probe](../tools/probe/README.md#the-manifest) names them: strings, numbers
  and booleans.

Each line only holds the values that changed since the previous line, and the first
holds them all. A line without changes has only its `frame`:

```json
{"frame":430}
{"frame":431,"roots":{"Celeste.Celeste::Instance":{"AutoSplitterInfo.Chapter":0,"AutoSplitterInfo.Mode":0,"AutoSplitterInfo.Level":"0","AutoSplitterInfo.TimerActive":true,"AutoSplitterInfo.ChapterStarted":true}}}
{"frame":432,"roots":{"Celeste.Celeste::Instance":{"AutoSplitterInfo.FileTime":170000}}}
```

Times are in ticks of 100 ns. The game adds 17 ms, 170,000 ticks, to them each
update that its timers run.

A trace has more lines than its TAS has input frames. CelesteTAS doesn't count the
updates in which the game is loading or saving, but the game still updates, and so
does what the auto splitter reads.
