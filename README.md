# celeste-autosplit

An auto splitter for Celeste, for every LiveSplit host. It's a wasm module that
attaches to the game, reads its memory, and publishes what it reads as debug
variables, on vanilla Celeste 1.4.0.0 on Windows, Linux and macOS, and on
Everest. It doesn't split yet: splitting, its settings and their editors are in
the [plan](docs/plan.md).

## Folders

| Folder | What |
|---|---|
| [`autosplitter/`](autosplitter/) | The auto splitter, in Rust, built to `celeste_autosplit.wasm`. `vanilla.rs` holds the layouts it reads vanilla with, kept by hand from the probe's dump, `everest.rs` Everest's struct, transcribed from Everest's source, and `memory.rs` the reading. |
| [`component/`](component/) | `celeste-autosplit.dll`, the LiveSplit component, in C#, which runs the wasm module in LiveSplit's auto splitting runtime, and its tests. Its settings editor is planned. |
| [`tools/probe/`](tools/probe/) | Poses as the game, in the game's own runtime: `host` holds the real game for the harness, and `dump` prints the offsets and machine code the layouts are kept from. C#. |
| [`harness/`](harness/) | The live check and the replay, which run the wasm module against the real game, held by the probe. Rust. |
| [`traces/`](traces/) | The game's values, update by update, while a TAS plays, and the TAS files. |
| [`tools/trace-recorder/`](tools/trace-recorder/) | The Everest mod and the script that record the traces. C#. |
| [`schema/`](schema/) | `settings-schema.json`, the schema of the planned settings. Nothing reads it yet. |
| [`site/`](site/) | A stub of the planned settings website, deployed to GitHub Pages. |
| [`docs/`](docs/) | How it all works, and how to maintain it. |
| [`update.xml`](update.xml) | LiveSplit's update manifest for the component, empty until the first release. |

## Building

From the repository's root, in Git Bash, with
[Rust](https://www.rust-lang.org/tools/install), the .NET SDK and Node.js.
`rust-toolchain.toml` and `global.json` pin Rust and the .NET SDK, and rustup
installs the pinned Rust on first use.

| Command | Builds |
|---|---|
| `(cd autosplitter && cargo b --release)` | the wasm module, in `autosplitter/target/wasm32-unknown-unknown/release/` |
| `dotnet build component` | the component, in `component/CelesteAutosplit/bin/Debug/net4.8.1/`, and its tests, which copy the wasm module, so build that first |
| `dotnet test component` | the component's tests, which run the wasm module in LiveSplit's ASR |
| `(cd harness && cargo build --release)` | the harness, in `harness/target/release/` |
| `dotnet build tools/probe -c Release` | the probe, in `tools/probe/bin/Release/net45/` |
| `dotnet build tools/trace-recorder` | the trace recorder |
| `node site/serve.mjs` | serves the site at http://localhost:8000 |

The wasm module is built from inside `autosplitter/`, because cargo reads its
wasm target from `autosplitter/.cargo/config.toml` only there. The component's
build fetches LiveSplit into `component/.deps/`, and the recorder's fetches
Everest into `tools/trace-recorder/.deps/`.

Running the harness needs the game: see
[Maintaining](docs/maintaining.md#running-the-probe-and-the-harness).

## Docs

- [How it works](docs/how-it-works.md): the terms, how a value gets from the
  game to a debug variable, what the tests prove and don't, known gaps, and
  supported versions. Start here.
- [Maintaining](docs/maintaining.md): the testing rules, releasing, keeping the
  layouts, bumping versions, recording traces, running things locally,
  debugging, and CI.
- [LiveSplit notes](docs/livesplit-notes.md): what the code relies on about
  LiveSplit, its auto splitting runtime, the auto splitter list and the updater.
- [Plan](docs/plan.md): what isn't built yet.
- [traces](traces/README.md): the TAS files, the recorder, and the traces'
  format.
- [schema](schema/README.md): the schema's format.
