# Maintaining

Commands run from the repository's root, in Git Bash.

## Testing rules

- Test against the real game and the real auto splitting runtime, not stand-ins
  built on the same assumptions as the code.
- Test inputs are independent of the code under test.
- A pass rests on as little as possible besides the code under test.
- Each check has one job.
- Vanilla is the primary target.
- The live check's designed values catch a layout mistake in any field by
  construction. Flags have only two values, so each flag is also set alone.

## Releasing

1. Set `<Version>` in `component/CelesteAutosplit/CelesteAutosplit.csproj`.
2. Push a matching tag, such as `v1.0.0`, on `master`. `release.yml` runs CI and
   the Live check, checks the tag, then publishes the `celeste-autosplit.dll` and
   `celeste_autosplit.wasm` that CI built and tested together.
3. Once it's published, and not before, add its entry to the top of
   `update.xml`, as [the updater](livesplit-notes.md#the-updater) needs, and
   commit it to `master`.

## Keeping the layouts

[`vanilla.rs`](../autosplitter/src/vanilla.rs) is kept by hand from the dump:
both Windows flavours give `CLR32`'s values, and Linux and macOS `MONO64`'s. The
Live check prints the dump on every flavour. To get it for a branch:
`gh workflow run live-check.yml --ref <branch>`.

To read another value of a root:

1. Find its offset in [`tools/probe/fields/`](../tools/probe/fields/) or a
   dump: `0x018  AutoSplitterInfo.Chapter : System.Int32` is `Chapter`'s offset
   after `AutoSplitterInfo`.
2. Add it to `Layout`, `CLR32` and `MONO64` in `vanilla.rs`; to `Values` and
   `Vanilla::read` in [`memory.rs`](../autosplitter/src/memory.rs), as an
   `Option` if it can be missing; and to `set_debug_info` in
   [`lib.rs`](../autosplitter/src/lib.rs).
3. Add its root, path, kind and debug variable to `PATHS` in the
   [harness](../harness/src/main.rs), or `None` for a value published only
   through another, as `Time` is through `Starting New File`. Then add what it
   publishes to `expected`, and live check frames that a wrong field or width
   fails, as `Time` has. The host can write a string, a `bool`, an `int` or a
   `long`.

To add a root:

1. Add its anchor and static field to `Anchors` in
   [`Game.cs`](../tools/probe/Game.cs), as `Namespace.Type::Name()` and
   `Namespace.Type::Field`: a method the game runs before the root is needed,
   whose code loads or stores the field. The dump then shows its code and where
   the slot's address is in it.
2. Write a signature for each runtime's code, before the address, and reference
   patterns for the instruction holding it, on Mono one for each width.
3. Add an `Anchor` to `Layout`, `CLR32` and `MONO64`, and in `memory.rs`, a
   `scan!` of it in each `Runtime`, and a search like `find_save_data`.
4. Add the size of the root's type, for the
   [type check](how-it-works.md#the-type-check), to `Layout`, `CLR32` and
   `MONO64`: from the dump, the end of its last field, rounded up to 4 and plus
   4 on `CLR32`, and rounded up to 8 on `MONO64`. A wrong size shows in the live
   check as the root's values missing.
5. Add its values as above. The host gives a root holding null an object of its
   own when a value is first written to it. If the game leaves the slot null at
   times, add live check frames with it null, as `SaveData.Instance` has.

## Bumping versions

- **LiveSplit:** `LiveSplitVersion` in `component/Directory.Build.props`. Run
  `dotnet test component`, then [try it in LiveSplit](#trying-the-component-in-livesplit).
  Last tried: LiveSplit 1.8.37, with ASR 0.0.12, on 2026-11-08.
- **Everest, for the recorder:** `EverestRelease` in
  `tools/trace-recorder/TraceRecorder.csproj`, and `EverestCore` in its
  `everest.yaml`. Then record every trace again.
- **Everest's struct:** if Everest changes it, transcribe it again into
  `everest.rs`, with its `InfoVersion`, and update the link in
  [How it works](how-it-works.md#everest). CI doesn't check it: do a
  [hand check](#a-hand-check-in-the-real-game).
- **CelesteTAS:** `CELESTETAS_RELEASE` in `tools/trace-recorder/record.sh`, and
  `CelesteTAS` in `everest.yaml`. Copy `traces/community/` again from the commit
  the release's `.github/workflows/Test.All.yml` sync-checks, its `vanilla` job's
  `tas-revision`, and record every trace again.
- **Rust, .NET, asr, livesplit-core:** `rust-toolchain.toml`, `global.json`, the
  `rev` in `autosplitter/Cargo.toml`, and the one `rev` of both crates in
  `harness/Cargo.toml`.

## Recording a trace

Never record or play a TAS in an install holding your real saves. `record.sh`
sets up its own, in `tools/trace-recorder/.deps/celeste/`, with the pinned
Everest and CelesteTAS. It runs on Windows, in Git Bash, and Linux, with the
.NET SDK, `curl` and `unzip`, but not macOS: on the arm64 runner, Everest's
installer exited silently.

```sh
tools/trace-recorder/record.sh <game> traces/prologue.tas
```

`<game>` is a zip or folder of the vanilla game without Everest: either Windows
build, or on Linux, the Linux build. The first run takes a couple of minutes and
1.6 GB. The game opens in a window, under a minute for the Prologue, and up to
four for the other traces. Linux needs a display: Xvfb works. A hung game is
stopped after 30 minutes, or by its process ID. The trace,
`traces/prologue.jsonl` here, is replaced only once the recorder says it's
complete. Commit it with its TAS file, and list a new one in
[traces/README.md](../traces/README.md). Recording again can shift its frames
and times, so read its scenarios' events again from the new trace.

## Running the probe and the harness

The game files are release assets of the private repository
`caeyo/celeste-autosplit-gamefiles`: `gamefiles-v1` has the full zips, and
`gamefiles-v2`, which CI fetches with the `GAME_FILES_TOKEN` secret, the same
trimmed to what the probe and the harness need, without `Content`.

1. Extract a Windows zip, without `Content`, into a folder of its own.
2. Set it up as the workflow does: move `Celeste.exe`, and `Celeste.exe.config`
   if there is one, into `real/`, and copy
   `tools/probe/bin/Release/net45/Probe.exe` in as `Celeste.exe`. The XNA build
   needs the XNA 4.0 redistributable.
3. Build the wasm module, the harness and the probe, as the
   [README](../README.md#building) says.
4. Make sure no other `Celeste.exe` is running: the auto splitting runtime
   attaches to the most recently started process with the game's name.
5. Run the harness with the traces, and after `--`, the probe's command, or run
   the dump:

```sh
harness/target/release/harness traces/*.jsonl -- <folder>/Celeste.exe host
<folder>/Celeste.exe dump
```

The harness runs each trace's [scenarios](../traces/README.md#scenarios) after
its replay, and prints `ok` for the live check, each trace and each scenario, or
the differences and the auto splitter's log, and exits with 1. If the probe fails
or hangs, it prints the probe's log. It waits 120 s for each answer, or 5 s for
the probe to exit after an error. An auto splitting runtime updated by hand has
no watchdog, so an update that hangs hangs the harness. It reads the wasm module
from `autosplitter/target/`: build it without `CARGO_TARGET_DIR`. Started from
a terminal, `host` keeps the game until its input ends, so the asr-debugger can
read it while you type values in.

Stop a stray probe by its process ID, never by name. Local timings come with
warm caches, and CI's runners start cold. Linux and macOS run only in CI: see
[`live-check.yml`](../.github/workflows/live-check.yml).

## Debugging with the asr-debugger

The [asr-debugger](https://github.com/LiveSplit/asr-debugger) runs the wasm
module in a window, with tabs for its log, its variables and its settings map:

```sh
cargo install --locked --git https://github.com/LiveSplit/asr-debugger
```

In VS Code, with CodeLLDB, the `Debug Auto Splitter` launch configuration builds
the debug wasm module and runs the debugger on it, which reloads it whenever the
`Build Auto Splitter (Debug)` task builds it again. Breakpoints work; inspecting
variables doesn't always. The `asr` crate's API is at
[livesplit.org/asr](https://livesplit.org/asr/asr/).

## A hand check in the real game

In an install of its own, never the one holding your saves: extract a full zip,
with `Content`, add `Saves/settings.celeste` with
`<Fullscreen>false</Fullscreen>`, as `record.sh` writes it, and start
`Celeste.exe`. Then run
`asr-debugger autosplitter/target/wasm32-unknown-unknown/release/celeste_autosplit.wasm`
and watch its Variables tab while you play. `Saves/0.celeste` holds the file's
values to compare with. For Everest, use a fresh install with fresh saves and no
`everest-savepath.txt`, started normally for its struct, or with `--vanilla`.

With Everest:

- Current Everest's `--vanilla` starts `orig/Celeste.exe` as a child process,
  and the launcher exits. That child loads `orig/Content`: if it's empty, as
  `record.sh` creates it, the game crashes loading `MonocleDefault.xnb`.
  Replacing it with a directory junction to the install's `Content`
  (`mklink /J`) fixes it, without admin rights.
- Everest's pause menu adds "Mod Options", which shifts the menu's entries.
- Everest `stable-1.4449.0`'s installer also adds FNA to the XNA build. On
  current Everest, the XNA build's `orig/` holds the vanilla game converted to
  FNA.

## Trying the component in LiveSplit

In a copy of stock LiveSplit, of `LiveSplitVersion`, in a new folder, never an
install you run with: delete `LiveSplit.Register.exe`, or it asks through UAC to
take over the `.lss` and `.lsl` file associations. Copy `celeste-autosplit.dll`
into `Components\` as `LiveSplit.Celeste.dll`, with `celeste_autosplit.wasm`.
LiveSplit takes that name from the first URL of the official list's Celeste
entry, finds it present, and activates the component for any Celeste splits. Its
settings are the editor, or a status if ASR couldn't run the module, and
DebugView shows the log.

To check a real legacy component, keep ours as `celeste-autosplit.dll` next to
`LiveSplit.Celeste.dll`, add a `LiveSplit.AutoSplitters.xml` whose Celeste
entry's first URL ends in `celeste-autosplit.dll`, and in this copy's
`LiveSplit.exe.config` a `defaultProxy` pointing at a dead address, so that
LiveSplit can't fetch the official list and reads the local one.

## CI

| Workflow | Runs |
|---|---|
| `ci.yml` | clippy and fmt on both crates, failing on any warning; on Windows, builds the wasm module and the component, and runs the component's tests; on Linux, runs the harness's tests and builds the trace recorder. On changes to `autosplitter/`, `harness/`, `component/`, `schema/`, `tools/trace-recorder/`, `rust-toolchain.toml` or `global.json`. |
| `live-check.yml` | on each vanilla flavour: the live check, the replay of every trace with its scenarios, then the dump. On changes to `autosplitter/`, `harness/`, `schema/`, `tools/probe/`, `traces/`, `rust-toolchain.toml` or `global.json`, and on dispatch. |
| `release.yml` | on a `v*` tag: both of the above, then the release. |
| `site.yml` | on pushes to `master` that change `site/` or `schema/`, and on dispatch: deploys `site/`, with the schema, to GitHub Pages. |

A change to Markdown files alone runs no workflow. In the Live check:

- The game files never go into caches or artifacts, which anyone who can read
  the repository can download, and any run can restore. Forks can't have the
  token, so their pull requests are skipped: review one, then push its branch
  here. The checkout keeps no token, since the game's code runs in the job.
- Only `~/.cargo` and the target directories are cached, one cache for both
  Windows flavours. Pull requests restore only their own and `master`'s caches.
- The XNA job installs the XNA 4.0 redistributable, since XNA loads from the
  GAC, with the VC++ 2010 runtime and DirectX pieces it imports.
- On Linux, the `Celeste` script runs `Celeste.bin.x86_64`, a MonoKickstart that
  runs `Celeste.exe` in the game's Mono, and on macOS,
  `Celeste.app/Contents/MacOS/Celeste` is the MonoKickstart. The game is x86_64
  only, so on the arm64 macOS runner it runs under Rosetta.
- FNA needs a display for its window, so on Linux the probe runs under
  `xvfb-run`, with software rendering, since the window never gets a GL context.
- On Linux, Yama lets the harness read its child's memory. On macOS that needs
  root, so the harness runs with `sudo`, and the probe with `sudo -u` as the
  normal user, as players run the game.
- Killing a probe behind `xvfb-run` or `sudo` can leave it running, so the
  harness reads the probe's standard error into its log, or a stray probe would
  hold the harness's own output open. It reads the probe's output as lossy UTF-8,
  since on Windows paths are in the console's code page.

## Other notes

- The probe targets .NET Framework 4.5, as `Celeste.exe` does, since it runs in
  the game's runtime: on Linux and macOS, the bundled Mono, which only has the
  4.5 profile. `Celeste.exe` is x86-only, so on Windows the probe is 32-bit too.
  Mono ignores the flag and runs 64-bit.
- The site is plain HTML, CSS and JavaScript, with no build step. `site.yml` and
  `serve.mjs` copy the schema next to `index.html`, where the copy is gitignored.
  The repository's Pages source has to be GitHub Actions.
- `.gitattributes` keeps LF endings for shell scripts, which bash fails on with
  CRLF, and for traces and TAS files, as the recorder and CelesteTAS write them.
