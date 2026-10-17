# probe

Runs inside the game's own runtime, posing as the game. There, it measures the
memory layout the auto splitter reads, and writes it as Rust, `<flavour>.rs`,
which the auto splitter uses. What it measures is listed in
[`Manifest.cs`](Manifest.cs). It also constructs the real game, which, in its
replay mode, it holds for the [harness](../../harness/) to check the auto
splitter against. The probe only checks what it measures: the harness checks
what the auto splitter reads.

The [`layouts.yml`](../../.github/workflows/layouts.yml) workflow runs it against
every vanilla flavour and builds the auto splitter with its layout. Then the
harness starts it again, in its replay mode, for the live check and the replay of
every trace. When dispatched, the workflow then opens a pull request with the
results. The [layouts README](../../layouts/README.md) describes the output.

In order, it:

1. detects the flavour: `windows-xna` or `windows-fna` from the game's
   references, `linux` or `macos` from the kernel;
2. resolves the manifest's names in `Celeste.exe`, and measures:
   - every path's offsets, with `ldflda` on uninitialized objects, since
     `Marshal.OffsetOf` doesn't work on auto-layout classes;
   - where strings hold their length and characters;
3. for each root, compiles the anchor method and checks the flavour's
   hand-written signature against its code: the signature must match once, and
   the earliest reference pattern match after it must hold the static slot's
   address, and end within the window that `memory.rs` searches;
4. constructs the game (`new Celeste()`), which sets the game's version that
   the layout records, and finds the object that holds each path's last field.
   A root the game leaves null until later, as it does `SaveData.Instance`
   until a file starts, gets an uninitialized object of the probe's own, so
   that the probe can write values into it;
5. prints the layout, and writes it if `--out` is given;
6. prints `probe: ok`. With `--replay`, it then writes distinctive values into
   the game, and takes values to write: see [Replay mode](#replay-mode).

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
  - reference patterns for an instruction after it that stores into the field
    or loads from it, with `SS` for the field's address: 4 or 8 bytes in a row.
    The earliest match of any of them after the signature holds the address:
    earliest, because a pattern with a 4-byte address can also match inside
    one with an 8-byte address. They're searched for rather than expected at a
    fixed distance, because the instructions in between can change length. On
    Linux, an address takes 4 or 8 bytes depending on where the heap is.

  Once it has checked them, the probe writes them into each flavour's layout,
  and [`memory.rs`](../../autosplitter/src/memory.rs) finds the address from
  them the same way. It only searches the 256 bytes after each signature match:
  `ReferenceWindow` in `Manifest.cs`, which the probe writes into the layouts
  too.
- **`Paths`**: instance fields to read from the root's object, joined with dots.
  Each name is looked up in the previous field's type, or its nearest base
  class with that field. A path only goes through objects of concrete classes,
  and ends in a `string`, `bool`, `int` or `long`, which the probe can write for
  the harness to check. The paths of a root the game leaves null until later
  are single fields of its object, since the probe's object has nothing in its
  fields.

To add a root, add it without signatures first. The probe then prints the
anchor's code and where the field's address appears in it, and stops. Write
each flavour's signature and reference patterns from that, here.

## Replay mode

With `--replay`, once every check has passed, the probe writes values into the
game, for the [harness](../../harness/). First, it writes distinctive values into
every number and flag, and into strings that are still null: every number and
string gets a value of its own, and the harness tells the flags apart by setting
each alone. The title keeps "Celeste", which the auto splitter checks for. It
prints `probe: values`, and as JSON on one line, its flavour, the values the
paths hold, keyed by root and then path, and the roots it set up:

```json
{"flavour":"windows-xna","roots":{"Celeste.Celeste::Instance":{"Title":"Celeste",...},"Celeste.SaveData::Instance":{"Time":1157442765409226768,"DebugMode":true}},"setUp":["Celeste.SaveData::Instance"]}
```

Then it reads sets of lines from its standard input, until its input ends, with
an empty line at the end of each set:

- `root=null` stores null in the root's static, and `root=object` stores the
  root's object in it again;
- `path=value` writes a value through the path, read as the path's type: `true`
  or `false`, a whole number, or for a string, the rest of the line.

It writes each set into the game through reflection, then prints
`probe: written`. A path is named without its root, so the replay fails if two
roots have paths with the same name. If a line can't be written, it prints why,
then `probe: failed`, and exits with 1.

Started from a terminal, it keeps the game until you end its input, so another
program, such as the asr debugger, can read it, while you type in values.

## Building

Requires the .NET SDK. No .NET Framework developer pack is needed.

```sh
dotnet build tools/probe -c Release
```

Output: `tools/probe/bin/Release/net45/Probe.exe`.

## Running

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
