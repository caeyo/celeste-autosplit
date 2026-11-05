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

## The Layouts workflow

- The game is x86_64 only: on the arm64 macOS runner, it runs under Rosetta.
- The game's code runs in the probe's job, so its checkout doesn't keep the
  token: `persist-credentials: false`.
- XNA's assemblies load from the GAC, so the XNA build needs the XNA 4.0
  redistributable installed. It also installs the VC++ 2010 runtime and the
  DirectX pieces XNA's assemblies import.
