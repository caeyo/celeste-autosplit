# harness

The live check and the replay: they check the auto splitter,
`celeste_autosplit.wasm`, against the real game. It's a native program, outside
the wasm build.

The harness starts the [probe](../tools/probe/) as the game, with its `host`
command. The probe constructs the real game, in the game's own runtime. The
probe runs as the game, so the auto splitter, which the harness loads, finds the
probe's process by the game's name. The harness holds a table of every value
the auto splitter reports: its static field, its path of instance fields, its
type, and the debug variable it's reported as. The probe writes each set of
values the harness sends into the game, through .NET's own reflection, and the
harness updates the auto splitter once. Its debug variables must then be
exactly the values the game holds:

- **The live check:** first distinctive values for every path in the table,
  then the same values with every number negated and every flag flipped, then
  each flag set alone. Every number and string has a value of its own, spread
  over all its bytes, and setting each flag alone tells the flags apart. So
  reading the wrong field, or the wrong width, shows in every field, and so does
  a flag that only ever reads one way.
- **The replay:** then each line of each [trace](../traces/) given: its changes
  to the game's values, over all the values before. A value a trace doesn't
  record keeps the one it had, and a path that isn't in the table fails.

The live check catches layout mistakes whatever traces there are. The replay
adds the values only real play produces.

So a pass rests on the game's own runtime and one comparison per update, with no
stand-in for the game. It checks:

- finding `Celeste.Instance`: the signature, the store into its slot, and the
  store of `AutoSplitterInfo`, which holds that field's offset;
- finding `SaveData.Instance`: the signature, and the load from its slot;
- every offset and pointer size `memory.rs` reads through;
- reading .NET strings, numbers and flags: the live check's distinctive values,
  and the values real play produces, such as empty strings;
- attaching to the game's process, on each OS.

The auto splitter runs on livesplit-core's own `Timer`, behind the runtime's
`Timer` trait, as in livesplit-core's own auto splitting, and both are pinned to
one revision in `Cargo.toml`. The harness records the debug variables the auto
splitter sets and what it logs. It updates the runtime by hand: until the auto
splitter reports, then once for each of the live check's other sets of values,
and once per line of each trace. A runtime updated by hand has no watchdog of
its own, so an update that hangs hangs the harness: in CI, the step's timeout
stops it, without the auto splitter's log.

## In CI

The [Live check workflow](../.github/workflows/live-check.yml) runs it on every
vanilla flavour, with every trace in `traces/`.

## Running it

Set up the probe in the game's folder, as
[Maintaining](../docs/maintaining.md#running-the-probe) describes. Then, with the
wasm module built (`cargo b --release` in `autosplitter/`, without
`CARGO_TARGET_DIR`, since the harness reads it from `autosplitter/target/`),
from this folder, give the traces to replay, and after `--`, the command that
starts the probe as the game. On Windows, for example:

```sh
cargo run --release -- ../traces/prologue.jsonl -- <game folder>/Celeste.exe host
```

Without traces, it only runs the live check. It prints `ok` for the live check
and for each trace, or the differences and the auto splitter's log, and exits
with 1 if they differ. If the probe fails, stops answering, or doesn't exit once
the harness is done, it prints the probe's log.
A probe that doesn't exit is killed: on Linux and macOS, that's the `xvfb-run`
or `sudo` in front of it, so a probe that hangs keeps running. It waits 120 s
for the probe to be ready, then for the auto splitter to report, then for each
answer from the probe, and at the end, for the probe to exit, which after an
error gets 5 s.

On Linux, the probe is the harness's descendant, whose memory Yama lets it read.
On macOS, reading another process's memory needs root: build the harness, run
`sudo target/release/harness ...`, and put `sudo -u <you>` before the
probe's command, so that it runs as you, as players run the game.

## What it doesn't check

- Everest, since no probe constructs it. Its offsets rest on the transcription
  in [`everest.rs`](../autosplitter/src/everest.rs).
- Vanilla started through an Everest install, which runs on an FNA of Everest's,
  and in a second AppDomain of Everest's process up to `stable-1.4449.0`, the last
  version before .NET Core. It was checked once by hand on Windows, with
  `stable-1.4449.0` and `stable-1.6662.0`, on both builds of the game.
- Values that neither the live check nor a trace writes, such as the smallest and
  largest numbers.
- `SaveData.Instance` holding null, as it does until a file starts: the probe
  always holds a save of its own, so nothing checks that `Deaths` goes unreported
  without one.
- The rules for code the game doesn't have: a second match of the signature,
  and a 4-byte reference pattern matching inside an 8-byte one.
- Reads in the middle of the game's update: the probe writes each line whole,
  between two updates of the auto splitter.
- Reading at the auto splitter's own tick rate, 120 Hz by default, which hosts
  follow: the harness updates it once per line of a trace, which is once per
  frame of the game.
