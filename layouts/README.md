# layouts

The memory layouts the auto splitter reads, one file per game flavour. What gets
measured is listed in the layout probe's
[`Manifest.cs`](../tools/layout-probe/Manifest.cs), described in its
[README](../tools/layout-probe/README.md#the-manifest).

| File | What | Made by |
|---|---|---|
| `windows-xna.json`, `windows-fna.json`, `linux.json`, `macos.json` | Each vanilla flavour's measured layout. | The [layout probe](../tools/layout-probe/), through the Layouts workflow. Don't edit them by hand. |
| `everest.json` | Everest's own info struct. | Transcribed from Everest's source. |

## Regenerating

Run the Layouts workflow, from the Actions tab or with:

```sh
gh workflow run layouts.yml --ref <branch>
```

It runs the probe on every vanilla flavour, then opens a pull request into
`<branch>` with the regenerated files, from the branch `layouts/<branch>`. If
nothing changed, it says so and opens nothing. A later run updates an open pull
request instead of opening another. The run uses `<branch>`'s workflow and
probe, so changes can be tried before they're merged.

For each flavour, the probe runs inside the game's own runtime, next to the real
game files. It:

1. resolves every name in the manifest, and measures each path's offsets;
2. checks that the flavour's hand-written signature matches the root's anchor
   method once, in its compiled code, and that the first of its store patterns
   after the match holds the slot's address;
3. constructs the game for real (`new Celeste()`), in a process named like the
   game. Then it writes distinctive values into every field that still holds its
   default, and reads every path back raw from the slot, as the auto splitter does.

Any failure fails the run, and no pull request is opened. To run the probe
yourself, see its [README](../tools/layout-probe/README.md).

## `<flavour>.json`

Abbreviated from `windows-fna.json`:

```json
{
  "flavour": "windows-fna",
  "os": "windows",
  "game": { "version": "1.4.0.0", "mvid": "d7b2abb4-05ec-4c97-9545-49f0a02e4a41" },
  "pointer_size": 4,
  "string": { "length": 4, "chars": 8 },
  "roots": {
    "Celeste.Celeste::Instance": {
      "signature": "68 38 04 00 00 68 c0 03 00 00 68 1c 02 00 00 ff 35",
      "store": ["8d 15 SS SS SS SS e8"],
      "paths": {
        "Title": { "offsets": [132], "types": ["System.String"] },
        "AutoSplitterInfo.Chapter": { "offsets": [160, 24], "types": ["Celeste.AutoSplitterInfo", "System.Int32"] }
      }
    }
  }
}
```

| Key | Meaning |
|---|---|
| `flavour` | `windows-xna`, `windows-fna`, `linux` or `macos`. |
| `os` | `windows`, `linux` or `macos`, as `asr::get_os()` names them. |
| `game` | The probed `Celeste.exe`: its version, and its module version ID. The three FNA flavours share one `Celeste.exe`. |
| `pointer_size` | 4 or 8 bytes. |
| `string` | Where a string object holds its length (an `i32`) and its UTF-16 characters. |
| `roots` | Per manifest root, by its `field`: |
| &emsp;`signature` | The probe's copy of the hand-written signature, which matches the anchor's code once: hex bytes, `??` for any byte. |
| &emsp;`store` | The probe's copy of the hand-written store patterns: the code that stores into the slot, after the signature. The first one to match after the signature holds the slot's address, in its 4 or 8 `SS` bytes. |
| &emsp;`paths` | Per manifest path: each field's offset and .NET type, one per hop. |

All offsets are in bytes, from the address a reference to the object holds. To read
a path, read the root's object from the slot. Then each offset but the last leads to
a pointer to the next object, and the last one to the value.

The anchor's machine code isn't stored: it embeds addresses that change on every
run. The probe prints it in the run's log.

## `everest.json`

Everest publishes its own struct, `CoreAutoSplitterInfo`, at the start of a memory
mapping of its own. The auto splitter finds it by checking the start of each memory
region for the magic bytes. The file is transcribed by hand from the source linked in
`source`. To update it, transcribe the struct again at a newer commit, and update the
link.

| Key | Meaning |
|---|---|
| `source` | The struct's source, at a pinned commit, and the Everest release it's from. |
| `pointer_size` | 8: Everest runs on 64-bit .NET. |
| `magic` | The struct's first 20 bytes. |
| `info_version` | The struct's version, in `InfoVersion`. |
| `updating_info_version` | What `InfoVersion` holds while Everest updates the struct. |
| `fields` | Each field's offset from the struct's start, and its type: `u8`, `u32`, `i32`, `i64`, or `utf8_ptr`, a pointer to a NUL-terminated UTF-8 string with its length in bytes in the `u16` before it, 0 for no string. Flag fields list each flag's bit number. |
