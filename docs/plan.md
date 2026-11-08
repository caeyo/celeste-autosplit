# Plan

None of this is built. It's the design for the component's editor and the
website, written before the code. What is built, the settings map, how the
module reads it, splitting and the migration from the legacy component, is in
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
- It imports legacy settings with [the component's mapping](how-it-works.md#migration-from-livesplitceleste),
  compiling the same source file, for runners taking old Windows `.lss` files to
  other hosts, since livesplit-core reads only `CustomSettings`.
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
