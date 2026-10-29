# Tracto — Claude Code Context

This file gives Claude Code everything it needs to generate, validate, and upload Tracto scripts on any machine.

---

## What is Tracto?

Tracto is a Windows desktop RPA platform with three components:

| Component | Language | Port | Purpose |
|-----------|----------|------|---------|
| **Orchestrator** | Go + SQLite | 5080 | Job queue, artifact store, API |
| **Orchestrator UI** | Node.js Express | 4000 | Web dashboard |
| **Desktop Runtime** | C# .NET 10 | — | Executes scripts on Windows |
| **Designer** | C# WPF .NET 10 | — | Visual script editor |
| **Hello World API** | C# .NET 10 | 3000 | Sample REST endpoint |

---

## Script Format

There are **two JSON formats**. Do not confuse them.

### Designer Format (flat) — use this when creating scripts
This is what you write to a `.json` file, import into Designer, or upload to Orchestrator.

```json
{
  "id": "my-script-001",
  "name": "My Script",
  "version": "1.0.0",
  "targetRuntime": "desktop",
  "steps": [
    {
      "stepNumber": 1,
      "action": "key",
      "description": "Optional description",
      "parameters": { "key": "{LWIN}s" }
    }
  ]
}
```

### Runtime Format (ScriptEnvelope) — internal only, never write this
The Orchestrator wraps the Designer format in a `ScriptEnvelope` when handing it to the runtime:
```json
{ "schemaVersion": "1", "script": { ...designer format here... } }
```
You never need to produce the ScriptEnvelope format. The Designer and `OrchestratorPublisher.cs` handle the wrapping automatically.

### Validation
Validate any script you generate against `tracto/script.schema.json`.

---

## Action Reference

All parameter values are **strings** (the runtime casts to int/bool internally).
`storeAs` is available on any action to namespace its outputs for variable chaining.

| Action | Required Params | Key Optional Params | Outputs |
|--------|----------------|---------------------|---------|
| `key` | `key` | `window` | `sendKeysValue` |
| `type` | `value` | `window` | `charactersTyped` |
| `delay` | `ms` (max 30000) | — | — |
| `click` | `coordinates` ("x,y") | `window`, `rightClick` | `x`, `y` |
| `scroll` | `coordinates` ("x,y") | `delta` (def "120"), `window` | — |
| `navigate` | `window` | — | `windowHandle` |
| `focus` | — | `window`, `target` | `windowHandle` |
| `app.launch` | `executable` | `args`, `timeoutMs`, `attachIfRunning` | `processId`, `windowTitle` |
| `http.get` | `url` | `timeoutMs`, `storeAs` | `statusCode`, `body`, `json` |
| `file.saveAs` | `filePath` | `windowTitle`, `overwrite`, `saveAsShortcut` | `savedPath`, `fileSizeBytes` |

Full details with examples: `tracto/docs/action-catalog.md`

### Key Syntax
- `{LWIN}s` — Windows key + S (opens Search) — uses Win32 keybd_event, NOT SendKeys
- `{ENTER}` — Enter key
- `%{F4}` — Alt+F4
- `^s` — Ctrl+S
- `{F12}` — F12 (use for Excel Save As)

### Variable Chaining
Use `storeAs` on a step to expose its outputs as `${storeAs.field}` in later steps:
```json
{ "action": "http.get", "parameters": { "url": "http://localhost:3000/api/hello", "storeAs": "api" } }
{ "action": "type",     "parameters": { "value": "${api.body}" } }
```

### Built-in Variables (no storeAs needed)
- `${now.iso}` — UTC ISO timestamp
- `${now.local}` — local datetime
- `${now.date}` — local date only
- `${now.time}` — local time only

---

## Upload a Script to Orchestrator

```powershell
$content = Get-Content "path\to\script.json" -Raw
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
$body = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/artifacts/<script-id>/versions/<version>" -Method PUT -Body $body -ContentType "application/json"
```

Replace `<script-id>` with the script's `id` field and `<version>` with its `version` field.

## Submit a Job

```powershell
$body = @{
    schemaVersion    = "1"
    scriptArtifactId = "<script-id>"
    scriptVersion    = "<version>"
    triggerType      = "Manual"
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/jobs" -Method POST -Body $body -ContentType "application/json"
```

## Check Job Status

```powershell
Invoke-RestMethod "http://localhost:5080/jobs/<runId>"
Invoke-RestMethod "http://localhost:5080/jobs/<runId>/telemetry"
```

---

## Starting All Services

See `tracto/README.md` for kill-all and start-all commands.

Quick start order:
1. `orchestrator-go` → `go run .` (port 5080)
2. `orchestrator-ui` → `node server.js` (port 4000, proxy to 5080)
3. `hello-world` → `dotnet run --project hello-world/src/Tracto.HelloWorld.Api` (port 3000)
4. Submit a job via Orchestrator UI (`▶ Run` button on an artifact)
5. `desktop-runtime` → `dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64`

---

## Project Layout

```
tracto/
├── CLAUDE.md                    ← this file
├── README.md                    ← kill/restart commands
├── script.schema.json           ← JSON Schema for script validation
├── docs/
│   └── action-catalog.md        ← full action reference
├── orchestrator-go/             ← Go Orchestrator (port 5080)
├── orchestrator-ui/             ← Node.js UI (port 4000)
│   └── public/
│       ├── index.html
│       └── app.js
├── desktop-runtime/             ← C# script executor
│   └── src/
│       └── Tracto.Desktop.Actions/
│           └── Handlers/        ← one Handler per action
├── designer/                    ← WPF script editor
│   └── src/
│       └── Tracto.Designer.Infrastructure/
│           └── Publication/
│               └── OrchestratorPublisher.cs
└── hello-world/                 ← sample REST API (port 3000)
```

---

## Cleaning Up a Designer Recording

When the Designer records a session it captures everything — including noise that must be removed before the script can run. Follow these rules every time you are given a raw recording file to clean up:

### Step 1 — Delete noise steps (always present)
Remove any step that matches one of these patterns:

| Pattern | Why it's noise |
|---------|---------------|
| `focus` with empty `parameters: {}` | Unfocused events — no target captured |
| `focus` with `window: ""` or missing window | Designer failed to identify the window |
| `navigate` with empty `parameters: {}` | Spurious window-activation event |
| `click` with only `coordinates` and no `window` in the first 5 steps | User clicking into the Designer UI itself before the automation started |
| Steps with `description` containing `"Tracto Designer"` | Recorder captured clicks on the Designer app itself — delete these |
| Steps with `window: "DesktopWindowXamlSource"` or `window: "CoreInput"` | Windows internal XAML host windows — not real targets |

### Step 2 — Convert VK codes to a `type` action
The recorder writes individual keystrokes as `key` actions with raw Windows virtual key codes (`VK_48`, `VK_45`, etc.). Replace any consecutive group of `key` steps that spell out a word or phrase with a single `type` action.

VK code to character map (common ones):
```
VK_20 = Space   VK_41=A  VK_42=B  VK_43=C  VK_44=D  VK_45=E
VK_46=F  VK_47=G  VK_48=H  VK_49=I  VK_4A=J  VK_4B=K
VK_4C=L  VK_4D=M  VK_4E=N  VK_4F=O  VK_50=P  VK_51=Q
VK_52=R  VK_53=S  VK_54=T  VK_55=U  VK_56=V  VK_57=W
VK_58=X  VK_59=Y  VK_5A=Z
VK_30=0  VK_31=1  VK_32=2  VK_33=3  VK_34=4
VK_35=5  VK_36=6  VK_37=7  VK_38=8  VK_39=9
```

Example — recorder output:
```json
{ "action": "key", "parameters": { "key": "VK_48" } },
{ "action": "key", "parameters": { "key": "VK_45" } },
{ "action": "key", "parameters": { "key": "VK_4C" } },
{ "action": "key", "parameters": { "key": "VK_4C" } },
{ "action": "key", "parameters": { "key": "VK_4F" } }
```
Clean version:
```json
{ "action": "type", "parameters": { "value": "HELLO" } }
```

### Step 3 — Add delays between major actions
The recorder captures no timing. Add `delay` steps wherever the UI needs time to respond:

| Transition | Recommended delay |
|-----------|------------------|
| After opening Windows Search (`{LWIN}s`) | 800 ms |
| After typing into a search box | 1000 ms |
| After pressing Enter to open an app | 2000 ms |
| After clicking a button that opens a dialog | 500 ms |
| After a `file.saveAs` | 1000 ms |

### Step 4 — Give the script a clean identity
Replace the recorder's auto-generated id (`c47069175c014c9e8e3c6022bec3414c`) and name (`Session 4f2c1b28`) with meaningful values:
```json
{
  "id": "descriptive-kebab-case-id",
  "name": "Human Readable Name",
  "version": "1.0.0",
  "targetRuntime": "desktop"
}
```

### Step 5 — Add a `file.saveAs` if the recording ends without saving
Raw recordings often end abruptly. If the intent included saving a file, add:
```json
{
  "stepNumber": N,
  "description": "Save the file",
  "action": "file.saveAs",
  "parameters": {
    "filePath": "%USERPROFILE%\\Downloads\\output.txt",
    "windowTitle": "Notepad",
    "overwrite": "true"
  }
}
```

### Renumber steps after cleanup
After deletions, renumber `stepNumber` sequentially starting from 1.

### Validate the result
The cleaned script must match `tracto/script.schema.json`. Every parameter value must be a string.

---

## Known Quirks

- **All parameter values are strings.** Even `ms`, `timeoutMs`, and `overwrite` must be string values in JSON.
- **`key` not `keys`.** The key action parameter is singular `"key"`.
- **`{LWIN}` requires Win32.** Standard .NET SendKeys doesn't support the Windows key. The runtime routes `{LWIN}*` tokens through `keybd_event` automatically.
- **`filePath` is a single full path.** `file.saveAs` takes one `filePath` param (e.g. `"%USERPROFILE%\\Downloads\\out.txt"`), not separate `fileName` + `directory`.
- **Stale leases auto-expire.** If the Desktop Runtime crashes mid-job, the next `POST /jobs/pull` auto-expires the stale lease so the worker can pick up new jobs.
- **SQLite lock.** If the Orchestrator hangs (endpoints time out), kill and restart it — see README.md.
- **Hello World API runs on port 3000**, not 5000.
