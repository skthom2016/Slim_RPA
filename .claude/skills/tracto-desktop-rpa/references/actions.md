# Actions

Fourteen desktop actions exist. Anything else fails with `UnknownAction:<name>`. (A browser segment of a segmented script uses the browser runtime's own actions, which are not listed here.) All parameter values are strings. Every action also accepts `storeAs` (a name) to expose its outputs as `${name.output}`; without `storeAs` the outputs go to bare names such as `${statusCode}`, which collide between steps, so always set `storeAs` when you will use an output.

Job parameters: a job can carry named values (a segmented script's `params`, the `params` of `POST /jobs`, and the outputs of earlier segments). Any parameter value of any action may use them as `${param.name}`; the validator warns when a name is not declared.

Error categories you will see in the runtime log: `CONFIGURATION` (a required parameter is missing or malformed), `NOT_FOUND` (window not found), `EXECUTION_FAILED` (the action threw or an HTTP call returned non-2xx), `TIMEOUT`.

Window parameters (`window`, `windowTitle`) are case-insensitive substrings of a window's title. The first match in Windows' window order wins. Be specific.

---

## `app.launch`
Starts a program through the Windows shell and waits until it is idle.

| Parameter | Required | Notes |
|---|---|---|
| `executable` | yes | Program name or full path: `"notepad.exe"`, `"explorer.exe"`, `"C:\\Windows\\System32\\calc.exe"` |
| `args` | no | Split on spaces into separate arguments. A path with a space cannot be passed. |
| `timeoutMs` | no | Wait for idle, default `"10000"` |
| `attachIfRunning` | no | `"true"`: attach to an already running process with that name instead of starting one. Fails over to launching if none is found. |

Outputs: `processId`, `windowTitle`.

Works (**verified**): `notepad.exe`; `explorer.exe` with a folder path as `args` opens that folder.

**Does not work for these, even though they may start:**
- **Console programs** (`cmd.exe`, `powershell.exe`, `robocopy.exe`, `xcopy.exe`): the command runs but the step fails, because the runtime waits for a window message queue. **Verified.** Use the Run dialog recipe.
- **Document paths** (`.pdf`, `.docx`, `.xlsx`, `.txt`): needs a registered default app and fails otherwise. On the test machine, `.pdf` pointed to an uninstalled Acrobat and the step failed. **Verified.** Open documents with an explicit program from the Run dialog.
- **Browsers already running** (`msedge.exe`, `chrome.exe`): the window opens but the step fails, because several processes share the name and the runtime cannot pick one. **Verified** with Edge.
- Programs that hand off to an existing instance and exit (modern Notepad does this and is handled; others may not be).

Win 11 Notepad is a tabbed app with session restore. Launching it may open a new window or add a tab to an existing window, and restored tabs from earlier sessions can already be present. The window title shows the active tab only, and it is the **first line of the text** while unsaved (`*Hello - Notepad`), `Untitled - Notepad` while empty, and the file name once saved (`note1.txt - Notepad`).

---

## `notepad.type`
Pastes text into a window using the clipboard (`Ctrl+V`). The right way to enter long or multi-line text.

| Parameter | Required | Notes |
|---|---|---|
| `text` | yes | Use `\r\n` for line breaks in the JSON string. `${...}` is expanded. |
| `windowTitle` | no | Default `"Notepad"`. Any title substring. |

Outputs: `charactersTyped`.

Fails (`NOT_FOUND`) if no window matches. It overwrites the clipboard. Appends at the caret; it does not select or clear existing text. By its code it can paste into any window (set `windowTitle` to that app's title), but only Notepad was exercised. Unicode (Chinese) is fine because it uses the clipboard.

---

## `type`
Types text with simulated keystrokes into the active window, or into the window matched by `window` (activated first).

| Parameter | Required | Notes |
|---|---|---|
| `value` | yes | Plain text. The characters `( ) { } [ ] ^ % ~ +` are typed literally. |
| `window` | no | Title substring to activate first. If not found, it types into whatever window is active. |

Outputs: `charactersTyped`.

It cannot press Enter, Tab or Escape: `{ENTER}` is typed as text. Use `key` for those. For long text or anything non-ASCII prefer `notepad.type`. Typing is subject to the target app's own autocomplete and focus.

---

## `key`
Presses a key or shortcut in the active window, or in `window` if given (activated first).

| Parameter | Required | Notes |
|---|---|---|
| `key` | yes | See below |
| `window` | no | Title substring to activate first. If not found, the key goes to the active window. |

Output: `sendKeysValue`.

Accepted forms:
- Readable: `"Enter"`, `"Tab"`, `"Esc"`, `"Delete"`, `"Backspace"`, `"Home"`, `"End"`, `"PageUp"`, `"PageDown"`, `"Up"`, `"Down"`, `"Left"`, `"Right"`, `"F1"`..`"F12"`, `"Space"`, and combinations `"Ctrl+S"`, `"Alt+F4"`, `"Shift+Tab"`, `"Ctrl+Shift+N"`. Modifiers: `Ctrl`, `Alt`, `Shift`.
- SendKeys syntax when the first character is one of `^ % + { ~`: `"^s"` Ctrl+S, `"%{F4}"` Alt+F4, `"{ENTER}"`.
- `"{LWIN}r"`, `"{LWIN}e"`, `"{LWIN}s"`: Windows key plus one letter, sent at OS level. `"{LWIN}"` alone opens Start. Only the first token may be `{LWIN}`.

Verified: `Ctrl+A`, `Ctrl+C`, `Ctrl+V`, `Enter`, `Delete`, `Alt+F4`, `{LWIN}r`.

---

## `click`
Left-clicks (or right-clicks) at a position.

| Parameter | Required | Notes |
|---|---|---|
| `window` | no | Visible window whose title contains this; it is brought to the front first |
| `offset` | no | `"x,y"` in pixels from that window's top-left corner. Used when the window is found. Survives the window moving. |
| `coordinates` | one of | `"x,y"` absolute screen pixels. Used when there is no `window`/`offset`. |
| `rightClick` | no | `"true"` for a right click |

Outputs: `x`, `y`. With neither `coordinates` nor a usable `offset`, it does nothing and succeeds. Only a single click exists: no double-click, no drag, no hold. Pixel positions depend on screen size, scaling and window size, so they are fragile; prefer keyboard routes. Click targets are found by looking at a screenshot of the target app, not from the script.

---

## `scroll`
Scrolls the mouse wheel at a screen position.

| Parameter | Required | Notes |
|---|---|---|
| `coordinates` | yes | `"x,y"` |
| `delta` | no | Default `"120"`; positive scrolls up, negative down, 120 is one notch |
| `window` | no | Title substring to activate first |

Without `coordinates` it does nothing and succeeds.

---

## `navigate`
Brings a window to the front by title. Does **not** fail when no window matches (output `windowHandle` is `"0"`). Required: `window`. Output: `windowHandle`.

## `focus`
Same as `navigate` with optional `window` and an informational `target`. Without `window` it does nothing.

---

## `delay`
Waits. Required `ms` (`"1500"`), capped at `30000`. This is the only way to wait: there is no "wait until window appears".

---

## `file.saveAs`
Saves the document in a window using its Save As dialog: sends the save shortcut, waits up to 5 s for a window titled "Save As", clears the filename box, types the path, presses Enter, and confirms an overwrite prompt.

| Parameter | Required | Notes |
|---|---|---|
| `filePath` | yes | Full destination path. `%USERPROFILE%`-style variables are expanded here (and only here). The folder must already exist. |
| `windowTitle` | no | Default `"Notepad"`. Fails (`NOT_FOUND`) if no match. |
| `overwrite` | no | Default `"true"`: confirms "file already exists" |
| `saveAsShortcut` | no | Default `"^s"`. Use `"{F12}"` for Excel. |

Outputs: `savedPath`, `fileSizeBytes` (0 if the file is not there afterwards).

**Verified:** pasting text then `file.saveAs` in Notepad created the file with the right content. The Save As dialog is titled "Save as"; matching is case-insensitive so this works. It saves the window that matches `windowTitle` first, so pass a specific title: once text is pasted, Notepad's title is the first line of that text (see `app.launch`), so use that, not `"Untitled - Notepad"`. If the title no longer matches, the step fails with `NOT_FOUND`. If the Save As dialog cannot be driven (for example the destination folder does not exist), the step can still report success while the dialog stays open or nothing is saved. If the document already has a name, Ctrl+S saves silently without showing the dialog, and the path is ignored. A failed save still reports success; check `fileSizeBytes`.

---

## `file.read`
Reads a text file into variables.

| Parameter | Required | Notes |
|---|---|---|
| `path` | yes | Full path. `%USERPROFILE%`-style variables are expanded. A relative path resolves against the Host's working directory, so use a full path. |
| `maxChars` | no | Longest text returned, default `"100000"` |

Outputs (with `storeAs: "x"`): `${x.text}` (content with line endings normalised to `\n`, trailing blank lines and spaces removed), `${x.firstLine}` (first non-empty line, trimmed), `${x.lineCount}`.

Fails `NOT_FOUND` when the file does not exist, and `EXECUTION_FAILED` when it is larger than 5 MB or cannot be read (for example locked by another program). Encodings: UTF-8, plus UTF-8/16/32 with a byte-order mark; other encodings may show wrong characters. The file is read directly and no application is opened. **Not verified by a run.**

## `clipboard.get`
Reads the text currently on the clipboard into variables. Pair it with a copy in the source app: `key Ctrl+A`, `key Ctrl+C` (with that app's `window`), a short `delay`, then `clipboard.get`.

No parameters except `storeAs`.

Outputs (with `storeAs: "x"`): `${x.text}` (line endings normalised to `\n`, trailing blank lines and spaces removed) and `${x.firstLine}` (first non-empty line, trimmed).

Fails `EXECUTION_FAILED` when the clipboard holds no text (for example the copy went to the wrong window). It retries a few times if another program holds the clipboard briefly. **Verified** in the Notepad to Bing run: Ctrl+A and Ctrl+C in a Notepad window, `clipboard.get`, and `${note.firstLine}` held the file's text. `notepad.type` overwrites the clipboard, so read it before any `notepad.type`.

---

## `http.get`
| Parameter | Required | Notes |
|---|---|---|
| `url` | yes | Full URL |
| `timeoutMs` | no | Default `"30000"` |

Outputs: `statusCode`, `body`, `json` (when the body is valid JSON). Any non-2xx status fails the step.

## `http.post`
| Parameter | Required | Notes |
|---|---|---|
| `url` | yes | Full URL |
| `body` | no | Sent as is. A JSON body is a string inside the JSON file, so inner quotes are `\"`. |
| `contentType` | no | Default `application/json` |
| `timeoutMs` | no | Default `"30000"` |

Outputs: `statusCode`, `body`, `json`, and each top-level string/number/boolean field of a JSON object reply as its own output (`${api.updated}`, `${api.text}`). A non-2xx reply fails the step, and the failure message contains the reply body, which usually explains the problem. Only `http://`/`https://` URLs work (no `file://`).
