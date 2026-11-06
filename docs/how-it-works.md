# How it works

The auto splitter is a wasm module that attaches to Celeste, reads its memory
each tick, and publishes what it reads as debug variables. It has no splitting
logic yet: that, and its settings, are in the [plan](plan.md).

## Terms

| Term | Meaning |
|---|---|
| Flavour | One of the four builds of vanilla Celeste 1.4.0.0: Windows XNA, Windows FNA, Linux and macOS. CI calls them `windows-xna`, `windows-fna`, `linux` and `macos`. |
| Runtime | The .NET runtime the game runs in. `CLR32`, 32-bit .NET Framework, runs both Windows flavours. `MONO64`, the 64-bit Mono bundled with the game, runs Linux and macOS. `Runtime` in [`memory.rs`](../autosplitter/src/memory.rs) pairs each with its OSes. |
| Auto splitting runtime | What runs the wasm module in a host: LiveSplit's ASR component, or livesplit-core's `livesplit-auto-splitting` crate. Never shortened to "runtime" here. |
| Host | A program that runs the wasm module: LiveSplit, apps built on livesplit-core, the asr-debugger, and the harness. |
| Framework | The library the game is built on: XNA, or a version of FNA. Everest brings its own FNA. |
| Root | A static field the auto splitter reads through: `Celeste.Instance` or `SaveData.Instance`. The probe, the harness and the traces write it `Namespace.Type::Field`, as in `Celeste.Celeste::Instance`. |
| Slot | Where a root is stored: the address that holds the reference to its object. |
| Path | Instance field names from a root's object, joined with dots, as in `AutoSplitterInfo.Chapter`. |
| Offset | The distance in bytes from the address a reference to an object holds to one of its fields. |
| Layout | What the auto splitter needs to read vanilla on one runtime: `CLR32` or `MONO64` in [`vanilla.rs`](../autosplitter/src/vanilla.rs). |
| Anchor | A method whose compiled code holds a root's slot address: `Celeste`'s constructor for `Celeste.Instance`, and `AutoSplitterInfo.Update` for `SaveData.Instance`. |
| Signature | Hex bytes, with `??` for any byte, that match an anchor's code once. |
| Pattern | The bytes before a value in code, the value's width, 4 or 8 bytes, least significant first, and the bytes after it, with `None` for any byte. |
| Reference pattern | A pattern for the instruction after the signature that holds the slot's address. |
| Store pattern | A pattern for the instruction before `Celeste.Instance`'s signature that stores the game's new `AutoSplitterInfo`, and holds that field's offset. |
| Window | How far from the signature a pattern is searched, the whole match inside: `REFERENCE_WINDOW`, 256 bytes after it, and `auto_splitter_info_store_window`, 128 bytes before it. |
| Everest's struct | `CoreAutoSplitterInfo`, which Everest writes into memory of its own each update, for auto splitters. |
| Debug variable | A value as the auto splitter publishes it: a custom variable such as `Chapter`, named in [`lib.rs`](../autosplitter/src/lib.rs). |
| Tick | One update of the auto splitter by its host: 120 a second by default. |
| Update | One update of the game, a frame: 60 a second. |
| Probe | [`tools/probe/`](../tools/probe/), which poses as the game in its own runtime. |
| Live check, replay | The harness's two checks, [below](#what-the-tests-prove). |
| Trace | A recording of the game's values, update by update, while a TAS plays: see [traces](../traces/README.md). |

## Attaching

The auto splitter attaches to the first process named `Celeste.bin.x86_64`,
`Celeste.bin.x86`, `Celeste.exe` or `Celeste`, and looks for Everest's struct,
then for vanilla's roots. A search scans the process's memory, and LiveSplit runs
the auto splitter on its UI thread, so after a failed search it waits a second,
and logs each kind of failure once. asr's `sleep` needs WASI, which the module
isn't built for, so a second is 120 ticks. After 10 failed searches, it detaches
and attaches again: on Linux, vanilla starts through a bash script named
`Celeste`, which runs `Celeste.bin.x86_64`, and if the host's process list is
refreshed between the two, the auto splitter attaches to the script. `Celeste`
stays in the list because under Everest on Linux, it's the game itself.

## Vanilla

The game's own classes lay out the same on every flavour of a runtime, so there's
one layout per runtime, picked by the OS. It holds where a .NET string keeps its
length, an `i32`, and its UTF-16 characters, an anchor for each root, the store
pattern, and each value's offset: after `AutoSplitterInfo`, or for
`TotalDeaths`, from the object in `SaveData.Instance`'s slot.

### Finding `Celeste.Instance`

The auto splitter searches every executable memory region for the signature.
`Celeste`'s constructor passes the game and window sizes, 1920x1080 and 960x540,
to `Engine`'s constructor, then stores itself in `Celeste.Instance`. The
signatures match the sizes: pushed on Windows, in registers on Mono.

For each match, the slot's address comes from the earliest match of any
reference pattern in the window after it: earliest, because a pattern with a
4-byte address can also match inside one with an 8-byte address. They match the
store into the slot:

- Windows: `lea edx, [slot]; call`, the address for the GC's write barrier.
- Mono: `mov eax, slot` or `mov rax, slot`, then `mov [rax], r15`. Which width
  depends on where the heap is, and so does the length of other instructions in
  between, so the reference is searched for, not expected at a fixed distance.

`Celeste`'s field initializers run before `Engine`'s constructor, so the store of
its new `AutoSplitterInfo` comes before the signature, and holds the field's
offset: `lea edx, [esi+offset]; call` on Windows, `lea rax, [r15+offset]` on
Mono. It's the last match in the window, the nearest, since the window can reach
code before the constructor. The match is accepted once the slot holds the game
and its `AutoSplitterInfo` isn't null, so until then, the search fails.

The offset is read from the code because it comes after the fields of the
framework's `Game` class: 0xa0 on the FNA 21.3.5 that the game ships with, 0xb0
on Everest 1.4449's FNA 21.7, 0xac on current Everest's FNA 24.1 and on XNA, and
0x118 on Mono.

### Finding `SaveData.Instance`

`AutoSplitterInfo.Update` runs from the game's first update. Right after setting
`ChapterTime` from the level's session, it loads `SaveData.Instance` for
`FileTime`. The signatures match setting `ChapterTime`, and the reference
patterns the load: `mov ecx, [slot]; test ecx, ecx` on Windows, and on Mono,
`mov rax, [slot]` when the address fits in 32 bits, as on Linux, or else, as on
macOS, `mov rax, slot; mov rax, [rax]`.

On these runtimes, code never addresses a static relative to the instruction:
x86 has no RIP-relative addressing, and Mono's JIT writes a static's address into
the instruction. So a reference always holds the slot's absolute address.

### Each tick

The auto splitter follows each value's pointer path: the root's object from the
slot, then for `Celeste.Instance`, the `AutoSplitterInfo` at the offset the store
held, then the value at its offset. It reads every value, and publishes them
only if every read succeeds. A flag is a byte, true when not 0. The exceptions:

- `Level` goes unpublished when there's no string, or it's over 512 characters.
- `Deaths`, `SaveData.TotalDeaths`, goes unpublished until a file starts, since
  the game leaves `SaveData.Instance` null until then.

## Everest

[`everest.rs`](../autosplitter/src/everest.rs) is Everest's struct as a
`#[repr(C)]` struct, transcribed by hand from
[`AutoSplitter.cs`](https://github.com/EverestAPI/Everest/blob/47d6a61fa918c17b8085b93b61ee59a7465968f8/Celeste.Mod.mm/Mod/Everest/AutoSplitter.cs)
at commit `47d6a61`, in Everest `stable-1.6623.0`. The compiler checks its size
and each field's offset against Everest's explicit ones.

- Everest puts the struct at the start of a memory mapping of its own, so the
  auto splitter checks the start of each memory region for its magic bytes. It
  accepts only `InfoVersion` 3, the version transcribed, and otherwise logs the
  version once and keeps looking.
- Everest runs on 64-bit .NET: pointers take 8 bytes, and the struct 0x70, its
  last field padded to a pointer's alignment.
- Each tick, the auto splitter copies the struct in one read, then reads the
  strings it points to: NUL-terminated UTF-8, with the length in bytes in the
  `u16` before it, or a null pointer for no string. Each update writes its
  strings to the other of two pools, so a copy's strings last until the update
  after next.
- Each update, Everest sets `InfoVersion` to 0xff, writes every field, raises
  `AutoSplitter.OnUpdateInfo`, and sets it back to 3. A tick whose copy holds
  0xff, or that reads 0xff right after copying, publishes nothing, so the last
  values stay. When attaching, a struct holding 0xff is skipped without a log.
- It has values vanilla doesn't, such as `Starting New File` and `Chapter SID`,
  and no `Deaths`.

Started with `--vanilla`, Everest runs the vanilla game on its own FNA, without
the struct, so the auto splitter reads it as vanilla. Before .NET Core, Everest
does so in a second AppDomain of its own process, which works the same.

## The probe

[`tools/probe/`](../tools/probe/) is installed as the game's `Celeste.exe`, runs
in the game's own runtime, and loads the game from `real/Celeste.exe`.

- `dump` prints the pointer size and where strings hold their length and
  characters; every instance field's offset, name and type, for `Celeste` and its
  base classes, `AutoSplitterInfo`, `SaveData`, `Session`, `AreaStats` and
  `AreaModeStats`, measured on uninitialized objects; and for each anchor in
  `Game.cs`'s `Anchors`, compiled as `host` compiles it, the slot's address, the
  anchor's machine code, and each offset in it where the slot's address appears,
  4 or 8 bytes wide. [`tools/probe/fields/`](../tools/probe/fields/) keeps the
  field tables from a Live check run: one per Windows flavour, and one for Linux
  and macOS, which are identical.
- `host` runs the class constructors, compiles each anchor, and constructs the
  game. `SaveData.Instance`, null until a file starts, gets an uninitialized
  object of the probe's own. Then it prints `probe: ready`, and reads sets of
  values until its input ends: a `root path=value` line per value, then an empty
  line. Each field of the path is looked up in the object's class or its nearest
  base class with it, and the value is parsed as the last field's type: `true` or
  `false`, a whole number, or for a string, the rest of the line. The host writes
  each line through reflection, and prints `probe: written` after each set.
- On a failure, either prints why, then `probe: failed`, and exits with 1, or
  with 2 for a bad command.

Why it's built as it is:

- An anchor's length isn't available, so the dump reads 768 bytes of its code,
  stopping at an unmapped page. That covers `Celeste`'s constructor everywhere:
  0xb6 bytes on Windows, 0x15a on Linux and 0x1e9 on macOS.
- The JIT may compile static accesses differently before a class is
  initialized, so both commands run the class constructors first, as the game
  has by then. `SaveData` has none.
- `RuntimeHelpers.PrepareMethod` compiles an anchor, and `GetFunctionPointer`
  then returns its code, not a stub, on both runtimes. The host has to compile
  them, because the game only runs `AutoSplitterInfo.Update` from its first
  update, which the host never runs. On Mono, without `GetFunctionPointer`, the
  auto splitter never finds `SaveData.Instance`.
- `GC.SuppressFinalize` keeps finalizers from running on the probe's
  uninitialized objects, and on the game, which nothing disposes.
- The game's constructor reads `Settings.Instance`, which `Main` loads first, so
  the host makes one.
- A field's offset is `&obj.field - obj`, taken with `ldflda` in one dynamic
  method, so that the GC can't move the object in between. `Marshal.OffsetOf`
  doesn't work on auto-layout classes.
- Reading an unmapped page crashes the process, so reads stop at the first. On
  Linux and macOS, `msync` fails with `ENOMEM` on one, which `mincore` doesn't on
  macOS, and `MS_ASYNC`, the 1 it's given, does nothing to anonymous memory.

## What the tests prove

The [harness](../harness/) starts the probe with `host`, so the probe's process
has the game's name, and the auto splitter, which the harness runs, attaches to
it. `PATHS` in the harness lists every value the auto splitter reads on vanilla:
its root, path, type and debug variable. The harness sends the probe a set of
values, the probe writes them into the game, and the harness updates the auto
splitter once. The debug variables must then be exactly the values the game
holds, none missing and none extra.

- **The live check** sends a distinctive value for every path, then the same
  with every number negated and every flag flipped, then each flag set alone.
  Every number and string has a value of its own, spread over all its bytes, so
  reading the wrong field, or the wrong width, shows in every field. Two swapped
  flags would agree when flipped together, so each is also set alone.
- **The replay** then sends each line of each trace: its changes, over all the
  values before. A path that isn't in `PATHS` fails.

The live check catches layout mistakes whatever traces there are, and the replay
adds the values only real play produces, such as empty strings. On all four
flavours, a pass shows that the signatures, references and store are found, that
every offset and pointer size is right, that .NET strings, numbers and flags read
right, and that attaching works on each OS. It rests on the game's own runtime
and one comparison per update, with no stand-in for the game. The auto splitter
runs on livesplit-core's own `Timer`, pinned with the auto splitting runtime to
one revision in `harness/Cargo.toml`.

The [component's tests](livesplit-notes.md#the-components-tests) prove that
LiveSplit's ASR loads the wasm module and keeps its settings.

### What they don't prove

- Everest: no probe constructs it, and CI doesn't run it. Its offsets rest on
  the transcription, and nothing checks the 0xff marker.
- Vanilla on Everest's FNA, with `--vanilla`: checked once by hand on Windows,
  with Everest `stable-1.4449.0` and `stable-1.6662.0`.
- Values neither check writes, such as the smallest and largest numbers. The
  traces don't record `TotalDeaths`: only the live check writes it.
- `SaveData.Instance` holding null: the probe always holds a save, so nothing
  checks that `Deaths` goes unpublished without one.
- Code the game doesn't have: a second match of a signature, or a 4-byte
  reference pattern matching inside an 8-byte one.
- Reads during the game's update, or during a garbage collection: the probe's
  game never runs, and each set is written whole between two ticks.
- Reading at the tick rate: the harness ticks once per line of a trace, once
  per update of the game.
- Detaching and attaching again after 10 failed searches.

## Known gaps

- **Garbage during garbage collection.** Reads can return garbage for up to
  about 0.24 s. It has been seen as a false `Chapter Started` and a false
  `Chapter Complete`, and as `Deaths` reading 3, 0, 3 for one tick. Nothing
  checks for it, by choice, so splitting logic will have to cope with it,
  especially triggers on a rising edge.
- **No new-file signal on vanilla.** Only Everest's struct has `Starting New
  File`. `SaveData.Instance` is read only for `Deaths` so far: splitting will
  need a source.
- **No splitting.** Nothing starts, splits or resets, and nothing reads settings.
  The component has no settings editor or migration, the website is a stub, and
  nothing reads the schema.

## Supported versions

- **Vanilla Celeste 1.4.0.0** on all four flavours, checked in CI.
- **The latest Everest**, through its struct for modded play, and as vanilla
  with `--vanilla`.
- **Everest 1.4449**, the last stable before Everest moved to .NET Core, only
  with `--vanilla`, checked once by hand. Modded play on it isn't supported,
  because only Everest on .NET Core has the struct.
- **LiveSplit** on 64-bit Windows, through the component, built against
  LiveSplit 1.8.37. Hosts built on livesplit-core load the wasm module alone.
