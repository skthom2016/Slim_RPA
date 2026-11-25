#!/usr/bin/env bash
# Opens one Windows Terminal window with a PowerShell tab for each Tracto service, in start order,
# with a pause after each tab so the service has time to come up before the next one opens.
# Run it from the repository root (Git Bash):   ./start-demo.sh
#
# Tabs: database, orchestrator, sheet-service, orchestrator-ui, desktop-runtime, browser-runtime, designer.
# Each tab also waits for what it depends on (the database, then the orchestrator) before it starts.

set -e
cd "$(dirname "${BASH_SOURCE[0]}")"

if ! command -v wt.exe >/dev/null 2>&1; then
  echo "Windows Terminal (wt.exe) was not found. Install it from the Microsoft Store." >&2
  exit 1
fi

# Windows-style paths, and no automatic path rewriting of the arguments passed to wt.exe.
ROOT="$(pwd -W | tr '/' '\\')"
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

TAB_SCRIPT="$ROOT\\tracto\\scripts\\demo-tab.ps1"

# Seconds to wait after opening each tab. Change these to slow down or speed up the start.
DELAY_AFTER_DATABASE=5
DELAY_AFTER_OTHERS=2

# Every tab goes into the same new window; the name is unique per run so an older window is left alone.
WINDOW="tracto-demo-$(date +%s)"

# open_tab <title> <service> <seconds to wait afterwards>
open_tab() {
  echo "Starting: $1"
  wt.exe -w "$WINDOW" new-tab --title "$1" --suppressApplicationTitle -d "$ROOT" \
    powershell.exe -NoExit -ExecutionPolicy Bypass -File "$TAB_SCRIPT" "$2"
  sleep "$3"
}

open_tab "1 Database"        database        "$DELAY_AFTER_DATABASE"
open_tab "2 Orchestrator"    orchestrator    "$DELAY_AFTER_OTHERS"
open_tab "3 Sheet service"   sheet-service   "$DELAY_AFTER_OTHERS"
open_tab "4 Orchestrator UI" orchestrator-ui "$DELAY_AFTER_OTHERS"
open_tab "5 Desktop runtime" desktop-runtime "$DELAY_AFTER_OTHERS"
open_tab "6 Browser runtime" browser-runtime "$DELAY_AFTER_OTHERS"
open_tab "7 Designer"        designer        0

echo "Opened Windows Terminal with 7 tabs. Orchestrator UI: http://localhost:4000"
