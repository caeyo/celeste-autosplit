# LiveSplit notes

## The component

- The factory's `Version` has three parts, so that it compares equal to the
  versions in `update.xml`.

## ASR

- ASR loads its native library, `Components\x64\asr_capi.dll`, relative to the
  process's working directory.
- `SetSettings` replaces the settings map rather than merging into it. The
  editor relies on that to remove keys.
- The auto splitting runtime supports every wasm feature that
  `autosplitter/.cargo/config.toml` enables.

## livesplit-core

- Its auto splitting ignores the timer's own errors, such as splitting with no
  attempt running. The harness's timer ignores them too.

## Tests

- The tests that run the wasm module change process-wide state, the working
  directory and `ComponentManager.BasePath`, so they never run in parallel.
- Their scratch folders are kept short and out of the output folder, since .NET
  Framework doesn't support paths over 260 characters by default.
