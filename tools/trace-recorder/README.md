# trace-recorder

Records [traces](../../traces/): what the auto splitter reads from the real game,
update by update, while CelesteTAS plays a TAS. It's an Everest code mod, and
`record.sh`, which records traces with it in an install of the game of its own.

Each update, right after the game updates its `AutoSplitterInfo`, Everest updates
its own values and raises `AutoSplitter.OnUpdateInfo`, from its patch of
`AutoSplitterInfo.Update`. The recorder writes both into the trace then, for every
update while the TAS plays. The traces' README describes their
[format](../../traces/README.md#format).

## Building

Requires the .NET SDK.

```sh
dotnet build tools/trace-recorder -c Release
```

It builds against the reference assemblies Everest publishes with each release,
which the build fetches into `.deps/` the first time: the game patched by Everest,
and its dependencies, without their code. `EverestRelease` in
`TraceRecorder.csproj` pins the release. CI doesn't build the recorder, which is
only used to record.

## Recording

On Windows (in Git Bash) or Linux, with the .NET SDK, `curl` and `unzip`.
From the repository's root:

```sh
tools/trace-recorder/record.sh <game> traces/prologue.tas
```

`<game>` is the vanilla game's build for your OS, as a zip or a folder: on Windows,
either build; on Linux, the Linux build, since Everest's installer tells the OS apart
by the game's files. Copy it from an install without Everest.

It records each TAS file given into the trace next to it, `traces/prologue.jsonl`
here, in three steps:

1. The first time, and when a pinned release changes, it sets up the recorder's own
   install of the game in `.deps/celeste/`: the game given, with Everest
   (`TraceRecorder.csproj`'s release) and CelesteTAS (`record.sh`'s release). That
   takes a couple of minutes and 1.6 GB. On its first launch, CelesteTAS also
   downloads Celeste Studio into it.
2. It builds the recorder, and puts it in the install's `Mods/`.
3. It starts the game for each TAS file: CelesteTAS plays it, the recorder records
   it, and exits the game once it has ended. A Prologue takes under a minute.

On Windows and Linux, it records the same trace. Linux needs a display: Xvfb works.

Its own install, rather than the player's: its versions are pinned, it has no other
mods, and it can't touch the player's saves. Each recording starts with fresh saves,
except for settings. Without them, the game would start fullscreen, after playing its
intro, and Everest's first-run screens would take the TAS's inputs. Everest would
also color its log.

The game runs in a window, at its own speed. CelesteTAS doesn't update the auto
splitter's values in a sync check, or while it fast-forwards. And headless, Everest
runs updates back to back, so anything the game waits for in real time, such as a
save, would last thousands of updates.

The recorder exits the game with 1, and logs why, when a trace is incomplete.
`record.sh` then prints the end of the game's log. That's when:

- the TAS doesn't start, as when a `Read` fails: CelesteTAS's error is in the log;
- an update isn't recorded, as when the TAS fast-forwards;
- the TAS pauses, at a breakpoint or at the end of a draft;
- the TAS doesn't reach its end, as when it aborts: CelesteTAS only fills in the TAS
  file's `FileTime:` at the end. It holds the time the file has gained since the
  TAS's first level began, which has to be the trace's.

To record by hand, start the install's launcher as `record.sh` does, with
`--tas <file.tas> --trace <file.jsonl>`.

## Updating the pinned versions

- **Everest:** `EverestRelease` in `TraceRecorder.csproj`, and the `EverestCore`
  dependency in `everest.yaml`.
- **CelesteTAS:** `CELESTETAS_RELEASE` in `record.sh`, and the `CelesteTAS`
  dependency in `everest.yaml`. Then update the community's TAS files in
  [`traces/community/`](../../traces/community/) to the commit that the release's
  `.github/workflows/Test.All.yml` sync-checks (its `tas-revision`).

Then re-record every trace. Its header holds the versions it was recorded with.
