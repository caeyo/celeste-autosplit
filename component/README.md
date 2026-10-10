# component

The LiveSplit (Windows) wrapper component, `celeste-autosplit.dll`. A .NET
Framework 4.8.1 class library that wraps the wasm module in LiveSplit's ASR
component and supplies its own settings editor.

| Project | Purpose |
|---|---|
| `CelesteAutosplit/` | The component. `AssemblyName` is `celeste-autosplit`, namespace `CelesteAutosplit`. |
| `CelesteAutosplit.Tests/` | xUnit tests (ASR compatibility, migration, round-trip, etc.). |

## Building

Requires the .NET SDK (version pinned in `global.json`). No .NET Framework
developer pack or local LiveSplit install is needed. The tests also need the
wasm module, so build it first:

```sh
cd autosplitter && cargo b --release
cd component
dotnet build            # or: dotnet build -c Release
dotnet test
```

Output: `CelesteAutosplit/bin/<Configuration>/net4.8.1/celeste-autosplit.dll`.

## Tests

`ComponentTests` runs the real wasm module inside LiveSplit's ASR component,
without LiveSplit's UI. The test output folder stands in for LiveSplit's folder,
with ASR's native library and the wasm under `Components\`, and a hidden form
stands in for LiveSplit's UI thread. The tests check that:

- ASR loads the module, and settings round-trip exactly through the native runtime;
- `Dispose` stops the runtime;
- a missing wasm, a wasm ASR can't load, and a LiveSplit without ASR are each
  reported, with the settings kept.

They catch ASR changes when `LiveSplitVersion` is bumped, and wasm changes ASR
can't run. Launching real LiveSplit stays a manual check.

- The test host is 64-bit, because ASR's native library only has a runtime in
  64-bit builds.
- The build copies the wasm only if it exists. After building it for the first
  time, rebuild the tests.

## LiveSplit reference

`LiveSplit.Core`, `UpdateManager` and `LiveSplit.AutoSplittingRuntime` aren't on
NuGet. On the first build, `Directory.Build.targets` downloads the official
LiveSplit release zip (version `LiveSplitVersion` in `Directory.Build.props`)
from GitHub and extracts it to `component/.deps/` (gitignored).

- The component references these with `Private="false"`, so they are never
  copied to its output. Never ship `LiveSplit.AutoSplittingRuntime.dll`.
- The test project references them normally so tests can load LiveSplit types.
- To move to a newer LiveSplit, bump `LiveSplitVersion`. The next build fetches
  it. Run the tests, then try it in LiveSplit.

## Shared schema

[`../schema/settings-schema.json`](../schema/settings-schema.json) is embedded
in the DLL as the resource `CelesteAutosplit.settings-schema.json`.

## Versioning

`<Version>` in `CelesteAutosplit.csproj` is the component's version. It must
match the release tag (`v<Version>`, checked by the release workflow) and the
newest entry in [`../update.xml`](../update.xml).
