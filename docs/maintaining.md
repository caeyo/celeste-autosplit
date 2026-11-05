# Maintaining

## Line endings

Shell scripts keep LF endings on every OS: bash fails on CRLF, which checkouts on
Windows would give them. Traces and their TAS files keep the LF endings the
recorder and CelesteTAS write. `.gitattributes` sets both.

## The probe

- It targets .NET Framework 4.5 (`net45`), as `Celeste.exe` does, since it runs in
  the game's own runtime. On Linux and macOS, that's the Mono bundled with the
  game, which only has the 4.5 profile's assemblies.
- `Celeste.exe` is marked x86-only, so on Windows only a 32-bit process can load
  it. Mono ignores the flag and runs 64-bit.

## The harness

- The probe's standard error is piped into its log, like its output. On Linux and
  macOS, the probe runs behind `xvfb-run` or `sudo`, so killing it can leave it
  running, and a probe left running would otherwise hold the harness's own output
  open.
- The probe's output is read as lossy UTF-8: on Windows, paths are in the
  console's code page.

## The Live check workflow

- The game files are release assets of a private repository, fetched with the
  `GAME_FILES_TOKEN` secret: only what the probe needs to construct the game,
  without `Content`, which it never reads. They never go into caches or
  artifacts, since artifacts can be downloaded by anyone who can read this
  repository, and caches restored by any run in it.
- Pull requests from forks can't have the token, so the workflow skips them. To
  check one, review it, then push its branch to this repository.
- Only `~/.cargo` and the target directories are cached, and the two Windows
  flavours share one cache. A pull request only restores the caches of its own
  runs and of `master`'s, so the runs on `master` keep every pull request's
  first run warm.
- The game is x86_64 only: on the arm64 macOS runner, it runs under Rosetta.
- The game's code runs in the job, so its checkout doesn't keep the token:
  `persist-credentials: false`.
- XNA's assemblies load from the GAC, so the XNA build needs the XNA 4.0
  redistributable installed. It also installs the VC++ 2010 runtime and the
  DirectX pieces XNA's assemblies import.
- On Linux, the `Celeste` script runs `Celeste.bin.x86_64`, a MonoKickstart that
  runs `Celeste.exe` with the game's own Mono. On macOS,
  `Celeste.app/Contents/MacOS/Celeste` is the MonoKickstart, and runs
  `Resources/Celeste.exe`.
- FNA needs a display to create its window, so on Linux the probe runs under
  `xvfb-run`. The runner image already has Xvfb and Mesa, and the window never
  gets a GL context, so software rendering is enough.
- The harness starts the probe, so on Linux the probe is its descendant, whose
  memory Yama lets it read. On macOS, reading another process's memory needs
  root, so the harness runs with `sudo` and starts the probe with
  `sudo -u` as the normal user, as players run the game.
- `release.yml` calls it, passing the token, and publishes only once it passes
  on every flavour.
