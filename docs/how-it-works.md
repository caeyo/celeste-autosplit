# How it works

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
the instruction. So the probe only searches the anchor's code for the slot's
absolute address: as a u32, and in 64-bit processes also as a u64.

## The probe

- The anchor's length isn't available, so the probe reads 768 bytes of its code,
  stopping early at an unmapped page. That covers `Celeste`'s constructor on every
  flavour: 0xb6 bytes on Windows, 0x15a on Linux and 0x1e9 on macOS.
- It runs the class constructors before compiling the anchor, as the game has by
  the time it runs it: the JIT may compile static accesses differently before a
  class is initialized. `SaveData` has none, so it doesn't matter that the game
  may not have initialized it when it first runs `AutoSplitterInfo.Update`.
- Once a method is compiled with `RuntimeHelpers.PrepareMethod`, both runtimes
  return its code from `GetFunctionPointer`, not a stub that jumps to it.
- `GC.SuppressFinalize` keeps the game's finalizers from running on the
  uninitialized objects the probe makes, and on the game it constructs, which
  nothing disposes before the process exits.
- The game's `Main` loads the settings before constructing the game, and the
  constructor reads `Settings.Instance.Fullscreen` and `VSync`.
- Mono reports Unix on both Linux and macOS, so the probe asks the kernel: the
  struct `uname` fills starts with the kernel's name on both.
- A field's offset is `&obj.field - obj`, computed in one dynamic method, so that
  the GC can't move the object in between. Its IL is unverifiable, which dynamic
  methods may be in a full-trust process.
- Reading an unmapped page crashes the process rather than throwing, so reads stop
  at the first one. `msync` fails with `ENOMEM` on unmapped pages on both Linux and
  macOS, which `mincore` doesn't on macOS. `MS_ASYNC` is 1 on both, and does
  nothing to anonymous memory.

## Everest

[`layouts/everest.rs`](../layouts/everest.rs) is Everest's `CoreAutoSplitterInfo`,
transcribed by hand from
[`AutoSplitter.cs`](https://github.com/EverestAPI/Everest/blob/47d6a61fa918c17b8085b93b61ee59a7465968f8/Celeste.Mod.mm/Mod/Everest/AutoSplitter.cs)
at commit `47d6a61`, in Everest `stable-1.6623.0`, where each field's type is. To
update it, transcribe the struct again at a newer commit, and update the link.

- Everest puts the struct at the start of a memory mapping of its own. Each
  field's offset is from the struct's start.
- Everest runs on 64-bit .NET, so a pointer takes 8 bytes.
- A string is a pointer to NUL-terminated UTF-8, with its length in bytes in the
  `u16` before it, or 0 for no string.
- `InfoVersion` reads 0xff while Everest updates the struct, as it does while it
  raises `AutoSplitter.OnUpdateInfo`.
