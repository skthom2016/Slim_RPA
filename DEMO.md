# Running the Tracto demo

Created: 2026-11-25 | Last updated: 2026-11-25

`start-demo.sh` opens one Windows Terminal window with a tab for each service. This guide takes a fresh clone to a running demo. Every step is a command, so a person or an AI assistant can follow it from top to bottom.

| Tab | Service | Address |
|---|---|---|
| 1 Database | PostgreSQL container `tracto-postgres` | `localhost:5432` |
| 2 Orchestrator | Go service: job queue, artifacts, API | http://127.0.0.1:5080 |
| 3 Sheet service | Go service: spreadsheets and batch runs | http://127.0.0.1:5090 |
| 4 Orchestrator UI | Web dashboard | http://localhost:4000 |
| 5 Desktop runtime | Runs desktop steps on this Windows machine | none |
| 6 Browser runtime | Runs browser steps in Chromium | none |
| 7 Designer | Script editor (Windows app) | none |

## Requirements

Windows 11, with these installed:

| Tool | Version | Used for |
|---|---|---|
| Docker Desktop | any | the PostgreSQL container |
| Git for Windows (includes Git Bash) | any | cloning, and running `start-demo.sh` |
| Windows Terminal (`wt.exe`) | any | the tabs |
| Go | 1.26 or newer (`sheet-service/go.mod` requires 1.26.4) | orchestrator, sheet service |
| .NET SDK | 10.0 | desktop runtime, browser runtime, Designer |
| Node.js | 18 or newer | orchestrator UI |

Check them in PowerShell:

```powershell
docker --version; git --version; wt --version; go version; dotnet --version; node --version
```

## One-time setup

Run the commands from the repository root (the folder that contains `start-demo.sh`) unless a step says otherwise.

### 1. Clone

```powershell
git clone https://github.com/skthom2016/Slim_RPA.git
cd Slim_RPA
git switch <branch>
```

### 2. Create the database container

The container is named `tracto-postgres` by `tracto/docker-compose.yml`. Do not rename it; the start script looks for that name.

```powershell
cd tracto
$env:TRACTO_DB_PASSWORD = "<choose a password>"
docker compose up -d postgres
cd ..
```

Create the container once. `start-demo.sh` starts it on later runs. The data lives in the Docker volume `tracto_pgdata`.

### 3. Connect the services to the database

Copy the two example files and put the same connection string in both:

```powershell
Copy-Item tracto\orchestrator-go\.env.example tracto\orchestrator-go\.env
Copy-Item tracto\sheet-service\.env.example   tracto\sheet-service\.env
```

In both `.env` files set:

```
DATABASE_URL=postgres://tracto:<the password from step 2>@localhost:5432/tracto?sslmode=disable
```

The sheet service also needs a key that encrypts its saved Microsoft sign-in. Generate one and put it in `tracto\sheet-service\.env`:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

```
TRACTO_SECRET_KEY=<the generated value>
```

`.env` files are ignored by git. Never commit a password or key.

### 4. The database schema

There is nothing to run by hand. Each service creates its own schema and tables when it starts, from migration files in the repository:

| Schema | Created by | Migration file |
|---|---|---|
| `orchestrator` | orchestrator | `tracto/orchestrator-go/internal/db/migrations/001_initial.sql` |
| `sheets` | sheet service | `tracto/sheet-service/internal/pg/migrations/sheets/001_initial.sql` |

`tracto/docs/migration-sqlite-to-postgres/schema.sql` is a single readable copy of both. The tables are in the `orchestrator` and `sheets` schemas, not in `public`, so look there when you open the database.

### 5. Point the demo flows at your clone

The flows in `tracto/demo` name their input files with the path of the machine they were written on (`H:\Neelitech\Slim_RPA`). Rewrite it to your clone. Run this from the repository root:

```powershell
$clone = (Get-Location).Path
$fwd   = $clone.Replace('\','/')
$esc   = $clone.Replace('\','\\')
$utf8  = New-Object System.Text.UTF8Encoding $false
Get-ChildItem tracto\demo -Recurse -Filter *.json | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    $new  = $text.Replace('H:/Neelitech/Slim_RPA', $fwd).Replace('H:\\Neelitech\\Slim_RPA', $esc)
    if ($new -ne $text) { [IO.File]::WriteAllText($_.FullName, $new, $utf8) }
}
```

This changes the flow files in your working copy. Do not commit those changes if the clone is shared.

### 6. Sign the browser in to Microsoft and ACME (browser flows only)

Browser flows run in a saved Chromium profile named `default`. The email batch needs that profile signed in to Microsoft (Outlook on the web) and the ACME batch needs it signed in to ACME. The sessions are kept in the profile, so this is done once. There are two ways; use either.

**From the Designer, after the demo has started** (the Designer is tab 7):

1. In the Designer toolbar, in the **Browser** group, click **Sign in**.
2. A browser window opens on the shared profile. Sign in with Microsoft (including any MFA prompt), and wait for the mailbox to load.
3. In the same window, open https://acme-test.uipath.com and sign in to ACME. The ACME flows do not type a password themselves; they use this saved session.
4. Close the window. The session is saved. Segments set to "Signed-in browser" use it.

**From a terminal, before starting the demo:**

```powershell
cd tracto\browser-runtime\src\TractoRuntime.Browser.Host
dotnet run -- --login default https://outlook.office.com        # Microsoft / Outlook
dotnet run -- --login default https://acme-test.uipath.com      # ACME site
cd ..\..\..\..
```

Each command opens a window: sign in, then close it before running the next.

Both ways write to `%LOCALAPPDATA%\Tracto\browser-profiles\default`. The first browser start downloads Chromium (about 140 MB). Close the sign-in window before running a browser flow, because a profile in use cannot be opened by a second browser. When a flow starts showing a login page again, the session has expired: sign in again.

This is separate from the sheet service's Microsoft sign-in (`sheet-service login`), which is used only for SharePoint and OneDrive workbooks (step 7).

### 7. Spreadsheet providers (only for the sheet demos)

The ACME and email batches use local Excel files, so they need nothing here. The `items-google-*` and `items-sharepoint-*` flows need a Google service account or a Microsoft sign-in (`sheet-service login`). See `tracto/docs/TEAMMATE-ONBOARDING.md`.

## Start the demo

Open **Git Bash** in the repository root (right-click the folder, "Git Bash Here") and run:

```bash
./start-demo.sh
```

From PowerShell use Git Bash explicitly, because plain `bash` there may be WSL:

```powershell
& "C:\Program Files\Git\bin\bash.exe" ./start-demo.sh
```

The script asks first:

```
Saved data:
  1) Keep everything (default)
  2) Start fresh - clears artifacts, schedules, runs, logs and workers (keeps the Microsoft sign-in)
  3) Keep the data, clear the saved browser sign-in (Microsoft, ACME) - you sign in again afterwards
Choose 1, 2 or 3 (no answer in 60s = 1):
```

- No answer within 60 seconds, or `1`: nothing is cleared and the tabs open as usual. To change the wait for one run, start with `CHOICE_TIMEOUT=10 ./start-demo.sh`.
- `2`: asks you to type `YES`, then clears the database before anything else starts. Anything other than `YES` cancels and opens no tabs.
- `3`: asks you to type `YES`, then deletes the saved browser profile before any tab opens. The database is kept. After the Designer opens, sign in again (step 6). It refuses while a browser window is using the profile.

Then it opens the tabs in order, 5 seconds after the database tab and 2 seconds between the others. Each tab also waits for what it depends on, so a slow start cannot send a service off early. The first start is slow, because Go and .NET build the projects and Chromium may download.

Check that everything is up:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health      # {"status":"Healthy"}
Invoke-RestMethod http://127.0.0.1:5090/health      # {"status":"ok"}
Invoke-RestMethod http://127.0.0.1:5080/workers     # a desktop and a browser worker once registered
```

Then open http://localhost:4000.

## Publish and run the demo flows

Flows live in `tracto/demo`. Publish them to the orchestrator once, then run them from the dashboard (Artifacts tab, **Run**).

**From the Designer** (tab 7): open the JSON file, **Validate**, **Run Test**, **Publish**. To change a published flow, edit it, raise the version and publish again. Publishing the same version with different content is refused.

**Or from PowerShell**, for one file:

```powershell
$script   = Get-Content "tracto\demo\acme-batch\acme-child-fetch-invoice_1.0.0.json" -Raw | ConvertFrom-Json
$envelope = @{ schemaVersion = "1"; script = $script } | ConvertTo-Json -Depth 20 -Compress
$payload  = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($envelope))
$body     = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod -Method PUT -ContentType "application/json" -Body $body `
    -Uri "http://127.0.0.1:5080/artifacts/$($script.id)/versions/$($script.version)"
```

### Main and child flows: versions and the `latest` tag

A main flow starts a batch that runs a child flow once per row. The main names the child by id and version. Instead of a number it can say `latest`, a tag that points at one version of the child. Then a new child version never needs a new main.

- **Version numbers never change.** A published version is fixed; publishing the same number with different content is refused.
- **`latest` is a pointer you move.** Publishing a version does not move it. Move it when you are ready, with **Set as latest** in the Designer's publish bar (unticked by default, and it unticks itself after each publish) or the **Make latest** button in the Orchestrator UI's Artifacts tab. Move it back the same way to roll back.
- **The tag is read once.** A batch turns `latest` into a version number when it starts and runs that number for every row, even if the tag moves meanwhile. The batch record and the job list show the number, never the word `latest`.
- **Not set means refused.** Starting a batch on `latest` fails until the tag has been set, as starting on an unpublished version does.
- **The tagged version cannot be deleted.** Move the tag first.

Developer routine for a changed child:

1. Edit the child, raise its version (for example 1.0.2) and publish it. Nothing live changes.
2. Test it by number: publish a main whose child version is `1.0.2`, or run the child on its own.
3. When it works, move `latest` to 1.0.2 (Designer **Set as latest** when publishing, or **Make latest** in the UI). To undo, move `latest` back.

To use the tag from a flow, write the child as `"version":"latest"` in the main's `/process/start` body. Publish the child first and set its `latest` once; the main flows named `_1.1.0` below do this.

### ACME invoice batch (`tracto/demo/acme-batch`)

| Order | File | What it is |
|---|---|---|
| 1 | `acme-child-fetch-invoice_1.0.0.json` | looks up one invoice on the ACME site |
| 2 | `acme-main-start-batch_1.1.0.json` | starts the batch over `acme-batch-invoices.xlsx`, using the child's `latest` |

`acme-main-start-batch_1.0.0.json` is the earlier main; it names child 1.0.0 directly. After publishing the child, set its `latest` to 1.0.0 (Designer **Set as latest**, or **Make latest** in the UI).

The browser profile must be signed in to ACME first (step 6). Run the main flow. It ends within seconds, once the batch has started, and the sheet service then runs the child once per row and writes the results into the workbook. Keep the workbook closed in Excel while it runs. Rows that already have `Status` = `Done` are skipped. Details: `tracto/demo/acme-batch/README.md`.

### Email batch (`tracto/demo/batchemail`)

| Order | File | What it is |
|---|---|---|
| 1 | `email-child-send-one_1.0.1.json` | sends one email in Outlook on the web |
| 2 | `email-batch-send_1.1.0.json` | sends one email per row of `email-content.xlsx`, using the child's `latest` |

Set the child's `latest` to 1.0.1 after publishing it. `email-batch-send_1.0.1.json` is the earlier main; it names child 1.0.1 directly.

These flows send real email. Before running, change the addresses in the `To` column of `email-content.xlsx` and the `to` default in the child flow. Rows whose `Status` is already `Done` are skipped, so clear `Status` and `Result` to send them again.

### Other flows in `tracto/demo`

`notepad-to-bing`, `topic-to-bing-results`, `acme-invoice-excel` and the `items-*` flows are single flows. They read the input files that sit beside them in `tracto/demo`.

While a desktop flow runs, do not use the keyboard or mouse. The desktop runtime types into whichever window has focus.

## Clearing the data

At the start prompt, choose `2`. To clear without starting the demo, stop the orchestrator and sheet service, then run:

```powershell
powershell -File demo-tools\clear-demo-database.ps1 -WhatIf     # lists what would be removed
powershell -File demo-tools\clear-demo-database.ps1             # asks for YES, then clears
```

The tool is separate from the application and works only on the local `tracto-postgres` container. It empties every table in the `orchestrator` and `sheets` schemas except `schema_migrations` and `ms_tokens` (the saved Microsoft sign-in). Browser profiles are files on disk and are never touched.

## Clearing the saved browser sign-in

The browser sign-in is not in the database. It is a folder of files (cookies, local storage, cache) at `%LOCALAPPDATA%\Tracto\browser-profiles\default`, or under `BROWSER_PROFILE_ROOT` when that variable is set. Clearing it means deleting that folder; the browser creates an empty one the next time it runs.

At the start prompt, choose `3`. To clear it without starting the demo, close every browser window that uses the profile, then run:

```powershell
powershell -File demo-tools\clear-browser-profile.ps1 -WhatIf     # shows the folder, its size and last use
powershell -File demo-tools\clear-browser-profile.ps1             # asks for YES, then deletes it
```

Options: `-Profile <name>` for another profile, `-All` for every profile under the root. The tool is separate from the application, deletes only folders inside the profile root, and leaves the database, the downloaded Chromium and the sheet service's Microsoft sign-in (which is in the database) alone.

## Restart one service

After changing code, restart only the service you changed.

**The tab is still open (usual case).** Press `Ctrl+C` in the tab to stop the service. The tab stays open, already in that service's folder, and its start command is in the tab's history. Press the Up arrow, then Enter. This history belongs to the tab only; your normal PowerShell history is not touched.

**The tab was closed.** From the repository root, in Git Bash, name the service:

```bash
./start-demo.sh sheet-service
./start-demo.sh orchestrator-ui designer      # several at once, in the order given
```

This opens a new tab for each named service in your Windows Terminal window, asks nothing about the data, and clears nothing. Names: `database`, `orchestrator`, `sheet-service`, `orchestrator-ui`, `desktop-runtime`, `browser-runtime`, `designer`. Stop the old copy first: two copies of a service cannot use the same port.

Do not run plain `./start-demo.sh` to restart one service. It opens all seven tabs again.

## Stop

Close the Windows Terminal window, or press `Ctrl+C` in a tab. The database container keeps running; stop it with `docker stop tracto-postgres`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Tabs 2 to 7 print "database tab did not finish" | The database tab failed. Read its output. Usually Docker is not running or the container does not exist (step 2). |
| Tab 1 says there is no `tracto-postgres` container | Do step 2. |
| Orchestrator stops with a database error | `DATABASE_URL` in `tracto\orchestrator-go\.env` is missing or has the wrong password. |
| Sheet service stops with a key error | `TRACTO_SECRET_KEY` is missing in `tracto\sheet-service\.env` (step 3). |
| "Port already in use" | An earlier run is still going. Close its Windows Terminal window, or stop the process using ports 5080, 5090 or 4000. |
| Clearing refuses with "the orchestrator is running" | Stop the orchestrator and sheet service first. |
| A flow fails with "file not found" on `H:\...` | Step 5 was skipped. |
| A browser flow fails on the first step | The profile is signed out or its sign-in window is still open. Close it, or repeat step 6. |
| `wt.exe` not found | Install Windows Terminal from the Microsoft Store. |

## Files

| Path | What it is |
|---|---|
| `start-demo.sh` | opens the tabs (this guide's entry point) |
| `tracto/scripts/demo-tab.ps1` | what each tab runs |
| `demo-tools/clear-demo-database.ps1` | stand-alone tool that empties the demo data |
| `demo-tools/clear-browser-profile.ps1` | stand-alone tool that deletes the saved browser sign-in |
| `tracto/docker-compose.yml` | the PostgreSQL container |
| `tracto/demo/` | demo flows and their input files |
