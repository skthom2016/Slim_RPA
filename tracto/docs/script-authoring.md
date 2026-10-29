# Tracto — Script Authoring Reference

## Script Format

There are **two JSON formats**. Do not confuse them.

### Designer Format (flat) — always use this when creating scripts

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
You never need to produce this. The Designer and `OrchestratorPublisher.cs` handle wrapping automatically.

### Validation

Validate any script against `tracto/script.schema.json`.

---

## Action Reference

**Critical rule: all parameter values must be strings.** The runtime casts to int/bool internally.  
`storeAs` is available on any action to expose its outputs for variable chaining.

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

Full details with examples: `docs/action-catalog.md`

---

## Key Syntax

| Key string | Meaning |
|-----------|---------|
| `{LWIN}s` | Windows key + S (opens Search) — routed through Win32 `keybd_event`, NOT SendKeys |
| `{ENTER}` | Enter |
| `{TAB}` | Tab |
| `{ESC}` | Escape |
| `%{F4}` | Alt+F4 |
| `^s` | Ctrl+S |
| `^c` | Ctrl+C |
| `^v` | Ctrl+V |
| `{F12}` | F12 (Excel Save As) |
| `+{TAB}` | Shift+Tab |

---

## Variable Chaining

Use `storeAs` on a step to expose its outputs as `${storeAs.field}` in later steps:

```json
{ "action": "http.get", "parameters": { "url": "http://localhost:3000/api/hello", "storeAs": "api" } },
{ "action": "type",     "parameters": { "value": "${api.body}" } }
```

After an `app.launch` with `storeAs: "app"`, you can reference `${app.processId}` and `${app.windowTitle}`.

---

## Built-in Variables

Available in any `value` or `filePath` parameter without a `storeAs`:

| Variable | Value |
|----------|-------|
| `${now.iso}` | UTC ISO timestamp (`2026-10-29T08:00:00Z`) |
| `${now.local}` | Local datetime string |
| `${now.date}` | Local date only (`2026-10-29`) |
| `${now.time}` | Local time only (`08:00:00`) |

---

## Known Quirks

- **All parameter values are strings.** `"ms": "1000"` not `"ms": 1000`. Applies to every parameter.
- **`key` not `keys`.** The key action parameter name is singular `"key"`.
- **`{LWIN}` requires Win32.** The runtime auto-routes `{LWIN}*` tokens through `keybd_event`. Do not try to use it via `type`.
- **`filePath` is one full path.** `file.saveAs` takes a single `filePath` (e.g. `"%USERPROFILE%\\Downloads\\out.txt"`), not separate directory + filename params.
- **`coordinates` format is `"x,y"` as a string.** e.g. `"coordinates": "960,540"`.
- **`delay` max is 30000 ms.** For longer waits, chain multiple delay steps.
