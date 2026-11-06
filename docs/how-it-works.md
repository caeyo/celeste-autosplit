# How it works

## Attaching

The auto splitter attaches to the first process named `Celeste.bin.x86_64`,
`Celeste.bin.x86`, `Celeste.exe` or `Celeste`, then searches its memory for what
it reads. A search scans the process's memory, and LiveSplit runs the auto
splitter on its UI thread, so after a failed search the auto splitter waits a
second before the next, and logs only the first failure. After 10 failed
searches, it detaches and attaches again:

- On Linux, vanilla starts through a bash script named `Celeste`, which runs
  `Celeste.bin.x86_64` as its child. If the host's process list is refreshed
  between the two, the auto splitter attaches to the script, and only attaching
  again reaches the game.
- Under Everest on Linux, the game itself runs as `Celeste`, so the name stays
  in the list.

asr's `sleep` and `timeout` need WASI, which the module isn't built for, so a
second is 120 ticks, the runtime's default tick rate, which hosts follow.

## The layouts

[`vanilla.rs`](../autosplitter/src/vanilla.rs) holds what the auto splitter
reads on each runtime the game runs on: `CLR32`, 32-bit .NET Framework, for
Windows's XNA and FNA builds, and `MONO64`, 64-bit Mono, for Linux's and
macOS's. The game's own classes lay out the same on every flavour of a runtime.
Each holds:

- the pointer size, and where a string holds its length, an `i32`, and its
  UTF-16 characters;
- for `Celeste.Instance` and `SaveData.Instance`, the anchor whose compiled code
  holds the static slot's address: a signature that matches the code once,
  reference patterns for an instruction after it that holds the address, and how
  many bytes after the signature are searched for the reference, which ends
  within them;
- the pattern for the store of `AutoSplitterInfo` before `Celeste.Instance`'s
  signature, and how many bytes before the signature are searched for it, which
  starts within them;
- each value's offset: after `AutoSplitterInfo`, or for `TotalDeaths`, from the
  object in `SaveData.Instance`'s slot.

A pattern is hex bytes, with `??` for any byte, and `SS` for the bytes of the
value it holds, in a row, least significant first. The reference is the earliest
match of any reference pattern after the signature: earliest, because a pattern
with a 4-byte address can also match inside one with an 8-byte address.

To read a value, the auto splitter reads the root's object from its slot, and
for `Celeste.Instance`, `AutoSplitterInfo`'s object from it, at the offset the
store holds. The value's offset leads from there. All offsets are in bytes, from
the address a reference to the object holds.

Each tick, the auto splitter reads every value, and reports them only if every
read succeeds. The exceptions are `Level`, which goes unreported when there's no
string, and `TotalDeaths`, which goes unreported until a file starts.

`AutoSplitterInfo`'s own offset is read from the code because it comes after the
fields of the framework's `Game` class, and Everest replaces the FNA that vanilla
runs on, with a different one in different versions, so no layout could hold it
for every game.

## Finding `Celeste.Instance`

`Celeste`'s constructor passes the game and window sizes, 1920x1080 and 960x540,
to `Engine`'s constructor, then stores itself in `Celeste.Instance`. The
signatures match the size arguments: pushed on Windows, in registers on Mono.

The reference patterns match the store into the slot that follows:

- Windows: the slot's address goes to the GC's write barrier: `lea edx, [slot]; call`.
- Mono: the address is loaded into `rax` and stored through: `mov eax, slot` or
  `mov rax, slot`, then `mov [rax], r15`. The 4-byte form is used when the address
  fits, which depends on where the heap is, so both forms are listed. Where the
  heap is also changes the length of other instructions between the signature and
  the store, so the reference is searched for, not expected at a fixed distance.

`Celeste`'s field initializers run before `Engine`'s constructor, so the store of
its new `AutoSplitterInfo` comes before the signature, and holds the field's
offset:

- Windows: the field's address goes to the write barrier: `lea edx, [esi+offset]; call`.
- Mono: it's computed for the write barrier: `lea rax, [r15+offset]`.

The store is the last match before the signature, the nearest one, because the
search can reach code before the constructor.

## Finding `SaveData.Instance`

`AutoSplitterInfo.Update` runs from the game's first update. It reads
`SaveData.Instance` for the file's values, first `FileTime`, right after setting
`ChapterTime` from the level's session. The signatures match setting
`ChapterTime`, and the reference is the load from `SaveData.Instance` that
follows:

- Windows: `mov ecx, [slot]; test ecx, ecx`.
- Mono: `mov rax, [slot]` when the address fits in 32 bits, as on Linux, and
  otherwise, as on macOS, `mov rax, slot; mov rax, [rax]`. Which one depends on
  where the heap is, so both are listed.

## Static addresses in code

On these runtimes, code never addresses a static relative to the instruction:
x86 has no RIP-relative addressing, and Mono's JIT writes a static's address into
the instruction. So the dump only searches the anchor's code for the slot's
absolute address: as a u32, and in 64-bit processes also as a u64.

## The probe

[`tools/probe/`](../tools/probe/) is one executable, installed as the game's
`Celeste.exe`, that runs inside the game's own runtime and loads the game from
`real/Celeste.exe` beside it. It takes one of two commands.

`dump` prints what keeping the layouts by hand needs:

1. the pointer size, and where strings hold their length and characters;
2. every instance field's offset, name and type, for `Celeste` and its base
   classes, `AutoSplitterInfo`, `SaveData`, `Session`, `AreaStats` and
   `AreaModeStats`, measured on uninitialized objects;
3. for each anchor in `Game.cs`'s `Anchors`, compiled as the host compiles it:
   the static slot's address, the anchor's machine code, and each offset in the
   code where the slot's address appears, with its width.

[`tools/probe/fields/`](../tools/probe/fields/) keeps the dump's field tables
from a Live check run, without the machine code: one file for each Windows
flavour, and one for Linux and macOS, whose tables are identical.

`host` holds the real game for the [harness](../harness/):

1. it runs the class constructors and compiles each anchor;
2. it constructs the game (`new Celeste()`). `SaveData.Instance`, which the game
   leaves null until a file starts, gets an uninitialized object of the probe's
   own, so that the host can write values into it;
3. it prints `probe: ready`, then reads sets of values from its standard input
   until its input ends. Each set is a `root path=value` line per value, then an
   empty line. The root is a static field, as `Namespace.Type::Field`, and the
   path is instance field names from the root's object, joined with dots, each
   looked up in the object's class or its nearest base class with that field. The
   value is read as the last field's type: `true` or `false`, a whole number, or
   for a string, the rest of the line. The host writes each line into the game
   through reflection, and prints `probe: written` after each set.

On a failure, either command prints why, then `probe: failed`, and exits with 1,
or with 2 for a bad command.

- The anchor's length isn't available, so the dump reads 768 bytes of its code,
  stopping early at an unmapped page. That covers `Celeste`'s constructor on every
  flavour: 0xb6 bytes on Windows, 0x15a on Linux and 0x1e9 on macOS.
- Both commands run the class constructors before compiling an anchor, as the
  game has by the time it runs it: the JIT may compile static accesses
  differently before a class is initialized. `SaveData` has none, so it doesn't matter that the game
  may not have initialized it when it first runs `AutoSplitterInfo.Update`.
- Each anchor is compiled with `RuntimeHelpers.PrepareMethod`, and its code is
  taken from `GetFunctionPointer`, which then returns the code, not a stub that
  jumps to it, on both runtimes. The host has to compile the anchors because the
  game only runs `AutoSplitterInfo.Update` from its first update, which the host
  never runs. On Mono, `PrepareMethod` alone isn't enough: unless the host also
  calls `GetFunctionPointer`, the auto splitter never finds `SaveData.Instance`.
- `GC.SuppressFinalize` keeps the game's finalizers from running on the
  uninitialized objects the probe makes, and on the game it constructs, which
  nothing disposes before the process exits.
- The game's `Main` loads the settings before constructing the game, and the
  constructor reads `Settings.Instance.Fullscreen` and `VSync`.
- A field's offset is `&obj.field - obj`, taken with `ldflda` in one dynamic
  method, so that the GC can't move the object in between: `Marshal.OffsetOf`
  doesn't work on auto-layout classes. Its IL is unverifiable, which dynamic
  methods may be in a full-trust process.
- Reading an unmapped page crashes the process rather than throwing, so reads stop
  at the first one. `msync` fails with `ENOMEM` on unmapped pages on both Linux and
  macOS, which `mincore` doesn't on macOS. `MS_ASYNC` is 1 on both, and does
  nothing to anonymous memory.

## Everest

[`everest.rs`](../autosplitter/src/everest.rs) is Everest's `CoreAutoSplitterInfo`,
transcribed by hand from
[`AutoSplitter.cs`](https://github.com/EverestAPI/Everest/blob/47d6a61fa918c17b8085b93b61ee59a7465968f8/Celeste.Mod.mm/Mod/Everest/AutoSplitter.cs)
at commit `47d6a61`, in Everest `stable-1.6623.0`, where each field's type is. To
update it, transcribe the struct again at a newer commit, and update the link.

- Everest puts the struct at the start of a memory mapping of its own, and the
  auto splitter finds it by checking the start of each memory region for its
  magic bytes. Each field's offset is from the struct's start.
- Everest runs on 64-bit .NET, so a pointer takes 8 bytes.
- A string is a pointer to NUL-terminated UTF-8, with its length in bytes in the
  `u16` before it, or 0 for no string.
- `InfoVersion` reads 0xff while Everest updates the struct, as it does while it
  raises `AutoSplitter.OnUpdateInfo`.
