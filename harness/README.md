# harness

The live check: checks the auto splitter, `celeste_autosplit.wasm`, against the
real game. It's a native program, outside the wasm build.

The [layout probe](../tools/layout-probe/) constructs the real game, in the
game's own runtime. Through .NET's own reflection, it sets every number and flag
the auto splitter reads, and every string that's still null, and reads each one
back raw, as the auto splitter does. With `--values`, it then writes the values
the game holds, as reflection reads them, and with `--hold` it keeps the game
and its process alive. The probe runs as the game, so the harness loads the auto
splitter, which finds the probe's process by the game's name. Its debug
variables must be exactly those values.

So a pass rests on the game's own runtime and one comparison, with no stand-in
for the game. It checks:

- finding `Celeste.Instance`: the signature, the store into its slot, and the
  title, which tells XNA and FNA apart;
- every offset and pointer size `memory.rs` reads through;
- reading .NET strings, numbers and flags;
- attaching to the game's process, on each OS.

The auto splitter runs on livesplit-core's own `Timer`, behind the runtime's
`Timer` trait, as in livesplit-core's own auto splitting, and both are pinned to
one revision in `Cargo.toml`. The harness records the debug variables the auto
splitter sets and what it logs. It steps the runtime by hand, until the auto
splitter reports, and interrupts it at the timeout, since a runtime stepped by
hand has no watchdog of its own.

## In CI

The [Layouts workflow](../.github/workflows/layouts.yml) runs the live check
on every vanilla flavour, right after the probe, with the auto splitter rebuilt
from the layout the probe has just written.

## Running it

Set up the probe in the game's folder, as its README describes, and start it.
On Windows, for example:

```sh
./Celeste.exe --game real/Celeste.exe --values values.json --hold 600
```

Then, with the wasm module built (`cargo b --release` in `autosplitter/`), from
this folder:

```sh
cargo run --release -- live --values <game folder>/values.json
```

It waits for the values file, which the probe writes once every check has
passed. It prints `ok`, or the differences and the auto splitter's log, and
exits with 1 if they differ. Options:

- `--values FILE`: the values the probe holds.
- `--wasm FILE`: the auto splitter (default: the release build in
  `autosplitter/`).
- `--timeout SECONDS`: how long to wait in all, for the values and then for the
  auto splitter to report them (default: 120).

## What it doesn't check

- Everest, since no probe constructs it. Its offsets rest on the transcription
  in [`layouts/everest.rs`](../layouts/everest.rs).
- Values the probe doesn't hold: it holds one set, the same on every run. So
  empty strings, and the smallest and largest numbers, are never read.
- The rules for code the game doesn't have: a second match of the signature,
  and a 4-byte store pattern matching inside an 8-byte store. The probe checks
  that the real store is within the window `memory.rs` searches.
