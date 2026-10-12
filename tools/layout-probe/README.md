# layout-probe

Measures the memory layout the auto splitter reads, inside the game's own
runtime, and writes it as `<flavour>.json`. What it measures comes from
[`layouts/manifest.json`](../../layouts/manifest.json). The
[`layouts.yml`](../../.github/workflows/layouts.yml) workflow runs it against
every vanilla flavour and opens a pull request with the results. The
[layouts README](../../layouts/README.md) describes both files.

In order, it:

1. detects the flavour: `windows-xna` or `windows-fna` from the game's
   references, `linux` or `macos` from the kernel;
2. resolves the manifest's names in `Celeste.exe`, and measures:
   - every path's offsets, with `ldflda` on uninitialized objects, since
     `Marshal.OffsetOf` doesn't work on auto-layout classes;
   - where strings and arrays hold their lengths and contents;
3. for each root, compiles the anchor method, finds where its code references
   the static slot, and checks that the flavour's hand-written signature
   ([`Signatures.cs`](Signatures.cs), a copy of the ones in
   `autosplitter/src/memory.rs`) matches once before it;
4. constructs the game (`new Celeste()`), writes distinctive values into every
   field that still holds its default, and reads every path back raw from the
   slot whose address the code holds, as the auto splitter does. It does this
   twice, with different values;
5. prints the layout, and writes it if `--out` is given.

It prints what it finds along the way, including every field of each type along
the paths, and the anchor's machine code. It stops at the first failure, and
exits with 1.

## Building

Requires the .NET SDK. No .NET Framework developer pack is needed.

```sh
dotnet build tools/layout-probe -c Release
```

Output: `tools/layout-probe/bin/Release/net45/LayoutProbe.exe`.

## Running

It has to run in the game's own runtime, from the game's folder. The auto
splitter finds the game by process name, so the probe runs as the game: it
becomes `Celeste.exe`, the game's own `Celeste.exe` moves to `real/`, and it's
started the way players start the game.

| Flavour | How |
|---|---|
| Windows (XNA or FNA) | Move `Celeste.exe` (and `Celeste.exe.config`, if there is one) to `real/`, copy `LayoutProbe.exe` to `Celeste.exe`, and run it. It's 32-bit, like the game. XNA builds need the XNA 4.0 redistributable. |
| Linux | Move `Celeste.exe` and `Celeste.exe.config` to `real/`, copy `LayoutProbe.exe` to `Celeste.exe`, and run the `Celeste` script. FNA needs a display for its window: Xvfb works. |
| macOS | In `Celeste.app/Contents/Resources/`, move `Celeste.exe` to `real/` and copy `LayoutProbe.exe` to `Celeste.exe`. Then run `Celeste.app/Contents/MacOS/Celeste`. |

For example, on Windows, from the game's folder:

```sh
./Celeste.exe --game real/Celeste.exe --manifest <repo>/layouts/manifest.json --out <repo>/layouts
```

Options:

- `--manifest PATH`: the manifest. Required.
- `--out DIR`: writes `DIR/<flavour>.json`, once every check has passed.
- `--game PATH`: the game's `Celeste.exe`, relative to the probe (default:
  `Celeste.exe` next to it).
- `--hold SECONDS`: stays alive afterwards, so the process can be read from
  outside. It prints `holding for N s` once it's ready.

The Linux script changes to the game's folder first, and passes its arguments on
unquoted, so give absolute paths without spaces.
