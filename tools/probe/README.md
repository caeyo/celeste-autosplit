# probe

Runs inside the game's own runtime, posing as the game, with one of two
commands: `host` holds the real game for the [harness](../../harness/) to check
the auto splitter against, and `dump` prints what collecting a layout by hand
needs. Both load the game from `real/Celeste.exe` beside the probe.

The [`live-check.yml`](../../.github/workflows/live-check.yml) workflow
installs it as the game on every vanilla flavour. The harness starts its host
for the live check and the replay of every trace, then the workflow runs its
dump.

`dump` prints:

1. the pointer size, and where strings hold their length and characters;
2. every instance field's offset, name and type, for `Celeste` and its base
   classes, `AutoSplitterInfo`, `SaveData`, `Session`, `AreaStats` and
   `AreaModeStats`, measured with `ldflda` on uninitialized objects, since
   `Marshal.OffsetOf` doesn't work on auto-layout classes;
3. for each anchor, once it has run the class constructors and compiled the
   anchor: the static slot's address, the anchor's machine code, and where the
   slot's address appears in it.

`host`:

1. runs the class constructors and compiles each anchor, as the dump does:
   without `AutoSplitterInfo.Update` compiled, the auto splitter never finds
   `SaveData.Instance`;
2. constructs the game (`new Celeste()`). `SaveData.Instance`, which the game
   leaves null until a file starts, gets an uninitialized object of the probe's
   own, so that the host can write values into it;
3. prints `probe: ready`, and takes values to write: see [The host](#the-host).

On a failure, either prints why, then `probe: failed`, and exits with 1, or with
2 for a bad command.

## The manifest

`Manifest.cs` lists the roots the auto splitter reads from. Each root is a
static field that the auto splitter finds through machine code:

- **`Field`**: the static field, as `Namespace.Type::Field`.
- **`Anchor`**: the method whose compiled code holds the field's address, as
  `Namespace.Type::Name(ParameterType, ...)`.
- **`CodeField`**: the first field of the root's paths, whose offset `memory.rs`
  reads from the anchor's code rather than from the layout. It comes after the
  fields of a base class in the game's framework, and Everest replaces the FNA
  that vanilla runs on, with a different one in different versions, so no layout
  can hold it for every game. The layouts hold each path's one offset after it.
  Without one, each path is a single field of the root's object.
- **`Signatures`**: per runtime, how to find the field's address in the
  anchor's code. Each has:
  - a signature that matches the anchor's code once: hex bytes, with `??` for
    any byte;
  - for a root with a `CodeField`, a pattern for its store before the signature,
    with `SS` for the field's offset, 4 bytes in a row. The last match before
    the signature holds it, and `memory.rs` only searches the 128 bytes before
    each signature match: `FieldStoreWindow` in `Manifest.cs`;
  - reference patterns for an instruction after it that stores into the field
    or loads from it, with `SS` for the field's address: 4 or 8 bytes in a row.
    The earliest match of any of them after the signature holds the address:
    earliest, because a pattern with a 4-byte address can also match inside
    one with an 8-byte address. They're searched for rather than expected at a
    fixed distance, because the instructions in between can change length. On
    Linux, an address takes 4 or 8 bytes depending on where the heap is.

  Once it has checked them, the probe writes them into each runtime's layout,
  and [`memory.rs`](../../autosplitter/src/memory.rs) finds the address from
  them the same way. It only searches the 256 bytes after each signature match:
  `ReferenceWindow` in `Manifest.cs`, which the probe writes into the layouts
  too.
- **`Paths`**: instance fields to read from the root's object, joined with dots.
  Each name is looked up in the previous field's type, or its nearest base
  class with that field. Each is a field of `CodeField`'s object, of a
  concrete class, or without one, of the root's object, and ends in a `string`, `bool`, `int` or `long`, which the probe
  can write for the harness to check.

To add a root, add it without signatures first. The probe then prints the
anchor's code and where the field's address appears in it, and stops. Write
each runtime's signature and reference patterns from that, here.

## The host

Once it's ready, the host reads sets of values from its standard input, until
its input ends:

- a `root path=value` line per value. The root is a static field, as
  `Namespace.Type::Field`, and the path is instance field names from the root's
  object, joined with dots, each looked up in the object's class, or its nearest
  base class with that field. The value is read as the last field's type: `true`
  or `false`, a whole number, or for a string, the rest of the line;
- an empty line at the end of each set.

It writes each line into the game through reflection, and prints
`probe: written` at the end of each set. If a line can't be written, it prints
why, then `probe: failed`, and exits with 1.

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
./Celeste.exe dump
```
