# Browser recipes

Worked patterns. Each says whether it has been run on a live worker (**verified**) or only
checked against the runtime code (**not verified**).

Every parameter value is a string. Every step may add `timeoutMs`; only the four readers accept
a useful `storeAs`.

---

## Choosing the action

| The page asks you to... | Action | Mandatory beyond the element |
|---|---|---|
| Go somewhere | `navigate` | `url` (absolute) |
| Put a value in a field | `fill` | `value` |
| Type so the page reacts as you go | `type` | `value` |
| Press a key | `press` | `key` |
| Press a button or link | `click` | — |
| Open a menu, reveal something | `hover` | — |
| Tick or untick | `check` / `uncheck` | — |
| Pick from a `<select>` | `select` | `value` (the option value) |
| Wait for something to appear | `wait` | — |
| Prove exactly one thing matches | `find` | — |
| Read text / a field value / an attribute | `getText` / `getValue` / `getAttr` | `attribute` for `getAttr` |
| Read every match at once | `getTexts` | — |
| Check the page is right | `assert` | `kind`, plus `expected` for `urlEquals` and `textEquals` |
| Wait a fixed time | `delay` | `ms` |

The element itself is mandatory for every action in that table except `navigate`, `press`,
`delay`, and `assert kind=urlEquals`.

---

## 1. Search a public page and read a result (**verified**)

Ran to `SUCCEEDED` on a live browser worker; the heading reached a later desktop segment.

```json
{ "stepNumber": 1, "action": "navigate", "description": "Open Bing",
  "parameters": { "url": "https://www.bing.com/" } },
{ "stepNumber": 2, "action": "delay", "description": "Let the page settle",
  "parameters": { "ms": "2000" } },
{ "stepNumber": 3, "action": "fill", "description": "Type the search",
  "parameters": { "placeholder": "Search the web", "value": "${param.query}" } },
{ "stepNumber": 4, "action": "delay", "description": "Bing swallows an Enter sent too early",
  "parameters": { "ms": "800" } },
{ "stepNumber": 5, "action": "press", "description": "Search",
  "parameters": { "placeholder": "Search the web", "key": "Enter" } },
{ "stepNumber": 6, "action": "wait", "description": "Wait for the results",
  "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "timeoutMs": "20000" } },
{ "stepNumber": 7, "action": "getText", "description": "Read the first heading",
  "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "storeAs": "headline" } }
```

The 800 ms `delay` at step 4 is not padding. Pressing Enter straight after `fill` is swallowed
while the page is still attaching its handlers; this was observed on Bing.

Template: `templates/search-and-read.json`.

---

## 2. Reach a site you are already signed in to (**verified**)

A person signs the profile in once, by hand:

```powershell
cd tracto\browser-runtime\src\TractoRuntime.Browser.Host
dotnet run -- --login default https://outlook.office.com
```

Then the segment names it. No password anywhere in the script:

```json
{ "name": "Read the mail", "runtime": "browser", "profile": "default", "steps": [
  { "stepNumber": 1, "action": "navigate", "description": "Open the mailbox",
    "parameters": { "url": "https://outlook.office.com/mail/" } },
  { "stepNumber": 2, "action": "delay", "description": "Let the mailbox load",
    "parameters": { "ms": "6000" } }
] }
```

**Verified:** a profile signed in to Outlook by hand was still signed in on a later run, and the
mailbox opened without a login page.

Close the sign-in window before running anything: the folder is locked while a browser has it
open, and a run naming the same profile fails until it is free.

Template: `templates/signed-in-profile.json`.

---

## 3. Sign in where the site keeps no cookie (**not verified**)

Only for sites that present a login form every visit. Read `credentials.md` first.

```json
{ "stepNumber": 2, "action": "wait", "description": "Wait for the form",
  "parameters": { "label": "Username", "timeoutMs": "20000" } },
{ "stepNumber": 3, "action": "fill", "description": "Type the username",
  "parameters": { "label": "Username", "value": "${param.username}" } },
{ "stepNumber": 4, "action": "fill", "description": "Type the password",
  "parameters": { "label": "Password", "value": "${secret:PORTAL_PASSWORD}" } },
{ "stepNumber": 5, "action": "click", "description": "Sign in",
  "parameters": { "role": "button", "name": "Sign in" } },
{ "stepNumber": 6, "action": "wait", "description": "Wait for the dashboard",
  "parameters": { "text": "Balance", "timeoutMs": "30000" } }
```

Set `TRACTO_SECRET_PORTAL_PASSWORD` on the machine running the browser worker and restart it.
A missing secret stops the script before a browser opens, naming the variable to define.

Template: `templates/login-with-secret.json`.

---

## 4. A field that only reacts to real typing (**verified**)

A recipient box, a search-as-you-type list, any autocomplete. `fill` sets the value in one go
and often leaves the suggestion list closed, so the next step finds nothing.

```json
{ "stepNumber": 5, "action": "click", "description": "Put the caret in the To field",
  "parameters": { "css": "[aria-label='To']" } },
{ "stepNumber": 6, "action": "type", "description": "Type the recipient",
  "parameters": { "css": "[aria-label='To']", "value": "${param.recipient}" } },
{ "stepNumber": 7, "action": "delay", "description": "Let the suggestions appear",
  "parameters": { "ms": "3500" } },
{ "stepNumber": 8, "action": "press", "description": "Accept the recipient",
  "parameters": { "css": "[aria-label='To']", "key": "Enter" } }
```

**Verified** against Outlook: these four steps ran green where a single `fill` had produced no
suggestion list at all.

---

## 5. Fill an ordinary form (**not verified**)

```json
{ "stepNumber": 2, "action": "fill", "description": "Full name",
  "parameters": { "label": "Full name", "value": "${param.name}" } },
{ "stepNumber": 3, "action": "select", "description": "Country",
  "parameters": { "label": "Country", "value": "IN" } },
{ "stepNumber": 4, "action": "check", "description": "Accept the terms",
  "parameters": { "role": "checkbox", "name": "I accept the terms" } },
{ "stepNumber": 5, "action": "click", "description": "Submit",
  "parameters": { "role": "button", "name": "Submit" } }
```

`select` takes the option's **value attribute**, not the text you see. If the markup is
`<option value="IN">India</option>`, write `IN`.

---

## 6. Read a whole list in one step (**verified**)

`getTexts` is the one action where a target matching many elements is the point.

```json
{ "stepNumber": 7, "action": "getTexts", "description": "Read the first five headings",
  "parameters": { "css": "#b_results .b_algo h2", "limit": "5", "separator": " | ",
                  "storeAs": "headings" } }
```

`limit` caps how many (`"0"` means all), `separator` joins them (a newline by default).

It publishes **two** fields: `${headings.value}`, the joined text, and `${headings.count}`, how
many it read. **Verified:** this step read five Bing headings and published a `count` of `5`.

There is no way to get them back as a list; whatever consumes it has to split on the separator.

---

## 7. Check the page is what you think (**not verified**)

```json
{ "stepNumber": 9, "action": "assert", "description": "We landed on the inbox",
  "parameters": { "kind": "urlEquals", "expected": "https://outlook.office.com/mail/" } },
{ "stepNumber": 10, "action": "assert", "description": "The Send button is usable",
  "parameters": { "kind": "enabled", "role": "button", "name": "Send" } }
```

`urlEquals` is the only kind that needs no element. `urlEquals` and `textEquals` need
`expected`; `visible`, `enabled` and `checked` do not. All comparisons are exact.

A failing assert fails the run, which is the point: use one before anything irreversible.

---

## 8. Hand a value to a desktop segment (**verified**)

The browser reads, the desktop writes. The only thing that crosses is a `storeAs` output.

```json
{ "name": "Search", "runtime": "browser", "steps": [
  { "stepNumber": 3, "action": "getText", "description": "Read the first heading",
    "parameters": { "css": "#b_results .b_algo h2 >> nth=0", "storeAs": "topResult" } }
] },
{ "name": "Write it down", "runtime": "desktop", "steps": [
  { "stepNumber": 3, "action": "notepad.type", "description": "Type what was found",
    "parameters": { "text": "Top result: ${param.topResult.value}",
                    "windowTitle": "Untitled - Notepad" } }
] }
```

`topResult` was published in an earlier segment, so it carries `param.`. Inside its own segment
it would be `${topResult.value}`.

**Verified:** a Bing heading reached Notepad this way and was saved to disk.

Template: `templates/browser-then-desktop.json`. Write the desktop steps with the
`tracto-desktop-rpa` skill.

---

## 9. Name an element that has no good name (**verified**)

Many apps generate ids per session, so the id itself is useless but its ending is stable.

```json
{ "parameters": { "css": "input[id$='_SUBJECT']" } }
```

**Verified** against Outlook, whose subject box is `MSG_66b5df4d3f8_SUBJECT`: the middle changes
every time the window opens, the `_SUBJECT` ending does not.

Order of preference, best first: `role` + `name`, `label`, `placeholder`, `testId`, `text`, then
`css`. A `css` that leans on layout (`div > div:nth-of-type(1)`) breaks on the next redesign.

---

## 10. When a step says the element was not found (**verified**)

Almost always one of three things.

1. **You named part of a value.** Matching is whole-value. `name: "New"` does not match a button
   named `New mail`. Write the whole accessible name.
2. **You used `name` without `role`.** On its own it is rejected; it describes a role.
3. **The page had not finished.** `navigate` waits for DOM ready, not for the app to render. Put
   a `wait` on the element you need, not a guess at a `delay`.

**Verified** on Outlook: `role: button, name: "New"` once matched three elements (`New`,
`Newsletters`, `Expand to see more New options`) and failed as ambiguous. The whole name fixed it.

When a step does fail, the host logs the page URL and title and saves a screenshot under
`bin/Debug/net10.0/evidence/<runId>-<stepId>.png`. Look at the screenshot first.

---

## 11. Repeating something

There is no loop. Write the steps again for each item, or read them all at once with `getTexts`
and let whatever consumes the result split it. A page of fifty rows cannot be walked by a
script; that is a limit, not a gap to work around. See `limits.md`.

---

## When a script misbehaves

| Symptom | Likely cause |
|---|---|
| `TARGET_NOT_FOUND` | Part of a value instead of the whole; `name` without `role`; the page had not rendered. |
| Ambiguous / matched several | Two elements share that name. Narrow with `role` + `name`, or `css` with `>> nth=0`. |
| A site asks to sign in although a profile is named | The sign-in expired, or the sign-in browser is still open and holding the folder. Run `--login` again. |
| The run is refused before a browser opens | A missing secret, an unknown action, a relative `url`, a step with no element. The message names the step. |
| Enter did nothing after `fill` | The page had not attached its handlers. Put a short `delay` before `press`, or use `type`. |
| A suggestion list never appeared | `fill` set the value without keystrokes. Use `type`. |
| `${param.x.value}` appears literally in the page or a file | The name is not published, or the prefix is wrong: no `param.` inside the same segment, `param.` from a later one. |
| A CAPTCHA page | Google blocks automated Chromium. Nothing can be done; use a site that does not. |
