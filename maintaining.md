# Understanding

There are a few components to this repo:
- [autosplitter](https://github.com/caeyo/celeste-autosplit/tree/master/autosplitter) itself - a wasm module written in Rust that acts as the splitting engine across OSes.
- [component](https://github.com/caeyo/celeste-autosplit/tree/master/component) - a .NET component specifically for LiveSplit on Windows that runs the auto splitter module and handles migration from legacy and presenting a settings UI.
- [site](https://github.com/caeyo/celeste-autosplit/tree/master/site) - a simple HTML/CSS/JS static site hosted on GitHub Pages for editing auto splitter settings in splits files.
- [schema](https://github.com/caeyo/celeste-autosplit/tree/master/schema) - contains a JSON file that lays out the settings schema shared by all parts of the repo to ensure consistency across implementations.
- [probe](https://github.com/caeyo/celeste-autosplit/tree/master/tools/probe) - a tool that hosts a dummy instance of the game for live testing and for dumping info needed for reading the game's memory in the auto splitter.
- [harness](https://github.com/caeyo/celeste-autosplit/tree/master/harness) - runs the auto splitter module against the real game for live testing.
- [trace-recorder](https://github.com/caeyo/celeste-autosplit/tree/master/tools/trace-recorder) - an Everest mod that plays back TAS inputs and records traces of the game's info that are used to drive live testing.
    - The traces and TAS files themselves are stored in [traces](https://github.com/caeyo/celeste-autosplit/tree/master/traces).
- [legacy-splits](https://github.com/caeyo/celeste-autosplit/tree/master/legacy-splits) - copies of splits saved under the legacy LiveSplit.Celeste component for testing migration.

# Releasing

1. Set `<Version>` in `component/CelesteAutosplit/CelesteAutosplit.csproj.`
2. Push a matching tag on master. `release.yml` runs CI then publishes `celeste-autosplit.dll` and `celeste_autosplit.wasm` to releases.
3. Once the release is published, add its entry to the top of `update.xml` and commit it to master.

# Building and Running

This repo expects system installations of Rust, the .NET SDK and Node.js. Versions should be pinned in the relevant places, and all other dependencies for running/testing should be downloaded automatically when needed. All commands below assume you're at the repo root.

## autosplitter

Before anything, build the auto splitter, which will be in `autosplitter/target/wasm32-unknown-unknown/release/`:
```
cd autosplitter && cargo b --release
```

The `asr-debugger` is the best way to quickly test auto splitter changes:

```
cargo install --locked --git https://github.com/LiveSplit/asr-debugger
asr-debugger autosplitter/target/wasm32-unknown-unknown/release/celeste_autosplit.wasm
```

You will need to run `asr-debugger` with `sudo` on macOS.

In VS Code, the `Debug Auto Splitter` launch configuration builds the debug wasm module, runs the debugger on it, and reloads it whenever the `Build Auto Splitter (Debug)` task builds it again.

## component

```
dotnet build component
dotnet test component
```

This will fetch a fresh copy of the pinned LiveSplit version into `component/.deps/` for referencing. Running the component's tests will run the wasm module inside that copy of LiveSplit (and thus requires you to be on Windows).

The component will be in `component/CelesteAutosplit/bin/Debug/net4.8.1/`.

## site

```
dotnet publish site/legacy-import -c Release   # Builds the C# module that performs migration
node site/serve.mjs   # Serves the site at http://localhost:8000
site/test/run.sh   # Runs the site's tests with Playwright in headless Chromium
```

## trace-recorder

Only needed if you are adding a new trace to test new behaviour. First, build the recorder:

```
dotnet build tools/trace-recorder
```

Then, use `record.sh` to record the trace from a TAS. Right now this only works on Windows and Linux, I could not get it to work on macOS.

```
tools/trace-recorder/record.sh <game> traces/tas_file_name.tas
# <game> is a zip or folder of the vanilla game - XNA or FNA, doesn't matter. 
```

This will set up a fresh install of Celeste from the game files you give it, install Everest onto it and download CelesteTAS, all in `tools/trace-recorder/.deps/celeste/`. It will then open a game window, playback the TAS and record the trace at `<tas_file_name>.jsonl`.

Commit the TAS file and the trace together in `traces`. If you rerecord an existing trace, the related scenario JSON files in `traces` may have their events shifted and will need to be readjusted.

## harness and probe

Honestly, these aren't worth building and running locally - they're meant for CI.

# Maintaining the Memory Layouts

## Vanilla

For vanilla, the `Live check` CI run prints a dump of all the information you should need from the game in order to add to the memory layout. To run it for a branch: `gh workflow run live-check.yml --ref <branch>`.

To add a value of an existing root:
1. Find its offset in the dump.
2. Add it to `Layout`, `CLR32` and `MONO64` in `vanilla.rs`; to `Values` and `Vanilla::read` in `memory.rs` (as an `Option` if it can be missing); and to `set_debug_info` in `lib.rs`.
3. Add its information to `PATHS` in `harness/src/main.rs`.

To add a new root:
1. Add its anchor and static field to `Anchors` in `tools/probe/Game.cs` as `Namespace.Type::Name()` and `Namespace.Type::Field`. The dump then shows its code and where the slot's address is in it.
2. Write a signature for each runtime's code before the address and reference patterns for the instruction holding it. On Mono you need one for each width (Linux and macOS differ here).
3. Add an `Anchor` to `Layout`, `CLR32` and `MONO64` in `vanilla.rs`; and a `scan!` of it in each `Runtime` plus a search like `find_save_data` in `memory.rs`.
4. Add the size of the root's type to `Layout`, `CLR32` and `MONO64` in `vanilla.rs`. You can find this in the dump at the end of its last field. A wrong size shows in the live check as the root's values missing.

## Everest

Everest (very kindly) maintains its own struct for communicating info to the auto splitter. The `everest.rs` layout is transcribed from the [source file](https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Everest/AutoSplitter.cs).
