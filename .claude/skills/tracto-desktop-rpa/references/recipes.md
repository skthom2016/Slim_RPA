# Recipes

Each recipe says whether it was **verified** (run on a Windows 11 machine with the real runtime) or only follows from how the runtime works. Step JSON is shown compactly; every value is a string in the real file.

Paths below use `C:\\Users\\<you>\\...` as JSON strings (doubled backslashes). Inside a `type` value that goes to the Run dialog the same string is typed as `C:\Users\...`.

---

## 1. Open an app and wait
**Verified** (`notepad.exe`, `explorer.exe`).

```json
{ "stepNumber": 1, "action": "app.launch", "description": "Open Notepad", "parameters": { "executable": "notepad.exe" } },
{ "stepNumber": 2, "action": "delay", "description": "Wait for the window", "parameters": { "ms": "1500" } }
```

Open File Explorer on a folder: `"executable": "explorer.exe", "args": "C:\\Users\\me\\Documents"` (path without spaces), then `delay` 2500.

## 2. Run a shell command or open anything: the Run dialog
**Verified.** This is the workhorse for everything `app.launch` cannot do: console commands, documents, browsers, apps that are already running.

```json
{ "stepNumber": 1, "action": "key",   "description": "Open the Run dialog", "parameters": { "key": "{LWIN}r" } },
{ "stepNumber": 2, "action": "delay", "description": "Wait for it", "parameters": { "ms": "1000" } },
{ "stepNumber": 3, "action": "type",  "description": "Command to run", "parameters": { "value": "cmd /c mkdir C:\\Users\\me\\Documents\\archive" } },
{ "stepNumber": 4, "action": "key",   "description": "Run it", "parameters": { "key": "Enter" } },
{ "stepNumber": 5, "action": "delay", "description": "Let it finish", "parameters": { "ms": "1500" } }
```

Rules:
- Four steps plus a delay for each command. Repeat the block for several commands.
- The command is typed as plain text: use full paths, no `{ENTER}`. `${now.date}` and other variables are expanded before typing (**verified**: `moved-${now.date}.txt` became `moved-2026-11-02.txt`).
- A console window flashes briefly. Nothing is captured: the script cannot read the command's output or exit code, and a failed command still reports success. Check the effect afterwards.
- Paths with spaces: wrap in quotes inside the value, `"value": "cmd /c copy \"C:\\My Files\\a.txt\" C:\\out\\"` (the quoting reaches `cmd`; a Word launch with a quoted path started correctly).
- Leave the delay generous for slow disks or large copies.

## 3. Create a text file with content
**Verified.**

```json
{ "stepNumber": 1, "action": "app.launch",   "parameters": { "executable": "notepad.exe" } },
{ "stepNumber": 2, "action": "delay",        "parameters": { "ms": "1500" } },
{ "stepNumber": 3, "action": "notepad.type", "description": "Content", "parameters": { "text": "Hello from Tracto ${now.local}" } },
{ "stepNumber": 4, "action": "file.saveAs",  "description": "Save it", "parameters": { "filePath": "C:\\Users\\me\\Documents\\note1.txt" } }
```

Notes:
- The folder must exist (create it first with recipe 4).
- **Win 11 Notepad titles its window after the content.** An empty new document is `Untitled - Notepad`. As soon as text is pasted the title becomes the first line of the text with a `*`, for example `*Hello from Tracto - Notepad`. After saving it is the file name, `note1.txt - Notepad`. So: paste with `windowTitle` `"Untitled - Notepad"` while the document is empty, then save with `windowTitle` set to the text of the first line, and close with the file name. Make the first line distinctive. The template `create-text-file.json` does exactly this (**verified**).
- Put a 1000 ms `delay` between the paste and `file.saveAs` so the title has updated.
- Notepad may already hold restored tabs from earlier sessions; the window title shows only the active tab.

Multi-line text: `"text": "Line 1\r\nLine 2\r\nLine 3"`.

## 4. Create folder, copy, move, rename, delete (files and folders)
**Verified** for mkdir, copy, move, del via the Run dialog (recipe 2). The others use the same mechanism and standard Windows commands but were not run.

| Job | Value to type in the Run dialog |
|---|---|
| Create folder | `cmd /c mkdir C:\data\archive` (also creates any missing parent folders; **verified**) |
| Copy file | `cmd /c copy /Y C:\data\a.txt C:\data\archive\a.txt` |
| Move file | `cmd /c move /Y C:\data\a.txt C:\data\archive\a-${now.date}.txt` |
| Delete file | `cmd /c del /Q C:\data\a.txt` |
| Rename file | `cmd /c ren C:\data\a.txt b.txt` (new name only, no path) |
| Copy folder tree | `cmd /c xcopy C:\data\in C:\data\out /E /I /Y` |
| Delete folder | `cmd /c rmdir /S /Q C:\data\old` |

`del` and `rmdir` are permanent (no Recycle Bin). Double-check the path; there is no undo and no confirmation. A path or command typo fails silently.

## 5. Copy or delete through File Explorer keystrokes
**Verified** on a Windows 11 machine. Use when you want Recycle Bin behaviour or a visible, human-like flow.

```json
{ "stepNumber": 1, "action": "app.launch", "parameters": { "executable": "explorer.exe", "args": "C:\\data" } },
{ "stepNumber": 2, "action": "delay",      "parameters": { "ms": "2500" } },
{ "stepNumber": 3, "action": "navigate",   "description": "Window is titled with the folder name", "parameters": { "window": "data" } },
{ "stepNumber": 4, "action": "type",       "description": "Typing a file name selects it", "parameters": { "value": "a.txt" } },
{ "stepNumber": 5, "action": "key",        "parameters": { "key": "Ctrl+C" } },
{ "stepNumber": 6, "action": "key",        "description": "Pasting in the same folder makes 'a - Copy.txt'", "parameters": { "key": "Ctrl+V" } }
```

Delete: select the file the same way, `key Delete`, then **handle the confirmation dialog**: `key Enter` with `window` `"Delete File"` (on the test machine the dialog appeared; the run without it reported success and deleted nothing). `delay` 1500 after. Close the window with `key Alt+F4` and `window` set to the folder name.

Notes: type-ahead selection needs the exact start of the name and works within about a second of typing; type the whole name. To paste into a different folder, go there first with `key Ctrl+L`, `type` the path, `key Enter`.

## 6. Open a file or URL with a chosen program
Through the Run dialog (recipe 2); the value to type is the program followed by its argument:

| Open | Value to type | Status |
|---|---|---|
| PDF in Edge | `msedge --new-window file:///C:/Users/me/Documents/invoice.pdf` | **verified** (opened) |
| URL in Edge | `msedge --new-window https://example.com` | same mechanism |
| Text file in Notepad | `notepad C:\data\a.txt` | not run |
| Excel workbook | `excel "C:\data\book.xlsx"` | not run |
| Word document | `winword "C:\data\doc.docx"` | started; Word showed a long "Opening" splash on the test machine |
| Folder in Explorer | `explorer C:\data` | not run |

Wait after opening: 4000-6000 ms for documents. The window title of a PDF in a browser starts with the file name (`invoice.pdf - ...`).

## 7. Copy data from one app and paste into another
Source app: click in or focus it, select, `Ctrl+C`. Target app: focus, `Ctrl+V`.

```json
{ "stepNumber": 1, "action": "key", "description": "Select all in the source", "parameters": { "key": "Ctrl+A", "window": "source.txt - Notepad" } },
{ "stepNumber": 2, "action": "key", "description": "Copy", "parameters": { "key": "Ctrl+C" } },
{ "stepNumber": 3, "action": "key", "description": "Paste in the target", "parameters": { "key": "Ctrl+V", "window": "target.txt - Notepad" } }
```

**Verified:** Select all + copy in Notepad puts the text on the clipboard. **Not reliable:** copying text out of a PDF open in a browser (see recipe 9).

The script cannot inspect the clipboard. `notepad.type` also sets the clipboard, so a later `Ctrl+V` pastes the last `notepad.type` text.

## 8. Close windows
`key` with `Alt+F4` and a specific `window` (**verified**).

- A document that is **saved** closes silently. Use the file name as the title: `"window": "note1.txt - Notepad"`.
- A document with **unsaved changes** shows "Do you want to save changes to ...?" with Save (default), Don't save and Cancel. To discard (**verified**): `key Alt+F4` with the window, `delay` 1000, `key Tab` (moves to Don't save), `key Enter`. To keep it, save first (recipe 3).
- The prompt is modal. While it is open, other keystrokes, including the Run dialog shortcut, go astray or are lost. Always resolve it before the next step.
- If the Alt+F4 target window is not found, the keys go to the active window and can close the wrong thing, then `Tab` and `Enter` land in another app. Give the window a specific title.

## 9. Open a PDF and copy part of its text into Notepad
**Opening works. Copying text out of the PDF did not work on the test machine.**

What was run: open the PDF in Edge or Chrome via the Run dialog (opens, **verified**); `key Ctrl+A` then `Ctrl+C` in the viewer; paste into Notepad. Result: the Notepad document was empty.
- Edge's built-in PDF viewer showed no selection after `Ctrl+A`, and no drag selection either; nothing reached the clipboard.
- Chrome's PDF viewer highlighted the whole page after `Ctrl+A`, but `Ctrl+C` and the right-click Copy menu both left the clipboard unchanged (the clipboard change counter did not move). Copy in Notepad worked on the same machine, so the clipboard itself is fine.
- Word (converts PDFs on open) hung on its "Opening in Protected View" splash and was not pursued.

So: do not promise PDF text extraction by keyboard copy. Options, in order of reliability:
1. Ask the core team for a `pdf.extractText` action (best; see `limits.md`).
2. Try the target machine's own PDF reader (Acrobat Reader desktop app, Word). Check manually first that Ctrl+A / Ctrl+C in it fills the clipboard (paste into Notepad by hand). If it does, the flow is: open via Run dialog, `delay`, click inside the page (`click` with `window` and `offset`), `key Ctrl+A`, `key Ctrl+C`, then paste into Notepad as in recipe 7.
3. For a fixed PDF layout, type the needed values into the script (not RPA reading).

When a script opens a PDF it can still: show it on screen, print it (`key Ctrl+P` and confirm), and save it elsewhere (recipe 4).

## 10. Call an API and use the result
**Verified.**

```json
{ "stepNumber": 1, "action": "http.get", "description": "Call the API", "parameters": { "url": "http://localhost:3000/api/hello", "storeAs": "api" } },
{ "stepNumber": 2, "action": "app.launch", "parameters": { "executable": "notepad.exe" } },
{ "stepNumber": 3, "action": "delay", "parameters": { "ms": "1500" } },
{ "stepNumber": 4, "action": "notepad.type", "parameters": { "text": "${now.local}: ${api.body}" } }
```

POST with a JSON body (inner quotes escaped):

```json
{ "stepNumber": 1, "action": "http.post", "description": "Create item",
  "parameters": { "url": "http://localhost:9000/items", "body": "{\"name\":\"widget\",\"qty\":3}", "storeAs": "created" } }
```

If the reply is `{"id":"42","status":"ok"}`, later steps can use `${created.id}` and `${created.status}`. `${created.body}` is the raw text. The step fails on any non-2xx status, and the failure text contains the reply.

Put values from earlier steps inside the body with `${...}`: `"body": "{\"id\":\"${created.id}\"}"`. Quote them so the result is valid JSON.

## 11. Rows from a sheet (Excel, Google Sheets, SharePoint) through sheet-service
**Verified** for a local `.xlsx` and a Google Sheet. SharePoint needs a one-time sign-in by a person.

sheet-service listens on `http://localhost:5090`. The path goes in `source`; credentials are never in the script. Row 1 is the header. Rows are numbered as in the sheet; columns are addressed by header name.

Read rows 2-21 (the `text` output is a tab-separated table, ready to paste):
```json
{ "stepNumber": 1, "action": "http.post", "description": "Read rows 2-21",
  "parameters": { "url": "http://localhost:5090/batch",
    "body": "{\"source\":{\"path\":\"C:/data/items.xlsx\",\"sheet\":\"Items\"},\"startRow\":2,\"endRow\":21}",
    "storeAs": "read" } }
```
Outputs: `${read.text}`, `${read.count}`, `${read.scanned}`, `${read.startRow}`, `${read.endRow}`, `${read.endOfData}`, `${read.nextStartRow}`.

Fill the next 2 rows that are not yet Approved/Rejected (one call, no loop needed):
```json
{ "stepNumber": 5, "action": "http.post", "description": "Process next 2 pending rows",
  "parameters": { "url": "http://localhost:5090/apply",
    "body": "{\"source\":{\"path\":\"C:/data/items.xlsx\",\"sheet\":\"Items\"},\"limit\":2,\"pending\":{\"column\":\"Approval_Status\",\"doneValues\":[\"Approved\",\"Rejected\"]},\"set\":{\"Approval_Status\":\"Approved\",\"Bot_Status\":\"SUCCESS\"}}",
    "storeAs": "apply" } }
```
Outputs: `${apply.updated}`, `${apply.text}`. `limit` and `set` are required and every `set` value is a string.

Write specific rows and columns in one call: `POST /update` with `{"source":{...},"rows":[{"row":5,"values":{"Bot_Status":"SUCCESS"}},{"row":6,"values":{"Bot_Status":"FAILED"}}]}`. Outputs `${upd.rows}`, `${upd.cells}`. Nothing is written if any row or column name is invalid.

`source.path` forms: local `C:/data/book.xlsx`; Google `https://docs.google.com/spreadsheets/d/<id>/edit` (a real Google Sheet, not an uploaded `.xlsx`, shared with the service account as Editor); SharePoint/OneDrive `https://<tenant>.sharepoint.com/...`. Use forward slashes in paths. A column name that does not match row 1 exactly fails with `column "X" not found in header`. A SharePoint 401 means the saved sign-in expired: a person must run `sheet-service login`; the script cannot fix it.

Show a result on screen: add `app.launch notepad.exe`, `delay`, then `notepad.type` with `${read.text}`.

## 12. Repeat something N times
There are no loops. Write the steps out N times (generate them in code if N is large), or use a set-based call (recipe 11). Number the steps continuously.

## 13. Paste a table into Excel
**Not run.** Excel splits tab-separated text across cells when it is pasted. The `text` from `/batch` is tab-separated, so: open Excel with the workbook (recipe 6), `delay` 5000, `notepad.type` with `windowTitle` `"Excel"` and the text (`notepad.type` pastes into any window it can match by title), or set the clipboard through a `notepad.type` into Notepad and copy it. Position the cursor first with `key Ctrl+Home` or `Ctrl+G` and a cell address. Verify on the target machine before relying on it.

---

## When a script misbehaves

| Symptom | Likely cause |
|---|---|
| Run succeeded but nothing happened | Keystrokes went to the wrong window, a dialog swallowed them, or the delay was too short. Add delays, use specific titles, look for confirmation dialogs. |
| Text appeared in the wrong Notepad window or tab | `"Notepad"` matched several windows. Use the file name or `"Untitled - Notepad"`. |
| Step failed right after a program started | Console program, document path or running browser used with `app.launch`. Use the Run dialog. |
| `UnknownAction:<name>` | The action does not exist, or the Host running the job is an older build. |
| `CONFIGURATION` on a step | A required parameter is missing or empty, or a number/boolean/coordinate is malformed. |
| Literal `${something}` in typed text | The variable was never defined, or the `storeAs` name differs. Run the validator. |
| `HTTP 4xx/5xx: ...` | The reply body after the colon says why. |
| Script has no steps / malformed | An envelope or `payload` was written by hand. Use the flat format. |
