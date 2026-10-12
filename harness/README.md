# harness

Runs the auto splitter, `celeste_autosplit.wasm`, on livesplit-core's own
`Timer`, against a fake game built from the layouts in
[`layouts/`](../layouts/), in the harness's own process. It's a native program,
outside the wasm build.

- `livesplit-core` and its auto splitting runtime, `livesplit-auto-splitting`,
  are pinned to one revision in `Cargo.toml`.
- The runtime is stepped by hand: one `update()` at a time, with a timeout for
  each case, since a runtime stepped by hand has no watchdog of its own.
- The timer is livesplit-core's, behind the runtime's `Timer` trait, as in
  livesplit-core's own auto splitting. The debug variables the auto splitter sets
  and what it logs are also recorded, for the tests.

## The reading test

For each flavour whose layout is for this OS, and for Everest, the harness runs
a case in a copy of itself named like the game: `Celeste.exe` on Windows,
`Celeste.bin.x86_64` on Linux, and `Celeste` on macOS, since the auto splitter
finds the game by its process name. The case builds the fake game in its own
memory, then loads the auto splitter, which attaches to the process it runs in.
Each frame writes known values into the fake game, then runs one update, which
must report exactly those values in its debug variables, and nothing else.

The fake game takes its offsets from the layouts, which the layout probe
measured, and the auto splitter from
[`memory.rs`](../autosplitter/src/memory.rs), which is hand-written. So the test
checks one against the other: the field offsets, the string offsets, and the
signatures and store patterns, since the fake game holds the layout's copies and
the auto splitter scans with its own.

| OS | Cases |
|---|---|
| Windows | `windows-fna`, `windows-xna`, `everest` |
| Linux | `linux` with a 4-byte store, `linux` with an 8-byte store, `everest` |
| macOS | `macos`, with an 8-byte store, and `everest` |

The frames set:

- every number and flag to zero, and every string to null, as at the title
  screen;
- distinct values: the n-th number to n × `0x01010101` (or
  `0x0101010101010101` for a 64-bit one), so that neighbouring fields differ in
  every byte, flags alternately, and strings to different ones. Then the same
  negated, with each flag flipped and non-ASCII strings;
- every number to -1 (chapter -1 is "not in a chapter"), and every string empty;
- every number to its minimum, then its maximum;
- each flag alone.

A .NET string that's null isn't reported at all, and Everest's is reported
empty.

### Running it

Build the wasm module first (`cargo b --release` in `autosplitter/`). Then, from
this folder:

```sh
cargo run --release -- read
```

Or run the `Reading Test` task in VS Code, which builds the wasm module first.

It prints `ok` or the differences for each case, and exits with 1 if any case
failed. Options:

- `--wasm FILE`: the auto splitter (default: the release build in
  `autosplitter/`).
- `--layouts DIR`: the layouts (default: `layouts/`).
- `--settings FILE`: settings for the auto splitter, as a JSON object, whose
  objects become maps and arrays lists (default: none).
- `--timeout SECONDS`: how long each case may take (default: 60).

## The fake game

The harness copies itself to `target/release/game/`, under the game's process
name, and runs each case there as `case INDEX`, with the same options. A
process for each case also keeps each case's fake game from the others. The case
builds the whole fake game before it loads the auto splitter, since the runtime
only lists processes, and each process's memory ranges, once a second.

From a vanilla flavour's layout, it builds:

- for each root, code holding the root's signature, 200 bytes of filler, and a
  store into the root's static slot, with the slot's address in the store's `SS`
  bytes. The slot holds the root's object. The game has 80 to 144 bytes between
  the two, depending on the flavour, so only a search for the store finds it.
  Before that code, the same code stores into a slot holding an object whose
  title is "Not Celeste", which the auto splitter has to skip;
- every object along each path, and "Celeste" in `Title`.

What the auto splitter reads through 32-bit addresses is mapped below 4 GB:
everything on Windows, where pointers are 32-bit, and the slot on Linux with a
4-byte store. With an 8-byte store, the slot's address has `b8` as its fourth
byte, so that the 4-byte store pattern also matches inside the 8-byte store, and
the auto splitter has to take the earlier match.

From Everest's, it builds the info struct, with its magic and version, at the
start of a mapping of its own.

Each frame writes its values into the paths or fields with those names, as their
types say. Everest's flags are named like `ChapterFlags.TimerActive`.
