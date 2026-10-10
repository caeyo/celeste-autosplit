# component

The LiveSplit (Windows) wrapper component, `celeste-autosplit.dll`. A .NET
Framework 4.8.1 class library that wraps the wasm module in LiveSplit's ASR
component and supplies its own settings editor.

| Project | Purpose |
|---|---|
| `CelesteAutosplit/` | The component. `AssemblyName` is `celeste-autosplit`, namespace `CelesteAutosplit`. |
| `CelesteAutosplit.Tests/` | xUnit tests (migration, round-trip, etc.). |

## Building

Requires the .NET SDK (version pinned in `global.json`). No .NET Framework
developer pack or local LiveSplit install is needed.

```sh
cd component
dotnet build            # or: dotnet build -c Release
dotnet test
```

Output: `CelesteAutosplit/bin/<Configuration>/net4.8.1/celeste-autosplit.dll`.

## LiveSplit reference

`LiveSplit.Core`, `UpdateManager` and `LiveSplit.AutoSplittingRuntime` aren't on
NuGet. On the first build, `Directory.Build.targets` downloads the official
LiveSplit release zip (version `LiveSplitVersion` in `Directory.Build.props`)
from GitHub and extracts it to `component/.deps/` (gitignored).

- The component references these with `Private="false"`, so they are never
  copied to its output. Never ship `LiveSplit.AutoSplittingRuntime.dll`.
- The test project references them normally so tests can load LiveSplit types.
- To move to a newer LiveSplit, bump `LiveSplitVersion`. The next build fetches
  it. Re-test against the new ASR.

## Shared schema

[`../schema/settings-schema.json`](../schema/settings-schema.json) is embedded
in the DLL as the resource `CelesteAutosplit.settings-schema.json`.

## Versioning

`<Version>` in `CelesteAutosplit.csproj` is the component's version. It must
match the release tag (`v<Version>`, checked by the release workflow) and the
newest entry in [`../update.xml`](../update.xml).
