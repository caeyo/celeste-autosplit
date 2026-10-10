# site

The settings website, hosted on GitHub Pages. It edits the
settings map inside a `.lss` file entirely in the browser; nothing is uploaded.

Plain static HTML/CSS/JS: no build step, no frameworks, no npm.

## Schema

The shared schema is served at `settings-schema.json` next to `index.html`. It
is not committed here: the deploy workflow copies it from
[`../schema/settings-schema.json`](../schema/settings-schema.json), and so does
`serve.mjs` for local development (the copy is gitignored).

## Local development

```sh
node site/serve.mjs       # http://localhost:8000, optional port argument
```

`serve.mjs` is a dependency-free static server for development only; it is not
deployed. Any static file server works if you copy the schema in yourself.

## Deployment

[`.github/workflows/site.yml`](../.github/workflows/site.yml) deploys on every
push to `master` that touches `site/` or `schema/`. The repository's Pages
source must be set to **GitHub Actions** (Settings → Pages).
