# Tracto — Teammate Onboarding Guide

**Who this is for:** A developer joining the Tracto project for the first time.  
**What this covers:** What the project is, how the repo is organized, how to get it running, and how to understand the design documents.

---

## 1. What Is Tracto?

Tracto is a **Windows RPA (Robotic Process Automation) platform** built from scratch. It lets you record, author, and execute automation scripts on a Windows desktop — similar in concept to UiPath or Power Automate Desktop, but purpose-built and fully under our control.

The platform has six components that work together:

| Component | Language / Stack | Port | Role |
|-----------|-----------------|------|------|
| **Orchestrator** (Go) | Go + SQLite | 5080 | Job queue, artifact store, REST API — the brain |
| **Orchestrator UI** | Node.js / Express | 4000 | Web dashboard — upload scripts, trigger runs, view results |
| **Desktop Runtime** | C# / .NET 10 | — | A worker — polls Orchestrator and executes `desktop` scripts on the local machine |
| **Browser Runtime** | C# / .NET 10 + Playwright | — | A worker — polls Orchestrator and executes `browser` scripts in Chromium |
| **Designer** | C# / WPF / .NET 10 | — | Visual drag-and-drop script editor with a screen recorder |
| **Hello World API** | C# / .NET 10 | 3000 | A sample REST endpoint used by test scripts |

A script's `targetRuntime` decides which worker runs it. A **segmented** script can mix
`desktop`, `browser` and `terminal` segments in one file, so both workers can be needed for
a single job — see `tracto/CLAUDE.md` for the segmented format.

**Flow in one sentence:** You write a script in the Designer → publish it to the Orchestrator → press Run in the Orchestrator UI → the Desktop Runtime picks it up and executes it on the Windows machine.

---

## 2. Repository Layout

The GitHub repository (`https://github.com/skthoms2016/Slim_RPA.git`) contains everything. After cloning, the top-level folders are:

```
New-NeeliRPA/
├── tracto/                          ← THE MAIN PLATFORM CODE (start here)
│   ├── orchestrator-go/             ← Go Orchestrator (active, port 5080)
│   ├── orchestrator/                ← C# Orchestrator (legacy, replaced by Go version)
│   ├── orchestrator-ui/             ← Node.js web dashboard (port 4000)
│   ├── desktop-runtime/             ← C# script execution engine
│   ├── designer/                    ← C# WPF script designer app
│   ├── hello-world/                 ← sample REST API (port 3000)
│   ├── browser-runtime/             ← C# browser automation runtime
│   ├── terminal-runtime/            ← C# terminal automation runtime
│   ├── contract/                    ← shared execution contract (interfaces)
│   ├── e2e/                         ← end-to-end test suite
│   ├── docs/
│   │   └── action-catalog.md        ← FULL reference for all 10 script actions
│   ├── CLAUDE.md                    ← context file (AI assistant instructions — also good reading)
│   ├── PROJECT-MAP.md               ← one-page map of every sub-project and its files
│   ├── README.md                    ← kill/start commands for all services
│   └── script.schema.json           ← JSON Schema to validate scripts
│
├── tracto-designer/                 ← standalone Designer project (older branch)
├── tracto-desktop-runtime/          ← standalone Desktop Runtime (older branch)
├── tracto-contract/                 ← standalone contract library
├── Tracto.Orchestrator/             ← C# Orchestrator (full standalone version)
├── Tracto.TerminalRuntime/          ← Terminal Runtime standalone
│
└── Final/                           ← DESIGN DOCUMENTS (not code)
    ├── Common-Execution-Contract/   ← Stage 1 contract design (13 docs + sprint plan)
    ├── designer/                    ← Designer functional specs
    ├── Desktop-runtime/             ← Desktop Runtime specs and user stories
    ├── doc/                         ← Developer guide, action library reference
    ├── rough docs/                  ← Working notes (this file is here)
    └── Tracto_Functional_Spec_Updates_v1_0/  ← Earlier functional specs
```

**Rule of thumb:** The `tracto/` folder is the living codebase. The `Final/` folder is the design and specification documentation.

---

## 3. Prerequisites

Install these before anything else:

| Tool | Version | Purpose |
|------|---------|---------|
| Go | 1.22+ | Run the Orchestrator |
| .NET SDK | 10.0 | Build Designer, Desktop Runtime, Hello World API |
| Node.js | 18+ | Run the Orchestrator UI |
| Git | any | Clone the repo |

Verify installs:
```powershell
go version
dotnet --version
node --version
```

---

## 4. Getting the Code Running

Open **5 separate PowerShell terminals**. Run one command in each.

### Terminal 1 — Orchestrator (start this first)
```powershell
cd New-NeeliRPA\tracto\orchestrator-go
go run .
# You should see: Orchestrator listening on :5080
```

### Terminal 2 — Orchestrator UI
```powershell
cd New-NeeliRPA\tracto\orchestrator-ui
node server.js
# Open http://localhost:4000 in your browser
```

### Terminal 3 — Hello World API (needed for http.get action tests)
```powershell
cd New-NeeliRPA\tracto
dotnet run --project hello-world/src/Tracto.HelloWorld.Api
# Listens on http://localhost:3000
```

### Terminal 4 — Desktop Runtime (start AFTER you submit a job)
```powershell
cd New-NeeliRPA\tracto
dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64
```
This worker registers as `runtimeType=desktop` and only picks up scripts whose
`targetRuntime` (or segment `runtime`) is `desktop`.

### Terminal 5 — Browser Runtime (start AFTER you submit a job that needs it)
```powershell
cd New-NeeliRPA\tracto\browser-runtime\src\TractoRuntime.Browser.Host
dotnet run
```
This worker registers as `runtimeType=browser` and only picks up scripts whose
`targetRuntime` (or segment `runtime`) is `browser`.

- **First run downloads Chromium.** The host fetches the Playwright-bundled Chromium
  (~140 MB) into `%LOCALAPPDATA%\ms-playwright` the first time it starts. After that it's
  cached, so later starts are instant.
- **Headed by default.** `BROWSER_MODE` defaults to `headed`, so a visible Chromium window
  opens for each run. Set `$env:BROWSER_MODE = "headless"` before `dotnet run` to run with
  no window.
- **To use installed Chrome/Edge instead of bundled Chromium:**
  ```powershell
  $env:BROWSER_CHANNEL = "chrome"   # or "msedge"
  dotnet run
  ```
- **Don't need it?** Skip this terminal if nothing you're testing has a `browser` segment or
  `targetRuntime: "browser"` — the Desktop Runtime alone covers everything else.
- **Need a site to stay signed in?** Every run normally gets a fresh browser with nothing
  signed in. To keep a login, sign a named profile in once and then name it on the segment:
  ```powershell
  dotnet run -- --login client-demo https://outlook.office.com
  ```
  Sign in, close the window, then put `"profile": "client-demo"` on the browser segment. Only
  one run can use a profile at a time.
- Full settings list (`BROWSER_VIEWPORT_WIDTH`, `LOCATOR_WAIT_MS`, `TRACTO_SECRET_<NAME>`,
  etc.) and the browser action reference: `tracto/browser-runtime/README.md`.

### Optional — Designer (for recording/editing scripts visually)
```powershell
cd New-NeeliRPA\tracto
dotnet run --project designer/src/Tracto.Designer.App -p:Platform=x64
```

### Health check — confirm everything is up
```powershell
Invoke-RestMethod "http://localhost:5080/health"   # Orchestrator
Invoke-RestMethod "http://localhost:3000/health"   # Hello World API
Invoke-RestMethod "http://localhost:5080/workers"  # shows desktop and browser workers once registered
# Then open http://localhost:4000 in a browser
```

---

## 5. Running Your First Script (End-to-End)

The simplest test is the "Hello Notepad" script — it opens Notepad, types "HELLO ", and saves a file.

### Step 1 — Upload the script to the Orchestrator
```powershell
$content = Get-Content "C:\Users\santh\Downloads\hello-notepad_1.0.0.json" -Raw
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
$body = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/artifacts/hello-notepad/versions/1.0.0" `
    -Method PUT -Body $body -ContentType "application/json"
```

### Step 2 — Submit a job
Open `http://localhost:4000` → find the `hello-notepad` artifact → click **▶ Run**.

Or via PowerShell:
```powershell
$body = @{
    schemaVersion    = "1"
    scriptArtifactId = "hello-notepad"
    scriptVersion    = "1.0.0"
    triggerType      = "Manual"
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/jobs" -Method POST -Body $body -ContentType "application/json"
```

### Step 3 — Start the Desktop Runtime (Terminal 4)
The runtime will pick up the queued job within seconds, open Notepad, type "HELLO ", and save the file to your Downloads folder.

### Step 4 — Check results
```powershell
# Get the runId from the job submit response, then:
Invoke-RestMethod "http://localhost:5080/jobs/<runId>"
Invoke-RestMethod "http://localhost:5080/jobs/<runId>/telemetry"
```

---

## 6. Understanding Scripts

Scripts are plain JSON files in what we call "Designer format":

```json
{
  "id": "my-script",
  "name": "My Script",
  "version": "1.0.0",
  "targetRuntime": "desktop",
  "steps": [
    { "stepNumber": 1, "action": "key",   "parameters": { "key": "{LWIN}s" },        "description": "Open Windows Search" },
    { "stepNumber": 2, "action": "delay", "parameters": { "ms": "800" },              "description": "Wait for search UI" },
    { "stepNumber": 3, "action": "type",  "parameters": { "value": "Notepad" },       "description": "Type app name" },
    { "stepNumber": 4, "action": "key",   "parameters": { "key": "{ENTER}" },         "description": "Open it" }
  ]
}
```

**The 10 available actions:**

| Action | What it does | Key parameter |
|--------|-------------|---------------|
| `key` | Press a keyboard shortcut | `key` (e.g. `"{LWIN}s"`, `"{ENTER}"`, `"^s"`) |
| `type` | Type a string of text | `value` |
| `delay` | Wait N milliseconds | `ms` (max 30000) |
| `click` | Click screen coordinates | `coordinates` (`"x,y"`) |
| `scroll` | Scroll at coordinates | `coordinates`, `delta` |
| `navigate` | Bring a window to focus | `window` |
| `focus` | Focus a specific UI element | `window`, `target` |
| `app.launch` | Launch an executable | `executable` |
| `http.get` | Make an HTTP GET request | `url` |
| `file.saveAs` | Save a file via Save As dialog | `filePath` |

**Important rules:**
- All parameter values must be **strings** — even numbers: `"ms": "1000"` not `"ms": 1000`
- The Windows key (`{LWIN}`) is routed through Win32 `keybd_event` — it will not work via normal SendKeys in Windows
- Full reference: `tracto/docs/action-catalog.md`

---

## 7. Design Documents (What's in Final/)

The `Final/` folder contains the planning and design documentation for the project. Here is what each folder covers and in what order to read them:

### Start here — Common Execution Contract (Stage 1)
`Final/Common-Execution-Contract/design/` — 13 documents covering the full design of Stage 1:

| File | What it covers |
|------|---------------|
| `01-source-inventory.md` | What source materials and specs were used |
| `02-stage1-scope.md` | What Stage 1 commits to delivering (and what's out of scope) |
| `03-technology-selection.md` | Why Go was chosen for the Orchestrator, why .NET 10, etc. |
| `04-implementation-architecture.md` | How all components connect — the technical design |
| `05-repository-structure.md` | How the codebase is organized |
| `06-diagrams.md` | Architecture and sequence diagrams |
| `07-development-plan.md` | Sprint-by-sprint work breakdown |
| `08-traceability-matrix.md` | Maps user stories to implemented code |
| `09-testing-strategy.md` | How testing works at each layer |
| `10-decision-log.md` | Key architectural decisions and why they were made |
| `11-risks-questions.md` | Open risks and unresolved questions |
| `12-exit-criteria.md` | What "done" looks like for Stage 1 |

Sprint plan: `Final/Common-Execution-Contract/13-sprint-plan.md`

### Component specs
- `Final/designer/` — Designer functional specs (v1.0, v1.1, v1.2)
- `Final/Desktop-runtime/` — Desktop Runtime spec, sprint plan, user stories
- `Final/doc/Tracto_Developer_Guide_v1_0.docx` — Developer guide
- `Final/doc/Tracto_Action_Library_Reference_v1_0.docx` — Full action library reference

### Earlier functional specs (pre-Stage 1)
`Final/Tracto_Functional_Spec_Updates_v1_0/` — original specs for Browser Runtime, Terminal Runtime, Desktop Runtime, and the Orchestrator contract.

---

## 8. Key Things to Know (Gotchas)

1. **Use the Go Orchestrator, not the C# one.** `tracto/orchestrator-go/` is the active version. `tracto/orchestrator/` and `Tracto.Orchestrator/` are the C# version — kept for reference but not in active use.

2. **Start Orchestrator before the Desktop Runtime.** The Runtime polls the Orchestrator on startup. If the Orchestrator isn't running, the Runtime will fail to connect.

3. **Submit the job before starting the Runtime.** The recommended flow: upload script → submit job → then start the matching Runtime(s). The Runtime picks up the queued job immediately.

3a. **Start the Runtime that matches the job's `targetRuntime`.** A `desktop` script needs Terminal 4 running; a `browser` script needs Terminal 5 running; a segmented script with both kinds of segment needs both terminals running. A job with no matching worker just stays `QUEUED` — nothing errors, so check `GET /workers` if a job never starts.

4. **The Designer recorder captures noise.** If you use the Designer to record a script, the raw recording will contain:
   - Empty focus events (delete them)
   - Clicks on the Designer UI itself (delete them)
   - VK codes (`VK_48`, `VK_45`…) instead of a `type` action (consolidate into one `type` step)
   - No delays (add 800–2000ms delays between major steps)

5. **SQLite lock.** If the Orchestrator stops responding, kill it and restart it (see `tracto/README.md` for the kill command).

6. **All parameter values are strings.** This is the most common mistake when writing scripts manually.

---

## 9. Where to Find What

| I want to know… | Go here |
|-----------------|---------|
| What every action does and all its parameters | `tracto/docs/action-catalog.md` |
| How to start/stop all services | `tracto/README.md` |
| Which file in the codebase does what | `tracto/PROJECT-MAP.md` |
| Script JSON schema for validation | `tracto/script.schema.json` |
| The full architectural design | `Final/Common-Execution-Contract/design/04-implementation-architecture.md` |
| Why we made a specific technical decision | `Final/Common-Execution-Contract/design/10-decision-log.md` |
| Sprint plan and work breakdown | `Final/Common-Execution-Contract/13-sprint-plan.md` |
| The Orchestrator REST API | `tracto/orchestrator-go/main.go` (all routes are in one file) |
| The Desktop Runtime action handlers | `tracto/desktop-runtime/src/Tracto.Desktop.Actions/Handlers/` |
| The Designer's publish logic | `tracto/designer/src/Tracto.Designer.Infrastructure/Publication/OrchestratorPublisher.cs` |

---

## 10. Quick Reference — Orchestrator API

Base URL: `http://localhost:5080`

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `GET` | `/health` | Health check |
| `PUT` | `/artifacts/{id}/versions/{ver}` | Upload a script |
| `GET` | `/artifacts` | List all scripts |
| `POST` | `/jobs` | Submit a job |
| `GET` | `/jobs/{runId}` | Get job status |
| `GET` | `/jobs/{runId}/telemetry` | Get per-step execution telemetry |
| `POST` | `/jobs/pull` | (Worker use) Claim next pending job |
| `GET` | `/workers` | List registered workers and their `runtimeType` (`desktop`, `browser`) |

---

*Last updated: 2026-10-28*
