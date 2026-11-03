# Tracto Desktop Runtime — Action Catalog

All parameter values are strings in JSON (the runtime casts to int/bool as needed).
`storeAs` is a universal optional parameter on any action — it namespaces that step's outputs into variables (e.g. `storeAs: "api"` → `${api.body}`, `${api.statusCode}`).

---

## `key`
Sends a keyboard shortcut or key press to the active window.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `key` | ✅ | Key string. Human-readable (`"Enter"`, `"Alt+F4"`, `"Ctrl+S"`) OR SendKeys syntax (`"{ENTER}"`, `"%{F4}"`, `"^s"`). Use `"{LWIN}s"` for Win+S. |
| `window` | ❌ | Partial window title to activate before sending. |

**Outputs:** `sendKeysValue`

**Key syntax cheatsheet:**
- `{LWIN}s` — Windows key + S (opens Search)
- `{ENTER}` — Enter
- `%{F4}` — Alt+F4
- `^s` — Ctrl+S
- `{F12}` — F12

---

## `type`
Types text into the active window using OS-level keystrokes.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `value` | ✅ | Text to type. Supports variable expansion: `"${api.body}"`. Use `\n` for newline. |
| `window` | ❌ | Partial window title to activate first. |

**Outputs:** `charactersTyped`

---

## `delay`
Pauses execution for a fixed duration.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `ms` | ✅ | Milliseconds to wait (max 30000). |

---

## `click`
Moves the mouse to screen coordinates and clicks.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `coordinates` | ✅ | `"x,y"` screen coordinates (e.g. `"960,540"`). |
| `window` | ❌ | Partial window title to activate first. |
| `rightClick` | ❌ | `"true"` for right-click (default: left-click). |

**Outputs:** `x`, `y`

---

## `scroll`
Scrolls the mouse wheel at a screen position.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `coordinates` | ✅ | `"x,y"` screen coordinates. |
| `delta` | ❌ | Wheel delta — positive = scroll up, negative = down (default: `"120"`). |
| `window` | ❌ | Partial window title to activate first. |

---

## `navigate`
Brings a window to the foreground by matching its title.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `window` | ✅ | Partial window title to match (case-insensitive). |

**Outputs:** `windowHandle`

---

## `focus`
Activates a control or window for subsequent input.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `window` | ❌ | Partial window title to activate. |
| `target` | ❌ | Control / automation ID (informational, used in logs). |

**Outputs:** `windowHandle`

---

## `app.launch`
Launches a Windows application (or attaches to an existing instance).

| Parameter | Required | Description |
|-----------|----------|-------------|
| `executable` | ✅ | Process name or full path (e.g. `"notepad"` or `"C:\\Windows\\notepad.exe"`). |
| `args` | ❌ | Space-separated command-line arguments. |
| `timeoutMs` | ❌ | Max wait for process to idle (default: `"10000"`). |
| `attachIfRunning` | ❌ | `"true"` to attach to existing process before launching. |

**Outputs:** `processId`, `windowTitle`

---

## `http.get`
Makes an HTTP GET request and stores the response.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `url` | ✅ | Full URL including query string. |
| `timeoutMs` | ❌ | Request timeout in ms (default: `"30000"`). |
| `storeAs` | ❌ | Variable namespace prefix for outputs (e.g. `"api"`). |

**Outputs:** `statusCode`, `body`, `json` (only when response is valid JSON)

**Variable chaining example:**
```json
{ "action": "http.get", "parameters": { "url": "http://localhost:3000/api/hello", "storeAs": "api" } }
{ "action": "type",     "parameters": { "value": "${api.body}" } }
```

---

## `http.post`
Makes an HTTP POST request with a body and stores the response.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `url` | ✅ | Full URL. |
| `body` | ❌ | Request body, sent as-is. |
| `contentType` | ❌ | Default `application/json`. |
| `timeoutMs` | ❌ | Default `"30000"`. |

**Outputs:** `statusCode`, `body`, `json`, and every top-level string/number/boolean field of a JSON object reply (so `${storeAs.field}` works). A non-2xx reply fails the step.

---

## `notepad.type`
Pastes text into Notepad through the clipboard. Use for long or multi-line text.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `text` | ✅ | Text to paste. Use `
` for new lines. |
| `windowTitle` | ❌ | Partial title to match (default `"Notepad"`). |

**Outputs:** `charactersTyped`

---

## `file.saveAs`
Saves a document via the host application's Save As dialog (Ctrl+S).

| Parameter | Required | Description |
|-----------|----------|-------------|
| `filePath` | ✅ | Full destination path including filename (e.g. `"%USERPROFILE%\\Downloads\\output.txt"`). Env vars expanded. |
| `windowTitle` | ❌ | Window title to trigger Save As on (default: `"Notepad"`). |
| `overwrite` | ❌ | `"true"` to auto-dismiss "file exists" dialog (default: `"true"`). |
| `saveAsShortcut` | ❌ | SendKeys to open Save As (default: `"^s"`; use `"{F12}"` for Excel). |

**Outputs:** `savedPath`, `fileSizeBytes`

---

## `file.read`
Reads a text file into variables.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `path` | ✅ | Full path of the file. `%USERPROFILE%`-style variables are expanded. |
| `maxChars` | ❌ | Longest text returned (default: `"100000"`). |
| `storeAs` | ❌ | Variable namespace prefix for outputs (e.g. `"doc"`). |

**Outputs:** `text` (content with line endings normalised to `\n`, trailing blank lines and spaces removed), `firstLine` (first non-empty line), `lineCount`.

Fails with `NOT_FOUND` when the file does not exist and `EXECUTION_FAILED` when it is larger than 5 MB or cannot be read. UTF-8 and UTF-8/16/32 with a byte-order mark are read correctly.

```json
{ "action": "file.read",    "parameters": { "path": "C:\\data\\input.txt", "storeAs": "doc" } }
{ "action": "notepad.type", "parameters": { "text": "${doc.firstLine}" } }
```

---

## `clipboard.get`
Reads the text on the clipboard into variables. Copy first in the source application (for example `key` `Ctrl+A`, `key` `Ctrl+C` with that application's `window`, then a short `delay`).

| Parameter | Required | Description |
|-----------|----------|-------------|
| `storeAs` | ❌ | Variable namespace prefix for outputs (e.g. `"note"`). |

**Outputs:** `text` (line endings normalised to `\n`, trailing blank lines and spaces removed), `firstLine` (first non-empty line).

Fails with `EXECUTION_FAILED` when the clipboard holds no text.

---

## Built-in Variables (always available, no `storeAs` needed)

| Variable | Value |
|----------|-------|
| `${now.iso}` | UTC timestamp — `2026-10-26T14:30:00.000Z` |
| `${now.local}` | Local datetime — `2026-10-26 14:30:00` |
| `${now.date}` | Local date — `2026-10-26` |
| `${now.time}` | Local time — `14:30:00` |

---

## Job parameters and outputs

A run can carry named values. Any parameter value of any action may use them as `${param.name}`.

| Source of a parameter | Example |
|-----------------------|---------|
| The `params` object of a segmented script (default values) | `"params": { "notePath": "C:\\data\\a.txt" }` → `${param.notePath}` |
| The `params` of `POST /jobs` (override the defaults) | `{"params": {"notePath": "C:\\data\\b.txt"}}` |
| Outputs of an earlier segment | `storeAs: "note"` on a `clipboard.get` → `${param.note.firstLine}` in a later segment |

When a run finishes, the host reports its outputs to the Orchestrator: the desktop Host reports every `storeAs` output (`name.field`); the browser host reports the stored value as `name` and `name.value`. `GET /jobs/{runId}` returns `params` and `outputs`.

---

## Segmented scripts

A segmented script runs several scripts in order, each on its own runtime. It replaces the top-level `targetRuntime` and `steps` with `segments`.

```json
{
  "id": "notepad-to-bing",
  "name": "Notepad To Bing",
  "version": "1.0.0",
  "params": { "notePath": "C:\\data\\search-text.txt", "noteName": "search-text.txt" },
  "segments": [
    { "name": "Read the note", "runtime": "desktop", "steps": [ { "stepNumber": 1, "action": "..." } ] },
    { "name": "Search Bing",   "runtime": "browser", "steps": [ { "stepNumber": 1, "action": "..." } ] }
  ]
}
```

| Field | Rules |
|-------|-------|
| `id`, `name`, `version` | Same rules as a flat script. When published, `id` and `version` equal the artifact id and version. |
| `params` | Optional object of string (number, boolean or null) defaults. |
| `segments` | Non-empty array. |
| `segments[].name` | Optional label. |
| `segments[].runtime` | `desktop`, `browser` or `terminal`. |
| `segments[].steps` | Non-empty array of flat steps for that runtime; `stepNumber` restarts at 1 in every segment. |

Execution: `POST /jobs` on a segmented script returns one parent run. The Orchestrator queues segment 1 for a worker of its runtime; when it succeeds, segment 2 is queued with the parameters plus every output so far, and so on. A segment that fails or is cancelled ends the parent run with that state, and later segments stay `PENDING`. `GET /jobs/{runId}` lists each segment with `index`, `name`, `runtime`, `runId`, `state` and `outputs`. Each segment is stored as a hidden artifact `<id>.seg<n>`.

The script is uploaded wrapped as `{"schemaVersion":"1","script":{...}}` inside the base64 payload (see `tracto/CLAUDE.md`).
