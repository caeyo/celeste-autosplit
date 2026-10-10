# schema

`settings-schema.json` is the hand-written, single source of truth for the
`cfg` settings layout. The editors render from it, and the wasm module
generates its settings types and checkpoint rooms from it, so field lists and
room tables are never maintained by hand in three places.

How each project consumes it:

| Project | How |
|---|---|
| `component/` | Embedded resource `CelesteAutosplit.settings-schema.json` (see `CelesteAutosplit.csproj`). |
| `site/` | Copied next to `index.html` at deploy time and by `site/serve.mjs`. |
| `autosplitter/` | Not wired up yet. Planned: read by a `build.rs` at build time, so no JSON parser ends up in the wasm. |

When changing it, additive changes don't bump `schema_version`. These are a
new optional key with a default, a new trigger kind, or a new optional field.
Renames, removals and changes of meaning do bump it.

## Format

Plain JSON with own vocabulary (not JSON Schema). Anything that has an
order is an array, because migration writes keys in this order and editors
list things in it.

### Top level

| Key | Type | Meaning |
|---|---|---|
| `schema_version` | number | The `cfg` layout version this file describes. |
| `settings` | array | The top-level `cfg` keys, in order. |
| `fields` | object | The fields triggers can have, by name. |
| `triggers` | array | The trigger kinds, in the order editors list them. |
| `presets` | array | Editor presets. |
| `chapters` | array | Vanilla chapters, with their checkpoints. |

### Types

Types use the settings map's own names, plus one for triggers:

| `type` | Meaning |
|---|---|
| `bool`, `i64`, `string` | The settings map value of that type. |
| `trigger` | A map holding a `kind` and that kind's fields. |
| `list` | A list, with the item type in `items` (only `"trigger"` so far). |

### `settings[]`

| Key | Required | Meaning |
|---|---|---|
| `key` | yes | The key inside `cfg`. |
| `type` | yes | See Types. |
| `items` | for `list` | Item type. |
| `default` | yes | Used when the key is missing. |
| `label` | yes | Display name. |
| `description` | no | Help text. |
| `values` | no | Allowed values (below). Anything else is invalid. |
| `host` | no | `"livesplit"`: only the LiveSplit (Windows) wrapper applies it. The wasm module ignores it, and the website labels it "LiveSplit (Windows) only". |

`values` entries are `{ "value", "label", "description"? }`.

### `fields{}`

| Key | Required | Meaning |
|---|---|---|
| `type` | yes | See Types. |
| `label` | yes | Display name. |
| `description` | no | Help text. |
| `default` | no | Used when the field is missing. Fields without one are filters that match anything when missing. |
| `values` | no | Allowed values, as in `settings`. |
| `values_from` | no | The allowed values come from `chapters`: `"chapters"` (chapter IDs) or `"checkpoints"` (checkpoint numbers of the trigger's chapter). |
| `no_chapter` | no | On `chapter`: the value and label for "not in a chapter". Editors offer it only for triggers with `allow_no_chapter`. |
| `everest` | no | `true`: Everest-only. Counts only when `cfg.everest` is on; otherwise editors hide it but keep it. |
| `replaces` | no | When this field is present and counts, it stands in for the named field, including where that field is required. |
| `read_only` | no | `true`: editors show it but never edit it. |

### `triggers[]`

| Key | Required | Meaning |
|---|---|---|
| `kind` | yes | The trigger's `kind` value. |
| `label` | yes | Display name. |
| `required` | yes | Field names the trigger must have. |
| `optional` | yes | Field names the trigger may have. |
| `offer` | yes | Where editors offer it: any of `"start"` and `"splits"`. Every kind is still valid in both places. An empty list means editors never offer it (for example `unknown`, which only migration produces). |
| `allow_no_chapter` | no | `true`: `chapter` may also be the `no_chapter` value. |

### `presets[]`

| Key | Meaning |
|---|---|
| `id` | Stable identifier. |
| `label` | Display name. |
| `settings` | Top-level `cfg` values the preset sets. |
| `trigger_fields` | Field values the preset sets on every trigger of a kind, by kind. |

Presets are never stored. Editors show the preset whose values all match, or
"Custom" if none does.

### `chapters[]`

| Key | Meaning |
|---|---|
| `id` | The game's chapter ID. |
| `name` | Display name. |
| `sides` | Side values the chapter has (`0` = A, `1` = B, `2` = C). |
| `checkpoints` | In order, so checkpoint `n` is entry `n - 1`. |

Each checkpoint has:
- `names`: indexed by side. A missing entry means the side has no named
  checkpoint there.
- `rooms`: indexed by side. `rooms[side]` is the room that fires the
  `checkpoint` trigger on that side. A missing entry means it never fires on
  that side.
