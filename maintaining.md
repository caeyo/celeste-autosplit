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

# Building and Running

This repo expects system installations of Rust, the .NET SDK and Node.js. Versions should be pinned in the relevant places, and all other dependencies for running/testing should be downloaded automatically when needed. All commands below assume you're at the repo root.

Before anything, build the auto splitter, which will be in `autosplitter/target/wasm32-unknown-unknown/release/`:
```
cd autosplitter && cargo b --release
```

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

Honestly, these aren't worth building and running locally - run them in CI 