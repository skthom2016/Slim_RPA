# Building a step in the Designer

A script is an ordered list of steps. Each step has four things:

| | What it is | Required |
|---|---|---|
| **Description** | What the step does, in your words. Shown in the list. | Yes |
| **Action** | What it actually does: `click`, `fill`, `delay`, `app.launch`… | Yes |
| **Parameters** | Name and value pairs the action needs, such as `url` or `value` | Depends on the action |
| **Store result as** | A name for what the step produces, so later steps can read it | Only when passing data on |

Which actions are available depends on the segment's **runtime** — a browser segment offers
`fill` and `getText`, a desktop segment offers `app.launch` and `notepad.type`.

---

## Adding a step

1. Select the segment the step belongs to, in the **Segments** list on the left.
2. Press **+** above the step list. A step called "New step" appears at the end.
3. Press the **pencil** next to its name to rename it, then the **tick** to keep the change.
   Enter saves, Escape cancels.
4. Press the **chevron** on the right of the row to open the step's details underneath it.
5. Pick an **Action** from the dropdown. The list holds the actions that segment's runtime
   knows; you can type another name if you need one that is not listed.
6. Press **+** next to **Parameters**. A row appears with an empty name.
7. Pick the parameter from the **dropdown** on the left. It lists the ones this action takes,
   required first, and drops any the step already has, so you cannot add the same one twice.
   You can type a name that is not in the list for an action the catalogue does not know.
8. Fill in the value on the right.

Order does not matter. Parameters are looked up by name, so you can add them in any order.

Reorder steps with the up and down arrows, and remove one with the bin. Step numbers renumber
themselves.

---

## Parameters, by action

Every value is a string. Required parameters are in **bold**.

### Browser actions

Every action that works on an element needs **one** way of naming that element:

| Name it by | Use when |
|---|---|
| `role` + `name` | Best choice. `role: button`, `name: Send` |
| `label` | A form field with a visible label |
| `placeholder` | An input with placeholder text |
| `testId` | The page sets `data-testid` |
| `text` | Visible text, nothing better available |
| `css` | A CSS selector. Last resort, but the only option for some pages |
| `xpath` | When CSS cannot express it |

**Values are matched whole.** `name: "New"` finds the element named exactly `New`, not
`Newsletters`. If you name only part of a value the step fails as not-found rather than
guessing. If two elements match, the step fails rather than picking one — narrow it down.

| Action | Parameters | Produces |
|---|---|---|
| `navigate` | **url** | |
| `fill` | element, **value** | Replaces the field's contents |
| `type` | element, **value** | Types key by key. Use instead of `fill` for autocomplete fields |
| `press` | element, **key** (`Enter`, `Tab`, `Control+a`) | |
| `click`, `dblclick`, `hover`, `scroll` | element | |
| `check`, `uncheck` | element | Checkbox or radio |
| `select` | element, **value** | Option of a `<select>` |
| `wait` | element, `timeoutMs` | Waits until visible |
| `find` | element | Fails unless exactly one match |
| `getText`, `getValue` | element, `storeAs` | `value` |
| `getAttr` | element, **attribute**, `storeAs` | `value` |
| `assert` | **kind** (`visible`, `enabled`, `checked`, `urlEquals`, `textEquals`), **expected**, element | |
| `delay` | **ms** | |

### Desktop actions

| Action | Parameters | Produces |
|---|---|---|
| `key` | **key** (`^s`, `{ENTER}`, `%{F4}`, `{LWIN}r`), `window` | `sendKeysValue` |
| `type` | **value**, `window` | `charactersTyped` |
| `delay` | **ms** (up to 30000) | |
| `click` | **coordinates** (`"x,y"`), `window`, `offset`, `rightClick` | `x`, `y` |
| `scroll` | **coordinates**, `delta`, `window` | |
| `navigate` | **window** | `windowHandle` |
| `focus` | `window`, `target` | `windowHandle` |
| `app.launch` | **executable**, `args`, `timeoutMs`, `attachIfRunning` | `processId`, `windowTitle` |
| `notepad.type` | **text**, `windowTitle` | `charactersTyped` |
| `file.saveAs` | **filePath**, `windowTitle`, `overwrite`, `saveAsShortcut` | `savedPath`, `fileSizeBytes` |
| `file.read` | **path**, `maxChars` | `text`, `firstLine`, `lineCount` |
| `clipboard.get` | — | `text`, `firstLine` |
| `http.get` | **url**, `timeoutMs` | `statusCode`, `body`, `json` |
| `http.post` | **url**, `body`, `contentType`, `timeoutMs` | `statusCode`, `body`, `json` |

`window` is matched as **part** of the window title, so `report.txt - Notepad` works. This is the
opposite of the browser rule above.

---

## Passing data between steps

A step publishes nothing unless you give it a name under **Store result as**. Once you do, it
publishes the outputs listed in the tables above.

**Inside one segment**, a later step reads them without a prefix:

```
${note.firstLine}
```

**Across segments**, the Orchestrator copies a finished segment's outputs into the parameters of
the segments after it, so they are read as parameters:

```
${param.topResult.value}
```

Outputs only ever flow **forward**. A step cannot read a later one.

### Doing it in the Designer

1. Open the step that produces the data and type a name under **Store result as**. The line
   underneath tells you exactly what to write, for example
   *Later segments read it as `${param.topResult.value}`*.
2. Open the step that needs it, select the parameter you want to put it in, then click the value
   in **Data you can use here**. It is inserted at the end of that parameter.

The panel lists everything in reach, labelled by where it comes from: *Script parameter*,
*Earlier step*, or *Earlier segment*. If it says nothing is published, give an earlier step a
name first.

### Also available anywhere

| Reference | What it is |
|---|---|
| `${param.name}` | A script parameter, overridable when a job is submitted |
| `${now.iso}` | UTC timestamp |
| `${now.local}`, `${now.date}`, `${now.time}` | Machine local time |
| `${secret:NAME}` | Read from the worker's environment, never logged |

**Validate** reports any reference that nothing before it produces. Without that check the
runtimes leave the text as-is, so a typo reaches the live run as the literal `${param.whatever}`.

---

## Script parameters

The **Parameters** box on the left holds script-level defaults, one `name=value` per line:

```
query=Wonderla
notePath=C:\Users\TNT\Documents\out.txt
```

Steps read them as `${param.query}`. A job can override them when it is submitted, so the same
script runs against different inputs without being edited.

---

## Browser segment settings

A browser segment has its own settings above the step list:

- **Browser mode** — `headed` shows the window, `headless` hides it. Empty leaves the host's own
  setting in charge.
- **Browser session** — **Empty browser** starts with nothing signed in, and several such
  segments can run at once. **Signed-in browser** uses the one signed-in profile on the machine;
  sign it in from **Sign in** on the toolbar. Only one segment using it runs at a time.
- **Record in** and **Port** — which browser the Designer opens while recording.
- **Start URL** — where recording begins.

**Browser session also decides what recording opens.** Set it to **Signed-in browser** before
pressing Start, or recording opens an empty browser and the site asks you to log in again.

---

## What to check before publishing

- **Validate** — catches missing descriptions, unknown runtimes, bad step numbering,
  unresolved `${...}` references, and the step problems below.

### Step problems, caught before you run

A step shows these under its parameters as soon as they appear, and Validate repeats them:

| Reported | Example |
|---|---|
| A required parameter is missing or empty | `fill` with no `value` |
| The step names no element | a `click` with no `role`/`css`/`label` |
| The element is named more than one way | both `label` and `css`; only the first is used |
| A parameter the action does not use | `labl` instead of `label`, silently ignored |
| `name` without a `role` | `name` describes a `role` and does nothing alone |
| A result name on an action that produces nothing | `storeAs` on a `fill` |
| A number that is not one, or out of range | `ms: "soon"`, or a desktop delay over 30000 |
| An action the runtime is not known to have | a typo in the action name |

Errors stop a publish; the rest are warnings worth reading.
- **Run Test** — a simulation. It confirms the shape of the script, not that the real app
  behaves. A passing test does not mean the script works.
- Timing is yours to manage: put a `delay` after anything that opens a window or loads a page.
