# harness

The live check and the replay: they check the auto splitter,
`celeste_autosplit.wasm`, against the real game. It's a native program, outside
the wasm build.

The harness starts the [layout probe](../tools/layout-probe/) as the game, in the
probe's replay mode. The probe constructs the real game, in the game's own
runtime. Through .NET's own reflection, it sets every number and flag the auto
splitter reads, and every string that's still null, and reads each one back raw,
as the auto splitter does. Once every check has passed, it sends the harness the
values the game holds. The probe runs as the game, so the auto splitter, which
the harness loads, finds the probe's process by the game's name. Then:

- **The live check:** the auto splitter's debug variables must be exactly the
  probe's values.
- **The replay:** for each line of each [trace](../traces/) given, the harness
  sends the probe the line's changes to the game's values. Once the probe has
  written them into the game, through reflection, the harness updates the auto
  splitter once. Its debug variables must be exactly the values the game holds:
  the probe's, with every line so far over them. A value a trace doesn't record
  keeps the one it had.

So a pass rests on the game's own runtime and one comparison per update, with no
stand-in for the game. It checks:

- finding `Celeste.Instance`: the signature, the store into its slot, and the
  title, which tells XNA and FNA apart;
- every offset and pointer size `memory.rs` reads through;
- reading .NET strings, numbers and flags: the probe's distinctive values, and
  the values real play produces, such as empty strings;
- attaching to the game's process, on each OS.

The auto splitter runs on livesplit-core's own `Timer`, behind the runtime's
`Timer` trait, as in livesplit-core's own auto splitting, and both are pinned to
one revision in `Cargo.toml`. The harness records the debug variables the auto
splitter sets and what it logs. It updates the runtime by hand: until the auto
splitter reports, then once per line of each trace. A runtime updated by hand
has no watchdog of its own, so a thread interrupts any update that runs past the
timeout.

## In CI

The [Layouts workflow](../.github/workflows/layouts.yml) runs it on every
vanilla flavour, with every trace in `traces/`, once the probe has measured the
layout and the auto splitter has been rebuilt with it.

## Running it

Set up the probe in the game's folder, as its README describes. Then, with the
wasm module built (`cargo b --release` in `autosplitter/`), from this folder, give
the traces to replay, and after `--`, the command that starts the probe as the
game. On Windows, for example:

```sh
cargo run --release -- replay ../traces/prologue.jsonl -- <game folder>/Celeste.exe --game real/Celeste.exe
```

It adds `--replay` to the command. Without traces, it only runs the live check.
It prints `ok` for the live check and for each trace, or the differences and the
auto splitter's log, and exits with 1 if they differ. If the probe fails, stops
answering, or doesn't exit once the harness is done, it prints the probe's log.
Options:

- `--wasm FILE`: the auto splitter (default: the release build in
  `autosplitter/`).
- `--timeout SECONDS`: how long to wait for the probe's checks, then for the auto
  splitter to report, then for each answer from the probe and each update of the
  auto splitter, and at the end, for the probe to exit (default: 120).

On Linux, the probe is the harness's descendant, whose memory Yama lets it read.
On macOS, reading another process's memory needs root: run the harness with
`sudo`, and the probe as yourself, with `sudo -u <you>` in its command.

## What it doesn't check

- Everest, since no probe constructs it. Its offsets rest on the transcription
  in [`layouts/everest.rs`](../layouts/everest.rs).
- Values that neither the probe nor a trace holds, such as the smallest and
  largest numbers.
- The rules for code the game doesn't have: a second match of the signature,
  and a 4-byte store pattern matching inside an 8-byte store. The probe checks
  that the real store is within the window `memory.rs` searches.
- Reads in the middle of the game's update: the probe writes each line whole,
  between two updates of the auto splitter.
- Reading at the auto splitter's own tick rate, 120 Hz by default, which hosts
  follow: the harness updates it once per line of a trace, which is once per
  frame of the game.
