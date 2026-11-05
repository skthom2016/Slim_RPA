---
name: tracto-desktop-rpa
description: Use when asked to create, edit, clean, review or validate a Tracto desktop RPA script (the JSON flow the Desktop Runtime executes on Windows): opening apps, typing, keyboard shortcuts, clicks, saving files, copying/moving/deleting files, calling APIs, reading Excel/Google/SharePoint sheets, moving data between apps. Covers a flow built from several desktop segments that hand values to each other, and a single-segment flow. Contains everything needed to write a correct script without reading the project's code or schema. A flow may also contain browser segments; this skill covers only how they join on, not the browser actions themselves. Not terminal runtimes; not for publishing or running.
---

# Tracto desktop RPA scripts

A script is one JSON file. It holds an ordered list of **segments**, and each segment holds an
ordered list of **steps**. The Desktop Runtime (Windows) runs a desktop segment's steps top to
bottom, driving the real desktop with keystrokes, mouse clicks and window activation, reading
text files and the clipboard, and calling HTTP APIs.

A flow can be:

- **several desktop segments**, each a stage of the same job, handing values forward;
- **one desktop segment**, which is the simplest case and may be written in the short flat form;
- **desktop and browser segments mixed**, where the browser part is written from its own
  reference (see "Browser segments" below).

You do not need to read the repository to write one. This skill is the reference.

Everything here was checked against the runtime code and, where marked **verified**, by running
it on a Windows 11 machine. Where something was not verified, it says so.

## How to work

1. Decide the **segments**: one per stage that can stand on its own. One segment is fine.
2. Break each segment into small desktop steps (open, wait, act, save, close).
3. For each step, pick the primitive in `references/actions.md`. For common jobs use the ready
   recipe in `references/recipes.md` (file copy/move/delete, save a file, open a PDF/Excel/URL,
   call an API, paste between apps, hand a value to the next segment).
4. If a step needs something the runtime cannot do, read `references/limits.md` and tell the
   developer plainly; do not invent an action.
5. Write the file with a file-writing tool (not a shell `echo` or heredoc: shells can halve the
   backslashes in Windows paths and silently produce `C:data` plus a line break). Start from a
   template in `references/templates/` when one fits.
6. Validate: `node .claude/skills/tracto-desktop-rpa/scripts/validate.js <file>.json`. Fix all
   `ERROR`s and read every `warn`.
7. Hand back the file with: what each segment and step does, what must be true beforehand (apps
   installed, folders exist, services up), and what to check after a run. Do not publish or run it.

## The format

```json
{
  "id": "invoice-run",
  "name": "Invoice Run",
  "version": "1.0.0",
  "params": { "inPath": "C:\\data\\in.txt", "outPath": "C:\\data\\out.txt" },
  "segments": [
    {
      "name": "Read the invoice",
      "runtime": "desktop",
      "steps": [
        { "stepNumber": 1, "action": "file.read", "description": "Read the file",
          "parameters": { "path": "${param.inPath}", "storeAs": "invoice" } }
      ]
    },
    {
      "name": "Write the summary",
      "runtime": "desktop",
      "steps": [
        { "stepNumber": 1, "action": "app.launch", "description": "Open Notepad",
          "parameters": { "executable": "notepad.exe" } },
        { "stepNumber": 2, "action": "delay", "description": "Wait for the window",
          "parameters": { "ms": "2000" } },
        { "stepNumber": 3, "action": "notepad.type", "description": "Type what was read",
          "parameters": { "text": "First line: ${param.invoice.firstLine}",
                          "windowTitle": "Untitled - Notepad" } }
      ]
    }
  ]
}
```

- Top level has exactly: `id`, `name`, `version`, optional `params`, `segments`.
  **There is no top-level `targetRuntime` and no top-level `steps`.** The runtime is named
  inside each segment.
- A segment has `runtime` (`desktop`, `browser` or `terminal`, lower case), `steps`, an optional
  `name`, and — **browser segments only** — optional `mode` and `profile`. No other fields.
- `id`: lowercase letters, digits and hyphens, not starting or ending with a hyphen.
  `version`: `1.0.0` style.
- Each step has `stepNumber` (1, 2, 3, no gaps, **restarting at 1 in every segment**), `action`,
  `parameters`, and optionally `description`. There is no `if`, `loop`, `onError`, `retry`,
  `timeout` or `label`. A `condition` field is accepted and **ignored** by every runtime; the
  Designer writes it, so leave it alone if it is there and never rely on it.
- **Every parameter value is a string**, including numbers and booleans: `"ms": "1500"`,
  `"overwrite": "true"`.
- `params` are defaults that a job can override. Read them in any segment as `${param.name}`.
- Save the file as `<id>_<version>.json`.
- Do not write a `{"schemaVersion","script"}` envelope or a `payload`: publishing adds those.
  The file's `id` and `version` must equal the artifact id and version it is published under.

### The short form, for one desktop segment

A one-segment flow may be written flat, which is what a recording produces:

```json
{
  "id": "open-notepad", "name": "Open Notepad", "version": "1.0.0",
  "targetRuntime": "desktop",
  "steps": [ { "stepNumber": 1, "action": "app.launch", "description": "Open Notepad",
               "parameters": { "executable": "notepad.exe" } } ]
}
```

It behaves exactly like a single segment. Use `segments` whenever there is more than one stage,
and never mix the two shapes in one file.

## Several desktop segments in one flow

Each segment is queued as its own run and starts only when the one before it succeeded. Split a
job when a stage is worth seeing, retrying or reading on its own:

- a stage produces a value the rest of the job needs, and you want it recorded;
- the stages are far apart in time or meaning (fetch, then process, then file);
- you want the Orchestrator to show which stage failed, with that stage's outputs.

Keep it as one segment when the steps only make sense together, because every boundary costs
you the two rules below.

**1. The next segment may run on a different machine.** A segment is handed to whichever
desktop worker claims it first; nothing pins consecutive segments to one worker. With a single
desktop worker they are the same machine in practice, but that is a property of the deployment,
not a promise of the platform.

**2. Only `storeAs` outputs cross the boundary.** Anything on the screen does not. An app the
previous segment left open, a window title, the clipboard, the focused control: none of it is
guaranteed. Each desktop segment must open what it needs and leave the machine in a state it
does not depend on later.

So a segment that opens Notepad and a segment that types into it belong **together**, in one
segment. A segment that reads a file and a segment that files the result elsewhere are a good
split, because all they share is a value.

## Passing a value between segments

A step publishes its outputs by carrying a `storeAs`:

```json
{ "stepNumber": 1, "action": "file.read", "description": "Read the note",
  "parameters": { "path": "C:\\data\\note.txt", "storeAs": "note" } }
```

How it is read depends on where from:

| Read from | Written as | Example |
|---|---|---|
| A later step in the **same** segment | `${name.field}` | `${note.firstLine}` |
| A later **segment** | `${param.name.field}` | `${param.note.firstLine}` |

The fields each action produces are in `references/actions.md` (`file.read` gives `text`,
`firstLine`, `lineCount`). Outputs flow **forward only**; a later segment cannot change an
earlier one. When two segments publish the same name, later segments see the later value.

To move text that is only on screen, copy it first: `key Ctrl+C`, then `clipboard.get` with a
`storeAs`.

## Browser segments

A flow may mix desktop and browser segments; the Orchestrator routes each segment to a worker of
its runtime and passes `${param...}` across exactly as above. That part is covered here. What is
**not** covered here is how to write a browser segment's own steps, which have their own actions
(`fill`, `click`, `getText`) and their own way of naming elements.

Write those from `tracto/browser-runtime/README.md` and its `samples/`, or from the browser skill
when it exists. This skill's validator checks a browser segment's structure and variable
references only.

Two differences that catch people out:

- A browser segment may carry `mode` (`headed` or `headless`) and `profile` (a signed-in browser
  folder on the worker). **Neither is valid on a desktop segment.**
- A browser step matches an element's name, label or placeholder **whole**, while a desktop
  `window` is matched as a **substring** of the title. The same wording means different things
  on either side of a segment boundary.

**Verified:** `notepad-to-bing` (a desktop segment that reads a Notepad file through the
clipboard, then a browser segment that searches Bing for that text) ran to `SUCCEEDED` on a test
Orchestrator with a desktop worker and a browser worker, and the first line of the note reached
the Bing search box as `${param.note.firstLine}`. A failing browser segment ended the run as
`FAILED` with the next segment never starting, and cancelling during a browser segment ended the
run as `CANCELLED`. See `references/recipes.md` (recipes 14 and 15) and
`references/templates/segmented-notepad-to-bing.json`.

## Core rules (the runtime behaves exactly like this)

1. **Straight line only.** Steps run in order. The first failed step stops the run, and the
   segments after it never start. There are no loops, conditions, branches or error handlers.
   For repetition, repeat the steps in the JSON, or use a set-based API call
   (`references/recipes.md`, "Rows from a sheet").
2. **Success does not mean it worked.** The runtime reports a step as succeeded when the
   keystrokes were sent. Many actions are silent when something is wrong (see rule 3), and
   dialogs can swallow keys. The run state alone proves nothing; design for observable effects
   (a saved file, a changed cell) and tell the developer how to check them.
3. **Silent no-ops.** `navigate` and `focus` succeed even when the window is not found. `type`,
   `key`, `click` and `scroll` with an unmatched `window` carry on and act on whatever window is
   active. Only `notepad.type` and `file.saveAs` fail when their window is missing. Put a
   `delay` after anything that opens a window, and use specific window titles.
4. **Window matching.** `window` is a case-insensitive **substring** of the title; the first
   matching window wins (Windows' own z-order enumeration, hidden windows included, except
   `click`, which matches visible windows only). `"Notepad"` matches every Notepad window and
   any window with Notepad in its title. Use the most specific title you can, for example the
   file name: `"report.txt - Notepad"`.
5. **Variables.** `${name}` in any parameter value is replaced before the step runs. Built-ins:
   `${now.iso}` (UTC), `${now.local}` (`2026-11-02 16:04:33`), `${now.date}` (`2026-11-02`),
   `${now.time}`; all but `now.iso` are the machine's local time. They are read when each step
   starts, so a name built from `${now.date}` in two steps can differ if the run crosses
   midnight. Substituted values are inserted as plain text and never expanded again, so braces in
   an API reply are safe. An unresolved `${...}` is **left as literal text**, not an error. The
   validator checks every reference.
6. **Only flat values.** `http.post` and `http.get` copy top-level string, number and boolean
   fields of a JSON reply into variables. Nested objects and arrays are not reachable.
   `file.read` and `clipboard.get` read a text file or the clipboard text into variables. A
   script still cannot read the screen.
7. **Timing is yours to manage.** Handlers wait a few hundred milliseconds internally, but
   opening windows, rendering documents and dialogs need explicit `delay` steps (`ms` up to
   30000). Typical: app start 1500-2000, document render 4000-6000, dialog 800-1000.
8. **Windows has one keyboard.** While a script runs, the machine should not be used by a
   person. Anything that steals focus (a Teams or Outlook notification, another window opening,
   a modal dialog such as Notepad's "save changes?" prompt) sends keystrokes to the wrong place
   or loses them. In testing, `Win+R` typed its command into an open Notepad document because a
   dialog and a notification held the focus. Resolve every dialog before the next step, and run
   on an idle, unlocked machine with notifications off.
9. **Notepad titles follow the content.** Win 11 Notepad's window title is `Untitled - Notepad`
   when empty, the first line of the text with a `*` while unsaved, and the file name once
   saved. Match on the right one for the moment (recipe 3).

## Which tool for the job

| I need to... | Use |
|---|---|
| Start a GUI app (`notepad.exe`, `explorer.exe`, `calc.exe`) | `app.launch` |
| Start a console tool, open a document, open a browser, run any command | Run dialog: `key {LWIN}r`, `delay`, `type`, `key Enter` (**verified**) |
| Put text into Notepad | `notepad.type` (clipboard paste) |
| Read a text file into variables | `file.read` |
| Read what was copied (Ctrl+C) in any app | `key Ctrl+C`, then `clipboard.get` |
| Use a value from an earlier step, same segment | `storeAs`, then `${name.field}` |
| Use a value from an earlier segment | `storeAs`, then `${param.name.field}` |
| Type short text into another app | `type` |
| Press a shortcut or special key | `key` |
| Save the open Notepad document to a path | `file.saveAs` |
| Create / copy / move / delete files and folders | Run dialog with `cmd /c ...` (**verified**) or File Explorer keystrokes (**verified**) |
| Call a REST API or sheet-service | `http.get` / `http.post` |
| Wait | `delay` |
| Switch to a window | `navigate` or `focus` |
| Click something | `click` with `window` + `offset`, only when no keyboard route exists |

## Files in this skill

- `references/actions.md`: all 14 actions with parameters, outputs, behaviour and traps.
- `references/recipes.md`: step-by-step patterns for common desktop jobs and for multi-segment
  flows, each marked verified or not.
- `references/limits.md`: what the runtime cannot do, workarounds, and what to ask the core team
  to add.
- `references/templates/*.json`: working scripts to copy, including
  `segmented-desktop-stages.json` (two desktop segments) and
  `segmented-notepad-to-bing.json` (desktop then browser).
- `scripts/validate.js`: offline validator (needs only Node) for segmented and flat files.
  `scripts/actions.json`: the desktop action table it uses.
