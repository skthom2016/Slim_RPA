#!/usr/bin/env bash
# Opens one Windows Terminal window with a PowerShell tab for each Tracto service, in start order,
# with a pause after each tab so the service has time to come up before the next one opens.
# Run it from the repository root (Git Bash):
#
#   ./start-demo.sh                      start everything (asks first what to do with the saved data)
#   ./start-demo.sh sheet-service        open only that service's tab, in your existing demo window
#   ./start-demo.sh orchestrator-ui designer        several services, in the order given
#
# Services: database, orchestrator, sheet-service, orchestrator-ui, desktop-runtime, browser-runtime, designer,
# finance-service, finance-app.
# Each tab also waits for what it depends on (the database, then the orchestrator) before it starts.
#
# To restart a service whose tab is still open, you do not need this script: stop it with Ctrl+C in its
# tab, press the Up arrow and Enter. Use the names above when the tab was closed.

set -e
cd "$(dirname "${BASH_SOURCE[0]}")"

SERVICES=(database orchestrator sheet-service orchestrator-ui desktop-runtime browser-runtime designer finance-service finance-app)
TITLES=("1 Database" "2 Orchestrator" "3 Sheet service" "4 Orchestrator UI" "5 Desktop runtime" "6 Browser runtime" "7 Designer" "8 Finance service" "9 Finance app")

if [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
  # Print the comment block at the top of this file.
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
  exit 0
fi

# Names given on the command line: only those services are started.
ONLY=("$@")
for name in "${ONLY[@]}"; do
  found=""
  for s in "${SERVICES[@]}"; do [ "$s" = "$name" ] && found=1; done
  if [ -z "$found" ]; then
    echo "Unknown service '$name'. Choose from: ${SERVICES[*]}" >&2
    exit 2
  fi
done

if ! command -v wt.exe >/dev/null 2>&1; then
  echo "Windows Terminal (wt.exe) was not found. Install it from the Microsoft Store." >&2
  exit 1
fi

# Windows-style paths, and no automatic path rewriting of the arguments passed to wt.exe.
ROOT="$(pwd -W | tr '/' '\\')"
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

TAB_SCRIPT="$ROOT\\tracto\\scripts\\demo-tab.ps1"

DB_FLAG=""
if [ "${#ONLY[@]}" -eq 0 ]; then
  # What to do with the saved data? Asked here, before any tab opens, so nothing is running while it is cleared.
  #   2 runs demo-tools/clear-demo-database.ps1   (database; done by the database tab)
  #   3 runs demo-tools/clear-browser-profile.ps1 (saved browser sign-in; done here)
  # Both are separate tools that are not part of the application.
  echo "Saved data:"
  echo "  1) Keep everything (default)"
  echo "  2) Start fresh - clears artifacts, schedules, runs, logs and workers (keeps the Microsoft sign-in)"
  echo "  3) Keep the data, clear the saved browser sign-in (Microsoft, ACME) - you sign in again afterwards"
  # No answer within this many seconds means option 1, so an unattended start never clears anything.
  # Override for one run with, for example:  CHOICE_TIMEOUT=10 ./start-demo.sh
  CHOICE_TIMEOUT="${CHOICE_TIMEOUT:-60}"
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
  # Test-Path first: removing a file that is not there makes PowerShell exit with 1, which "set -e"
  # would turn into the whole script stopping here. "|| true" keeps this step from ever doing that.
  powershell.exe -NoProfile -Command 'if (Test-Path (Join-Path $env:TEMP "tracto-demo-database-ready")) { Remove-Item -Force (Join-Path $env:TEMP "tracto-demo-database-ready") }' || true
else
  # Restarting single services: the database is already up, so no question and nothing is cleared. The tabs
  # wait for the "database ready" marker, which is in place unless the temp folder was cleaned; make sure of
  # it. The tab still checks that the database really answers before the service starts.
  powershell.exe -NoProfile -Command 'if (-not (Test-Path (Join-Path $env:TEMP "tracto-demo-database-ready"))) { Set-Content (Join-Path $env:TEMP "tracto-demo-database-ready") "restart" }' || true
fi

# Seconds to wait after opening each tab. Change these to slow down or speed up the start.
DELAY_AFTER_DATABASE="${DELAY_AFTER_DATABASE:-5}"
DELAY_AFTER_OTHERS="${DELAY_AFTER_OTHERS:-2}"

# open_tab <window> <title> <service> <seconds to wait afterwards> [extra tab-script switch]
open_tab() {
  echo "Starting: $2"
  wt.exe -w "$1" new-tab --title "$2" --suppressApplicationTitle -d "$ROOT" \
    powershell.exe -NoExit -ExecutionPolicy Bypass -File "$TAB_SCRIPT" "$3" $5
  sleep "$4"
}

title_of() {
  for i in "${!SERVICES[@]}"; do
    if [ "${SERVICES[$i]}" = "$1" ]; then echo "${TITLES[$i]}"; return; fi
  done
}

if [ "${#ONLY[@]}" -eq 0 ]; then
  # Every tab goes into the same new window; the name is unique per run so an older window is left alone.
  WINDOW="tracto-demo-$(date +%s)"
  last=$(( ${#SERVICES[@]} - 1 ))
  for i in "${!SERVICES[@]}"; do
    delay="$DELAY_AFTER_OTHERS"
    [ "$i" -eq 0 ] && delay="$DELAY_AFTER_DATABASE"
    [ "$i" -eq "$last" ] && delay=0
    flag=""
    [ "$i" -eq 0 ] && flag="$DB_FLAG"
    open_tab "$WINDOW" "${TITLES[$i]}" "${SERVICES[$i]}" "$delay" "$flag"
  done
  echo "Opened Windows Terminal with ${#SERVICES[@]} tabs. Orchestrator UI: http://localhost:4000  Finance app: http://localhost:5173"
else
  # Window 0 is the Windows Terminal window used last, normally the demo window.
  for name in "${ONLY[@]}"; do
    open_tab 0 "$(title_of "$name")" "$name" "$DELAY_AFTER_OTHERS"
  done
  echo "Opened ${#ONLY[@]} tab(s): ${ONLY[*]}"
fi
