# layout-probe

Measures the memory layout the auto splitter reads, inside the game's own
runtime, and writes it as `<flavour>.json`. What it measures is listed in
[`Manifest.cs`](Manifest.cs). The
[`layouts.yml`](../../.github/workflows/layouts.yml) workflow runs it against
every vanilla flavour and opens a pull request with the results. The
[layouts README](../../layouts/README.md) describes the output.

In order, it:

1. detects the flavour: `windows-xna` or `windows-fna` from the game's
   references, `linux` or `macos` from the kernel;
2. resolves the manifest's names in `Celeste.exe`, and measures:
   - every path's offsets, with `ldflda` on uninitialized objects, since
     `Marshal.OffsetOf` doesn't work on auto-layout classes;
   - where strings hold their length and characters;
3. for each root, compiles the anchor method and checks the flavour's
   hand-written signature against its code, as the auto splitter will use it:
   the signature must match once, and the first store pattern to match after it
   must hold the static slot's address;
4. constructs the game (`new Celeste()`), writes distinctive values into every
   field that still holds its default, and reads every path back raw from the
   slot whose address the store holds. It does this twice, with different
   values;
5. prints the layout, and writes it if `--out` is given.

It prints what it finds along the way, including every field of each type along
the paths, and the anchor's machine code. It stops at the first failure, and
exits with 1.

## The manifest

`Manifest.cs` lists the roots the auto splitter reads from. Each root is a
static field that the auto splitter finds through machine code:

- **`Field`**: the static field, as `Namespace.Type::Field`.
- **`Anchor`**: the method whose compiled code holds the field's address, as
  `Namespace.Type::Name(ParameterType, ...)`.
- **`Signatures`**: per flavour, how the auto splitter finds the field's
  address in the anchor's code. They're copies of the ones in
  [`memory.rs`](../../autosplitter/src/memory.rs). Each has:
  - a signature that matches the anchor's code once: hex bytes, with `??` for
    any byte;
  - store patterns for the code that stores into the field after it, with `SS`
    for the field's address: 4 or 8 bytes in a row. The first one to match
    after the signature holds the address. They're searched for rather than
    expected at a fixed distance, because the instructions in between can
    change length. On Linux, an address takes 4 or 8 bytes depending on where
    the heap is.
- **`Paths`**: instance fields to read from the root's object, joined with dots.
  Each name is looked up in the previous field's type, or its nearest base
  class with that field. A path only goes through objects of concrete classes,
  and ends in a `string`, `bool`, `int`, `long` or object.

A new root needs a signature for every flavour, both here and in `memory.rs`.
The probe prints where the field's address appears in the anchor's code, to
help write the store patterns.

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
./Celeste.exe --game real/Celeste.exe --out <repo>/layouts
```

Options:

- `--out DIR`: writes `DIR/<flavour>.json`, once every check has passed.
- `--game PATH`: the game's `Celeste.exe`, relative to the probe (default:
  `Celeste.exe` next to it).
- `--hold SECONDS`: stays alive afterwards, so the process can be read from
  outside. It prints `holding for N s` once it's ready.

The Linux script changes to the game's folder first, and passes its arguments on
unquoted, so give `--out` an absolute path without spaces.
