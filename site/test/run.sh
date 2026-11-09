#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

chrome_version=131.0.6778.204

case "$(uname -s)" in
  Linux) platform=linux64; binary=chrome-headless-shell ;;
  MINGW*|MSYS*|CYGWIN*) platform=win64; binary=chrome-headless-shell.exe ;;
  *) echo "site/test/run.sh runs on Windows and Linux only" >&2; exit 1 ;;
esac

deps=site/test/.deps/chrome-$chrome_version
if [ ! -f "$deps/.complete" ]; then
  rm -rf "$deps"
  mkdir -p "$deps"
  curl -fsSL -o "$deps/chrome.zip" \
    "https://storage.googleapis.com/chrome-for-testing-public/$chrome_version/$platform/chrome-headless-shell-$platform.zip"
  unzip -q "$deps/chrome.zip" -d "$deps"
  rm "$deps/chrome.zip"
  touch "$deps/.complete"
fi

dotnet publish site/legacy-import -c Release

export CHROME="$PWD/$deps/chrome-headless-shell-$platform/$binary"
(cd harness && cargo test --locked -- --ignored the_site_writes)
