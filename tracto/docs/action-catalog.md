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

## Built-in Variables (always available, no `storeAs` needed)

| Variable | Value |
|----------|-------|
| `${now.iso}` | UTC timestamp — `2026-10-26T14:30:00.000Z` |
| `${now.local}` | Local datetime — `2026-10-26 14:30:00` |
| `${now.date}` | Local date — `2026-10-26` |
| `${now.time}` | Local time — `14:30:00` |
