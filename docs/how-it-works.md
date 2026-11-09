# How it works

The auto splitter is a wasm module that attaches to Celeste, reads its memory
each tick, publishes what it reads as debug variables, and starts, splits and
resets the timer as its settings say. [The component](#the-component) adds its
settings editor on Windows, and [the website](#the-website) edits the settings of
splits files for every other host.

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
| `schema_version` | i64 | editors; the component, when migrating | The version of `cfg`'s layout. |
| `cfg` | map | editors; the component, when migrating | Every real setting. |
| `legacy` | map | the component, when migrating | The legacy settings, verbatim, for migrating again and debugging: each boolean's text under its legacy name, only when the node has it, then `Splits`, a list of each split's text, in order. |
| `_ui_*` | title widgets | the module | Reserved for the module's own widgets, since widget keys and map keys share one namespace. |

In `cfg`:

- `start` is the trigger that starts the timer, `reset` the one that resets it,
  [below](#splitting), and `splits[i]` the one that ends segment `i`, bound by
  position to the timer's current split index, `asr::timer::current_split_index`,
  so manual splits, skips and undos keep it aligned. Segments past the list's
  end are split by hand, and extra entries are ignored.
- `timing_method` is the clock game time follows, [below](#game-time).
- `everest` lets triggers name their chapter by `sid`, [below](#splitting).
- `high_priority` and `set_game_time` are the component's alone, and the module
  ignores them.
- An empty `cfg` does nothing but report game time.

A new optional key, trigger kind or field doesn't bump `schema_version`.
Renames, removals and changes of meaning do, once a release has the layout.

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
second, and when it finds the timer reset, unless it has started again by
then. It parses `schema_version` and `cfg` only when their debug text differs
from the last parsed, so each problem is logged once per change. It never
writes the map.

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
  logs it. `chapter` takes any ID from 0 up, modded ones included, and
  `checkpoint` the numbers of its chapter's checkpoints.
- Keys the module doesn't know, in `cfg` or in a trigger, are ignored, and so are
  fields a trigger's kind doesn't take. An Everest-only field, `sid`, is ignored
  while `everest` is off.

## Splitting

[`splitter.rs`](../autosplitter/src/splitter.rs) runs each tick after the
values are read, and does nothing on a tick that read none. It keeps the
previous tick's values in asr's watchers, which take their first value as both,
so attaching is never a change.

- While the timer is running, paused or ended, if `reset` fires, it resets the
  timer, which commits the attempt, and checks no split on that tick, as legacy
  gives a reset precedence over a split. The timer then isn't running, so
  `start` can start it again in the same tick: with a `reset` of the same kind
  as `start`, the new attempt keeps its first frame. The exception is
  `chapter_complete` with `timing: "chapter_exit"`, whose start arms only while
  the timer isn't running, so after that reset nothing starts.
- While the timer isn't running, if `start` fires, it starts the timer and sets
  game time. The tick that starts the timer doesn't check splits.
- While it's running or paused, it sets game time, then splits if the trigger
  for the current split index fires.
- Once the run has ended, only `reset` applies.

| `kind` | Fires when |
|---|---|
| `manual` | never |
| `unknown` | never. Only migration makes it, and editors flag it. |
| `new_file` | a new save file is started: `Starting New File` rises |
| `chapter_start` | the chapter's timer starts: `ChapterStarted` rises, unless the chapter is [resuming](#resuming) |
| `chapter_enter`, `chapter_exit` | the chapter ID changes to or from `chapter`, or, when `chapter` is absent, to or from any chapter: anything but -1, which is outside a chapter. With a `side`, the side changes to or from it too. |
| `chapter_complete` | the chapter is completed (`timing: "chapter_complete"`, the default), or the game leaves the completed chapter (`timing: "chapter_exit"`) |
| `checkpoint` | the room becomes the checkpoint's room for the side being played, in `chapter` |
| `heart`, `cassette` | `ChapterHeart` or `ChapterCassette` rises: the chapter's crystal heart or cassette is collected |
| `room_enter`, `room_exit` | inside a chapter, the room changes to or from `room` |

`chapter` is the game's chapter ID, not its number: 9 is Core, chapter 8, and
10 is Farewell, chapter 9. Higher IDs are modded. An absent optional field
matches anything, except the `chapter` of `chapter_enter` and `chapter_exit`,
as above: `{kind: "chapter_complete"}` is any chapter. The chapter and side a
trigger matches are the current tick's, except that `chapter_exit` matches
those it left, and `chapter_complete` the side it completed.

- `room` matches the game's room name exactly, case included.
- `checkpoint` is a checkpoint's number in its chapter, from 1, and the schema
  has its room for each side. C-sides have no checkpoints, but legacy checks
  B-side rooms on C-sides, so the schema's C-side rooms repeat B's. Spawning
  into the room counts as entering it, but resuming in it doesn't.
- With `everest` on, a trigger's `sid`, a chapter's SID such as
  `Celeste/1-ForsakenCity`, replaces its `chapter`, and is matched against
  Everest's `Chapter SID`. Modded chapters have IDs above 10, which Everest
  assigns at load time, so they change with the mods installed, while SIDs
  don't. Without Everest's struct, the module logs once that triggers with a
  `sid` don't fire, and they don't.

### Resuming

A chapter is resuming on a tick where `ChapterStarted` rises while
`ChapterTime` is above 0 and at least the last `ChapterTime` read inside a
chapter. `ChapterTime` reads 0 outside a chapter, so on every rise the tick
before read 0. Resuming after a Save and Quit, and the Summit's credits level,
carry on from the last value read inside the chapter, while a real start reads
near 0. With no last value, just after attaching, a rise is a start. No
threshold is involved, so it holds at any tick rate.

- `chapter_start`, `room_enter` and `checkpoint` don't fire on a tick that
  resumes.
- `heart` and `cassette` don't fire on a tick the chapter ID changes: on
  resuming, `ChapterHeart` comes back set without a collection.

### Game time

`timing_method` picks the clock:

- `file`, the default: `FileTime`, the save file's time.
- `chapter`: `ChapterTime`, or outside a chapter the last read inside one, so a
  Save and Quit doesn't drop it to 0.
- `file_since_start` and `chapter_since_start`: the same, minus its value on
  the tick the timer started, by the module or, seen on the next tick, by
  anything else. Both clocks' values are kept, so a change of `timing_method`
  during a run subtracts the right one.

The tick that starts the timer sets game time from the clock. Every tick after,
while the run is in progress, sets it, except on a tick where the clock drops
to 0, which keeps the game time before. Game time stays paused through the
whole run, from the tick it starts, whoever started it, so it only ever holds
the game's own values and never runs on by itself between ticks.

### Against legacy

These rules keep parity with the legacy component, so none has a setting:

- `chapter_complete` arms on the rising edge of `ChapterComplete` in the
  matching chapter. With `timing: "chapter_complete"` it fires at once; with
  `"chapter_exit"`, when `ChapterComplete` falls again, in any chapter, which can
  be on another frame than the chapter ID changing. In chapter 7, it arms only in
  a room whose name isn't empty and doesn't start with `credits`, ignoring case,
  except without a `chapter` or `sid`, as legacy does. The side matched is the
  previous tick's, when it fires. It disarms on every tick the split index
  differs from the one it armed for, as legacy does on a split, skip, undo or
  reset.
- `chapter_exit` fires on a Save and Quit, and `chapter_enter` on resuming, as
  `AreaOnExit` and `AreaOnEnter` do. With a `side`, the side has to change too.
- `room_enter` fires on entering a chapter, where the room changes from none,
  and `room_exit` never fires on leaving one, as `LevelEnter` and `LevelExit`
  do.
- `checkpoint`'s rooms are legacy's, by side.
- `chapter` game time is held outside a chapter, as the IL case's is, and the
  `*_since_start` methods subtract the clock's value at the start, as the
  custom-start case and `FileTimeOffset` do. Game time keeps its value on a drop
  to 0. Legacy pauses it only while the clock holds still, letting it run on in
  real time otherwise; paused throughout, it shows the same values.

These deviate from legacy on purpose:

| Legacy | Now | Who notices |
|---|---|---|
| A chapter split whose side doesn't match when it fires stays armed, so a later chapter's completion can split it | It disarms | Runners with a side on a chapter split, who complete that chapter on another side first. |
| A new-file start fires whenever `Starting New File` is true, so after a reset it starts again at once while the value stays true | `new_file` fires when it rises | Runners who reset just after starting a new file. |
| The IL start ignores the first 3 s after attaching | Attaching is never a start, and any rise after it is | Runners whose chapter starts within 3 s of the auto splitter attaching. |
| The IL start fires whenever `ChapterStarted` rises, so also on resuming after a Save and Quit, and in the Summit's credits | `chapter_start` doesn't fire on resuming | IL runners who save and quit: with a `chapter_start` reset, the attempt carries on, and with a `chapter_exit` one, the next attempt doesn't start on resuming. |
| `AutoReset` resets on every tick the game is outside a chapter, and is ignored in full game | `reset` is a trigger of any kind, such as `chapter_exit`, which fires on one tick, in every case | Runners who start the timer by hand outside a chapter, which legacy resets at once, and full-game runners, who can reset on a new file. |
| `LevelEnter` fires on resuming, in the room resumed in | `room_enter` doesn't, for consistency with `room_exit`, which doesn't fire on the Save and Quit | Runners whose `room_enter` split is the room they saved and quit in, which shouldn't come up. Recorded so a report of it can be traced here. |
| Room names ignore case | Exact match | Saved names whose case differs from the game's. |
| Checkpoints fire while in the room, so also on resuming in it after a Save and Quit | Fire on entering it, but not on resuming in it, since a checkpoint split marks entering the checkpoint for the first time | Lists whose previous split fired in that room, and runners who save and quit in a checkpoint's room before its split. |
| Heart and cassette splits fire when the file's count goes up by 1, and in IL or with `FileTimeOffset`, also while the chapter's flag is set | The flag's rising edge, except on a tick the chapter ID changes | Runners on a file that already has the heart, who now get the split outside IL. Counting would break individual levels there, and a flag that stays set double-splits consecutive splits. |
| Chapter changes compare with the chapter from when a split other than manual was last checked, and room changes with the room from the last tick of a run, so after a reset, an `AreaOnEnter` or `LevelEnter` split for where the next run starts can fire at once | They compare with the previous tick | Lists with such a split. |

The module uses only asr functions every host's auto splitting runtime has.

## Migration from `LiveSplit.Celeste`

[`LegacyMigration.cs`](../component/CelesteAutosplit/LegacyMigration.cs) turns
a node the legacy component saved into the settings map, in the XML format
ASR reads and writes, using only `System.Xml`, so the website's WebAssembly
project can compile the same file. The component runs it from `SetSettings`,
before anything reaches ASR, and only when it has an ASR to run the module.
Settings it holds instead, on 32-bit, with a missing wasm or with ASR failing,
stay as they were, so saving keeps what the legacy component reads.

The result reaches disk only when the user saves the splits, since the
component doesn't set `Run.HasChanged`. Until then, it migrates again on each
load, so migration is pure and deterministic.

### The legacy component

Audited at [ShootMe/LiveSplit.Celeste `5c5bcb2`](https://github.com/ShootMe/LiveSplit.Celeste/tree/5c5bcb2c1456ee04a241575608febb9d35f69084),
v1.4.6, the last release. It saves flat children of `<AutoSplitterSettings>`:
`AutoReset`, `SetHighPriority`, `SetGameTime` and `FileTimeOffset`, each found
as the first descendant with its name and parsed with `bool.Parse`, all true by
default but `FileTimeOffset`; and `<Splits>`, with a `<Split>` each, `Type[,Value]`,
where `Type` is one of the 71 `SplitType` names and `Value` is optional.

It infers its mode from the list, counting names: chapter, any 8 characters long
(`Prologue`, `Epilogue`, `ChapterA`, `Chapter1` to `Chapter9`); heart, any
containing `HeartGem`; area, `AreaComplete`; cassette, any containing
`Cassette`. `ILSplits` is no splits or every count at most 1, and
`ChapterSplits` any count above 0:

| Case | When | Start | Segment `i` split by | Game time | Chapter splits | `AutoReset` |
|---|---|---|---|---|---|---|
| E: empty | no splits | any room change | one segment: the next room change, unless the start room is entered again within 2.5 s, which moves the start | chapter time since start | none | applies |
| F: full game | `ChapterSplits && !ILSplits` | new file | `splits[i]` | file time | when the flag falls | ignored |
| I: IL | `ChapterSplits && ILSplits` | chapter started | `splits[i]` | chapter time, held in the menu | when the flag rises | applies |
| C: custom start | splits, none counted | `splits[0]` | `splits[i+1]`; with only `splits[0]`, the next room change | chapter time since start | none | applies |

`FileTimeOffset` makes game time the file time since start, and lets heart and
cassette splits use the chapter's flag, as otherwise only IL does. When
`AutoReset` applies, the timer resets whenever the game is outside a chapter.

| Legacy types | Fire when |
|---|---|
| `Prologue`, `Chapter1` to `Chapter9`, `Epilogue` | `ChapterComplete` rises in that chapter, any side, or in case F, falls again. Chapter 7 also needs a room not starting with `credits`. |
| `ChapterA` | the same, any chapter, without the `credits` check |
| `AreaComplete,<chapter>[-<side>]` | the same, for that chapter and side |
| `AreaOnEnter`, `AreaOnExit,<chapter>[-<side>]` | the chapter ID changes to or from it, and the side too if given |
| `ChapterNCheckpointK` | in chapter N, whenever the room is the checkpoint's for the side |
| `HeartGemAny`, `ChapterNHeartGem`, `ChapterNCassette` | the file's count goes up by 1, or in IL or with `FileTimeOffset`, the chapter's flag is set. No "any cassette". |
| `LevelEnter`, `LevelExit,<room>` | inside a chapter, the room changes to or from `<room>`, ignoring case |

Its defects:

1. `SetGameTime` never worked: read in the constructor, before LiveSplit calls
   `SetSettings`, it was always true, and every user was switched to Game Time.
2. Unknown types silently become `Manual`: `Enum.TryParse` is case-sensitive and
   falls back to the default. It takes integers, so `5` is `HeartGemAny`, and
   `200` never fires.
3. Case C's final split gets the absolute chapter time.
4. `FileTimeOffset` in cases C and E mixes clocks: the file time since start,
   minus the chapter time at start.
5. `Value` stops at the first comma.
6. A `LevelEnter` or `LevelExit` without a room, or an `Area*` value that doesn't
   parse, never fires, and an `Area*` split with no value throws in `Update`.
7. Turning high priority off forces Normal, even if it didn't raise it.

Its other Windows-only behaviour is a debug log, `_Celeste.txt`, which ASR's log
replaces.

### Mapping

A node is legacy when `CustomSettings` is absent and `Splits` or `AutoReset`
present; anything else passes through. The node is the only input, never the run.
The booleans parse as `bool.Parse` does, trimmed, falling back to legacy's
defaults, and the list is classified exactly as legacy classifies it.

| | E | F | I | C |
|---|---|---|---|---|
| `start` | `manual` | `new_file` | `chapter_start` | `splits[0]` |
| `reset` | `chapter_exit` | `manual` | `chapter_exit` | `chapter_exit` |
| `splits` | none | each, `timing: "chapter_exit"` | each, `timing: "chapter_complete"` | `splits[1..]` |
| `timing_method` | `file` | `file` | `chapter` | `chapter_since_start` |

`FileTimeOffset` makes `timing_method` `file_since_start` in every case.
`reset` is `manual` without `AutoReset`, and its `chapter_exit` has no chapter,
so it fires on leaving any. `everest` is false, `high_priority`
`SetHighPriority`, and `set_game_time` `SetGameTime`. Every top-level key and
every `timing` is written, in the schema's order, so a later default can't
change a migrated user's behaviour, and the output is the same each time.
`id(N)` maps a chapter number in a name to its ID: 1 to 7 stay, 8 is 9, 9 is
10.

| Legacy type | Trigger |
|---|---|
| `Manual` | `manual` |
| `ChapterA` | `chapter_complete` |
| `Prologue`, `ChapterN`, `Epilogue` | `chapter_complete`, chapter 0, `id(N)` and 8 |
| `ChapterNCheckpointK`, 36 of them | `checkpoint`, chapter `id(N)`, checkpoint K |
| `HeartGemAny`, `ChapterNHeartGem` | `heart`, the second with chapter `id(N)` |
| `ChapterNCassette` | `cassette`, chapter `id(N)` |
| `AreaComplete`, `AreaOnEnter`, `AreaOnExit,V` | `chapter_complete`, `chapter_enter`, `chapter_exit`, with chapter and side from V |
| `AreaOnEnter`, `AreaOnExit,Menu`, without a side | `chapter_exit`, `chapter_enter`, without a chapter |
| `LevelEnter`, `LevelExit,V` | `room_enter`, `room_exit`, room V as written |
| anything else, or a value that never fires | `unknown`, with the text as `raw` |

A split parses as legacy's does: on commas, keeping fields 0 and 1, with the
type as `Enum.TryParse` reads it, an undefined integer being `unknown`. An area
value splits on `-`, part 0 a chapter name (`Menu`, `Prologue`, `ForsakenCity`,
..., `Core`, `Farewell`) or integer, part 1 `ASide`, `BSide`, `CSide` or 0 to 2,
trimmed and ignoring case. A failure, or a side outside `ASide` to `CSide`, is
`unknown`, and an integer can't be negative, since the value is split on `-`
first. `Menu`, -1, isn't a chapter: `AreaOnEnter` and `AreaOnExit` of it map as
above, and with a side, or on `AreaComplete`, it's `unknown`, since
`ChapterComplete` is false outside a chapter and no side but `None` matches
there, which is a deviation below. An empty room or area value counts as none.
`legacy` keeps the raw texts, as [above](#settings) says.

### Deliberate deviations

Those of splitting itself are [above](#splitting). Those of the mapping:

| Legacy | Now | Who notices |
|---|---|---|
| Always switches to Game Time | [Game Time](#game-time-and-priority) honours `set_game_time`, which keeps `SetGameTime` | Users who unchecked it. |
| Unknown types silently manual | `unknown`, flagged by the [editor](#the-settings-editor) | Nobody, timing-wise. |
| High priority set on hooking the game, and forced to Normal when turned off | [High](#game-time-and-priority) each time the timer starts, never lowered | Nobody, timing-wise. |
| Case C's final split time is absolute | Consistent with the run | Custom-start runners. |
| `FileTimeOffset` in C and E mixes clocks | `file_since_start` | A rare combination. |
| No splits: start on any room change, split on room changes | Nothing automatic but the reset, with `AutoReset` | Users without splits, and single-room timers. |
| Case C with one segment splits on the next room change | Split by hand | Custom-start single-room timers. |
| With `AutoReset`, nothing starts or splits outside a chapter, since the reset comes first, so in case C a start that fires there, such as `AreaOnExit,X` or `AreaOnEnter,Menu`, never starts | It starts, and during the run, leaving X with `AreaOnExit,X`, or any chapter with `AreaOnEnter,Menu`, resets and starts again | Custom-start runners whose start fires outside a chapter. |
| `AreaOnEnter,Menu` and `AreaOnExit,Menu` don't fire when a check sees one chapter change straight to another, as can happen between the Summit and the Epilogue, where the chapter ID is -1 for only 2 updates | Their `chapter_exit` and `chapter_enter` fire | Lists with such a split, when legacy's check would miss the -1. |
| `AreaOnEnter,Menu-None` and `AreaOnExit,Menu-None` fire on leaving and entering a chapter, since the side is -1 outside one | `unknown`, like any side of `Menu` | Nobody on purpose: legacy's value is typed by hand, and no list we've seen has one. |
| `LevelExit,` with an empty value fires on entering a chapter, since the room reads empty at the map | `unknown`, like any empty value | Nobody: only editing the `.lss` by hand writes one. |
| A boolean that doesn't parse throws from `SetSettings` | Its default | Nobody: only editing the `.lss` by hand writes one. |

## The component

[`CelesteAutosplitComponent`](../component/CelesteAutosplit/CelesteAutosplitComponent.cs)
runs the wasm module in ASR. Without an ASR to run it, its settings show only a
status and it keeps the settings it was given, so of what's below, only
[a missing wasm module](#a-missing-wasm-module) applies.

### The settings editor

[`SettingsEditor.cs`](../component/CelesteAutosplit/SettingsEditor.cs) renders
the schema, which the component reads with the framework's `JavaScriptSerializer`,
so no parser ships with it. Each time it opens, it reads `inner.GetSettings`, and
edits that `CustomSettings` in place, so unknown keys stay and the XML is ASR's
own. Each change calls `inner.SetSettings`, as
[LiveSplit's settings dialog](livesplit-notes.md#settings-in-the-lss) needs.

It is one column, fitting the dialog's fixed width and scrolling with it: the
booleans on one row, a row of preset buttons, the timing method as radio buttons
on one row, then Reset, Start and, under a heading, the segments, separated by
rules. Each trigger
is a row with its name and its kind, and below it, when its kind takes any, one
row of fields. The fields have no captions, since the worst case wouldn't fit:
each says what it is by its choices (`Any chapter`) or its cue text (`Room`), and
by its tooltip.

- **Segments**: there is one trigger for each segment of the run, so triggers
  can't be added, removed or moved, and the count can't differ from the run's.
  Setting one past the end of `splits` fills those before it with `manual`.
  Entries past the run's last segment, which never fire, aren't shown and are
  kept.
- **Presets** are actions, not a state: applying one rewrites its settings, and
  each trigger of a kind its `trigger_fields` name, keeping unknown keys. Nothing
  shows whether the settings match one.
- **Kinds** are offered per each kind's `offer`, plus the current one. Changing
  it drops the old kind's fields the new one doesn't take, but keeps unknown keys.
- **Flags**: a trigger says in red what the module reads as invalid, and `unknown`,
  with a line at the top saying some do, as the list can be longer than the
  dialog. An invalid `timing_method` has its own line there, and no radio button
  selected. The checks are the module's, as
  [Reading them](#reading-them) describes.
- **Everest-only fields** are hidden while `everest` is off, and kept.
- A `schema_version` above the schema's opens read-only and is never written, and
  nothing in it is flagged, since the module ignores it.
- Dropdowns ignore the mouse wheel unless they're open, and pass it to the dialog,
  so scrolling the list can't change a setting.
- The triggers are only built once the editor gets a window handle, since
  LiveSplit [also calls `GetSettingsControl` without showing it](livesplit-notes.md#running-the-component),
  and kept while the segments' names stay the same. Each trigger's windows take time
  to create, so a long run's dialog is slow to open: on the development machine,
  4 s for 38 segments, and 17 s for a community run of 95 with fields on every
  trigger, driven by UI automation.

### Game Time and priority

The two behaviours outside the module, each from `cfg`, a key's default when
missing, and neither under a newer `schema_version`:

- **Game Time**: once per activation, `state.CurrentTimingMethod` is set to Game
  Time if `set_game_time` is on: on the first `SetSettings`, or, for a run with no
  settings for this game, which never gets one, on the first `Update`, where
  the key's default applies. That also keeps a Cancel in the settings, which calls
  `SetSettings` much later, from switching anything.
- **Priority**: each time the timer starts, if `high_priority` is on, a background
  task sets every `Celeste` process to High, logging a failure. It's never
  lowered. On the development machine, something outside the game saves a
  `Celeste.exe` process's priority and sets Normal when it gains the foreground,
  and restores it when it loses it; the game's own code doesn't, and the game
  renamed isn't affected. The cause is inferred to be Windows' game recognition,
  not confirmed. The game normally has the foreground when the timer starts, so
  High then lasts until the next time it loses the foreground.

### A missing wasm module

Its status says to close LiveSplit, delete `celeste-autosplit.dll` from
`Components\` and start it again, or to download the wasm module from the latest
release. Once the list's first URL is that DLL, deleting it makes LiveSplit
download both files on the next activation. LiveSplit holds the DLL open, so it
has to be closed first.

## The website

[`site/`](../site/) is a static page on GitHub Pages: a `.lss` is edited in the
browser and never uploaded. It opens a `.lss`, shows its settings from the schema
as the component's editor does, and downloads the file with only
`<AutoSplitterSettings>` replaced. It tells users to close their splits in their
host first, or the host overwrites the edit, and it links the latest
`celeste_autosplit.wasm`, with steps for hosts that don't read the list:
AlexKnauth's livesplit-one-druid, which takes a path and renders title, bool,
choice and file-select widgets, and CryZe's older desktop and Druid apps, which
take a path in a config file. CryZe's newer, private desktop app reads the list.

### Reading and writing a `.lss`

[`lss.js`](../site/lss.js) splices bytes, so nothing else in the file changes.

- It reads the encoding from the byte order mark: UTF-16 with one, otherwise
  UTF-8. A declared encoding that's neither is refused, with a message to save
  the file as UTF-8: the editor has to write text back in the file's own
  encoding, and these are the ones it can.
- `DOMParser` reads the text, for the settings and the segments' names. A small
  scanner finds the byte range of the `<AutoSplitterSettings>` directly under
  `<Run>`, skipping comments, CDATA and attribute values, since `DOMParser` gives
  no positions. If there's none, the new one goes before `</Run>`.
- The new element is written as ASR writes it: each child on its own line, two
  spaces per level, `<x />` for an empty element, the file's own line endings and
  indentation, or no whitespace at all in a file without line breaks. Its text is
  encoded back to the file's encoding, so a BOM and a UTF-16 file stay as they were.
- A download without edits is the original bytes.

### The editor

[`app.js`](../site/app.js) and [`settings.js`](../site/settings.js) are the
component's editor in JavaScript, working on the same XML, so
[its rules](#the-settings-editor) apply: unknown keys stay, one trigger per
segment of the file with entries past the last hidden and kept, presets as
actions, kinds offered per `offer`, a newer `schema_version` read-only and never
written, Everest-only fields hidden while `everest` is off and kept. The layout is
its layout, with `high_priority` and `set_game_time` labelled "LiveSplit (Windows)
only". `problem` in `settings.js` is the third copy of the module's
[invalid-trigger rules](#reading-them), kept in step by the table in
[What the tests prove](#what-the-tests-prove).

### Importing legacy settings

A `.lss` with a legacy node, as [the component](#mapping) defines one, offers an
import, for runners taking old Windows files to other hosts: livesplit-core reads
only `CustomSettings`. [`site/legacy-import/`](../site/legacy-import/) is a small
`net9.0` project on `Microsoft.NET.Sdk.WebAssembly`, which compiles
`LegacyMigration.cs` itself, the component's source file, with one `[JSExport]`.
`legacy.js` loads it only when importing: 16 files, 5.4 MB, 2.1 MB gzipped, from
`legacy/wwwroot/_framework/`. Publishing with the default trimming keeps
`LegacyMigration`'s `Enum.TryParse` and `Enum.ToString` working: the site's
checks run it in the browser on every `.lss` in `LegacySplits/`. Its output
replaces the settings in the editor, and comes out as a normal download.

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
  starts, splits and resets the `Timer` gets, their frames, and their game times
  must be exactly the scenario's, which are written by hand from the trace. A
  split's game time is the split time the run records, a start's the game time
  once its tick ends, and a reset has none. They're compared exactly, to the
  tick of 100 ns: game time is paused, so livesplit-core holds exactly what the
  module set, and game time left running would show as a few microseconds
  past it.

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
`cfg`, or another chapter, and that a `sid` is ignored with `everest` off, and
never fires on vanilla with it on. The other traces' scenarios add what
[their tables](../traces/README.md#scenarios) list.

The [component's tests](livesplit-notes.md#the-components-tests) prove that
LiveSplit's ASR loads the wasm module and keeps its settings, that legacy
settings migrate to hand-written XML, which round-trips through ASR unchanged,
and that the editor's changes reach ASR as hand-written XML. The process
priority has no test: it rests on a hand check in a copy of LiveSplit 1.8.37 on
2026-11-08. It also covered the settings dialog and Cancel, Game Time, a deleted
wasm module, the layout editor, and, in the real game, the module's resets, alone, after a run that ended with new bests, and with a start
in one tick, none showing a prompt.
The harness's test, `cargo test`, reads the same XML through livesplit-core's
`StoredAutoSplitterSettings` and gets it back.

The site's checks, `site/test/run.sh`, run the real page in a pinned
chrome-headless-shell, driving its controls, and post what it downloads to an
ignored test in the harness, which also serves the page:

- Each edit case in `site/test/cases/` must come out byte for byte as its
  hand-written `.expected.lss`: UTF-8 with and without a BOM, CRLF and LF, UTF-16,
  a file with no line breaks, a missing element, and a comment and a CDATA section
  holding the tag's text. Those UTF-8 files livesplit-core can read then go through
  its own `.lss` parser, and what it reads has to equal the expected file's
  settings. Unknown keys, an entry past the last segment, a hidden `sid` and the
  file's other bytes all come through. `dotnet test` also takes every
  `.expected.lss` through the real ASR, which writes the same XML back.
- Importing runs the browser's build of `LegacyMigration.cs` on every `.lss` in
  `LegacySplits/`, and livesplit-core must read the download as its hand-written
  `.expected.xml`, the files the component's tests use. So the browser build,
  with XPath, `Enum.TryParse` and `Enum.ToString` after trimming, gives the
  component's results.
- A table of 43 triggers, valid and invalid, is a scenario on `prologue`, once
  with `everest` off and once on, with a hand-written `invalid` list: the triggers
  the module logs as invalid. The Live check runs the real module and compares
  its log with the list; the site's checks run `problem` on the same table and
  compare with the same list. The table is the only link between them, so a
  change to either that disagrees with it fails its check.
- A newer `.lss` says so, disables every control and offers no download, and
  invalid triggers say what the module would read as invalid.

A pass rests on the hand-written files and that browser's `DOMParser`, on Windows
or Linux. How a control looks, focus and keyboard use, and a real download, are
left to a hand check.

### What they don't prove

- Everest: no probe constructs it, and CI doesn't run it. Its offsets rest on
  the transcription, and nothing checks the 0xff marker. No trace has its
  values, so matching a `sid` rests on a hand check on 2026-11-07, on Windows
  with Everest `stable-1.6623.0`, while `traces/forsaken-city.tas` played:
  `chapter_start`, `chapter_complete`, `chapter_enter`, `room_enter`, `heart`
  and `chapter_exit` with the Prologue's or Forsaken City's SID fired on the
  updates the trace gives, `chapter_enter` on resuming too, a `sid` of another
  chapter didn't, and with `everest` off, a `sid` was ignored. `Chapter SID`
  is empty outside a chapter.
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
- Loading the settings again, once a second and on a reset: a scenario's map
  never changes. Nor what the module logs.
- What no trace has: disarming when the split index changes or the side
  doesn't match; the chapter changing while the side doesn't, which
  `chapter_enter` and `chapter_exit` with a `side` ignore, since on vanilla
  the side is -1 outside a chapter; a checkpoint's room for the wrong side,
  since neither side's checkpoint rooms appear on the other; a rise of
  `ChapterStarted` with `ChapterTime` above 0 and no last value, as on resuming
  just after attaching on the map, since every trace starts from the title
  screen or a fresh load; `ChapterCassette` coming back set on entering a
  chapter, so nothing checks that `cassette` ignores it there, as the heart's
  flag shows `heart` does; resuming into a checkpoint's room; and a paused
  timer.
- Starts not made by the module, and `timing_method` changing during a run:
  the harness never starts the timer by hand, and a scenario's map never
  changes. A reset and start by hand between two ticks isn't seen as a start.
- Splitting in LiveSplit: the component's tests don't run the game.
- That a migrated list splits as legacy did: the mapping's expectations are
  hand-written from the audit of legacy's source, and no test runs legacy.
- The site's download in a real browser, on GitHub Pages, and the hosts that don't
  read the list: the site's checks read the file before the browser would save it.

## Known gaps

- **Garbage the type check can't see.** An object of another type with the
  same size, where a garbage collection left a reference, would pass the check.
  So would a string moved between reading its length and its characters. In
  `SaveData.Instance`'s slot, with 0 where `Time` is, such an object would make
  `Starting New File` rise.
- **A file saved before its timer started.** On vanilla, continuing it reads as
  a new file, since its `Time` is still 0.
- **Resuming read as a start.** Resuming reads as a start when the last
  `ChapterTime` read inside a chapter isn't the resumed chapter's: after
  attaching on the map between a Save and Quit and the resume, or after playing
  another file to a higher `ChapterTime` than the resumed chapter's. Then
  `chapter_start`, `room_enter` and `checkpoint` fire, so a `chapter_start`
  reset resets the attempt, and after a `chapter_exit` reset on the Save and
  Quit, as the IL preset's, a `chapter_start` start begins a new one.
- **Negative game time.** With a `*_since_start` method, a clock below its
  value at the start, as `ChapterTime` after restarting the chapter, makes game
  time negative, as legacy's custom-start case does.

## Supported versions

- **Vanilla Celeste 1.4.0.0** on all four flavours, checked in CI.
- **The latest Everest**, through its struct for modded play, and as vanilla
  with `--vanilla`.
- **Everest 1.4449**, the last stable before Everest moved to .NET Core, only
  with `--vanilla`, checked once by hand. Modded play on it isn't supported,
  because only Everest on .NET Core has the struct.
- **LiveSplit** on 64-bit Windows, through the component, built against
  LiveSplit 1.8.37. Hosts built on livesplit-core load the wasm module alone.
