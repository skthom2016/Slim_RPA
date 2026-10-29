# Tracto Desktop Runtime — Action Catalog

All parameter values are strings in JSON (the runtime casts to int/bool as needed).
`storeAs` is a universal optional parameter on any action — it namespaces that step's outputs into variables (e.g. `storeAs: "api"` → `${api.body}`, `${api.statusCode}`).

---

## `key`
Sends a keyboard shortcut or key press to the active window.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `key` | ✅ | — | Key string. Accepts human-readable (`"Enter"`, `"Alt+F4"`, `"Ctrl+S"`, `"Shift+Tab"`) OR SendKeys syntax (`"{ENTER}"`, `"%{F4}"`, `"^s"`, `"+{TAB}"`). Use `"{LWIN}s"` for Win+S. |
| `window` | ❌ | — | Partial window title to activate before sending. |

**Outputs:** `sendKeysValue` — the final SendKeys string that was sent.

**Key syntax cheatsheet:**

| You write | Meaning |
|-----------|---------|
| `{LWIN}s` | Windows key + S (opens Search) — routed via Win32 `keybd_event` |
| `{WIN}s` | Alias for `{LWIN}s` |
| `{RWIN}` | Right Windows key |
| `{ENTER}` or `"Enter"` | Enter |
| `{ESC}` or `"Escape"` | Escape |
| `{TAB}` or `"Tab"` | Tab |
| `{DELETE}` or `"Delete"` | Delete |
| `{BACKSPACE}` or `"Backspace"` | Backspace |
| `{HOME}` / `{END}` | Home / End |
| `{PGUP}` / `{PGDN}` | Page Up / Page Down |
| `{UP}` `{DOWN}` `{LEFT}` `{RIGHT}` | Arrow keys |
| `{F1}`…`{F12}` or `"F1"`…`"F12"` | Function keys |
| `^s` or `"Ctrl+S"` | Ctrl+S |
| `%{F4}` or `"Alt+F4"` | Alt+F4 |
| `+{TAB}` or `"Shift+Tab"` | Shift+Tab |
| `{F12}` | F12 (use for Excel Save As) |

> `{LWIN}`, `{WIN}`, and `{RWIN}` bypass SendKeys and use Win32 `keybd_event` directly — they cannot be sent via `type`.

---

## `type`
Types text into the active window using OS-level keystrokes (SendKeys).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `value` | ✅ | — | Text to type. Supports variable expansion: `"${api.body}"`. |
| `window` | ❌ | — | Partial window title to activate first. |

**Outputs:** `charactersTyped` (int)

> **Note:** `type` uses SendKeys under the hood. For text containing special characters (parentheses, brackets, `+`, `^`, `%`, `~`) these are escaped automatically. For long text or text with complex Unicode, prefer `notepad.type` (clipboard paste) when the target is Notepad.

---

## `notepad.type`
Types text into Notepad (or any app accepting Ctrl+V paste) via the clipboard. More reliable than `type` for long strings, special characters, and Unicode.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `text` | ✅ | — | Text to paste. Note: parameter name is `text`, not `value`. |
| `windowTitle` | ❌ | `"Notepad"` | Partial window title to bring to focus before pasting. |

**Outputs:** `charactersTyped` (int)

**How it works:** Sets the clipboard to `text`, brings the target window to the foreground, sends Ctrl+V to paste.

**When to use `notepad.type` over `type`:**
- The text contains parentheses, curly braces, `+`, `^`, `%`, or `~`
- The text is long (hundreds of characters)
- SendKeys is producing garbled output

**Example:**
```json
{
  "stepNumber": 5,
  "description": "Paste report text into Notepad",
  "action": "notepad.type",
  "parameters": {
    "text": "Revenue: $1,200 (Q3) — up 12%",
    "windowTitle": "Notepad"
  }
}
```

---

## `delay`
Pauses execution for a fixed duration.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `ms` | ❌ | `"1000"` | Milliseconds to wait (max 30000). Omitting uses 1000ms default. |

> Best practice: always supply `ms` explicitly — relying on the default makes scripts harder to understand.

---

## `click`
Moves the mouse to screen coordinates and clicks.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `coordinates` | ✅ | — | `"x,y"` screen coordinates (e.g. `"960,540"`). If omitted, step is a no-op. |
| `window` | ❌ | — | Partial window title to activate first. |
| `rightClick` | ❌ | `"false"` | `"true"` for right-click. |
| `target` | ❌ | — | Control name for logging only — has no effect on execution. |

**Outputs:** `x`, `y` (int) — coordinates where the click landed.

---

## `scroll`
Scrolls the mouse wheel at a screen position.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `coordinates` | ✅ | — | `"x,y"` screen coordinates. |
| `delta` | ❌ | `"120"` | Wheel delta. Positive = scroll up, negative = scroll down. One "notch" = 120. |
| `window` | ❌ | — | Partial window title to activate first. |

**Outputs:** None.

---

## `navigate`
Brings a window to the foreground by matching its title.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `window` | ❌* | — | Partial window title to match (case-insensitive). |

*If `window` is missing or empty, the step is a **no-op** — it logs a warning and succeeds without doing anything. In practice always supply a window title.

**Outputs:** `windowHandle` (string) — hex handle of the activated window, or `"0"` if not found.

> Window not found is non-fatal — the runtime logs a warning and the script continues.

---

## `focus`
Activates a window for subsequent input.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `window` | ❌ | — | Partial window title to activate. Without this, the step is a no-op. |
| `target` | ❌ | — | Control / automation ID — informational only, used in logs. Has no effect on execution. |

**Outputs:** `windowHandle` (string)

> `focus` and `navigate` both call `SetForegroundWindow`. The difference is semantic: `navigate` expresses a window transition, `focus` expresses readying a control for input.

---

## `app.launch`
Launches a Windows application (or attaches to an existing instance).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `executable` | ✅ | — | Process name or full path (`"notepad"` or `"C:\\Windows\\notepad.exe"`). |
| `args` | ❌ | — | Command-line arguments as a single space-separated string. **Arguments with spaces in them cannot be quoted** — if you need a path with spaces, use `app.launch` to launch the app first, then open the file through the app's UI. |
| `timeoutMs` | ❌ | `"10000"` | Max wait (ms) for the process to become idle after launch. |
| `attachIfRunning` | ❌ | `"false"` | `"true"` — try attaching to an existing process before launching a new one. |

**Outputs:** `processId` (int), `windowTitle` (string)

---

## `http.get`
Makes an HTTP GET request and stores the response.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `url` | ✅ | — | Full URL including query string. |
| `timeoutMs` | ❌ | `"30000"` | Request timeout in ms. |
| `storeAs` | ❌ | — | Variable namespace prefix for outputs (e.g. `"api"` → `${api.body}`). |

**Outputs:** `statusCode` (int), `body` (string), `json` (object — only when body is valid JSON)

**Non-2xx responses:** The step fails (marking the run as failed) but `statusCode` and `body` outputs are still populated. If you use `storeAs`, those variables will hold the error response.

**Variable chaining example:**
```json
{ "action": "http.get", "parameters": { "url": "http://localhost:3000/api/hello", "storeAs": "api" } },
{ "action": "type",     "parameters": { "value": "${api.body}" } }
```

---

## `file.saveAs`
Saves a document via the host application's Save As dialog.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `filePath` | ✅ | — | Full destination path including filename and extension (e.g. `"%USERPROFILE%\\Downloads\\output.txt"`). Environment variables are expanded. |
| `windowTitle` | ❌ | `"Notepad"` | Partial title of the window to trigger Save As on. |
| `overwrite` | ❌ | `"true"` | `"true"` to auto-dismiss "file already exists" confirmation dialog. |
| `saveAsShortcut` | ❌ | `"^s"` | SendKeys string used to open the Save As dialog. Use `"{F12}"` for Excel (avoids the backstage menu). |

**Outputs:** `savedPath` (string) — expanded absolute path saved to; `fileSizeBytes` (long) — file size after save (0 if file was not found within timeout).

**Excel example:**
```json
{
  "action": "file.saveAs",
  "parameters": {
    "filePath": "%USERPROFILE%\\Documents\\report.xlsx",
    "windowTitle": "Excel",
    "saveAsShortcut": "{F12}"
  }
}
```

---

## Built-in Variables (always available, no `storeAs` needed)

| Variable | Value |
|----------|-------|
| `${now.iso}` | UTC timestamp — `2026-10-29T14:30:00.000Z` |
| `${now.local}` | Local datetime — `2026-10-29 14:30:00` |
| `${now.date}` | Local date — `2026-10-29` |
| `${now.time}` | Local time — `14:30:00` |
