# Plan

None of this is built. It's the design for the component's editor, the website,
and the migration from the legacy component, written before the code. What is
built, the settings map, how the module reads it, and splitting, is in
[How it works](how-it-works.md). Check each part against the code before
building it.

## The design

- The wasm module is the only splitting engine, on every host. All its
  configuration lives in the settings map, which every host saves in the `.lss`,
  so settings travel with the splits.
- ASR's own settings widgets are too limited, so each family of hosts gets an
  editor that only reads and writes that map: on Windows, the component, which
  replaces ASR's settings UI with its own, and for hosts built on livesplit-core,
  a website that edits the settings in a `.lss`. The module's own settings show
  only where the website is.
- One entry in the auto splitter list serves both: LiveSplit reads its top-level
  `Type=Component` part, the DLL and the wasm, and livesplit-core the nested
  `<AutoSplittingRuntime>`, the wasm alone.
- On Windows, the component also migrates `LiveSplit.Celeste` settings, which
  exist only there.
- Editors only edit data. Nothing that affects starting, splitting, resetting or
  timing lives outside the wasm module. The two Windows-only exceptions, the
  game's process priority and the switch to Game Time, affect none of these.
- Not planned: editors inside livesplit-core hosts, migrating on other hosts,
  downgrading to the legacy component, 32-bit Windows, and changing LiveSplit,
  livesplit-core, ASR or the list's format.

## Settings

The settings map, how the module reads it, and the triggers are in
[How it works](how-it-works.md#settings). This is what's left, for editors:

- Presets exist only in editors. A configuration matching none is "Custom".
- Editors warn about a reset the run passes, such as `chapter_exit` in a run
  through several chapters.

### Rules

1. Editors keep unknown keys in `cfg` and in triggers, so an older editor doesn't
   delete newer settings, and open a newer `schema_version` read-only.
2. The schema is the one definition: the component and the website render from
   it, as the module's `build.rs` generates from it.
3. The module writes the map only to migrate a schema, with
   `Map::store_if_unchanged`, retrying on `false`, never `Map::store`.
4. The website labels `high_priority` and `set_game_time` "LiveSplit (Windows)
   only".
5. Editors flag invalid triggers.
6. Editors hide Everest-only fields while `everest` is off, but keep them. Later
   candidates: a level set filter, and a golden-grabbed trigger from Everest's
   chapter flags.

## The module

It registers title widgets only, plain text saying where the website is, with
a short URL. On Windows they're never shown.

## The component

It runs the wasm module in ASR, and reports a 32-bit LiveSplit, a missing wasm,
and ASR failing to be created or to load the module, keeping the settings. To
add:

- **The editor**: when it opens, it parses `inner.GetSettings`'s
  `CustomSettings` and renders from the schema. On every change, it writes the
  map in ASR's format and calls `inner.SetSettings`, because of
  [LiveSplit's settings dialog](livesplit-notes.md#settings-in-the-lss). It reads
  again each time it opens, so it sees a migration the module ran.
- **Migration**, in `SetSettings`, before anything reaches ASR, below.
- **Process priority**, `cfg.high_priority`, on by default: at most every 2 s,
  from `Update`, find `Process.GetProcessesByName("Celeste")`, and set High once
  per process ID, and again when the setting changes. Set Normal only if it
  raised it, when the setting goes off or on `Dispose`. Log and carry on after
  `Win32Exception`, `InvalidOperationException` or access denied, dispose each
  `Process`, and never block the UI thread.
- **Game Time**, `cfg.set_game_time`, on by default: on the first `SetSettings`,
  when the real settings are first known, set `state.CurrentTimingMethod` to
  Game Time if it's on. Neither ASR nor asr can.
- **A legacy component in the layout**: look for `LiveSplit.Celeste.dll` in
  `state.Layout.LayoutComponents`, warn to remove it, to avoid double splits,
  and offer to import its settings through the migration if the `.lss` has none.
- **A missing wasm**: download it again from the latest release, or keep saying
  how to repair it, since LiveSplit never will.

## The website

- Static, on GitHub Pages: the `.lss` is edited in the browser, never uploaded.
- It opens a `.lss`, renders `<AutoSplitterSettings>/<CustomSettings>` from the
  schema, writes the map back in ASR's exact format, with `schema_version`,
  following the rules above, and downloads the `.lss` with only
  `<AutoSplitterSettings>` replaced.
- It tells users to close the splits in their host first, or the host
  overwrites the edit.
- It imports legacy settings with the component's mapping, for runners taking
  old Windows `.lss` files to other hosts, since livesplit-core reads only
  `CustomSettings`.
- It links the latest `celeste_autosplit.wasm`, with steps for hosts that don't
  read the list: AlexKnauth's livesplit-one-druid, which takes a path and
  renders title, bool, choice and file-select widgets, and CryZe's older desktop
  and Druid apps, which take a path in a config file. CryZe's newer, private
  desktop app reads the list.

## Distribution

The list entry replaces the existing Celeste entry, keeping every game name it
has. Its URLs are `celeste-autosplit.dll`, first, then `celeste_autosplit.wasm`,
from `https://github.com/caeyo/celeste-autosplit/releases/latest/download/`,
with `<Type>Component</Type>` and the repository as `<Website>`. Its nested
`<AutoSplittingRuntime>` has the same wasm URL, the website,
`https://caeyo.github.io/celeste-autosplit/`, and the description "Autosplitting
is available. Configure at the linked website." The Hollow Knight and Path of
Exile 2 entries have this shape already. Asset names never change, so
`latest/download/` always resolves.

Existing users don't have `celeste-autosplit.dll`, so the next activation, when
they load Celeste splits, downloads both files. With the old file name, LiveSplit
would find `LiveSplit.Celeste.dll` and download nothing. The old DLL stays in
`Components\`, harmless unless it's in a layout. Pushing the files through the
legacy component's updates was rejected: it needs push access to its repository,
and reaches only users with update checks on.

## Migration from `LiveSplit.Celeste`

### The legacy component

Audited at [ShootMe/LiveSplit.Celeste `5c5bcb2`](https://github.com/ShootMe/LiveSplit.Celeste/tree/5c5bcb2c1456ee04a241575608febb9d35f69084),
v1.4.6, the last release. It saves flat children of `<AutoSplitterSettings>`:
`AutoReset`, `SetHighPriority`, `SetGameTime` and `FileTimeOffset`, each found
as the first descendant with its name and parsed with `bool.Parse`, all true by
default but `FileTimeOffset`; and `<Splits>`, with a `<Split>` each, `Type[,Value]`,
where `Type` is one of the 71 `SplitType` names and `Value` is optional.

It infers its mode from the list, counting names: chapter, any 8 characters long
(`Prologue`, `Epilogue`, `ChapterA`, `Chapter1` to `Chapter9`); heart, any
containing `HeartGem`; area, `AreaComplete`; cassette, any containing
`Cassette`. `ILSplits` is no splits or every count at most 1, and
`ChapterSplits` any count above 0:

| Case | When | Start | Segment `i` split by | Game time | Chapter splits | `AutoReset` |
|---|---|---|---|---|---|---|
| E: empty | no splits | any room change | one segment: the next room change, unless the start room is entered again within 2.5 s, which moves the start | chapter time since start | none | applies |
| F: full game | `ChapterSplits && !ILSplits` | new file | `splits[i]` | file time | when the flag falls | ignored |
| I: IL | `ChapterSplits && ILSplits` | chapter started | `splits[i]` | chapter time, held in the menu | when the flag rises | applies |
| C: custom start | splits, none counted | `splits[0]` | `splits[i+1]`; with only `splits[0]`, the next room change | chapter time since start | none | applies |

`FileTimeOffset` makes game time the file time since start, and lets heart and
cassette splits use the chapter's flag, as otherwise only IL does. When
`AutoReset` applies, the timer resets whenever the game is outside a chapter.

| Legacy types | Fire when |
|---|---|
| `Prologue`, `Chapter1` to `Chapter9`, `Epilogue` | `ChapterComplete` rises in that chapter, any side, or in case F, falls again. Chapter 7 also needs a room not starting with `credits`. |
| `ChapterA` | the same, any chapter, without the `credits` check |
| `AreaComplete,<chapter>[-<side>]` | the same, for that chapter and side |
| `AreaOnEnter`, `AreaOnExit,<chapter>[-<side>]` | the chapter ID changes to or from it, and the side too if given |
| `ChapterNCheckpointK` | in chapter N, whenever the room is the checkpoint's for the side |
| `HeartGemAny`, `ChapterNHeartGem`, `ChapterNCassette` | the file's count goes up by 1, or in IL or with `FileTimeOffset`, the chapter's flag is set. No "any cassette". |
| `LevelEnter`, `LevelExit,<room>` | inside a chapter, the room changes to or from `<room>`, ignoring case |

Its defects:

1. `SetGameTime` never worked: read in the constructor, before LiveSplit calls
   `SetSettings`, it was always true, and every user was switched to Game Time.
2. Unknown types silently become `Manual`: `Enum.TryParse` is case-sensitive and
   falls back to the default. It takes integers, so `5` is `HeartGemAny`, and
   `200` never fires.
3. Case C's final split gets the absolute chapter time.
4. `FileTimeOffset` in cases C and E mixes clocks: the file time since start,
   minus the chapter time at start.
5. `Value` stops at the first comma.
6. A `LevelEnter` or `LevelExit` without a room, or an `Area*` value that doesn't
   parse, never fires, and an `Area*` split with no value throws in `Update`.
7. Turning high priority off forces Normal, even if it didn't raise it.

Its other Windows-only behaviour is a debug log, `_Celeste.txt`, which ASR's log
replaces.

### Mapping

A node is legacy when `CustomSettings` is absent and `Splits` or `AutoReset`
present; anything else passes through. The node is the only input, never the run.
Parse the booleans as `bool.Parse` does, trimmed, falling back to legacy's
defaults, and classify the list exactly as legacy does.

| | E | F | I | C |
|---|---|---|---|---|
| `start` | `manual` | `new_file` | `chapter_start` | `splits[0]` |
| `reset` | `chapter_exit` | `manual` | `chapter_exit` | `chapter_exit` |
| `splits` | none | each, `timing: "chapter_exit"` | each, `timing: "chapter_complete"` | `splits[1..]` |
| `timing_method` | `file` | `file` | `chapter` | `chapter_since_start` |

`FileTimeOffset` makes `timing_method` `file_since_start` in every case.
`reset` is `manual` without `AutoReset`, and its `chapter_exit` has no chapter,
so it fires on leaving any. `everest` is false, `high_priority`
`SetHighPriority`, and `set_game_time` `SetGameTime`. Every top-level key and
every `timing` is written, in the schema's order, so a later default can't
change a migrated user's behaviour, and the output is the same each time.
`id(N)` maps a chapter number in a name to its ID: 1 to 7 stay, 8 is 9, 9 is
10.

| Legacy type | Trigger |
|---|---|
| `Manual` | `manual` |
| `ChapterA` | `chapter_complete` |
| `Prologue`, `ChapterN`, `Epilogue` | `chapter_complete`, chapter 0, `id(N)` and 8 |
| `ChapterNCheckpointK`, 36 of them | `checkpoint`, chapter `id(N)`, checkpoint K |
| `HeartGemAny`, `ChapterNHeartGem` | `heart`, the second with chapter `id(N)` |
| `ChapterNCassette` | `cassette`, chapter `id(N)` |
| `AreaComplete`, `AreaOnEnter`, `AreaOnExit,V` | `chapter_complete`, `chapter_enter`, `chapter_exit`, with chapter and side from V |
| `LevelEnter`, `LevelExit,V` | `room_enter`, `room_exit`, room V as written |
| anything else, or a value that never fires | `unknown`, with the text as `raw` |

Parse as legacy does: split on commas, keeping fields 0 and 1; parse the type as
`Enum.TryParse` does, an undefined integer being `unknown`; split an area value
on `-`, part 0 a chapter name (`Menu` -1, `Prologue`, `ForsakenCity`, ...,
`Core`, `Farewell`) or integer, part 1 `ASide`, `BSide`, `CSide` or 0 to 2,
trimmed and ignoring case. A failure, or side `None` or -1, is `unknown`.
`legacy` keeps each boolean's text and each split's text, in order.

The result reaches disk only when the user saves the splits, since the component
doesn't set `Run.HasChanged`. Until then, it migrates again on each load, so
migration must stay pure and deterministic.

### Deliberate deviations

Those of splitting itself are in
[How it works](how-it-works.md#splitting). Those of the mapping:

| Legacy | Now | Who notices |
|---|---|---|
| Always switches to Game Time | Honours `SetGameTime` | Users who unchecked it. |
| Unknown types silently manual | `unknown`, flagged | Nobody, timing-wise. |
| Case C's final split time is absolute | Consistent with the run | Custom-start runners. |
| `FileTimeOffset` in C and E mixes clocks | `file_since_start` | A rare combination. |
| No splits: start on any room change, split on room changes | Nothing automatic but the reset, with `AutoReset` | Users without splits, and single-room timers. |
| Case C with one segment splits on the next room change | Split by hand | Custom-start single-room timers. |
| With `AutoReset`, nothing starts or splits outside a chapter, since the reset comes first, so in case C a start that fires there, such as `AreaOnExit,X` or `AreaOnEnter,Menu`, never starts | It starts, and with `AreaOnExit,X`, leaving X during the run resets and starts again | Custom-start runners whose start fires outside a chapter. |

The legacy component becomes easier to add to a layout by mistake: while its DLL
was the entry's first URL, the layout editor hid it, and once the entry points
elsewhere, it's listed. Downgrading isn't supported, since the legacy component
reads flat elements the new format lacks: release notes should say so.

## Open items

- Whether livesplit-one-druid and CryZe's older desktop app keep the map in the
  `.lss` is unverified.
- Legacy's "auto add enter/exit splits" menu entries record room transitions as
  `LevelEnter` and `LevelExit` splits. A port: the module publishes the room as a
  variable, and the component's `ContextMenuControls` appends splits. Undecided.
- 32-bit Windows users lose auto splitting once the entry points at the
  component, which tells them to use the legacy one.

## Tests to add

- Migration: real community `.lss` files, snapshotted and checked by hand; one
  input twice giving the same output; all 71 types, integers and garbage, one by
  one; each case, crossed with `FileTimeOffset` and `AutoReset`; odd area and
  room values.
- Round-trip: the component's writer, then ASR's parser and writer, gives the
  same XML, which livesplit-core's `StoredAutoSplitterSettings` also parses.
- The module: loading the settings again once a second and on a reset.
- In LiveSplit, built with `AutoSplitterFactory.AutoSplittersXmlUrl` pointing at
  a test copy of the list: a fresh install, a legacy user, a legacy component in
  the layout, the layout editor, an update, a deleted wasm, Cancel in the
  settings dialog, the priority, Game Time, and the module's resets, alone, as
  after a run that ended with new bests, and with a start in one tick, which
  LiveSplit's ASR has to apply without a prompt, the second in order, as only
  livesplit-core is shown to. Those that download nothing can
  use [a copy of LiveSplit](maintaining.md#trying-the-component-in-livesplit).
- Across hosts: a `.lss` saved on Windows opens in the Druid fork the same, one
  edited on the website opens on Windows the same, and the Druid fork shows the
  module's widgets.
