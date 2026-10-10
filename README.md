# celeste-autosplit

An auto splitter for Celeste that runs on every LiveSplit host.

## Repository layout

| Folder | What | Built with |
|---|---|---|
| [`autosplitter/`](autosplitter/) | The auto splitter itself, compiled to `celeste-autosplit.wasm`. The only splitting engine on every host. | Rust (`cargo`) |
| [`component/`](component/) | `celeste-autosplit.dll`, the LiveSplit (Windows) component that wraps the wasm module and provides the settings editor and legacy migration. | .NET SDK (`dotnet`) |
| [`schema/`](schema/) | `settings-schema.json`, the shared settings schema consumed by the component and the website. | — |
| [`site/`](site/) | The GitHub Pages settings editor for `.lss` files, for livesplit-core hosts. | Static HTML/CSS/JS |
| [`update.xml`](update.xml) | LiveSplit's update manifest for the component. | — |

## Building

```sh
cd autosplitter && cargo b --release    # wasm module
cd component && dotnet build            # LiveSplit component (+ dotnet test)
node site/serve.mjs                     # website at http://localhost:8000
```

See each folder's README for details.

## CI and releases

| Workflow | Runs |
|---|---|
| `autosplitter.yml` | Build, clippy and fmt on changes to `autosplitter/`. |
| `component.yml` | Build and test on Windows on changes to `component/` or `schema/`. |
| `site.yml` | Deploys `site/` (plus the schema) to GitHub Pages on pushes to `master`. |
| `release.yml` | On a `v*` tag: builds both, then publishes `celeste-autosplit.dll` and `celeste-autosplit.wasm` together. |

Releasing:

1. Set `<Version>` in `component/CelesteAutosplit/CelesteAutosplit.csproj`.
2. Push a matching tag, for example `v1.0.0`.
3. After the release is published, add the version to `update.xml` and commit it.
