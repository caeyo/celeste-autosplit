# schema

`settings-schema.json` is the hand-written, single source of truth for the
`cfg` settings layout. The file format itself is still to
be designed; it currently holds only the schema version.

How each project consumes it:

| Project | How |
|---|---|
| `component/` | Embedded resource `CelesteAutosplit.settings-schema.json` (see `CelesteAutosplit.csproj`). |
| `site/` | Copied next to `index.html` at deploy time and by `site/serve.mjs`. |
| `autosplitter/` | Not wired up yet (e.g. `include_str!` or a `build.rs`). |

When changing it, additive changes (a new optional key with a default) don't
bump `schema_version`; renames, removals and changes of meaning do.
