# Tracto Browser Runtime

A worker that runs browser scripts with Playwright (Chromium). It registers with the
Orchestrator as `runtimeType=browser`, pulls one job at a time, drives a fresh, isolated
browser context for each run and reports the result.

Two projects:

| Project | Purpose |
|---|---|
| `src/TractoRuntime.Browser` | Library: actions, locators, contexts, dialogs, tabs, files, failure classification. |
| `src/TractoRuntime.Browser.Host` | Executable: Orchestrator client, worker loop, flat-script translation, run engine. |

## Start the worker

```powershell
cd tracto\browser-runtime\src\TractoRuntime.Browser.Host
dotnet run
```

On the first start the host downloads the Playwright-bundled Chromium (about 140 MB) into
`%LOCALAPPDATA%\ms-playwright`. Every machine therefore runs the same browser build.
To use an installed browser instead, set `BROWSER_CHANNEL=chrome` or `msedge`.

Settings come from environment variables, then `appsettings.json` (`Host` section), then defaults:

| Environment variable | Default | Meaning |
|---|---|---|
| `ORCHESTRATOR_URL` | `http://localhost:5080` | Orchestrator base URL |
| `WORKER_ID` | `browser-runtime-001` | Worker identity |
| `BROWSER_MODE` | `headed` | `headed` (visible window) or `headless` |
| `BROWSER_CHANNEL` | empty | empty = bundled Chromium; `chrome`, `msedge` = installed browser |
| `BROWSER_AUTO_INSTALL` | `true` | Install the bundled Chromium when missing |
| `BROWSER_VIEWPORT_WIDTH` / `_HEIGHT` | `1280` / `720` | Page size |
| `LOCATOR_WAIT_MS` | `15000` | How long a step waits for its target to appear |
| `BROWSER_ALLOW_CSS` | `true` | Allow `css` selectors in scripts |
| `PAUSE_AFTER_RUN_MS` | `3000` | Keep the window open after the last step (headed only) |
| `RUN_TIMEOUT_MS` | `600000` | Whole-run limit |
| `TRACTO_SECRET_<NAME>` | – | Value for `${secret:NAME}` in a script |

The Orchestrator reads `targetRuntime` from each published script and routes the job to a
worker of that runtime, so desktop and browser workers can run on the same Orchestrator.

## Script format

The same flat format as desktop scripts: `id`, `name`, `version`, `targetRuntime: "browser"`,
`steps[]` with `stepNumber`, `action`, optional `description` and string-only `parameters`.
Steps run in order; the first failure stops the run. `${name}` expands variables; a step with
`storeAs` exposes its outputs as `${name.key}`; `${now.iso}`, `${now.local}`, `${now.date}`,
`${now.time}` are built in; `${secret:NAME}` is read from the worker's environment and never logged.

### Target element

Every element action takes one of these parameters (tried in this order):

| Parameter | Matches |
|---|---|
| `role` (+ `name`) | ARIA role and accessible name, e.g. `role: button`, `name: Login` |
| `label` | Form label text |
| `placeholder` | Input placeholder |
| `testId` | `data-testid` attribute |
| `text` | Exact visible text |
| `css` | CSS selector (Playwright syntax; `>> nth=0` picks one of several matches) |
| `xpath` | XPath |

A selector that matches more than one element fails with `TARGET_AMBIGUOUS`; the runtime
never silently takes the first. Narrow it (`nth=0`, a parent, role + name).

### Actions

| Action (short name) | Parameters | Notes |
|---|---|---|
| `navigate` | `url`, optional target | Waits for DOM ready; with a target also waits for it |
| `fill` | target, `value` | Sets a field's value (clears first) |
| `type` | target, `value` | Types key by key; for autocomplete fields |
| `press` | `key`, optional target | `Enter`, `Tab`, `Control+a`; with a target the element is focused first |
| `click`, `dblclick`, `hover`, `scroll` | target | |
| `check`, `uncheck` | target | Checkbox / radio |
| `select` | target, `value` | Option value of a `<select>` |
| `wait` | target, `timeoutMs` | Until visible |
| `find` | target | Fails unless exactly one match |
| `getText`, `getValue` | target, `storeAs` | Output `${x.value}` |
| `getAttr` | target, `attribute`, `storeAs` | Output `${x.value}` |
| `assert` | `kind`, `expected`, target | `visible`, `enabled`, `checked`, `urlEquals`, `textEquals` |
| `delay` | `ms` | Fixed pause |

Full runtime names (`browser.page.navigate`, `browser.element.click`, …) are accepted too.
Every step accepts `timeoutMs`.

## Samples

`samples/` holds scripts verified against the runtime and `run-sample.ps1`, which publishes a
script and submits one job:

```powershell
cd tracto\browser-runtime\samples
.\run-sample.ps1 -File bing-search_1.0.0.json
$env:TRACTO_SECRET_DEMO_PASSWORD = 'SuperSecretPassword!'   # set where the worker runs
.\run-sample.ps1 -File login-demo_1.0.0.json
```

Observed while verifying:

- Google answers automated Chromium with a reCAPTCHA page (`/sorry/`). The runtime does not
  bypass CAPTCHAs, so Google cannot be scripted this way; Bing works.
- Pressing `Enter` immediately after `fill` on Bing's search box is swallowed while the page is
  still attaching its handlers. A `delay` of 500 ms before `press` fixes it.
- When a step fails the host logs the page URL and title and saves a screenshot under
  `bin/Debug/net10.0/evidence/<runId>-<stepId>.png`.

## Tests

```powershell
cd tracto\browser-runtime
dotnet test
```
