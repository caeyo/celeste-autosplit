# Plan

What's left is the module's title widgets and the list entry, written before
the code. What is built, the settings map, how the module reads it, splitting,
the migration from the legacy component, the component and the website, is in
[How it works](how-it-works.md).
Check each part against the code before building it.

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
[How it works](how-it-works.md#settings), and what editors do with them in
[The settings editor](how-it-works.md#the-settings-editor). This is what's left:

- The module writes the map only to migrate a schema, with
  `Map::store_if_unchanged`, retrying on `false`, never `Map::store`.
- Later candidates: a level set filter, and a golden-grabbed trigger from
  Everest's chapter flags.

## The module

It registers title widgets only, plain text saying where the website is, with
a short URL. On Windows they're never shown.

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
  a test copy of the list: a fresh install, a legacy user, and an update.
- Across hosts: a `.lss` saved on Windows opens in the Druid fork the same, one
  edited on the website opens on Windows the same, and the Druid fork shows the
  module's widgets.
