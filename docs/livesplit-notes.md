# LiveSplit notes

What the code relies on about LiveSplit, its auto splitting runtime component
(ASR), the auto splitter list and the updater, which the code can't show. The
component builds against LiveSplit 1.8.37, `LiveSplitVersion` in
`component/Directory.Build.props`.

## Loading the component

- LiveSplit loads every DLL in `Components\` with `Assembly.UnsafeLoadFrom`, and
  two assemblies with one identity can resolve to whichever loaded first. So the
  assembly is `celeste-autosplit`, not the legacy component's `LiveSplit.Celeste`.
- .NET Framework resolves a component's dependencies from its own folder, so the
  reference to ASR resolves to the one LiveSplit loaded from `Components\`. ASR
  isn't strong-named, so it binds by name, and a newer ASR binds too. Its
  identity is `Version=1.0.0.0, PublicKeyToken=null`; `0.0.x` is only its
  factory's `Version`.
- So the component references `LiveSplit.Core`, `UpdateManager` and ASR with
  `Private="false"`, and never ships them. They aren't on NuGet: the build
  fetches LiveSplit's release zip into `component/.deps/`.
- `ComponentManager.LoadFactory` swallows every exception, so a factory that
  failed on ASR's types would silently be null. The factory uses none.
- The JIT resolves a method's types before running any of it, so a constructor
  using `ASRComponent` would throw in its caller if ASR were missing or changed,
  outside the constructor's `try`. So the inner component is a `LogicComponent`,
  and only `CreateInner` and `HasRuntime`, `NoInlining`, use ASR types, each
  inside a `try`, where a changed member throws `MissingMethodException`,
  `MissingFieldException` or `TypeLoadException`.
- The layout editor leaves out any component whose DLL is the first URL of an
  auto splitter list entry without `<ShowInLayoutEditor/>`, whatever its
  category. Once the list points at `celeste-autosplit.dll`, the component can
  only run as the auto splitter.
- The factory's `Version` has three parts, to compare equal to `update.xml`'s.

## Running the component

- LiveSplit reports a failed activation only as "could not be activated". Its
  `Log` writes through `System.Diagnostics.Trace`: warnings and errors reach the
  Windows Event Log if the `LiveSplit` event source exists, which takes admin
  rights once to create, and info goes only to the debug output, which
  Sysinternals DebugView shows. That includes all the wasm module prints, and
  why ASR failed to load a module.
- LiveSplit calls `Update` on the active auto splitter every frame, but ASR's
  does nothing: ASR steps its runtime on its own timer, on the UI thread through
  `state.Form`, every 15 ms and then at the module's tick rate. The component
  forwards `Update` anyway.
- LiveSplit calls `Dispose` when it deactivates the auto splitter or the game
  changes. Without forwarding it, ASR's timer would keep running the old module.
- `GetSettingsControl` must not return null, or LiveSplit disables the Settings
  button. The component's shows its status for now.

## ASR

- What the component uses isn't a stable API: the `ASRComponent(state,
  scriptPath)` constructor, the `CustomSettings` XML, and
  `ComponentSettings.runtime`, the only way to tell that ASR loaded the module.
- `ComponentSettings.ReloadRuntime` catches a load failure, logs it, and leaves
  `runtime` null, so creating the component still succeeds.
- LiveSplit ships one zip, whose `LiveSplit.exe` runs 32-bit only on 32-bit
  Windows. There, ASR's native library is a stub: creating a runtime succeeds,
  stepping does nothing, and every settings map is empty.
- An ASR component without a runtime drops what `SetSettings` gives it, and
  `GetSettings` writes an empty `CustomSettings`, which LiveSplit would save. So
  then the component keeps the settings itself.
- ASR loads `Components\x64\asr_capi.dll` relative to the working directory.
- ASR starts the module with an empty settings map, and `SetSettings` applies the
  real one moments later, so a module must not read its settings only at startup.
- `SetSettings` replaces the map rather than merging, so an editor can remove
  keys. Given XML in another format, such as the legacy component's, it empties
  the map. It ignores an empty `<AutoSplitterSettings/>`.
- It renders only `bool`, `title`, `choice` and `file-select` widgets, so asr's
  `gui::add_text_input` shows nothing.
- It supports every wasm feature `autosplitter/.cargo/config.toml` enables.

## Settings in the `.lss`

ASR's `ComponentSettings.GetSettings` and livesplit-core's
`StoredAutoSplitterSettings` both write the whole settings map, not only keys
with widgets, into `<AutoSplitterSettings>`, under `<CustomSettings>`, alike:
`bool`, `i64` and `f64` values in the text, in the invariant culture, with
`True` and `False`; strings in a `value` attribute; maps and lists as nested
`<Setting>` elements, list items without an `id`. Maps keep their order, so
reading and writing a map again gives the same XML. ASR's parser is strict: one
element that fails to parse drops the whole map. ASR created with a fixed path
writes no `<ScriptPath>`, which livesplit-core ignores anyway.

LiveSplit's settings dialog snapshots `GetSettings()` when it opens. On OK, the
splits editor stores `GetSettings()` as `Run.AutoSplitterSettings`. On Cancel,
it calls `SetSettings` with the snapshot. So the two have to round-trip exactly,
and an editor has to pass on each change at once.

## The auto splitter list

`LiveSplit.AutoSplitters.xml`, in the LiveSplit/LiveSplit.AutoSplitters repository.

- LiveSplit reads only an entry's direct children: `Type`, `URLs`, `Description`
  and `Website`.
- livesplit-core reads a nested `<AutoSplittingRuntime>` if there is one: its
  `URL`, the last if there are several, and its `Description` and `Website`. It
  ignores the top-level URLs.
- LiveSplit takes the file name from the first URL, and downloads only when that
  file is missing, so it never downloads a missing wasm module again.
- livesplit-core matches game names exactly, and takes the first match. LiveSplit
  ignores case. LiveSplit remembers the active auto splitter by game name.
- LiveSplit's downloader follows GitHub's `releases/latest/download/` redirect.

## The updater

At startup, LiveSplit fetches each factory's `XMLURL`. If it lists a newer
version, the updater downloads each file's `UpdateURL + path` to its
`localPath`, or `path`. An entry in `update.xml`, newest first:

```xml
<update version="1.0.1">
  <files>
    <file path="latest/download/celeste-autosplit.dll" localPath="Components/celeste-autosplit.dll" status="changed"/>
    <file path="latest/download/celeste_autosplit.wasm" localPath="Components/celeste_autosplit.wasm" status="changed"/>
  </files>
  <changelog>
    <change>...</change>
  </changelog>
</update>
```

- The updater gathers the files of every newer version by `path`, so the same
  `latest/download/` paths in every version collapse into one download of the
  latest. Versioned paths would download several onto one file, in no set order.
- The newest `version` has to equal the component's, or LiveSplit offers the
  same update on every start.

## livesplit-core

- Its auto splitting ignores the timer's own errors, such as splitting with no
  attempt running. The harness's timer ignores them too.

## The component's tests

They run the real wasm module in ASR, without LiveSplit's UI. The output folder
stands in for LiveSplit's, with ASR's native library and the wasm module in
`Components\`, and a hidden form on an STA thread for LiveSplit's UI thread. They
check that ASR loads the module, that settings round-trip exactly, and that a
missing wasm, one ASR can't load, and a LiveSplit without ASR are reported, with
the settings kept. For migration, they check each `.lss` in
`CelesteAutosplit.Tests/LegacySplits/`, community files and `odd-values.lss`,
against its hand-written `.expected.xml`, that ASR writes the migrated XML back
the same, all 71 types, integers and
garbage, each case crossed with `FileTimeOffset` and `AutoReset`, odd area and
room values, and pass-through.

- The test host is 64-bit, since ASR's native library only runs in 64-bit.
- The build copies the wasm module only if it exists: after building it the first
  time, build the tests again.
- They change the working directory and `ComponentManager.BasePath`, so they
  never run in parallel.
- Their scratch folders are short and outside the output folder: .NET Framework
  doesn't take paths over 260 characters by default.
