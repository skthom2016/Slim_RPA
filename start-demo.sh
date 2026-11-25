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

# What to do with the saved data? Asked here, before any tab opens, so nothing is running while it is cleared.
#   2 runs demo-tools/clear-demo-database.ps1   (database; done by the database tab)
#   3 runs demo-tools/clear-browser-profile.ps1 (saved browser sign-in; done here)
# Both are separate tools that are not part of the application.
DB_FLAG=""
echo "Saved data:"
echo "  1) Keep everything (default)"
echo "  2) Start fresh - clears artifacts, schedules, runs, logs and workers (keeps the Microsoft sign-in)"
echo "  3) Keep the data, clear the saved browser sign-in (Microsoft, ACME) - you sign in again afterwards"
# No answer within this many seconds means option 1, so an unattended start never clears anything.
CHOICE_TIMEOUT=5
DB_CHOICE=""
# read returns non-zero when it times out; "|| true" keeps "set -e" from ending the script there.
read -t "$CHOICE_TIMEOUT" -r -p "Choose 1, 2 or 3 (no answer in ${CHOICE_TIMEOUT}s = 1): " DB_CHOICE || true
echo
DB_CHOICE="${DB_CHOICE%$'\r'}"
if [ "$DB_CHOICE" = "2" ]; then
  read -r -p "This permanently deletes the demo data. Type YES to continue: " CONFIRM
  CONFIRM="${CONFIRM%$'\r'}"
  if [ "$CONFIRM" != "YES" ]; then
    echo "Cancelled. Nothing was changed and no tabs were opened."
    exit 0
  fi
  DB_FLAG="-Fresh"
  echo "Starting with a fresh database."
elif [ "$DB_CHOICE" = "3" ]; then
  read -r -p "This signs every site out of the saved browser profile. Type YES to continue: " CONFIRM
  CONFIRM="${CONFIRM%$'\r'}"
  if [ "$CONFIRM" != "YES" ]; then
    echo "Cancelled. Nothing was changed and no tabs were opened."
    exit 0
  fi
  if ! powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ROOT\\demo-tools\\clear-browser-profile.ps1" -Yes; then
    echo "The browser sign-in was NOT cleared, so no tabs were opened. Fix the problem above and run this again." >&2
    exit 1
  fi
  echo "Keeping the existing data. Sign in again from the Designer when it opens."
else
  if [ -z "$DB_CHOICE" ]; then
    echo "No answer in ${CHOICE_TIMEOUT} seconds. Keeping everything."
  else
    echo "Keeping everything."
  fi
fi

# A leftover "database ready" marker from an earlier run must not let the other tabs start early.
powershell.exe -NoProfile -Command 'Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $env:TEMP "tracto-demo-database-ready")'

# Seconds to wait after opening each tab. Change these to slow down or speed up the start.
DELAY_AFTER_DATABASE=5
DELAY_AFTER_OTHERS=2

# Every tab goes into the same new window; the name is unique per run so an older window is left alone.
WINDOW="tracto-demo-$(date +%s)"

# open_tab <title> <service> <seconds to wait afterwards> [extra tab-script switch]
open_tab() {
  echo "Starting: $1"
  wt.exe -w "$WINDOW" new-tab --title "$1" --suppressApplicationTitle -d "$ROOT" \
    powershell.exe -NoExit -ExecutionPolicy Bypass -File "$TAB_SCRIPT" "$2" $4
  sleep "$3"
}

open_tab "1 Database"        database        "$DELAY_AFTER_DATABASE" "$DB_FLAG"
open_tab "2 Orchestrator"    orchestrator    "$DELAY_AFTER_OTHERS"
open_tab "3 Sheet service"   sheet-service   "$DELAY_AFTER_OTHERS"
open_tab "4 Orchestrator UI" orchestrator-ui "$DELAY_AFTER_OTHERS"
open_tab "5 Desktop runtime" desktop-runtime "$DELAY_AFTER_OTHERS"
open_tab "6 Browser runtime" browser-runtime "$DELAY_AFTER_OTHERS"
open_tab "7 Designer"        designer        0

echo "Opened Windows Terminal with 7 tabs. Orchestrator UI: http://localhost:4000"
