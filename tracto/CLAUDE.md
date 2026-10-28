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

## Known Quirks

- **All parameter values are strings.** Even `ms`, `timeoutMs`, and `overwrite` must be string values in JSON.
- **`key` not `keys`.** The key action parameter is singular `"key"`.
- **`{LWIN}` requires Win32.** Standard .NET SendKeys doesn't support the Windows key. The runtime routes `{LWIN}*` tokens through `keybd_event` automatically.
- **`filePath` is a single full path.** `file.saveAs` takes one `filePath` param (e.g. `"%USERPROFILE%\\Downloads\\out.txt"`), not separate `fileName` + `directory`.
- **Stale leases auto-expire.** If the Desktop Runtime crashes mid-job, the next `POST /jobs/pull` auto-expires the stale lease so the worker can pick up new jobs.
- **SQLite lock.** If the Orchestrator hangs (endpoints time out), kill and restart it — see README.md.
- **Hello World API runs on port 3000**, not 5000.
