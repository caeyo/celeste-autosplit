# Maintaining

## Line endings

Shell scripts keep LF endings on every OS: bash fails on CRLF, which checkouts on
Windows would give them. Traces and their TAS files keep the LF endings the
recorder and CelesteTAS write. `.gitattributes` sets both.

## The probe

- It targets .NET Framework 4.5 (`net45`), as `Celeste.exe` does, since it runs in
  the game's own runtime. On Linux and macOS, that's the Mono bundled with the
  game, which only has the 4.5 profile's assemblies.
- `Celeste.exe` is marked x86-only, so on Windows only a 32-bit process can load
  it. Mono ignores the flag and runs 64-bit.

## Running the probe

Building it requires the .NET SDK, without a .NET Framework developer pack:

```sh
dotnet build tools/probe -c Release
```

Output: `tools/probe/bin/Release/net45/Probe.exe`.

It has to run in the game's own runtime, from the game's folder. The auto
splitter finds the game by process name, so the probe runs as the game: it
becomes `Celeste.exe`, the game's own `Celeste.exe` moves to `real/`, and it's
started the way players start the game.

| Flavour | How |
|---|---|
| Windows (XNA or FNA) | Move `Celeste.exe` (and `Celeste.exe.config`, if there is one) to `real/`, copy `Probe.exe` to `Celeste.exe`, and run it. It's 32-bit, like the game. XNA builds need the XNA 4.0 redistributable. |
| Linux | Move `Celeste.exe` and `Celeste.exe.config` to `real/`, copy `Probe.exe` to `Celeste.exe`, and run the `Celeste` script. FNA needs a display for its window: Xvfb works. |
| macOS | In `Celeste.app/Contents/Resources/`, move `Celeste.exe` to `real/` and copy `Probe.exe` to `Celeste.exe`. Then run `Celeste.app/Contents/MacOS/Celeste`. |

For example, on Windows, from the game's folder:

```sh
./Celeste.exe dump
```

Started from a terminal, `host` keeps the game until you end its input, so
another program, such as the asr debugger, can read it while you type in values.

The Live check workflow prints the dump on every vanilla flavour, after the
harness. To collect it for a branch, dispatch it:
`gh workflow run live-check.yml --ref <branch>`.

## Keeping the layouts

[`vanilla.rs`](../autosplitter/src/vanilla.rs) is kept by hand from the dump's
output. Both Windows flavours give `CLR32`'s values, and Linux and macOS give
`MONO64`'s. The live check on all four flavours is the evidence that they're
right.

To read another value of a root the auto splitter already finds:

1. Find its offset in the dump's field tables, in
   [`tools/probe/fields/`](../tools/probe/fields/) or a Live check run's dump.
   `0x018  AutoSplitterInfo.Chapter : System.Int32` is `Chapter`'s offset after
   `AutoSplitterInfo`.
2. Add a field to `Layout` in `vanilla.rs`, with its offset in both `CLR32` and
   `MONO64`.
3. Add it to `Values` in [`memory.rs`](../autosplitter/src/memory.rs), as an
   `Option` if it can be missing, read it in `Vanilla::read`, and report it in
   [`lib.rs`](../autosplitter/src/lib.rs).
4. Add its root, path, kind and debug variable to `PATHS` in the
   [harness](../harness/src/main.rs), so that the live check writes it and checks
   what the auto splitter reports. The host can only write a string, a `bool`, an
   `int` or a `long`.

To add a root:

1. Add its anchor and its static field to `Anchors` in
   [`Game.cs`](../tools/probe/Game.cs), as `Namespace.Type::Name()` and
   `Namespace.Type::Field`. The anchor is a method the game runs before the auto
   splitter needs the root, whose code loads or stores the field. The host then
   compiles it, and the dump prints the slot's address, the anchor's code, and
   where the slot's address appears in it.
2. From each runtime's code, write a signature that matches it once, before the
   address, and reference patterns for the instruction that holds the address.
   On Mono, write one for each width the address can take.
3. Add an `Anchor` for it to `Layout`, with its patterns in both `CLR32` and
   `MONO64`, and in `memory.rs`, a `scan!` of it in each `Runtime`, and a search
   for its slot like `find_save_data`'s.
4. Add its values as above, with a constant for the root in the harness. If the
   game leaves the slot null until later, as it leaves `SaveData.Instance` until
   a file starts, give the host an object for it, as
   [`Host.cs`](../tools/probe/Host.cs) does for `SaveData.Instance`.

## The harness

- The probe's standard error is piped into its log, like its output. On Linux and
  macOS, the probe runs behind `xvfb-run` or `sudo`, so killing it can leave it
  running, and a probe left running would otherwise hold the harness's own output
  open.
- The probe's output is read as lossy UTF-8: on Windows, paths are in the
  console's code page.

## The Live check workflow

- The game files are release assets of a private repository, fetched with the
  `GAME_FILES_TOKEN` secret: only what the probe needs to construct the game,
  without `Content`, which it never reads. They never go into caches or
  artifacts, since artifacts can be downloaded by anyone who can read this
  repository, and caches restored by any run in it.
- Pull requests from forks can't have the token, so the workflow skips them. To
  check one, review it, then push its branch to this repository.
- Only `~/.cargo` and the target directories are cached, and the two Windows
  flavours share one cache. A pull request only restores the caches of its own
  runs and of `master`'s, so the runs on `master` keep every pull request's
  first run warm.
- The game is x86_64 only: on the arm64 macOS runner, it runs under Rosetta.
- The game's code runs in the job, so its checkout doesn't keep the token:
  `persist-credentials: false`.
- XNA's assemblies load from the GAC, so the XNA build needs the XNA 4.0
  redistributable installed. It also installs the VC++ 2010 runtime and the
  DirectX pieces XNA's assemblies import.
- On Linux, the `Celeste` script runs `Celeste.bin.x86_64`, a MonoKickstart that
  runs `Celeste.exe` with the game's own Mono. On macOS,
  `Celeste.app/Contents/MacOS/Celeste` is the MonoKickstart, and runs
  `Resources/Celeste.exe`.
- FNA needs a display to create its window, so on Linux the probe runs under
  `xvfb-run`. The runner image already has Xvfb and Mesa, and the window never
  gets a GL context, so software rendering is enough.
- The harness starts the probe, so on Linux the probe is its descendant, whose
  memory Yama lets it read. On macOS, reading another process's memory needs
  root, so the harness runs with `sudo` and starts the probe with
  `sudo -u` as the normal user, as players run the game.
- `release.yml` calls it, passing the token, and publishes only once it passes
  on every flavour.
