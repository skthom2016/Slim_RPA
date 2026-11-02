---
name: tracto-desktop-rpa
description: Use when asked to create, edit, clean, review or validate a Tracto desktop RPA script (the JSON flow the Desktop Runtime executes on Windows): opening apps, typing, keyboard shortcuts, clicks, saving files, copying/moving/deleting files, calling APIs, reading Excel/Google/SharePoint sheets, moving data between apps. Contains everything needed to write a correct script without reading the project's code or schema. Desktop flows only; not browser or terminal runtimes; not for publishing or running.
---

# Tracto desktop RPA scripts

A script is one JSON file: an ordered list of steps. The Desktop Runtime (Windows) runs them top to bottom, driving the real desktop with keystrokes, mouse clicks and window activation, and calling HTTP APIs. You do not need to read the repository to write one. This skill is the reference.

Everything here was checked against the runtime code and, where marked **verified**, by running it on a Windows 11 machine. Where something was not verified, it says so.

## How to work

1. Break the request into small desktop steps (open, wait, act, save, close).
2. For each step, pick the primitive in `references/actions.md`. For common jobs use the ready recipe in `references/recipes.md` (file copy/move/delete, save a file, open a PDF/Excel/URL, call an API, paste between apps).
3. If a step needs something the runtime cannot do, read `references/limits.md` and tell the developer plainly; do not invent an action.
4. Write the file with a file-writing tool (not a shell `echo` or heredoc: shells can halve the backslashes in Windows paths and silently produce `C:data` plus a line break). Start from a template in `references/templates/` when one fits.
5. Validate: `node .claude/skills/tracto-desktop-rpa/scripts/validate.js <file>.json`. Fix all `ERROR`s and read every `warn`.
6. Hand back the file with: what each step does, what must be true beforehand (apps installed, folders exist, services up), and what to check after a run. Do not publish or run it.

## The format

```json
{
  "id": "invoice-to-notepad",
  "name": "Invoice To Notepad",
  "version": "1.0.0",
  "targetRuntime": "desktop",
  "steps": [
    { "stepNumber": 1, "action": "app.launch", "description": "Open Notepad", "parameters": { "executable": "notepad.exe" } },
    { "stepNumber": 2, "action": "delay", "description": "Wait for the window", "parameters": { "ms": "1500" } }
  ]
}
```

- Top level has exactly these fields: `id`, `name`, `version`, `targetRuntime`, `steps`. No others.
- `id`: lowercase letters, digits and hyphens, not starting or ending with a hyphen. `version`: `1.0.0` style. `targetRuntime`: always `"desktop"`.
- Each step has `stepNumber` (1, 2, 3, no gaps), `action`, `parameters`, and optionally `description`. No other fields exist: no `if`, `loop`, `onError`, `retry`, `timeout`, `label`.
- **Every parameter value is a string**, including numbers and booleans: `"ms": "1500"`, `"overwrite": "true"`.
- Save the file as `<id>_<version>.json`.
- Write only this flat format. Do not write a `{"schemaVersion","script"}` envelope or a `payload`: the Designer builds those when publishing.

## Core rules (the runtime behaves exactly like this)

1. **Straight line only.** Steps run in order. The first failed step stops the run. There are no loops, conditions, branches, error handlers or sub-flows. For repetition, repeat the steps in the JSON, or use a set-based API call (`references/recipes.md`, "Rows from a sheet").
2. **Success does not mean it worked.** The runtime reports a step as succeeded when the keystrokes were sent. Many actions are silent when something is wrong (see rule 3), and dialogs can swallow keys. The run state alone proves nothing; design for observable effects (a saved file, a changed cell) and tell the developer how to check them.
3. **Silent no-ops.** `navigate` and `focus` succeed even when the window is not found. `type`, `key`, `click` and `scroll` with an unmatched `window` carry on and act on whatever window is active. Only `notepad.type` and `file.saveAs` fail when their window is missing. Put a `delay` after anything that opens a window, and use specific window titles.
4. **Window matching.** `window` is a case-insensitive substring of the title; the first matching window wins (the order is Windows' own z-order enumeration, hidden windows included, except `click`, which matches visible windows only). `"Notepad"` matches every Notepad window and also any window with Notepad in its title. Use the most specific title you can, for example the file name: `"report.txt - Notepad"`.
5. **Variables.** `${name}` in any parameter value is replaced before the step runs. A step with `"storeAs": "x"` exposes its outputs as `${x.field}`. Built-ins: `${now.iso}` (UTC), `${now.local}` (`2026-11-02 16:04:33`), `${now.date}` (`2026-11-02`), `${now.time}`; all but `now.iso` are the machine's local time. They are read when each step starts, so a name built from `${now.date}` in two steps can differ if the run crosses midnight. Substituted values are inserted as plain text and never expanded again, so braces in an API reply are safe. An unresolved `${...}` is **left as literal text**, not an error. The validator checks every reference.
6. **Only flat values.** `http.post` and `http.get` copy top-level string, number and boolean fields of a JSON reply into variables. Nested objects and arrays are not reachable. A script cannot read the screen, the clipboard or a file into a variable.
7. **Timing is yours to manage.** Handlers wait a few hundred milliseconds internally, but opening windows, rendering documents and dialogs need explicit `delay` steps (`ms` up to 30000). Typical: app start 1500-2000, document render 4000-6000, dialog 800-1000.
8. **Windows has one keyboard.** While a script runs, the machine should not be used by a person. Anything that steals focus (a Teams or Outlook notification, another window opening, a modal dialog such as Notepad's "save changes?" prompt) sends keystrokes to the wrong place or loses them. In testing, `Win+R` typed its command into an open Notepad document because a dialog and a notification held the focus. Resolve every dialog before the next step, and run on an idle, unlocked machine with notifications off.
9. **Notepad titles follow the content.** Win 11 Notepad's window title is `Untitled - Notepad` when empty, the first line of the text with a `*` while unsaved, and the file name once saved. Match on the right one for the moment (recipe 3).

## Which tool for the job

| I need to... | Use |
|---|---|
| Start a GUI app (`notepad.exe`, `explorer.exe`, `calc.exe`) | `app.launch` |
| Start a console tool, open a document, open a browser, run any command | Run dialog: `key {LWIN}r`, `delay`, `type`, `key Enter` (**verified**) |
| Put text into Notepad | `notepad.type` (clipboard paste) |
| Type short text into another app | `type` |
| Press a shortcut or special key | `key` |
| Save the open Notepad document to a path | `file.saveAs` |
| Create / copy / move / delete files and folders | Run dialog with `cmd /c ...` (**verified**) or File Explorer keystrokes (**verified**) |
| Call a REST API or sheet-service | `http.get` / `http.post` |
| Wait | `delay` |
| Switch to a window | `navigate` or `focus` |
| Click something | `click` with `window` + `offset`, only when no keyboard route exists |

## Files in this skill

- `references/actions.md`: all 12 actions with parameters, outputs, behaviour and traps.
- `references/recipes.md`: step-by-step patterns for common desktop jobs, each marked verified or not.
- `references/limits.md`: what the runtime cannot do, workarounds, and what to ask the core team to add.
- `references/templates/*.json`: working scripts to copy.
- `scripts/validate.js`: offline validator (needs only Node). `scripts/actions.json`: the action table it uses.
