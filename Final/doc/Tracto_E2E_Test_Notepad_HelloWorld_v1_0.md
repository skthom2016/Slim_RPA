# Tracto Platform — End-to-End Test: Notepad + Hello World API
**Test ID:** E2E-NOTEPAD-001  
**Version:** 1.0  
**Date:** 2026-10-26  
**Author:** Tracto Platform Team  

---

## Overview

This document provides step-by-step instructions to execute a full end-to-end test of the Tracto platform using a Notepad automation scenario that also exercises the `http.get` action and runtime variable chaining.

**Designer** (record script) → **Orchestrator** (dispatch job) → **Desktop Runtime** (execute script) → **Verification**

### What the automation does
1. Opens Windows Search (`Win + S`) and searches for **Notepad**
2. Presses `Enter` to launch Notepad
3. Types **"I love you, Jesus."**
4. Calls the **Hello World API** (`GET http://localhost:3000/`) via the `http.get` action
5. Types the API response body followed by the current local timestamp into Notepad
6. Saves the file as **`notepad_hello_world.txt`** in the Downloads folder
7. Closes Notepad

**Expected result:** `C:\Users\<username>\Downloads\notepad_hello_world.txt` exists and contains:
```
I love you, Jesus.
<Hello World API response body>
[YYYY-MM-DD HH:mm:ss]
```

> **Variable chaining note:** The `http.get` step stores its response body in the runtime's shared
> variable table as `api.body`. The following `type` step references it as `${api.body}`. The
> built-in variable `${now.local}` is seeded automatically at run start (format: `YYYY-MM-DD HH:mm:ss`).

---

## Prerequisites

Complete every numbered step below in order before running the test.

### Step 0.1 — Software checklist

- [ ] Windows Notepad present (built-in to Windows — normally no install needed)
- [ ] **Hello World API** running on `http://localhost:3000/` (or your configured port)
- [ ] Go 1.23+ installed (`go version` should show `go1.23.x`)
- [ ] .NET 10 SDK installed (`dotnet --version` should show `10.x.x`)
- [ ] Tracto monorepo present at `C:\Users\santh\Downloads\New-NeeliRPA\tracto\`

> **Verify the Hello World API is reachable before continuing:**
> ```powershell
> Invoke-RestMethod http://localhost:3000/
> ```
> Expected: any non-empty response (e.g. `Hello, World!` or a JSON object).  
> If the API is on a different port, update the `url` parameter in the JSON script (Step 1b) to match.

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

```powershell
Invoke-RestMethod http://localhost:5080/health
```

Expected response:

```json
{ "status": "Healthy" }
```

> **If the orchestrator fails to start:** delete the stale database and retry:
> ```powershell
> Remove-Item C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go\orchestrator-dev.db -Force
> ```
> Then restart with the command in Step 0.3.

---

### Step 0.5 — Build and start the Desktop Runtime

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

---

## Part 1 — Record the Script in the Designer

The Designer is a single window with a **toolbar at the top**, a **script name/version row**, a **script step list** in the middle, and four panels below: Validation, Test Run, Script Signing, and Publish Script.

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

### Step 1a: Name the script

At the top of the script editor area, fill in:
- **Name:** `Notepad Hello World`
- **Version:** `1.0.0`

### Step 2: Select the capture engine

In the **Engine** dropdown (top-left of the toolbar), select the engine appropriate for desktop recording (leave on the default if only one option is shown).

### Step 3: Start recording

Click **Start** in the toolbar.

The status bar changes to **Recording**. The Designer is now capturing every desktop interaction.

### Step 4: Open Notepad via Windows Search

On the Windows desktop (while the Designer is recording):

1. Press `Win + S` to open Windows Search
2. Type **Notepad**
3. Wait for the search results to appear (~1 second)
4. Press `Enter` to open Notepad
5. Wait for the Notepad window to fully open (~2 seconds)

> **Designer capture:** `desktop.keyboard.press` Win+S, `desktop.keyboard.type` "Notepad", `desktop.keyboard.press` Enter

### Step 5: Type the devotional line

In the Notepad window:

1. Click inside the text area (ensure it has focus)
2. Type exactly: `I love you, Jesus.`
3. Press `Enter` for a new line

> **Designer capture:** `desktop.keyboard.type` value `I love you, Jesus.`

### Step 6: Call the Hello World API

> **During recording** the Designer captures your keyboard and mouse actions, not API calls.
> For the recorded script you will call the API **and then type the response** so the keystrokes
> are captured. A separate, hand-authored script (Part 1b) uses the `http.get` action to make the
> call live at runtime without hardcoding the response.

**Option A — Recording approach (response baked in):**

1. Open a **new browser tab** or PowerShell window alongside Notepad
2. Navigate to / run: `http://localhost:3000/`
3. Copy the response text
4. Switch back to Notepad
5. Press `Ctrl + V` to paste the response
6. Press `Enter`
7. Type the current date and time manually (e.g. `2026-10-26 14:30:00`)
8. Press `Enter`

> **Designer capture:** `desktop.keyboard.type` (pasted API text + timestamp as literal keystrokes)

**Option B — Live API approach (recommended — see Part 1b):**

Skip typing the API response during recording. Instead, hand-author or import the pre-built JSON
script provided in **Step 1b** which uses `http.get` and runtime variable substitution.

### Step 7: Save the file

1. Press `Ctrl + S`
2. In the Save dialog:
   - **Location:** `C:\Users\<your-username>\Downloads\`
   - **File name:** `notepad_hello_world`
   - **File type:** `Text Documents (*.txt)`
3. Click **Save**

> **Designer capture:** `desktop.dialog.saveAs` with path `Downloads\notepad_hello_world.txt`

### Step 8: Close Notepad

1. Press `Alt + F4` (or click the **X** button)

> **Designer capture:** `desktop.app.close`

### Step 9: Stop recording

Switch back to the Tracto Designer window and click **Stop** in the toolbar.

The status bar returns to **Idle**. The script step list populates with the captured events.

---

### Step 1b — Import the pre-built script (Option B — Live API)

Instead of recording manually, you can import the hand-authored JSON script that uses the
`http.get` action and runtime variable chaining to call the Hello World API live every time
the script runs.

**The script is saved at:**
```
C:\Users\santh\AppData\Local\Temp\claude\C--Users-santh-Downloads-New-NeeliRPA-Final\bed75b4d-aa47-4ec4-88f1-28724037bb97\scratchpad\notepad_hello_script_v1.json
```

Copy it to a convenient location first:
```powershell
Copy-Item `
  "C:\Users\santh\AppData\Local\Temp\claude\C--Users-santh-Downloads-New-NeeliRPA-Final\bed75b4d-aa47-4ec4-88f1-28724037bb97\scratchpad\notepad_hello_script_v1.json" `
  "$env:USERPROFILE\Downloads\notepad_hello_script_v1.json"
```

Then in the Designer, click **Import** and select the copied file.

**Script steps (Option B — 14 steps):**

| # | Action | Description |
|---|--------|-------------|
| 1 | `key` | `{LWIN}s` — Open Windows Search |
| 2 | `delay` | 800 ms — wait for search box |
| 3 | `type` | `Notepad` |
| 4 | `delay` | 1200 ms — wait for results |
| 5 | `key` | `{ENTER}` — open Notepad |
| 6 | `delay` | 2000 ms — wait for window |
| 7 | `type` | `I love you, Jesus.` |
| 8 | `key` | `{ENTER}` — new line |
| 9 | `http.get` | `http://localhost:3000/` → stored as `api.*` |
| 10 | `type` | `${api.body}\n[${now.local}]` — API response + timestamp |
| 11 | `key` | `{ENTER}` — new line |
| 12 | `file.saveAs` | Save as `notepad_hello_world.txt` in Downloads |
| 13 | `delay` | 1000 ms — wait for dialog close |
| 14 | `key` | `%{F4}` — close Notepad (Alt+F4) |

> **How `${api.body}` works:** Step 9 (`http.get`) stores its response outputs into the engine's
> shared variable table using `storeAs: "api"`. This makes the response body available as
> `api.body` and the HTTP status code as `api.statusCode`. Step 10's `value` parameter contains
> `${api.body}\n[${now.local}]`; the engine expands both placeholders before the `type` action
> runs. `${now.local}` is a built-in variable seeded at the start of every run.

---

## Part 2 — Generate Draft, Review, Sign, and Publish

### Step 10: Generate the script draft

Click **Generate Draft** in the toolbar.

The Designer converts the raw captured events into structured script steps. Review the step list
and use **Add Step**, **Delete**, **Move Up**, **Move Down** to correct any missing or
out-of-order steps.

### Step 11: Validate the script

Click **Validate** in the toolbar.

The **Validation** panel shows either:
- **✓ Valid** (green) — proceed to Step 12
- **✗ Invalid** (red) — fix the listed errors, then validate again

### Step 12: Run the simulation test

Click **Run Test** in the toolbar.

The progress bar fills quickly (~50 ms per step — no desktop interaction). The Test Run panel shows:

```
✓ PASSED    Duration: 0:00:00.7
```

> The test is a **local simulation only** — it does not open Notepad, call the Hello World API,
> or touch the desktop. It runs schema validation then marks each step "Simulated OK".

If it shows **✗ FAILED**, fix the validation error and re-run.

### Step 13: Sign the script

Click **Sign** in the toolbar.

The Script Signing panel shows:
- **Signed** (green label)
- Algorithm, Timestamp, and Digest values

> **Why this order matters:** The **Publish** button only enables when the script is loaded,
> signed, AND a passing test result exists. All three must be true before Publish lights up.

### Step 14: Publish the script

In the **Publish Script** panel:

1. Check **Output Directory** — it defaults to `C:\Users\<you>\Documents\Tracto\Published\`. Leave it as-is.
2. Click **Publish**.

The panel confirms:
```
Published — ID: <publication-id>
```

Note the publication ID — you will use it as the artifact ID in Step 15b.

### Step 14b: Upload the artifact to the Orchestrator

Push the published file to the Go Orchestrator:

```powershell
# Find the most recently published file
$published = Get-ChildItem "$env:USERPROFILE\Documents\Tracto\Published\" |
             Sort-Object LastWriteTime -Descending |
             Select-Object -First 1
Write-Host "Publishing: $($published.Name)"

# Upload to Orchestrator
$payload = [Convert]::ToBase64String([IO.File]::ReadAllBytes($published.FullName))
$h = @{ "Content-Type" = "application/json" }
Invoke-RestMethod http://localhost:5080/artifacts/notepad-hello-world-001/versions/v1 `
    -Method Put -Headers $h `
    -Body (@{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json)
Write-Host "Artifact uploaded to Orchestrator ✓"
```

Expected: `200 OK` and `artifactId: notepad-hello-world-001`.

---

## Part 3 — Dispatch the Job via the Orchestrator

### Step 15: Submit the job

```powershell
$h = @{ "Content-Type" = "application/json" }
$j = Invoke-RestMethod http://localhost:5080/jobs -Method Post -Headers $h -Body (@{
    scriptArtifactId = "notepad-hello-world-001"
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

### Step 16: Monitor job state

Poll the job status (replace `{runId}` with the GUID from Step 15):

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
info: ScriptExecutionEngine[0]
      Script ready: "Notepad Hello World" v1.0.0 — 14 step(s)
info: ScriptExecutionEngine[0]
      [step.9] action=http.get  Call Hello World API
info: HttpGetHandler[0]
      http.get http://localhost:3000/ → 200
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
| **Steps 1–6** | Windows Search opened, "Notepad" typed, Notepad launched |
| **Step 7** | `I love you, Jesus.` typed into Notepad |
| **Step 9** | `http.get` calls `http://localhost:3000/`; response stored in `api.body` |
| **Step 10** | `type` action expands `${api.body}` and `${now.local}`, types both into Notepad |
| **Step 12** | `file.saveAs` saves the file as `notepad_hello_world.txt` in Downloads |
| **Step 14** | `Alt+F4` closes Notepad |
| **Result reported** | `POST /jobs/{runId}/result` with `terminalState: Succeeded` |

> **Important:** The Desktop Runtime requires an **interactive Windows session** (the machine must
> be unlocked and the desktop visible). Do not lock the screen while the job is running.

---

## Part 5 — Verification

### Step 17: Confirm run state is SUCCEEDED

```powershell
(Invoke-RestMethod http://localhost:5080/jobs/{runId}).state
```

Expected: `SUCCEEDED`

### Step 18: Verify the file was created

Open File Explorer and navigate to:

```
C:\Users\<your-username>\Downloads\
```

Confirm:
- [ ] File `notepad_hello_world.txt` is present
- [ ] File size is greater than 0 bytes
- [ ] Last modified timestamp matches the time of the run

### Step 19: Verify the file contents

Open `notepad_hello_world.txt` in Notepad or any text editor and check:

| Line | Expected content |
|------|-----------------|
| Line 1 | `I love you, Jesus.` |
| Line 2 | The Hello World API response body (e.g. `Hello, World!`) |
| Line 3 | `[YYYY-MM-DD HH:mm:ss]` — the timestamp from when the script ran |

> **Example file content:**
> ```
> I love you, Jesus.
> Hello, World!
> [2026-10-26 14:32:07]
> ```

### Step 20: Verify via PowerShell (optional)

```powershell
$content = Get-Content "$env:USERPROFILE\Downloads\notepad_hello_world.txt"
Write-Host "Line count: $($content.Count)"
Write-Host "Line 1: $($content[0])"
Write-Host "Line 2: $($content[1])"
Write-Host "Line 3: $($content[2])"
```

Expected:
```
Line count: 3
Line 1: I love you, Jesus.
Line 2: Hello, World!
Line 3: [2026-10-26 14:32:07]
```

### Step 21: Review execution evidence (optional)

```powershell
Invoke-RestMethod http://localhost:5080/admin/telemetry/{runId}
```

Check for step timings. Step 9 (`http.get`) should have a non-zero duration matching the API round-trip.

---

## Pass / Fail Criteria

| # | Check | Pass condition |
|---|-------|---------------|
| 1 | Orchestrator health | `GET /health` returns `{ "status": "Healthy" }` |
| 2 | Hello World API reachable | `GET http://localhost:3000/` returns a non-empty response |
| 3 | Desktop Runtime registered | Log shows `Worker registered` |
| 4 | Script published | Designer shows "Published — ID: ..." and file appears in `Documents\Tracto\Published\` |
| 5 | Job dispatched | `runId` (GUID) returned by `POST /jobs` |
| 6 | Run state | `GET /jobs/{runId}` → `state: "SUCCEEDED"` |
| 7 | File exists | `notepad_hello_world.txt` present in Downloads |
| 8 | Line 1 content | `I love you, Jesus.` |
| 9 | Line 2 content | Hello World API response body |
| 10 | Line 3 content | Timestamp in format `[YYYY-MM-DD HH:mm:ss]` |

All 10 checks pass → test is **PASSED**.

---

## Quick-Start Command Reference

All commands run from `C:\Users\santh\Downloads\New-NeeliRPA\tracto\`.

| What | Command |
|------|---------|
| **Build Orchestrator** (once) | `cd orchestrator-go; go build -o orchestrator-go.exe .` |
| **Start Orchestrator** | `cd orchestrator-go; $env:ORCHESTRATOR_PORT="5080"; $env:ORCHESTRATOR_DB_PATH="orchestrator-dev.db"; $env:TRACTO_MODE="Connected"; .\orchestrator-go.exe` |
| **Health check** | `Invoke-RestMethod http://localhost:5080/health` |
| **Check Hello World API** | `Invoke-RestMethod http://localhost:3000/` |
| **Start Desktop Runtime** | `$env:ORCHESTRATOR_URL="http://localhost:5080"; dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64` |
| **List workers** | `Invoke-RestMethod http://localhost:5080/workers` |
| **Upload artifact** | See Step 14b PowerShell block |
| **Submit job** | See Step 15 PowerShell block |
| **Poll job state** | `(Invoke-RestMethod http://localhost:5080/jobs/{runId}).state` |
| **Delete stale DB** | `Remove-Item orchestrator-go\orchestrator-dev.db -Force` |

---

## Troubleshooting

### Orchestrator fails to start — port already in use

```powershell
Get-Process -Id (Get-NetTCPConnection -LocalPort 5080 -State Listen).OwningProcess
```

Find and stop the conflicting process, then restart the Orchestrator.

### Orchestrator fails to start — database error

```powershell
Remove-Item C:\Users\santh\Downloads\New-NeeliRPA\tracto\orchestrator-go\orchestrator-dev.db -Force
```

### Desktop Runtime fails to register — connection refused

The Orchestrator is not running on port 5080. Start it first (Step 0.3), then retry.

### `POST /jobs` returns `422 Unprocessable Entity` — `ARTIFACT_NOT_FOUND`

The artifact ID or version in Step 15 does not match what was uploaded in Step 14b. Check the exact artifact ID used.

### Run state is `FAILED` at step 9 — HTTP error

| `errorCategory` | Likely cause | Fix |
|---|---|---|
| `TIMEOUT` | Hello World API unreachable | Start the API; check the port number |
| `EXECUTION_FAILED` | API returned 4xx/5xx | Check the API is responding correctly |
| `CONFIGURATION` | `url` parameter missing | Verify the JSON script has `url` in step 9's parameters |

Verify the API is running:
```powershell
Invoke-RestMethod http://localhost:3000/
```

If the API is on a different port, update the `url` parameter in the JSON script and re-upload (Step 14b).

### Run state is `FAILED` at step 1 — Windows Search didn't open

Notepad search via `Win+S` can be slow. Increase the delay in step 2 from `800` to `1500` ms, or try sending `Win` alone and waiting for the Start menu.

### Run state is `FAILED` at step 12 — Save dialog issue

If the Notepad Save dialog doesn't appear as expected, verify that `FileSaveAsHandler` is
registered in `Program.cs`. Check the Desktop Runtime log for `[step.12]` details.

### File exists but Line 2 is empty or shows `${api.body}`

This means the `http.get` step succeeded but the variable was not expanded. Ensure you built
the Desktop Runtime **after** the variable-chaining changes to `ScriptExecutionEngine.cs`
(the `ExpandVars` method and shared `runVars` dictionary). Rebuild with:

```powershell
cd C:\Users\santh\Downloads\New-NeeliRPA\tracto
dotnet build desktop-runtime/src/Tracto.Desktop.Host -p:Platform=x64
```

### Run state is `UNKNOWN`

An effectful action (e.g., Save or Close) completed but the runtime crashed before confirming outcome. Check:

1. Whether `notepad_hello_world.txt` exists in Downloads
2. If yes — the file was saved; note the run as resolved
3. If no — the save did not complete; delete any partial file and re-run the job

---

## Related Documents

- `Tracto_Script_Authoring_Guide_v1_0.docx` — full action catalogue and script schema
- `Tracto_Operations_Guide_v1_0.docx` — Orchestrator API reference
- `Tracto_Desktop_Runtime_Functional_Spec_v1_1.html` — Desktop Runtime acceptance tests
- `Tracto_Developer_Guide_v1_0.docx` — platform setup and deployment
- `Tracto_E2E_Test_Excel_Automation_v1_0.md` — parallel E2E test covering Excel automation
