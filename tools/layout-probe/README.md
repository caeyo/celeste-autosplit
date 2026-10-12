# layout-probe

Prints the memory layout the auto splitter reads, measured inside the game's own
runtime. For now it's a spike: it prints text, and the
[`layouts.yml`](../../.github/workflows/layouts.yml) workflow runs it against
every vanilla flavour.

It prints:

- every instance field of `Celeste.Celeste` and `Celeste.AutoSplitterInfo`, and
  `String`'s length and characters, at their offsets from the object's address.
  The offsets are measured with `ldflda`, then checked against values written
  through reflection;
- the address of the `Celeste.Instance` slot, the `Celeste` constructor's
  compiled code, where the slot's address appears in it, and whether the
  hand-written signatures in `autosplitter/src/memory.rs` agree.

## Building

Requires the .NET SDK. No .NET Framework developer pack is needed.

```sh
dotnet build tools/layout-probe -c Release
```

Output: `tools/layout-probe/bin/Release/net45/LayoutProbe.exe`.

## Running

It has to run in the game's own runtime, from the game's folder:

| Flavour | How |
|---|---|
| Windows (XNA or FNA) | Copy `LayoutProbe.exe` next to `Celeste.exe` and run it. It's 32-bit, as the game is. XNA builds need the XNA 4.0 redistributable. |
| Linux | Copy `LayoutProbe.exe` next to `Celeste.exe`, copy `Celeste.bin.x86_64` to `LayoutProbe.bin.x86_64`, and run that. The game's launcher runs the `.exe` named after itself, with the game's own Mono. |
| macOS | Copy `LayoutProbe.exe` into `Celeste.app/Contents/Resources/`, copy `Contents/MacOS/Celeste` to `Contents/MacOS/LayoutProbe`, and run that. |

Options:

- `--construct`: also runs `new Celeste()` and reads the result back through
  the slot, as the auto splitter does. Linux needs a display for FNA's window
  (Xvfb works).
- `--game PATH`: the game's `Celeste.exe`, relative to the probe. The auto
  splitter finds the game by process name, so for it to attach to the probe,
  the probe has to be `Celeste.exe` and launched like the game, with the game's
  assembly moved elsewhere (for example `--game real/Celeste.exe`).
- `--hold SECONDS`: stays alive afterwards.
