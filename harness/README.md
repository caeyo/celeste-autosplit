# harness

The live check and the replay: they check the auto splitter,
`celeste_autosplit.wasm`, against the real game. It's a native program, outside
the wasm build.

The harness starts the [probe](../tools/probe/) as the game, in the probe's
replay mode. The probe constructs the real game, in the game's own runtime,
with an object of its own for each root the game leaves null until later, such
as `SaveData.Instance`. Through .NET's own reflection, it writes distinctive
values into every number and flag the auto splitter reads, and every string
that's still null, and sends the harness those values. The probe runs as the game, so the auto splitter,
which the harness loads, finds the probe's process by the game's name. From
then on, the probe writes each set of values the harness sends into the game,
through reflection, and the harness updates the auto splitter once. Its debug
variables must then be exactly the values the game holds:

- **The live check:** first the probe's values, then the same values with every
  number negated and every flag flipped, then each flag set alone, then each
  root the probe set up null, and back. Every number and string has a value of
  its own, spread over all its bytes, and setting each flag alone tells the
  flags apart. So reading the wrong field, or the wrong width, shows in every
  field, and so does a flag that only ever reads one way. While a root is
  null, the auto splitter reports none of its values, and once it's back, it
  has to report them again.
- **The replay:** then each line of each [trace](../traces/) given: its changes
  to the game's values, over all the values before. A value a trace doesn't
  record keeps the one it had, and a root that holds null keeps its values for
  when it's back.

The live check catches layout mistakes whatever traces there are. The replay
adds the values only real play produces.

So a pass rests on the game's own runtime and one comparison per update, with no
stand-in for the game. It checks:

- finding each root's slot: the signature, and the reference to its slot,
  and for `Celeste.Instance`, the title, which tells XNA and FNA apart, and for
  `SaveData.Instance`, the version check, with a save's version null, as the
  traces have it until the game first saves, and set;
- reading a slot that holds null as no object, not as a failed read that the
  auto splitter would search again for;
- every offset and pointer size `memory.rs` reads through;
- reading .NET strings, numbers and flags: the live check's distinctive values,
  and the values real play produces, such as empty strings;
- attaching to the game's process, on each OS.

The auto splitter runs on livesplit-core's own `Timer`, behind the runtime's
`Timer` trait, as in livesplit-core's own auto splitting, and both are pinned to
one revision in `Cargo.toml`. The harness records the debug variables the auto
splitter sets and what it logs. It updates the runtime by hand: until the auto
splitter reports, then once for each of the live check's other sets of values,
and once per line of each trace. A runtime updated by hand has no watchdog of its own, so a thread
interrupts any update that runs past the timeout.

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
A probe that doesn't exit is killed: on Linux and macOS, that's the `xvfb-run`
or `sudo` in front of it, so a probe that hangs keeps running.
Options:

- `--wasm FILE`: the auto splitter (default: the release build in
  `autosplitter/`).
- `--timeout SECONDS`: how long to wait for the probe's checks, then for the auto
  splitter to report, then for each answer from the probe and each update of the
  auto splitter, and at the end, for the probe to exit, which after an error
  gets at most 5 s (default: 120).

On Linux, the probe is the harness's descendant, whose memory Yama lets it read.
On macOS, reading another process's memory needs root: build the harness, run
`sudo target/release/harness replay ...`, and put `sudo -u <you>` before the
probe's command, so that it runs as you, as players run the game.

## What it doesn't check

- Everest, since no probe constructs it. Its offsets rest on the transcription
  in [`layouts/everest.rs`](../layouts/everest.rs).
- Values that neither the probe nor a trace holds, such as the smallest and
  largest numbers.
- The anchors as the game compiles them: the probe compiles each itself, before
  constructing the game. `AutoSplitterInfo.Update` was checked by hand to be
  found at the title screen of the real game, on Windows with XNA.
- Searching again for a slot not found yet, as when the auto splitter attaches
  before the game's first update: the probe has compiled every anchor by then.
- The rules for code the game doesn't have: a second match of the signature,
  and a reference pattern with a 4-byte address matching inside one with an
  8-byte address. The probe checks that the real reference is within the
  window `memory.rs` searches.
- Reads in the middle of the game's update: the probe writes each line whole,
  between two updates of the auto splitter.
- Reading at the auto splitter's own tick rate, 120 Hz by default, which hosts
  follow: the harness updates it once per line of a trace, which is once per
  frame of the game.
