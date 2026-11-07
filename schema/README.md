# schema

`settings-schema.json` describes the settings: the `cfg` keys, the trigger kinds
and their fields, the presets, and the vanilla chapters with their checkpoints.
It's written by hand. The wasm module's build reads it, as
[How it works](../docs/how-it-works.md#settings) describes, with what the
settings mean. The component embeds it as the resource
`CelesteAutosplit.settings-schema.json`, and the website's deploy and
`site/serve.mjs` copy it next to `index.html`, but neither uses it yet.

## Format

Plain JSON with its own vocabulary, not JSON Schema. Anything with an order is an
array: migration writes keys in that order, and editors list things in it.

| Key | Type | Meaning |
|---|---|---|
| `schema_version` | number | The `cfg` layout version this file describes. |
| `settings` | array | The top-level `cfg` keys, in order. |
| `fields` | object | The fields triggers can have, by name. |
| `triggers` | array | The trigger kinds, in the order editors list them. |
| `presets` | array | Editor presets. |
| `chapters` | array | Vanilla chapters, with their checkpoints. |

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
| `type` | yes | See the types above. |
| `items` | for `list` | Item type. |
| `default` | yes | Used when the key is missing. |
| `label` | yes | Display name. |
| `description` | no | Help text. |
| `values` | no | The allowed values, each `{ "value", "label", "description"? }`. Anything else is invalid. |
| `host` | no | `"livesplit"`: only the LiveSplit component applies it. |

### `fields{}`

| Key | Required | Meaning |
|---|---|---|
| `type` | yes | See the types above. |
| `label` | yes | Display name. |
| `description` | no | Help text. |
| `default` | no | Used when the field is missing. Fields without one are filters that match anything when missing, except `chapter` on `chapter_enter` and `chapter_exit`, which then matches any chapter. |
| `values` | no | The allowed values, as in `settings`. |
| `values_from` | no | The allowed values come from `chapters`: `"chapters"` (chapter IDs; the module also takes modded IDs above them) or `"checkpoints"` (checkpoint numbers of the trigger's chapter). |
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
| `offer` | yes | Where editors offer it: any of `"start"`, `"reset"` and `"splits"`. Every kind is still valid in all three. An empty list means editors never offer it, as for `unknown`, which only migration makes. |

### `presets[]`

| Key | Meaning |
|---|---|
| `id` | Stable identifier. |
| `label` | Display name. |
| `settings` | Top-level `cfg` values the preset sets. |
| `trigger_fields` | Field values the preset sets on every trigger of a kind, by kind. |

### `chapters[]`

| Key | Meaning |
|---|---|
| `id` | The game's chapter ID. |
| `name` | Display name. |
| `sides` | Side values the chapter has (`0` = A, `1` = B, `2` = C). |
| `checkpoints` | In order, so checkpoint `n` is entry `n - 1`. |

Each checkpoint has `names` and `rooms`, both indexed by side. `rooms[side]` is
the room that fires the `checkpoint` trigger on that side. A missing entry means
the side has no named checkpoint there, or that the trigger never fires on it.
