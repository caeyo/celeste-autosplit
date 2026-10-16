#!/usr/bin/env bash
# Records the trace of each TAS file given, next to it: traces/prologue.tas makes
# traces/prologue.jsonl. Runs on Windows (in Git Bash) and Linux. See README.md.
#
# Usage: tools/trace-recorder/record.sh GAME TAS_FILE...
#
#   GAME   the vanilla game's build for this OS, as a zip or a folder: on Windows, either
#          build; on Linux, the Linux build. Only read to set up the install.
set -euo pipefail

# The CelesteTAS release traces are recorded with. The Everest release is TraceRecorder.csproj's.
CELESTETAS_RELEASE=v3.47.1

if [ $# -lt 2 ]; then
  echo "usage: $0 GAME TAS_FILE..." >&2
  exit 2
fi
game=$1
shift

here=$(cd "$(dirname "$0")" && pwd)
deps=$here/.deps
install=$deps/celeste
everest=$(dotnet msbuild "$here" -getProperty:EverestRelease)

# Everest replaces the game's Celeste.exe with a launcher of its own.
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*) os=windows installer=MiniInstaller-win64.exe launcher=Celeste.exe ;;
  Linux) os=linux installer=MiniInstaller-linux launcher=Celeste ;;
  *) echo "unsupported OS: $(uname -s)" >&2; exit 2 ;;
esac

# The game takes Windows paths on Windows.
native() {
  if [ "$os" = windows ]; then cygpath -w "$1"; else echo "$1"; fi
}

dotnet build "$here" -c Release --nologo -v q

if [ "$(cat "$install/.complete" 2>/dev/null)" != "$everest $CELESTETAS_RELEASE" ]; then
  echo "Setting up the install: Everest $everest, CelesteTAS $CELESTETAS_RELEASE"
  rm -rf "$install" "$deps/everest"
  mkdir -p "$install"
  if [ -d "$game" ]; then cp -R "$game/." "$install/"; else unzip -q "$game" -d "$install"; fi
  if [ -e "$install/Celeste.Mod.mm.dll" ]; then
    echo "$game has Everest installed: give the vanilla game" >&2
    exit 2
  fi
  curl -fsSL "https://github.com/EverestAPI/Everest/releases/download/$everest/main.zip" -o "$deps/everest.zip"
  unzip -q "$deps/everest.zip" -d "$deps/everest"
  cp -R "$deps/everest/main/." "$install/"
  rm -r "$deps/everest.zip" "$deps/everest"
  # Everest's installer links orig/Content and orig/Saves to the game's own, so that the vanilla
  # game it keeps in orig/ shares Everest's saves. On Windows, that needs admin rights, which it
  # asks for, unless they exist already.
  mkdir -p "$install/orig/Content" "$install/orig/Saves"
  chmod +x "$install/$installer"
  if ! (cd "$install" && "./$installer") > "$deps/miniinstaller.log" 2>&1; then
    cat "$deps/miniinstaller.log"
    exit 1
  fi
  # It tells the OS apart by the game's files.
  if [ ! -f "$install/$launcher" ]; then
    echo "Everest's installer made no $launcher: is $game the game's $os build?" >&2
    exit 1
  fi
  curl -fsSL "https://github.com/EverestAPI/CelesteTAS-EverestInterop/releases/download/$CELESTETAS_RELEASE/CelesteTAS.zip" -o "$install/Mods/CelesteTAS.zip"
  echo "$everest $CELESTETAS_RELEASE" > "$install/.complete"
fi
mkdir -p "$install/Mods/TraceRecorder"
cp "$here/everest.yaml" "$here/bin/Release/net8.0/TraceRecorder.dll" "$install/Mods/TraceRecorder/"

failed=0
for tas in "$@"; do
  tas=$(cd "$(dirname "$tas")" && pwd)/$(basename "$tas")
  trace=${tas%.tas}.jsonl
  run=$(mktemp -d)
  # Fresh saves, except for settings. Without them, the game would start fullscreen, and play its
  # intro first, and Everest's first-run screens would take the TAS's inputs. Everest would also
  # color its log.
  mkdir "$run/Saves"
  printf '<?xml version="1.0"?>\n<Settings>\n  <Version>1.4.0.0</Version>\n  <Fullscreen>false</Fullscreen>\n  <WindowScale>3</WindowScale>\n</Settings>\n' \
    > "$run/Saves/settings.celeste"
  printf 'CurrentVersion: %s\nLaunchWithoutIntro: true\nColorizedLogging: false\n' "${everest#stable-}" \
    > "$run/Saves/modsettings-Everest.celeste"
  # CelesteTAS fills it in again once the TAS reaches its end, which the recorder checks: a value
  # left from an earlier recording could pass for this one's.
  sed -i 's/^FileTime:.*/FileTime:/' "$tas"
  echo "Recording $trace"
  # The game can also exit without the recorder, as when it crashes, so the recorder has to have
  # said the trace is complete.
  if (cd "$install" && EVEREST_SAVEPATH=$(native "$run") timeout 30m "./$launcher" --disable-splash \
      --tas "$(native "$tas")" --trace "$(native "$run/trace.jsonl")") > "$run/log.txt" 2>&1 \
    && grep -o '\[TraceRecorder\] recorded [0-9]* updates' "$run/log.txt"; then
    mv "$run/trace.jsonl" "$trace"
    rm -rf "$run"
  else
    failed=1
    echo "FAILED: the game's log, $run/log.txt, ends with"
    tail -20 "$run/log.txt"
  fi
done
exit $failed
