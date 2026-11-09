#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

export PLAYWRIGHT_BROWSERS_PATH="$PWD/.browsers"

npm ci
npx playwright install --only-shell chromium
dotnet publish ../legacy-import -c Release
npx playwright test "$@"
