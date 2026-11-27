# Browser actions

Created: 2026-11-06 | Last updated: 2026-11-27

Every parameter value is a **string**. Every action also accepts `timeoutMs` (a positive whole
number, how long to wait for its element) and `storeAs` (a name for what it publishes, though
only the readers and the two file actions publish anything).

Actions that work on an element need exactly one way of naming it: `role` (+ `name`), `label`,
`placeholder`, `testId`, `text`, `css` or `xpath`. See "Naming the element" in `SKILL.md`.

Each short name below is what you write. The runtime also accepts the long form
(`browser.element.click`) and a `browser.` prefix on the short one; they mean the same thing.

### Mandatory at a glance

Write these or the script is refused before a browser opens. "An element" means exactly one of
`role` (+`name`), `label`, `placeholder`, `testId`, `text`, `css`, `xpath`.

| Action | You must give |
|---|---|
| `navigate` | `url`, absolute |
| `delay` | `ms` |
| `press` | `key` |
| `fill`, `type` | an element, `value` |
| `select` | an element, `value` (the option value) |
| `getAttr` | an element, `attribute` |
| `getTexts` | an element |
| `upload` | an element (the file field or its button), `path` |
| `click`, `dblclick`, `hover`, `scroll`, `check`, `uncheck`, `wait`, `find`, `getText`, `getValue` | an element |
| `assert` | `kind`; an element unless `kind` is `urlEquals`; `expected` for `urlEquals` and `textEquals` |

Everything else is optional. `timeoutMs` works on any action; `storeAs` only publishes anything
on `getText`, `getValue`, `getAttr`, `getTexts`, `download` and `upload`.

### The full table

| Action | Needs an element | Required | Optional | Publishes |
|---|---|---|---|---|
| `navigate` | no | `url` | `timeoutMs` | |
| `wait` | **yes** | | `timeoutMs` | |
| `find` | **yes** | | `timeoutMs` | |
| `fill` | **yes** | `value` | `timeoutMs` | |
| `type` | **yes** | `value` | `timeoutMs` | |
| `select` | **yes** | `value` | `timeoutMs` | |
| `check` | **yes** | | `timeoutMs` | |
| `uncheck` | **yes** | | `timeoutMs` | |
| `click` | **yes** | | `timeoutMs` | |
| `dblclick` | **yes** | | `timeoutMs` | |
| `hover` | **yes** | | `timeoutMs` | |
| `scroll` | **yes** | | `timeoutMs` | |
| `press` | optional | `key` | `timeoutMs` | |
| `assert` | depends on `kind` | `kind` | `expected`, `timeoutMs` | |
| `getText` | **yes** | | `timeoutMs` | `value` |
| `getValue` | **yes** | | `timeoutMs` | `value` |
| `getAttr` | **yes** | `attribute` | `timeoutMs` | `value` |
| `getTexts` | **yes** | | `limit`, `separator`, `timeoutMs` | `value`, `count` |
| `download` | optional | | `folder`, `expectedFileName`, `contentType`, `maxSizeBytes`, `stampName`, `timeoutMs` | `value`, `path`, `fileName`, `size` |
| `upload` | **yes** | `path` | `timeoutMs` | `value`, `path` |
| `delay` | no | `ms` | | |

### `ms`, `timeoutMs`, `limit` and `maxSizeBytes` must be literal numbers

These are read while the script is validated, which happens before any `${...}` is
substituted. A variable in one of them is never a number by the time it is read, so the run is
rejected before a browser opens:

```
Run ...: script rejected — step.7: limit must be a non-negative integer.
```

Write the number in the step. A desktop segment is the other way round — it expands variables
per step, before the handler parses them — so `"ms": "${param.wait}"` works there and not here.
The validator in this skill reports it as an error.

---

## navigate

Goes to a page. `url` must be absolute (`https://...`); a relative one is refused before the
browser opens. Waits for DOM ready — **not** for the page to finish settling, so follow it with
a `delay` or a `wait` on the element you need.

```json
{ "stepNumber": 1, "action": "navigate", "description": "Open the portal",
  "parameters": { "url": "https://portal.example.com/login" } }
```

Aliases: `goto`.

## wait, find

`wait` holds until the element is visible. `find` fails unless exactly one element matches, which
makes it a cheap guard before a run does anything irreversible.

```json
{ "stepNumber": 3, "action": "wait", "description": "Wait for the results",
  "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "timeoutMs": "20000" } }
```

## fill, type

Both put text in a field, and the difference matters.

- **`fill`** clears the field and sets the value in one go. Fast, and right for an ordinary box.
- **`type`** presses the keys one at a time. Use it whenever the page reacts as you type: an
  autocomplete, a search-as-you-type list, a recipient box. `fill` often leaves such a list
  closed, and the step after it then finds nothing.

```json
{ "stepNumber": 2, "action": "type", "description": "Type the recipient",
  "parameters": { "css": "[aria-label='To']", "value": "${param.recipient}" } }
```

A password goes in as `"value": "${secret:NAME}"`, never as text. See `credentials.md`.

## press

Sends a key. With an element it focuses that element first; without one it goes to the page.
Keys are written as `Enter`, `Tab`, `Escape`, `Control+a`, `Shift+Tab`.

```json
{ "stepNumber": 5, "action": "press", "description": "Submit",
  "parameters": { "placeholder": "Search the web", "key": "Enter" } }
```

Aliases: `key`.

## click, dblclick, hover, scroll, check, uncheck, select

All act on one element. `select` takes the option's **value**, not its visible label:

```json
{ "stepNumber": 6, "action": "select", "description": "Choose the country",
  "parameters": { "label": "Country", "value": "IN" } }
```

## getText, getValue, getAttr, getTexts

The only actions that publish anything. Each publishes one field, `value`.

- `getText` — the text the element displays.
- `getValue` — what a form field currently holds.
- `getAttr` — one attribute, named with `attribute`.
- `getTexts` — the text of **every** element the target matches, joined. `limit` caps how many
  (`"0"` means no cap) and `separator` sets what joins them (a newline by default). This is the
  one action where a target matching several elements is intended rather than an error. It
  publishes two fields: `value`, the joined text, and `count`, how many elements it read
  (**verified**: a Bing search published `headings.count` of `5` alongside `headings.value`).

```json
{ "stepNumber": 7, "action": "getText", "description": "Read the first heading",
  "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "storeAs": "headline" } },
{ "stepNumber": 8, "action": "getTexts", "description": "Read all the headings",
  "parameters": { "css": "#b_results .b_algo h2", "limit": "5", "separator": " | ",
                  "storeAs": "headings" } }
```

Later in the same segment: `${headline.value}`. In a later segment: `${param.headline.value}`.

## assert

Checks the page is as expected and fails the run if not. `kind` is one of:

| `kind` | Needs an element | Needs `expected` |
|---|---|---|
| `visible` | yes | no |
| `enabled` | yes | no |
| `checked` | yes | no |
| `textEquals` (or `text`) | yes | yes |
| `urlEquals` (or `url`) | **no** | yes |

```json
{ "stepNumber": 9, "action": "assert", "description": "We landed on the inbox",
  "parameters": { "kind": "urlEquals", "expected": "https://outlook.office.com/mail/" } }
```

Comparisons are exact. Only these five technical checks exist; there is no "greater than", no
arithmetic and no business rule, and anything of that sort is rejected at validation.

## download

Clicks the element and saves the file the page sends. The saved name is the site's name with the
local date and time added, down to the second: `donations.csv` becomes
`donations_2026-11-27_14-14-21.csv`, so a later download never replaces an earlier one (a name
already taken in the same second gets `_2`, `_3`, ...). Read the final name from
`${x.path}` / `${x.fileName}`, never by guessing it. `"stampName": "false"` keeps the site's
own name and replaces a file of that name.

**Always give `download` the element whose click starts the file**: the link, the button, or the
"Download" item of a menu. When the file sits behind a menu or a preview, open them with ordinary
`click` steps and let the `download` step do the last click. Without an element the step only
starts listening once the previous step has finished, so a download that the previous `click`
already started can be missed and the step times out.

- `folder`: where to save. A full path (`D:\finance\portals`), or a name taken inside the
  worker's first file root (`givewell` saves into `<root>\givewell`). Without `folder` the file
  goes into the first file root itself. The folder is created when missing.
- The worker only writes inside its **file roots**, set with `BROWSER_FILE_ROOTS` (folders
  separated by `;`, default the Downloads folder of the user running the worker). A folder
  outside them fails the step before any byte is written.
- `expectedFileName`, `contentType` (`text/csv`, `application/pdf`, ...; checked from the file's
  extension) and `maxSizeBytes`: optional checks. A file that fails one is deleted and the step
  fails. `expectedFileName` is compared with the site's name, before the date and time are added.
- `stampName`: `"true"` (default) or `"false"`, see above.
- `timeoutMs`: how long to wait for the download to start (default 30000).
- Publishes `path` (the full path of the saved file, also the step's `value`), `fileName` and
  `size` (bytes).

```json
{ "stepNumber": 6, "action": "download", "description": "Download this month's donations",
  "parameters": { "role": "button", "name": "Download report", "folder": "givewell",
                  "contentType": "text/csv", "timeoutMs": "60000", "storeAs": "report" } }
```

Later in the same segment: `${report.path}`. In a later segment: `${param.report.path}`.

## upload

Puts a file from disk into a page's file field. The element is the `<input type="file">` itself
or the button that opens the file picker; both work. `path` is the full path of the file, and it
must be inside the worker's file roots (see `download`). A variable is fine:
`"path": "${report.path}"` uploads the file an earlier `download` saved. Publishes `path`.

Uploading only fills the field. Press the page's own submit or import button with a `click`
afterwards.

```json
{ "stepNumber": 4, "action": "upload", "description": "Choose the import file",
  "parameters": { "label": "Import file", "path": "D:\\finance\\donations\\import.csv" } }
```

## delay

Waits a fixed time. `ms` is a whole number of milliseconds, **capped at 60000** (a desktop
delay caps at 30000). Prefer `wait` on the element you actually need; use `delay` for the cases
where there is nothing to wait for, such as a page that re-attaches its handlers after load.

Aliases: `sleep`.
