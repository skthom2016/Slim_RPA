# Tracto — Claude Code Context

This file gives Claude Code everything it needs to generate, validate, and upload Tracto scripts on any machine.

To create, edit, clean or validate a desktop script, use the `tracto-desktop-rpa` skill (`.claude/skills/tracto-desktop-rpa/` at the repository root). It documents every action, recipe and limit, and ships a validator.

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

Scripts are written in the **Designer format**, which has two shapes: flat (one runtime) and segmented (several runtimes in one file). Uploads wrap either shape in a **runtime envelope**. Do not confuse the Designer format with the envelope.

### Designer Format (flat) — use this when creating scripts
This is what you write to a `.json` file or import into Designer.

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

### Segmented Format — one file, several runtimes
A job that spans runtimes (for example read text in Notepad, then search it in a browser) is one file with `segments` instead of `targetRuntime` and `steps`. It never has a top-level `targetRuntime`.

```json
{
  "id": "notepad-to-bing",
  "name": "Notepad To Bing",
  "version": "1.0.0",
  "params": { "notePath": "C:\\data\\search-text.txt", "noteName": "search-text.txt" },
  "segments": [
    { "name": "Read the note", "runtime": "desktop", "steps": [ { "stepNumber": 1, "action": "clipboard.get", "parameters": { "storeAs": "note" } } ] },
    { "name": "Search Bing",   "runtime": "browser", "steps": [ { "stepNumber": 1, "action": "fill", "parameters": { "css": "#sb_form_q", "value": "${param.note.firstLine}" } } ] }
  ]
}
```

- `runtime` is `desktop`, `browser` or `terminal`. Steps restart at `stepNumber` 1 in every segment. A script with one segment behaves like a flat script.
- `params` are default values, overridden by the `params` of `POST /jobs`. Read them in any segment as `${param.name}`.
- A `storeAs` output of one segment reaches later segments as `${param.<storeAs>.<field>}`. Outputs flow forward only.
- Segments run in order, each when the previous one succeeded; a failed or cancelled segment ends the run.
- Browser segments use the flat browser format; see `browser-runtime/README.md`.

### Runtime Format (ScriptEnvelope)
Uploaded scripts (flat or segmented) are wrapped in a `ScriptEnvelope`, and the Desktop Host rejects a bare script:
```json
{ "schemaVersion": "1", "script": { ...designer format here... } }
```
The Designer and `OrchestratorPublisher.cs` wrap scripts automatically. When uploading by hand, put the envelope inside the base64 payload (see "Upload a Script to Orchestrator").

### Validation
Validate any script you generate against `tracto/script.schema.json` (flat or segmented), or run `node .claude/skills/tracto-desktop-rpa/scripts/validate.js <file>`.

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
| `http.post` | `url` | `body`, `contentType`, `timeoutMs`, `storeAs` | `statusCode`, `body`, `json`, plus top-level fields of a JSON reply |
| `notepad.type` | `text` | `windowTitle` | `charactersTyped` |
| `file.saveAs` | `filePath` | `windowTitle`, `overwrite`, `saveAsShortcut` | `savedPath`, `fileSizeBytes` |
| `file.read` | `path` | `maxChars` | `text`, `firstLine`, `lineCount` |
| `clipboard.get` | — | — | `text`, `firstLine` |

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

The payload is the script wrapped in the envelope, base64-encoded. The same steps upload a flat or a segmented script.

```powershell
$script   = Get-Content "path\to\script.json" -Raw | ConvertFrom-Json
$envelope = @{ schemaVersion = "1"; script = $script } | ConvertTo-Json -Depth 20 -Compress
$payload  = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($envelope))
$body     = @{ schemaVersion = "1"; payload = $payload } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/artifacts/$($script.id)/versions/$($script.version)" -Method PUT -Body $body -ContentType "application/json"
```

The URL uses the script's `id` and `version`. For a segmented script they must equal the script's own `id` and `version`; the Orchestrator rejects a mismatch with `422 INVALID_SCRIPT`, and also rejects an unknown `runtime`, an empty `steps` or an empty `segments`. Publishing a segmented script also stores one hidden artifact per segment (`<id>.seg1`, `<id>.seg2`, ...), which `GET /artifacts` hides unless `includeSegments=true`.

## Submit a Job

```powershell
$body = @{
    schemaVersion    = "1"
    scriptArtifactId = "<script-id>"
    scriptVersion    = "<version>"
    triggerType      = "Manual"
    params           = @{ notePath = "C:\data\other.txt" }   # optional; overrides the script's params
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5080/jobs" -Method POST -Body $body -ContentType "application/json"
```

A segmented script returns one parent `runId` with `"state": "RUNNING"` and a `segments` count. `GET /jobs` lists parent runs only; add `includeSegments=true` to include the per-segment runs.

## Check Job Status

```powershell
Invoke-RestMethod "http://localhost:5080/jobs/<runId>"
Invoke-RestMethod "http://localhost:5080/jobs/<runId>/telemetry"
```

`GET /jobs/{runId}` returns `state`, `params` and `outputs`. For a segmented script it also returns `segments`:
```json
{ "state": "RUNNING",
  "segments": [
    { "index": 0, "name": "Read the note", "runtime": "desktop", "runId": "...", "state": "SUCCEEDED", "outputs": { "note.firstLine": "..." } },
    { "index": 1, "name": "Search Bing",   "runtime": "browser", "runId": "...", "state": "RUNNING",   "outputs": {} }
  ] }
```
A segment that has not started is `PENDING`. When a segment fails or is cancelled the parent takes that state and later segments stay `PENDING`. `POST /jobs/{runId}/cancel` on the parent cancels the active segment.

---

## Starting All Services

See `tracto/README.md` for kill-all and start-all commands.

Quick start order:
1. `orchestrator-go` → `go run .` (port 5080)
2. `orchestrator-ui` → `node server.js` (port 4000, proxy to 5080)
3. `hello-world` → `dotnet run --project hello-world/src/Tracto.HelloWorld.Api` (port 3000)
4. Submit a job via Orchestrator UI (`▶ Run` button on an artifact)
5. `desktop-runtime` → `dotnet run --project desktop-runtime/src/Tracto.Desktop.Host --no-launch-profile -p:Platform=x64`
6. `browser-runtime` → `dotnet run --project browser-runtime/src/TractoRuntime.Browser.Host` (headed Chromium; see `browser-runtime/README.md`)

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
