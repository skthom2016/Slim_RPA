# Tracto Platform — End-to-End Test: Excel Automation
**Test ID:** E2E-EXCEL-001  
**Version:** 1.2  
**Date:** 2026-10-25  
**Author:** Tracto Platform Team  

> **v1.2 Change:** Orchestrator is now the Go implementation (`orchestrator-go/`).
> The .NET `Tracto.Orchestrator.Api` is superseded.  
> All commands updated accordingly.

---

## Overview

This document provides step-by-step instructions to execute a full end-to-end test of the Tracto platform using an Excel automation scenario. The test covers the complete platform workflow:

**Designer** (record script) → **Orchestrator** (dispatch job) → **Desktop Runtime** (execute script) → **Verification**

### What the automation does
1. Opens Microsoft Excel
2. Enters `42` in **Sheet1, cell A1**
3. Enters `58` in **Sheet1, cell B1**
4. Navigates to **Sheet2**
5. Enters `=Sheet1!A1+Sheet1!B1` in **Sheet2, cell A1**
6. Saves the workbook as **`automation test Excel 1.xlsx`** in the **Downloads** folder
7. Closes Excel

**Expected result:** `C:\Users\<username>\Downloads\automation test Excel 1.xlsx` exists, Sheet2!A1 evaluates to `100`.

> **Current scope note:** Checks 1–5 (infrastructure) are fully automated and verified.
> Checks 6–9 (Excel file content) require the Designer to record and publish the actual
> script, which the Desktop Runtime will then execute on the visible desktop.

---

## Prerequisites

Complete every numbered step below in order before running the test.

### Step 0.1 — Software checklist

- [ ] Microsoft Excel (Microsoft 365 or Excel 2019+) installed on this machine
- [ ] Go 1.23+ installed (`go version` should show `go1.23.x`)
- [ ] .NET 10 SDK installed (`dotnet --version` should show `10.x.x`)
- [ ] Tracto monorepo present at `C:\Users\santh\Downloads\New-NeeliRPA\tracto\`

---

### Step 0.2 — Build the Go Orchestrator (first time only)

Only needed once — skip if `orchestrator-go.exe` is already built.

Open a **PowerShell window** and run:

```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go
go build -o orchestrator-go.exe .
```

Expected output: no errors, `orchestrator-go.exe` appears in the folder.

---

### Step 0.3 — Start the Go Orchestrator

Open a **new PowerShell window** (keep it open for the rest of the test):

```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go
$env:ORCHESTRATOR_PORT     = "5080"
$env:ORCHESTRATOR_DB_PATH  = "orchestrator-dev.db"
$env:TRACTO_MODE           = "Connected"
.\orchestrator-go.exe
```

Wait until the console shows:

```
time=... level=INFO msg="Tracto Orchestrator (Go) starting" addr=0.0.0.0:5080 mode=connected
time=... level=INFO msg="database ready" path=orchestrator-dev.db
```

> **Note:** No JWT secret is needed for local testing. The orchestrator runs in dev/no-auth mode.

---

### Step 0.4 — Verify the Orchestrator is healthy

In a browser or new PowerShell window, run:

```powershell
Invoke-RestMethod http://localhost:5080/health
```

Expected response:

```json
{ "status": "Healthy" }
```

If you prefer curl:

```
curl http://localhost:5080/health
```

> **If the orchestrator fails to start:** delete the stale database and retry:
> ```powershell
> Remove-Item C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go\orchestrator-dev.db -Force
> ```
> Then restart with the command in Step 0.3.

---

### Step 0.5 — Start the Desktop Runtime

Open a **second new PowerShell window** (keep it open for the rest of the test):

```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
$env:ORCHESTRATOR_URL = "http://localhost:5080"
dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64
```

Wait for the log line:

```
info: DesktopRuntimeWorker[0]
      Worker registered — workerId: desktop-runtime-001
```

This confirms the Desktop Runtime is connected and ready to pick up jobs.

> **If `dotnet run` takes time** — it is compiling from source on the first run. Subsequent
> runs are faster.

---

### Step 0.6 — Confirm registration (optional API check)

```powershell
Invoke-RestMethod http://localhost:5080/workers
```

You should see `desktop-runtime-001` with state `Available`.

Or send a manual registration probe:

```http
POST http://localhost:5080/workers/register
Content-Type: application/json

{
  "workerId": "desktop-runtime-verify",
  "runtimeType": "desktop",
  "actionNames": ["desktop.*"],
  "capabilityDigest": null,
  "absenceThresholdSeconds": 60
}
```

Expected: `200 OK` with body `{ "workerId": "desktop-runtime-verify" }`.

---

## Part 1 — Record the Script in the Designer

The Designer is a single window with a **toolbar at the top**, a **script step list** in the middle, and four panels below it: Validation, Test Run, Script Signing, and Publish Script.

### Designer UI reference

| Toolbar button | What it does |
|---|---|
| **Engine** (dropdown) | Select capture engine — leave on default |
| **Start** | Begin recording desktop actions |
| **Pause** / **Resume** | Pause and resume an active recording |
| **Stop** | Stop recording and lock the captured steps |
| **Discard** | Discard the current recording without saving |
| **Generate Draft** | Convert captured events into editable script steps |
| **Validate** | Run schema/rule validation on the current script |
| **Run Test** / **Stop Test** | Simulate the script locally without touching the desktop |
| **Sign** / **Verify** | Sign the script with the HMAC key; verify an existing signature |
| **Publish** | Save the signed script artifact to the Output Directory |
| **Export** / **Import** | Export/import script files |

The **status bar** at the bottom shows the current recording state (Idle / Recording / Stopped).

---

### Step 1: Open the Designer

Launch the Tracto Designer:

```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
dotnet run --project designer/src/Tracto.Designer.App --no-launch-profile -p:Platform=x64
```

The **Tracto Designer** window opens. The status bar shows **Idle**.

### Step 2: Select the capture engine

In the **Engine** dropdown (top-left of the toolbar), select the engine appropriate for desktop recording (leave on the default if only one option is shown).

### Step 3: Start recording

Click **Start** in the toolbar.

The status bar changes to **Recording**. The Designer is now capturing every desktop interaction.

### Step 4: Open Microsoft Excel

On the Windows desktop (while the Designer is recording):

1. Press `Win + S`, type **Excel**, press `Enter`
2. In Excel's start screen, click **Blank workbook**
3. Wait for the blank workbook to open

> **Designer capture:** `desktop.app.launch` targeting `EXCEL.EXE`

### Step 5: Enter a value in Sheet1, cell A1

1. Ensure you are on **Sheet1** (default tab)
2. Click cell **A1**
3. Type `42`
4. Press `Tab` to move to B1

> **Designer capture:** `desktop.element.click` on A1, `desktop.keyboard.type` value `42`

### Step 6: Enter a value in Sheet1, cell B1

1. You are now in cell **B1**
2. Type `58`
3. Press `Enter`

> **Designer capture:** `desktop.keyboard.type` value `58`

### Step 7: Navigate to Sheet2

1. Click the **Sheet2** tab at the bottom of the Excel window
2. If Sheet2 does not exist: right-click Sheet1 tab → **Insert** → **Worksheet**

> **Designer capture:** `desktop.element.click` on the Sheet2 tab

### Step 8: Enter the SUM formula in Sheet2, cell A1

1. On Sheet2, click cell **A1**
2. Type exactly: `=Sheet1!A1+Sheet1!B1`
3. Press `F9` to force recalculation
4. Press `Enter`

> **Designer capture:** `desktop.element.click` on A1, `desktop.keyboard.type` formula, `desktop.keyboard.press` F9

### Step 9: Save the workbook

1. Press `Ctrl + Shift + S` (or **File** → **Save As**)
2. Set:
   - **Location:** `C:\Users\<your-username>\Downloads\`
   - **File name:** `automation test Excel 1`
   - **File type:** `Excel Workbook (*.xlsx)`
3. Click **Save** — if a compatibility dialog appears click **Keep Current Format**

> **Designer capture:** `desktop.dialog.saveAs` with path `Downloads\automation test Excel 1.xlsx`

### Step 10: Close Excel

1. Press `Alt + F4` (or click the **X** button)
2. If prompted to save again, click **Don't Save**

> **Designer capture:** `desktop.app.close` targeting `EXCEL.EXE`

### Step 11: Stop recording

Switch back to the Tracto Designer window and click **Stop** in the toolbar.

The status bar returns to **Idle**. The script step list populates with the captured events.

---

## Part 2 — Generate Draft, Review, Sign, and Publish

### Step 12: Generate the script draft

Click **Generate Draft** in the toolbar.

The Designer converts the raw captured events into structured script steps in the editor. The step list should contain (in order):

| # | Description |
|---|-------------|
| 1 | `desktop.app.launch` — `EXCEL.EXE` |
| 2 | `desktop.element.click` — Sheet1, Cell A1 |
| 3 | `desktop.keyboard.type` — `42` |
| 4 | `desktop.element.click` — Sheet1, Cell B1 |
| 5 | `desktop.keyboard.type` — `58` |
| 6 | `desktop.element.click` — Sheet2 tab |
| 7 | `desktop.element.click` — Sheet2, Cell A1 |
| 8 | `desktop.keyboard.type` — `=Sheet1!A1+Sheet1!B1` |
| 9 | `desktop.keyboard.press` — `F9` |
| 10 | `desktop.dialog.saveAs` — `Downloads\automation test Excel 1.xlsx` |
| 11 | `desktop.app.close` — `EXCEL.EXE` |

Use **Add Step**, **Delete**, **Move Up**, **Move Down** in the script editor area to fix any missing or out-of-order steps.

### Step 13: Validate the script

Click **Validate** in the toolbar (or the **Validate** button in the Validation panel).

The **Validation** panel below the editor shows either:
- **✓ Valid** (green) — proceed to Step 14
- **✗ Invalid** (red) — fix the listed errors, then validate again

### Step 14: Run the simulation test

Click **Run Test** in the toolbar (or the **Run Test** button in the **Test Run** panel).

The progress bar fills quickly (~50 ms per step — no desktop interaction). The Test Run panel then shows:

```
✓ PASSED    Duration: 0:00:00.5
```

> The test is a **local simulation only** — it does not open Excel or touch the desktop.
> It runs schema validation then marks each step "Simulated OK".

If it shows **✗ FAILED**, the script has a validation error — fix it (Validate → fix → Generate Draft again) then re-run the test.

### Step 15: Sign the script

Click **Sign** in the toolbar (or the **Sign** button in the **Script Signing** panel).

The Script Signing panel shows:
- **Signed** (green label)
- Algorithm, Timestamp, and Digest values

> **Why this order matters:** The **Publish** button only enables when the script is loaded, signed, AND a passing test result exists. All three must be true before Publish lights up.

### Step 16: Publish the script

In the **Publish Script** panel at the bottom:

1. Check **Output Directory** — it defaults to `C:\Users\<you>\Documents\Tracto\Published\`. Leave it as-is.
2. Click **Publish** (in the panel, or the **Publish** button in the toolbar).

The panel confirms with:
```
Published — ID: <publication-id>
```

Note the publication ID — you will use it as the artifact ID in Step 17.

> **Where is the file?** The script is saved as a JSON file in the Output Directory. The Orchestrator does not receive it automatically — you must upload it manually (Step 16b below).

### Step 16b: Upload the artifact to the Orchestrator

After the Designer publishes locally, push the file to the Go Orchestrator. Run in PowerShell:

```powershell
# Find the most recently published file
$published = Get-ChildItem "$env:USERPROFILE\Documents\Tracto\Published\" |
             Sort-Object LastWriteTime -Descending |
             Select-Object -First 1
Write-Host "Publishing: $($published.Name)"

# Upload to Orchestrator
$payload = [Convert]::ToBase64String([IO.File]::ReadAllBytes($published.FullName))
$h = @{ "Content-Type" = "application/json" }
Invoke-RestMethod http://localhost:5080/artifacts/excel-automation-test-001/versions/v1 `
    -Method Put -Headers $h `
    -Body (@{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json)
Write-Host "Artifact uploaded to Orchestrator ✓"
```

Expected: `200 OK` and `artifactId: excel-automation-test-001`.

---

## Part 3 — Dispatch the Job via the Orchestrator

### Step 17: Submit the job

**Via PowerShell:** After completing Step 16b, submit the job with the artifact ID you uploaded:

```powershell
$h = @{ "Content-Type" = "application/json" }
$j = Invoke-RestMethod http://localhost:5080/jobs -Method Post -Headers $h -Body (@{
    scriptArtifactId = "excel-automation-test-001"
    scriptVersion    = "v1"
    schemaVersion    = "1"
    triggerType      = "Manual"
    inputRefs        = @()
    secretRefs       = @()
} | ConvertTo-Json)
Write-Host "runId: $($j.runId)   state: $($j.state)"
```

Expected output:

```
runId: 550e8400-e29b-41d4-a716-446655440000   state: PENDING
```

Copy the `runId` GUID — you need it in the next steps.

---

### Step 18: Monitor job state

Poll the job status (replace `{runId}` with the GUID from Step 17):

```powershell
Invoke-RestMethod http://localhost:5080/jobs/{runId}
```

Or continuously poll until it reaches a terminal state:

```powershell
$runId = "PASTE-YOUR-RUNID-HERE"
do {
    $job = Invoke-RestMethod "http://localhost:5080/jobs/$runId"
    Write-Host "$(Get-Date -Format 'HH:mm:ss')  state=$($job.state)"
    Start-Sleep 2
} while ($job.state -notin @("SUCCEEDED","FAILED","CANCELLED","TIMEDOUT","UNKNOWN"))
Write-Host "Final state: $($job.state)"
```

Expected state progression:

```
PENDING  →  RUNNING  →  SUCCEEDED
```

Watch the Desktop Runtime PowerShell window for:

```
info: DesktopRuntimeWorker[0]
      Job acquired — runId: 550e8400-...
info: DesktopRuntimeWorker[0]
      Run 550e8400-... complete — state: Succeeded
```

---

## Part 4 — Desktop Runtime Execution

The Desktop Runtime picks up the job from the Orchestrator without any manual action. Here is what happens internally:

| Runtime step | What happens |
|---|---|
| **Pull** | Runtime polls `POST /jobs/pull`; Orchestrator offers the matching job |
| **Lease accepted** | Runtime calls `POST /jobs/{runId}/lease/accept`; job moves to RUNNING |
| **Heartbeats** | Runtime sends `POST /jobs/{runId}/heartbeat` every 15 s to keep the lease alive |
| **Script fetched** | Signed artifact downloaded from artifact store and SHA-256 verified |
| **Actions executed** | Steps 1–11 replayed on the desktop in sequence |
| **Result reported** | `POST /jobs/{runId}/result` with `terminalState: Succeeded` (or error detail) |

> **Important:** The Desktop Runtime requires an **interactive Windows session** (the machine must be unlocked and the desktop visible). Do not lock the screen while the job is running.

---

## Part 5 — Verification

### Step 19: Confirm run state is SUCCEEDED

```powershell
(Invoke-RestMethod http://localhost:5080/jobs/{runId}).state
```

Expected: `SUCCEEDED`

### Step 20: Verify the file was created

Open File Explorer and navigate to:

```
C:\Users\<your-username>\Downloads\
```

Confirm:
- [ ] File `automation test Excel 1.xlsx` is present
- [ ] File size is greater than 0 bytes
- [ ] Last modified timestamp matches the time of the run

### Step 21: Verify the file contents

Open `automation test Excel 1.xlsx` in Excel and check:

| Location | Expected value |
|----------|----------------|
| Sheet1, cell A1 | `42` |
| Sheet1, cell B1 | `58` |
| Sheet2, cell A1 | `100` (formula result) |

> If Sheet2!A1 shows the formula text (`=Sheet1!A1+Sheet1!B1`) instead of `100`, open the file and press `F9` — Excel will recalculate.

### Step 22: Review execution evidence (optional)

Query the journal for the run:

```powershell
Invoke-RestMethod http://localhost:5080/admin/telemetry/{runId}
```

Check for step timings and no FAILED states.

---

## Pass / Fail Criteria

| # | Check | Pass condition |
|---|-------|---------------|
| 1 | Orchestrator health | `GET /health` returns `{ "status": "Healthy" }` |
| 2 | Desktop Runtime registered | Log shows `Worker registered` |
| 3 | Script published | Designer shows "Published — ID: ..." and file appears in Documents\Tracto\Published\ |
| 4 | Job dispatched | `runId` (GUID) returned by `POST /jobs` (Step 16) |
| 5 | Run state | `GET /jobs/{runId}` → `state: "SUCCEEDED"` |
| 6 | File exists | `automation test Excel 1.xlsx` present in Downloads |
| 7 | Sheet1!A1 | `42` |
| 8 | Sheet1!B1 | `58` |
| 9 | Sheet2!A1 | `100` |

All 9 checks pass → test is **PASSED**.

---

## Quick-Start Command Reference

All commands run from `C:\Users\santh\Downloads\New-NeeliRPA\tracto\`.

| What | Command |
|------|---------|
| **Build Orchestrator** (once) | `cd orchestrator-go; go build -o orchestrator-go.exe .` |
| **Start Orchestrator** | `cd orchestrator-go; $env:ORCHESTRATOR_PORT="5080"; $env:ORCHESTRATOR_DB_PATH="orchestrator-dev.db"; $env:TRACTO_MODE="Connected"; .\orchestrator-go.exe` |
| **Health check** | `Invoke-RestMethod http://localhost:5080/health` |
| **Start Desktop Runtime** | `$env:ORCHESTRATOR_URL="http://localhost:5080"; dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64` |
| **List workers** | `Invoke-RestMethod http://localhost:5080/workers` |
| **Submit job** | See Step 14 PowerShell block |
| **Poll job state** | `(Invoke-RestMethod http://localhost:5080/jobs/{runId}).state` |
| **Delete stale DB** | `Remove-Item orchestrator-go\orchestrator-dev.db -Force` |

---

## Troubleshooting

### Orchestrator fails to start — port already in use

Another process is on port 5080. Find and stop it:

```powershell
Get-Process -Id (Get-NetTCPConnection -LocalPort 5080 -State Listen).OwningProcess
```

Then stop the process and restart the Orchestrator.

### Orchestrator fails to start — database error

Delete the stale database and restart:

```powershell
Remove-Item C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go\orchestrator-dev.db -Force
```

### Desktop Runtime fails to register — connection refused

The Orchestrator is not running on port 5080. Start it first (Step 0.3), then retry.

### `POST /jobs` returns `422 Unprocessable Entity` — `ARTIFACT_NOT_FOUND`

The artifact ID or version in Step 14 does not match what was published in Step 13. Check the exact artifact ID and version shown by the Designer after Sign & Publish.

### Run state is `FAILED`

Check the error in `GET /jobs/{runId}`:

| `errorCategory` | Likely cause | Fix |
|---|---|---|
| `TARGET_NOT_FOUND` | Excel cell selector not matched | Re-record; check Excel version |
| `TIMEOUT` | Excel took too long to open | Increase step timeout in script |
| `ENVIRONMENT` | Excel not installed or wrong path | Verify Excel installation |
| `POLICY_DENIED` | Script not signed or signature invalid | Re-sign via Designer |
| `SESSION_UNAVAILABLE` | Machine locked during run | Unlock the desktop before dispatching |

### Run state is `UNKNOWN`

An effectful action (e.g., Save) completed but the runtime crashed before confirming outcome. Check:

1. Whether `automation test Excel 1.xlsx` exists in Downloads
2. If yes — the file was saved; note the run as resolved
3. If no — the save did not complete; delete any partial file and re-run the job

### `Sheet2!A1` shows formula text, not `100`

Add an `F9` (force recalculate) step between the formula entry and the Save step in the Designer, then re-record.

---

## Related Documents

- `Tracto_Script_Authoring_Guide_v1_0.docx` — full action catalogue and script schema
- `Tracto_Operations_Guide_v1_0.docx` — Orchestrator API reference
- `Tracto_Desktop_Runtime_Functional_Spec_v1_1.html` — Desktop Runtime acceptance tests
- `Tracto_Developer_Guide_v1_0.docx` — platform setup and deployment
