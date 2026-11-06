# How it works

The auto splitter is a wasm module that attaches to Celeste, reads its memory
each tick, publishes what it reads as debug variables, and starts and splits the
timer as its settings say. Some triggers and timing methods aren't built yet:
they, and the settings' editors, are in the [plan](plan.md).

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
| Window | How far from the signature a pattern is searched, after or before it, the whole match inside: `WINDOW`, 256 bytes. |
| Everest's struct | `CoreAutoSplitterInfo`, which Everest writes into memory of its own each update, for auto splitters. |
| Debug variable | A value as the auto splitter publishes it: a custom variable such as `Chapter`, named in [`lib.rs`](../autosplitter/src/lib.rs). |
| Tick | One update of the auto splitter by its host: 120 a second by default. |
| Update | One update of the game, a frame: 60 a second. |
| Probe | [`tools/probe/`](../tools/probe/), which poses as the game in its own runtime. |
| Live check, replay, scenario | The harness's three checks, [below](#what-the-tests-prove). |
| Trace | A recording of the game's values, update by update, while a TAS plays: see [traces](../traces/README.md). |
| Trigger | A map in the settings saying when to start or split, such as `{kind: "new_file"}`. |

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
`TotalDeaths` and `Time`, from the object in `SaveData.Instance`'s slot. For the
type check, it also holds the path from an object's type pointer to its type's
size, and the sizes of `AutoSplitterInfo`, `SaveData` and `String`.

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

The auto splitter follows `Celeste.Instance`'s slot to the game, and the offset
the store held to its `AutoSplitterInfo`, then copies that object in one read.
It copies `SaveData.Instance`'s object the same way, and the start of the
`Level` string. Each copy has to pass the type check before any value is taken
from it. A flag is a byte, true when not 0.

- If `AutoSplitterInfo` fails the check, or a read fails, the tick publishes
  nothing, so the last values stay.
- `Level` goes unpublished when there's no string, it fails the check, or it's
  over 512 characters. Its characters are read after the check, in a second
  read.
- `Deaths`, `SaveData.TotalDeaths`, goes unpublished while `SaveData.Instance`
  is null, and on a tick its object fails the check.
- `Starting New File` is true while the save's `Time` is 0, false while
  `SaveData.Instance` is null, and unpublished on a tick the save fails the
  check, so a garbage read can't make it rise again.

### A new file

The game leaves `SaveData.Instance` null from launch until a file starts, and
after that clears it only before PICO-8, so a save in the slot doesn't mean a
file has just started. Starting a new file puts a new save there, with a `Time`
of 0, in the same update that closes the file select and disables Madeline on
the mountain. Its `Time` stays 0 until the Prologue's timer starts, when she
first moves, while a file that's continued has played for longer.
`AutoSplitterInfo.FileTime` can't tell a new file from no save: it's `Time`, or
0 without a save.

Everest's `Starting New File` is its own flag, which its file select sets.

### The type check

A garbage collection can move an object while the auto splitter reads it, and
for a moment, a reference can lead to memory holding something else. Before the
check, that was seen at boot, as a string where `AutoSplitterInfo` should be,
and after Save and Quit, as `Deaths` reading 0 for a tick, and as every value
of `AutoSplitterInfo` reading garbage, its flags at 1.

Every .NET object starts with a pointer to its type, and the type records its
objects' size. The check takes the type pointer from the copy, follows the
layout's path to the size, and accepts the copy only if that's the layout's
size for the type expected:

- CLR: the type pointer is the `MethodTable`, which holds the base size at
  +0x4. The base size counts the 4-byte header before the object, so a copy is 4
  bytes shorter than it.
- Mono: the type pointer is the `MonoVTable`, whose first field is the
  `MonoClass`, which holds the instance size at +0x1c, counted from the object's
  start.

The type pointer comes from the same read as the values, so a copy that passes
held an object of the right size when it was read. Types don't move, so reading
the size later is safe. Nothing has to be learned, so values are published from
the first tick.

A hand check on 2026-11-06, on Windows XNA, over four boots and seven Save and
Quits, logged every failed check. Each collection failed it for one to five
ticks: the type pointer had its lowest bit set, which is how the CLR marks an
object during a collection, or `AutoSplitterInfo`'s new address held 0 or other
data, or `Level` led to a type pointer of 0xc. Nothing wrong was published, and
`Deaths` counted up one per Save and Quit, matching `Saves/0.celeste`.

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
- It has values vanilla doesn't, such as `Chapter SID`, and no `Deaths`.

Started with `--vanilla`, Everest runs the vanilla game on its own FNA, without
the struct, so the auto splitter reads it as vanilla. Before .NET Core, Everest
does so in a second AppDomain of its own process, which works the same.

## Settings

[`schema/settings-schema.json`](../schema/settings-schema.json) defines the keys,
their defaults, the trigger kinds and their fields, the presets and the
chapters' checkpoints, in the format [schema/README.md](../schema/README.md)
describes.

| Top-level key | Type | Written by | Purpose |
|---|---|---|---|
| `schema_version` | i64 | editors; the module when migrating | The version of `cfg`'s layout. |
| `cfg` | map | editors | Every real setting. |
| `legacy` | map | the component, when migrating | The legacy settings, verbatim, for migrating again and debugging. |
| `_ui_*` | title widgets | the module | Reserved for the module's own widgets, since widget keys and map keys share one namespace. |

In `cfg`:

- `start` is the trigger that starts the timer, and `splits[i]` the one that
  ends segment `i`, bound by position to the timer's current split index,
  `asr::timer::current_split_index`, so manual splits, skips and undos keep it
  aligned. Segments past the list's end are split by hand, and extra entries are
  ignored.
- `timing_method`: `file` is the save file's time. It's the only one built.
- `high_priority` and `set_game_time` are the component's alone, and the module
  ignores them.
- An empty `cfg` does nothing but report game time.

A new optional key, trigger kind or field doesn't bump `schema_version`.
Renames, removals and changes of meaning do.

### Reading them

[`build.rs`](../autosplitter/build.rs) reads the schema when the module is built,
with `serde_json` as a build dependency, so no JSON parser ends up in the wasm.
It generates `schema.rs`, which [`settings.rs`](../autosplitter/src/settings.rs)
includes: `SCHEMA_VERSION`; an enum of the trigger kinds, with the fields each
requires and takes; an enum of the fields, with which are Everest-only and what
they replace; an enum for each setting or field with string values, such as
`TimingMethod` and `Timing`, defaulting to the schema's default; the other
defaults and allowed values; and each chapter's ID and checkpoint rooms. A shape
of schema it doesn't know fails the build, and so does a new field, until
`settings.rs` reads it.

The module loads the map once attached, then every 120 ticks, about once a
second, and when the timer resets. It parses `schema_version` and `cfg` only when
their debug text differs from the last parsed, so each problem is logged once
per change. It never writes the map.

- A missing key means its default. `gui::add_bool` returns a default without
  writing it, so the map only holds what someone set.
- A `schema_version` newer than the module's means `cfg` may mean something else,
  so the module logs it and ignores `cfg`: nothing starts or splits. An older
  one, or one that isn't a number, is logged and read as the module's.
- A setting of the wrong type or with a value not allowed falls back to its
  default, and is logged.
- A trigger that isn't a map, has no `kind` or an unknown one, lacks a required
  field, or has a field of the wrong type or with a value not allowed, behaves
  like `manual`, so an older module treats a newer kind as `manual`. The module
  logs it. `chapter` takes the schema's chapter IDs, and `-1` only for the kinds
  that allow it; `checkpoint` takes the numbers of its chapter's checkpoints.
- Keys the module doesn't know, in `cfg` or in a trigger, are ignored, and so are
  fields a trigger's kind doesn't take. An Everest-only field, `sid`, is ignored
  while `everest` is off.
- Not built yet, and so logged and read as their defaults: a trigger of a kind
  other than those [below](#splitting), or with a `sid` while `everest` is on,
  is `manual`; a `timing_method` other than `file` is `file`; `auto_reset` is
  off.

## Splitting

[`splitter.rs`](../autosplitter/src/splitter.rs) runs each tick after the
values are read, and does nothing on a tick that read none. It keeps the
previous tick's values in asr's watchers.

- While the timer isn't running, if `start` fires, it starts the timer and sets
  game time.
- While it's running or paused, it sets game time, then splits if the trigger
  for the current split index fires. The tick that starts the timer doesn't
  check splits.
- Once the run has ended, it does nothing.

| `kind` | Fires when |
|---|---|
| `manual` | never |
| `unknown` | never. Only migration makes it, and editors flag it. |
| `new_file` | a new save file is started: `Starting New File` rises |
| `chapter_complete` | the chapter is completed (`timing: "chapter_complete"`, the default), or the game leaves the completed chapter (`timing: "chapter_exit"`) |

`chapter` is the game's chapter ID, not its number: 9 is Core, chapter 8, and
10 is Farewell, chapter 9. An absent optional field matches anything:
`{kind: "chapter_complete"}` is any chapter.

These rules keep parity with the legacy component, so none has a setting:

- `chapter_complete` arms on the rising edge of `ChapterComplete` in the
  matching chapter. With `timing: "chapter_complete"` it fires at once; with
  `"chapter_exit"`, when `ChapterComplete` falls again, in any chapter, which can
  be on another frame than the chapter ID changing. In chapter 7, it arms only in
  a room whose name isn't empty and doesn't start with `credits`, ignoring case,
  except without a `chapter`, as legacy does. The side matched is the previous
  tick's, when it fires. It disarms on every tick the split index differs from
  the one it armed for, as legacy does on a split, skip, undo or reset.
- Game time is the file time, `FileTime`, set every tick while the run is in
  progress, except on a tick where it drops to 0, which keeps the game time
  before. It's paused whenever `FileTime` hasn't changed since the previous
  tick, so it runs on between ticks only while the game's timer does.

These deviate from legacy on purpose:

| Legacy | Now | Who notices |
|---|---|---|
| A chapter split whose side doesn't match when it fires stays armed, so a later chapter's completion can split it | It disarms | Runners with a side on a chapter split, who complete that chapter on another side first. |
| A new-file start fires whenever `Starting New File` is true, so after a reset it starts again at once while the value stays true | `new_file` fires when it rises | Runners who reset just after starting a new file. |

The module uses only asr functions every host's auto splitting runtime has.

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
  game. Then it prints `probe: ready`, and reads sets of values until its input
  ends: a `root path=value` line per value, or `root=null` to make a root null,
  then an empty line. Each field of the path is looked up in the object's class or
  its nearest base class with it, and the value is parsed as the last field's
  type: `true` or `false`, a whole number, or for a string, the rest of the line.
  A root holding null, as `SaveData.Instance` does until a file starts, first
  gets an uninitialized object of the probe's own. The host writes each line
  through reflection, and prints `probe: written` after each set.
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
its root, path, type and debug variable, if it has one of its own. The harness
sends the probe a set of values, the probe writes them into the game, and the
harness updates the auto splitter once. The debug variables must then be exactly
the values the game holds, none missing and none extra, with none for a null
root, and `Starting New File` 1 exactly when the save's `Time` is 0.

- **The live check** sends a distinctive value for every path, then the same
  with every number negated and every flag flipped, then each flag set alone,
  then no save, then a save whose `Time` is 0, then only `Time`'s high half set.
  `Time` is only published through `Starting New File`, so a wrong field or
  width shows in those last two.
  Every number and string has a value of its own, spread over all its bytes, so
  reading the wrong field, or the wrong width, shows in every field. Two swapped
  flags would agree when flipped together, so each is also set alone.
- **The replay** then sends each line of each trace: its changes, over all the
  values before. A path that isn't in `PATHS` fails.
- **The scenarios** follow each trace's replay, one per scenario file next to
  it, as [traces](../traces/README.md#scenarios) describes. A new auto splitter,
  with the scenario's settings map and a fresh `Timer`, attaches while the game
  holds the trace's first frame, then the trace is replayed through it. The
  starts and splits the `Timer` gets, their frames, and their game times must be
  exactly the scenario's, which are written by hand from the trace. A split's
  game time is the split time the run records, and a start's the game time once
  its tick ends. They're compared to the millisecond: the game's times are whole
  milliseconds, and while game time isn't paused, livesplit-core runs it on in
  real time, a few microseconds past what the module set.

The live check catches layout mistakes whatever traces there are, and the replay
adds the values only real play produces, such as empty strings. On all four
flavours, a pass shows that the signatures, references and store are found, that
every offset and pointer size is right, that each object passes the type check,
that .NET strings, numbers and flags read right, and that attaching works on
each OS. A wrong size in the type check fails it: the values of that object go
missing. It rests on the game's own runtime
and one comparison per update, with no stand-in for the game. The auto splitter
runs on livesplit-core's own `Timer`, pinned with the auto splitting runtime to
one revision in `harness/Cargo.toml`. The scenarios add that the module reads its
settings from that auto splitting runtime's map, and, in the Prologue, starts on
a new file and splits on its completion, with both timings, on the frames and at
the game times the trace gives, and doesn't with an empty, invalid or newer
`cfg`, or another chapter.

The [component's tests](livesplit-notes.md#the-components-tests) prove that
LiveSplit's ASR loads the wasm module and keeps its settings.

### What they don't prove

- Everest: no probe constructs it, and CI doesn't run it. Its offsets rest on
  the transcription, and nothing checks the 0xff marker.
- Vanilla on Everest's FNA, with `--vanilla`: checked once by hand on Windows,
  with Everest `stable-1.4449.0` and `stable-1.6662.0`.
- Values neither check writes, such as the smallest and largest numbers.
- Code the game doesn't have: a second match of a signature, or a 4-byte
  reference pattern matching inside an 8-byte one.
- Reads during the game's update, or during a garbage collection: the probe's
  game never runs, and each set is written whole between two ticks. So nothing
  in CI fails the type check, and that it skips garbage rests on a hand check.
- Reading at the tick rate: the harness ticks once per line of a trace, once
  per update of the game.
- Detaching and attaching again after 10 failed searches.
- Pausing game time: the harness ticks once per update, back to back, and the
  module sets game time every tick, so whether it runs on between ticks doesn't
  show.
- Loading the settings again, once a second and on a reset: a scenario's map
  never changes. Nor what the module logs.
- What no trace has: the chapter 7 `credits` rule, disarming when the split
  index changes, a side that doesn't match, and game time keeping its value when
  `FileTime` drops to 0.
- Splitting in LiveSplit: the component's tests don't run the game.

## Known gaps

- **Garbage the type check can't see.** An object of another type with the
  same size, where a garbage collection left a reference, would pass the check.
  So would a string moved between reading its length and its characters. In
  `SaveData.Instance`'s slot, with 0 where `Time` is, such an object would make
  `Starting New File` rise.
- **A file saved before its timer started.** On vanilla, continuing it reads as
  a new file, since its `Time` is still 0.
- **Splitting is partial.** Only the triggers [above](#splitting) and the `file`
  timing method are built, and nothing resets. The component has no settings
  editor or migration, and the website is a stub.

## Supported versions

- **Vanilla Celeste 1.4.0.0** on all four flavours, checked in CI.
- **The latest Everest**, through its struct for modded play, and as vanilla
  with `--vanilla`.
- **Everest 1.4449**, the last stable before Everest moved to .NET Core, only
  with `--vanilla`, checked once by hand. Modded play on it isn't supported,
  because only Everest on .NET Core has the struct.
- **LiveSplit** on 64-bit Windows, through the component, built against
  LiveSplit 1.8.37. Hosts built on livesplit-core load the wasm module alone.
