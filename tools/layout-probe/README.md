# layout-probe

Measures the memory layout the auto splitter reads, inside the game's own
runtime, and writes it as Rust, `<flavour>.rs`, which the auto splitter uses.
What it measures is listed in [`Manifest.cs`](Manifest.cs). The
[`layouts.yml`](../../.github/workflows/layouts.yml) workflow runs it against
every vanilla flavour and builds the auto splitter with its layout. Then the
[harness](../../harness/) starts it again, in its replay mode, for the live check
and the replay of every trace. When dispatched, the workflow then opens a pull
request with the results. The [layouts README](../../layouts/README.md)
describes the output.

In order, it:

1. detects the flavour: `windows-xna` or `windows-fna` from the game's
   references, `linux` or `macos` from the kernel;
2. resolves the manifest's names in `Celeste.exe`, and measures:
   - every path's offsets, with `ldflda` on uninitialized objects, since
     `Marshal.OffsetOf` doesn't work on auto-layout classes;
   - where strings hold their length and characters;
3. for each root, compiles the anchor method and checks the flavour's
   hand-written signature against its code: the signature must match once, and
   the earliest store pattern match after it must hold the static slot's
   address, and end within the window that `memory.rs` searches;
4. constructs the game (`new Celeste()`), writes distinctive values into every
   number and flag, and into strings that are still null, then reads every path
   back raw from the slot whose address the store holds. It does this twice,
   with different values;
5. prints the layout, and writes it if `--out` is given;
6. prints `probe: ok`. With `--replay`, it then takes values to write into the
   game: see [Replay mode](#replay-mode).

It prints what it finds along the way, including every field of each type along
the paths, and the anchor's machine code. It stops at the first failure, prints
`probe: failed`, and exits with 1, or with 2 for bad arguments.

## The manifest

`Manifest.cs` lists the roots the auto splitter reads from. Each root is a
static field that the auto splitter finds through machine code:

- **`Field`**: the static field, as `Namespace.Type::Field`.
- **`Anchor`**: the method whose compiled code holds the field's address, as
  `Namespace.Type::Name(ParameterType, ...)`.
- **`Signatures`**: per flavour, how to find the field's address in the
  anchor's code. Each has:
  - a signature that matches the anchor's code once: hex bytes, with `??` for
    any byte;
  - store patterns for the code that stores into the field after it, with `SS`
    for the field's address: 4 or 8 bytes in a row. The earliest match of any
    of them after the signature holds the address: earliest, because a 4-byte
    pattern can also match inside an 8-byte store. They're searched for rather
    than expected at a fixed distance, because the instructions in between can
    change length. On Linux, an address takes 4 or 8 bytes depending on where
    the heap is.

  Once it has checked them, the probe writes them into each flavour's layout,
  and [`memory.rs`](../../autosplitter/src/memory.rs) finds the address from
  them the same way. It only searches the 256 bytes after each signature match:
  `StoreWindow` in `Manifest.cs`, which the probe writes into the layouts too.
- **`Paths`**: instance fields to read from the root's object, joined with dots.
  Each name is looked up in the previous field's type, or its nearest base
  class with that field. A path only goes through objects of concrete classes,
  and ends in a `string`, `bool`, `int`, `long` or object.

To add a root, add it without signatures first. The probe then prints the
anchor's code and where the field's address appears in it, and stops. Write
each flavour's signature and store patterns from that, here.

## Replay mode

With `--replay`, once every check has passed, the probe takes values from its
standard input and writes them into the game, for the [harness](../../harness/).
First it prints `probe: values`, and the values the paths hold as JSON on one
line, keyed by root and then path: those of its second pass, which it read back
raw. Then it reads sets of values, until its input ends:

- a `path=value` line per path, with the value read as the path's type: `true`
  or `false`, a whole number, or for a string, the rest of the line;
- an empty line at the end of each set.

It writes each set into the game through reflection, into the objects it read
back raw, then prints `probe: written`. A path is named without its root, so the
replay fails if two roots have paths with the same name. If a line can't be
written, it prints why, then `probe: failed`, and exits with 1.

Started from a terminal, it keeps the game until you end its input, so another
program, such as the asr debugger, can read it, while you type in values.

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
./Celeste.exe --game real/Celeste.exe --out <folder>
```

The layouts in the repository only come from the workflow, so write somewhere
else.

Options:

- `--out DIR`: writes `DIR/<flavour>.rs`, once every check has passed.
- `--game PATH`: the game's `Celeste.exe`, relative to the probe (default:
  `Celeste.exe` next to it).
- `--replay`: once every check has passed, takes values to write into the game,
  until its input ends (see [Replay mode](#replay-mode)). The harness adds it.

The Linux script changes to the game's folder first, and passes its arguments on
unquoted, so give `--out` an absolute path without spaces.
