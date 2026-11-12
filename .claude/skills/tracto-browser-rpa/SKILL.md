---
name: tracto-browser-rpa
description: Use when asked to create, edit, clean, review or validate a Tracto browser segment or browser RPA script (the JSON the Browser Runtime executes with Playwright): opening pages, filling and typing into fields, clicking, selecting, checking, waiting, asserting, and reading text, values or attributes off a page. Covers signing in without putting a password in the script, through a signed-in browser profile or a secret on the worker. Also covers a flow that mixes browser and desktop segments and passes values between them; for the desktop segment's own actions use the tracto-desktop-rpa skill. Not terminal runtimes; not for publishing or running.
---

# Tracto browser RPA scripts

A script is one JSON file holding an ordered list of **segments**, each with an ordered list of
**steps**. The Browser Runtime runs a browser segment's steps top to bottom in Playwright
Chromium, naming elements by what they are rather than where they sit.

A flow can be:

- **one browser segment**, which may be written in the short flat form;
- **several browser segments**, each a stage of the same job;
- **browser and desktop segments mixed**, passing values between them. Write the desktop
  segments with the `tracto-desktop-rpa` skill; the shapes and the hand-off are identical.

Everything here was checked against `TractoRuntime.Browser.Host/StepTranslator.cs`, which
rejects a bad script before a browser opens. Where something was run on a live worker it says
**verified**.

## How to work

1. Decide the **segments**: one per stage that can stand on its own.
2. Decide how the site is reached: signed in through a profile, or not signed in at all. Read
   `references/credentials.md` before writing any sign-in step.
3. Break each segment into steps: navigate, wait, act, read.
4. For each step pick the action from the table in `references/recipes.md`, check its mandatory
   parameters in `references/actions.md`, and name its element by role, label or placeholder
   before reaching for CSS. Start from a recipe when one fits.
5. Write the file with a file-writing tool (not a shell heredoc: shells halve backslashes).
   Start from `references/templates/` when one fits.
6. Validate: `node .claude/skills/tracto-browser-rpa/scripts/validate.js <file>.json`.
   Fix all `ERROR`s and read every `warn`.
7. Hand back the file with: what each step does, what must be true beforehand (the profile
   signed in, secrets set on the worker), and what to check after a run. Do not publish or run it.

## The format

```json
{
  "id": "price-check",
  "name": "Price Check",
  "version": "1.0.0",
  "params": { "query": "Wonderla" },
  "segments": [
    {
      "name": "Search and read the first result",
      "runtime": "browser",
      "mode": "headed",
      "steps": [
        { "stepNumber": 1, "action": "navigate", "description": "Open Bing",
          "parameters": { "url": "https://www.bing.com/" } },
        { "stepNumber": 2, "action": "fill", "description": "Type the search",
          "parameters": { "placeholder": "Search the web", "value": "${param.query}" } },
        { "stepNumber": 3, "action": "delay", "description": "Bing swallows an Enter sent too early",
          "parameters": { "ms": "800" } },
        { "stepNumber": 4, "action": "press", "description": "Search",
          "parameters": { "placeholder": "Search the web", "key": "Enter" } },
        { "stepNumber": 5, "action": "getText", "description": "Read the first heading",
          "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "storeAs": "headline" } }
      ]
    }
  ]
}
```

- Top level: `id`, `name`, `version`, optional `params`, `segments`. **No top-level
  `targetRuntime` and no top-level `steps`.** The runtime is named inside each segment.
- A browser segment takes `runtime`, `steps`, optional `name`, and two of its own:
  **`mode`** (`headed` or `headless`, overriding the host's own setting) and **`profile`**
  (a signed-in browser folder; see `references/credentials.md`). Neither is valid on a desktop
  segment.
- Each step has `stepNumber` (restarting at 1 in every segment), `action`, `parameters`, and
  optionally `description`. A `condition` field is accepted and ignored.
- **Every parameter value is a string**, including numbers: `"ms": "800"`, `"limit": "5"`.
- Save the file as `<id>_<version>.json`. Do not write a `{"schemaVersion","script"}` envelope;
  publishing adds it.

A single browser segment may be written flat with `"targetRuntime": "browser"` and a top-level
`steps`, which is what a recording produces. It behaves exactly the same.

## Naming the element

Every action that works on an element needs exactly one way of naming it. They are tried in
this order, and the first that matches **exactly one** element wins:

| Parameter | Matches | Use when |
|---|---|---|
| `role` (+ `name`) | ARIA role and accessible name | First choice. `role: button`, `name: Send` |
| `label` | A form label, or `aria-label` | A field with a visible label |
| `placeholder` | Placeholder text | An input with no label |
| `testId` | `data-testid` | The page sets one |
| `text` | Visible text | Nothing better is available |
| `css` | CSS selector | Last resort. May be switched off by host policy |
| `xpath` | XPath | CSS cannot express it |

Four rules that decide whether a step works:

1. **Values are matched whole.** `name: "New"` finds the element whose accessible name is
   exactly `New`, not `Newsletters`. The same goes for `label`, `placeholder` and `text`.
   Naming only part of a value gives **not found**, not a lucky match.
2. **Two matches is a failure, not a choice.** The runtime never clicks the first of several.
   Narrow it with `role` + `name`, a `css` with `>> nth=0`, or a more specific value.
3. **`name` only works with `role`.** On its own it is rejected; it describes a role rather
   than standing alone.
4. **A generated id is not a selector.** Many apps rebuild ids per session
   (`MSG_66b5df4d3f8_SUBJECT`). Match the stable part: `css: "input[id$='_SUBJECT']"`.

## Getting values in, and passing them on

Identical to a desktop flow, so the two mix freely.

**Into the first segment:** `params` at the top of the file are the inputs and their defaults,
read anywhere as `${param.name}`. A job overrides any of them when it is submitted.

**Between steps and segments:** a step with `storeAs` publishes its outputs.

| Reading from | Written as | Example |
|---|---|---|
| A later step in the **same** segment | `${name.field}` | `${headline.value}` |
| A later **segment** | `${param.name.field}` | `${param.headline.value}` |

`getText`, `getValue` and `getAttr` each publish one field, `value`. `getTexts` publishes
`value` and also `count`, how many elements it read. Every other action publishes nothing, so a
`storeAs` on one does nothing.

Getting the prefix wrong does not fail the run: the reference is left in the page or the file
exactly as written. The validator catches it.

**Verified:** a browser segment read a Bing result heading with `getText` and `storeAs`, and a
desktop segment after it typed `${param.headline.value}` into Notepad and saved it.

## Signing in

**Never put a password in a script.** Two supported ways, both in
`references/credentials.md`:

1. **A signed-in profile** — a person signs in once with `dotnet run -- --login <profile>`, and
   the segment names that `profile`. The script holds no password. One run at a time per
   profile, and sign-ins expire.
2. **`${secret:NAME}`** — read from the environment variable `TRACTO_SECRET_NAME` on the worker
   machine, never logged, and the run is refused before a browser opens if it is not set. For
   sites that keep no cookie and demand a login every visit.

Without a profile every run gets an empty browser with nothing signed in, which is the right
choice for a search or any public page and lets several runs go at once.

## Core rules

1. **Straight line only.** Steps run in order; the first failure stops the run and the segments
   after it never start. No loops, conditions or error handlers, and a script never gets them.
   **To repeat for each row, write a small child script for one item** (the item arrives as
   `${param.<name>}`, the results leave as `storeAs`), and let a main job start a sheet-service
   batch that runs the child once per row. **A decision is made outside the script,** by choosing
   which published script runs. Read `references/loops-and-conditions.md` before writing
   anything that repeats or branches.
   **A repeat over any source of items (rows of a sheet, invoices in a PDF, files in a folder, records from an API) is a main job and a child job with a Go API between them.** If no API reads that kind of source yet, say so and create the matching API first; see `references/loops-and-conditions.md`, "Any other source of items". **A decision that depends on a value (if this, run that) is also a main job and a child job: the main job passes the values to a Go API and ends, and the API checks the condition and queues the appropriate child.** No branch API exists yet; if the task needs one, say so and create it first; see `references/loops-and-conditions.md`, "Conditions". Do not copy steps N times when N can change.
2. **A bad script is rejected before a browser opens.** An unknown action, a missing required
   parameter, a step with no element, a `name` without a `role`, a missing secret: all are
   refused up front, so the failure names the step rather than a timeout.
3. **Not found and ambiguous are both failures.** A locator that matches nothing fails with
   `TARGET_NOT_FOUND`; one that matches several fails rather than guessing.
4. **A page is slower than a script.** `navigate` waits for DOM ready, not for the page to
   settle. Put a `delay` or a `wait` on the element before acting. A known one: pressing Enter
   straight after `fill` on Bing is swallowed while the page attaches its handlers; an 800 ms
   `delay` fixes it (**verified**).
5. **`fill` replaces, `type` presses keys.** Use `type` when the page reacts as you type, such
   as an autocomplete or a recipient box; `fill` sets the value in one go and may open no
   suggestion list at all.
6. **A browser `delay` caps at 60000 ms**, where a desktop one caps at 30000.
7. **Only flat text comes back.** A read publishes a single `value`. There is no way to read a
   whole table into structured data; `getTexts` joins several elements into one string with a
   separator.
8. **Evidence on failure.** The host logs the page URL and title and saves a screenshot under
   `bin/Debug/net10.0/evidence/<runId>-<stepId>.png`. Look there first.
9. **Google refuses automated Chromium** with a CAPTCHA page. The runtime does not solve
   CAPTCHAs. Bing works.

## Which action for the job

| I need to... | Use |
|---|---|
| Open a page | `navigate` (absolute URL) |
| Put text in a field | `fill` |
| Type so the page reacts, for autocomplete | `type` |
| Press a key, with or without a field | `press` (`Enter`, `Tab`, `Control+a`) |
| Click, double-click, hover, scroll | `click`, `dblclick`, `hover`, `scroll` |
| Tick a box, choose from a `<select>` | `check` / `uncheck`, `select` |
| Wait for something to appear | `wait` |
| Check there is exactly one of something | `find` |
| Read text, a field value, an attribute | `getText`, `getValue`, `getAttr` |
| Read every matching element at once | `getTexts` (with `limit`, `separator`) |
| Check the page is as expected | `assert` (`visible`, `enabled`, `checked`, `urlEquals`, `textEquals`) |
| Wait a fixed time | `delay` |
| Use a value in a later desktop segment | `storeAs`, then `${param.name.value}` there |

## Files in this skill

- `references/actions.md`: every action with its mandatory and optional parameters, what it
  publishes, and its traps.
- `references/recipes.md`: worked patterns — a search, a sign-in either way, an autocomplete
  field, a form, reading a list, asserting, handing a value to a desktop segment — each marked
  verified or not.
- `references/loops-and-conditions.md`: how a loop ("for each row") and a decision ("if this, run
  that") are done in Tracto: not in the script, but by sheet-service running a child job per row
  from a main job that ends at once. Includes a verified browser child. What is verified and what
  is not built.
- `references/credentials.md`: signing in without a password in the script.
- `references/limits.md`: what the runtime cannot do, and what to ask for.
- `references/templates/*.json`: scripts to copy.
- `scripts/validate.js`: offline validator (needs only Node) for flat and segmented files.
  `scripts/actions.json`: the action table it uses.

For a desktop segment's own actions, window titles and file handling, use the
`tracto-desktop-rpa` skill.
