# Migrating an existing UiPath flow into Tracto

How to take a flow that already runs in UiPath and end up with a Tracto script that runs as a
job. Companion to `UIPATH-TRACTO-CAPABILITY-MAPPING.md`, which lists what Tracto can and cannot
do; this document is about the conversion process itself.

---

## 1. What to export from UiPath

| Artifact | How to get it | Contents | Use it? |
|---|---|---|---|
| **Project folder** | Copy the folder: `project.json` + `Main.xaml` + any other `.xaml` | The source of truth: every activity, selector, variable, argument and expression | **Yes — this is the input** |
| **`.nupkg` package** | Studio → Publish → publish to a local folder | A ZIP containing exactly the above (rename to `.zip`, extract) | Yes — identical content; use when only a published package is available |
| **Project documentation** | Studio → Project Documentation (Markdown/PDF) | Human-readable outline of workflows and activities | Secondary: good for confirming intent, not sufficient to convert from |
| **Workflow Analyzer report** | Studio → Analyze Project | Rule violations | No |
| **Orchestrator export** | Orchestrator REST API | Processes, assets, credentials, queues, triggers | Only for the surroundings: credential assets → `${secret:NAME}`, triggers → `/admin/schedules` |
| **Verbose job logs** | Run the process with verbose logging | The activity sequence that actually executed, happy path only | Fallback when the project is unavailable, or to confirm which branch runs in practice |

A UiPath `.xaml` is Windows Workflow XML. Activities are elements (`TypeInto`, `Click`,
`NavigateTo`, `ExcelReadRange`); targets carry UiPath selectors
(`<wnd app='notepad.exe' …/>`, `<webctrl id='email' tag='INPUT'/>`); every value is a VB.NET
expression string. Classic and modern activity packs differ enough that the activity mapping
table must be built against the actual project, not from a generic list.

---

## 2. The governing principle

**If an activity does not need to see a screen, it should not drive one.**

Much of what UiPath does through activities is data work, not UI work. Such activities do not
convert into desktop steps — they convert into an HTTP call to a backend service, which removes
the fragility and often removes the control-flow problem at the same time.

| UiPath activity | Not this | This |
|---|---|---|
| `ExcelReadRange` + `ForEach row` | keystrokes in Excel | one `http.post`; the loop runs inside the service (capability mapping Table 2 #13) |
| `CopyFile` / `MoveFile` / `DeleteFile` | Run-dialog `cmd /c` | native file actions (Table 2 #15) |
| `SendOutlookMail` / SMTP | browser automation of Outlook web | an email endpoint |
| `ReadPDFText` | — | a PDF extraction action (Table 2 #14) |
| Queue items / transactions | — | a business-data queue (Table 2 #3) |
| `DateTime` / string expressions | — | computed by the caller and passed as a job param |

Two consequences worth stating plainly:

- A step moved into a backend service cannot be broken by a notification stealing focus. Desktop
  rule 8 (the machine has one keyboard) stops applying to it.
- The service is ordinary, testable backend code. Per unit of reliability it is far cheaper than
  keystroke choreography, and one endpoint serves every future flow, not only the migrated one.

**For desktop control targeting, capability-mapping Table 2 #23 is the highest-value item.**
Adding `System.Windows.Automation` selectors to the desktop runtime (`AutomationId` / `Name` /
`ControlType`) turns a UiPath desktop control-click into a direct translation instead of a
rewrite. Without it, every desktop click must be re-expressed as a keyboard route. With it, the
keyboard route becomes the fallback for apps that expose no automation tree.

---

## 3. The five options

### Option 1 — Skill only

A `tracto-uipath-migration` skill reads the `.xaml`, maps activities to Tracto actions, writes
the script, runs the existing `validate.js`, and reports what it could not carry.

- No product code.
- Fastest to stand up; copes with messy real projects.
- Each conversion is a judgement call, so every output needs review and a real run.

### Option 2 — Deterministic converter in code

A parser with a fixed activity table, shipped as a CLI or as "Import → UiPath project" beside the
existing JSON/YAML import in `ExportImportViewModel.cs`.

- Repeatable and auditable.
- Large build; brittle across UiPath versions; covers only the hard-coded subset.
- Does not help with VB expressions or desktop selector rewriting — the parts that are actually
  hard.

### Option 3 — Hybrid: deterministic extractor + skill

A small script flattens the XAML into a neutral intermediate file (ordered
`{activity, container, target, selector, text, expression}` plus variables, arguments, and an
unsupported list). The skill then authors the Tracto script from that intermediate rather than
from raw XAML.

- The extractor's only claim is **completeness** — that no activity is silently missed. It does
  not make the translation better.
- That claim matters at several hundred activities and is pure overhead at thirty. **It is a
  scale tool, not a quality tool.**
- Flattening nested Sequences and Flowcharts is easy; flattening control flow is not, and is
  answered by section 2 rather than by the extractor.

### Option 4 — Read the flow, state it in English, author from that

Claude reads the UiPath project and restates it as an explicit step list in plain language —
what is opened, what is typed, what is read, what is decided. That English statement is then
converted into a Tracto script, with each step resolved as either a UI step or a backend call per
section 2. Where Tracto cannot express something, the gap is named along with the service that
would close it.

- The English intermediate is reviewable by anyone, not just by whoever knows UiPath.
- It separates *what the flow does* from *how Tracto will do it*, which is where the real
  decisions are.
- It makes the limits explicit: an unmigratable step surfaces as a concrete recommendation
  ("this needs a Go endpoint that reads the sheet"), not as a silent omission.
- No human recording step, so it is not slow.

### Option 5 — Log mining

Run the UiPath process with verbose logging and parse the job log for the executed activity
sequence.

- Useful only when the project cannot be obtained, or to confirm which branch runs in practice.
- Captures one path through the flow, not the flow.

---

## 4. Recommended path

**Option 4 is the main route**, with Option 3's extractor added only when a project is large
enough that enumerating activities by hand becomes unreliable.

1. **Assess.** Read the project; produce the English step list plus a per-activity verdict:
   *direct*, *needs a backend service* (naming which capability-mapping row), or *not
   migratable*. This alone decides whether a given flow is worth migrating.
2. **Convert what is green.** Author the Tracto script, validate with the runtime skill's
   `validate.js`, publish the artifact, run the job, check the observable effect.
3. **Grow the skill from the conversions.** Each migration done by hand contributes a recipe
   row: a UiPath activity shape, the Tracto steps it became, and why. This is how the desktop and
   browser skills were built, and the rules in them that matter came from running things.
4. **Close the recurring gaps with services**, driven by which capability-mapping rows keep
   appearing in step 1. Expect Table 2 #23 (desktop UI Automation selectors), #13 (native Excel)
   and #15 (file operations) to dominate.

---

## 5. What does not cross

| UiPath | Tracto today | Resolution |
|---|---|---|
| `If`, `Switch`, `While`, `ForEach`, `Try/Catch`, `Retry Scope` | straight line only | move the iteration into a backend service, unroll the steps, or split into jobs. Never drop silently — a converted flow missing its `Try/Catch` looks correct and behaves differently |
| VB.NET expressions (`row("Email").ToString`, `DateTime.Now.AddDays(-1)`) | `${param.x}`, `${now.date}` | simple reads map directly; anything computed becomes a job parameter |
| Desktop control selectors | window title, keystrokes, pixel offsets | Table 2 #23, or a keyboard-route rewrite |
| Browser `webctrl` selectors | `role`/`label`/`placeholder`/`css` | mechanical: `webctrl id='email'` → `css: "#email"` |
| Credential assets | `${secret:NAME}`, browser profiles | clean mapping; set `TRACTO_SECRET_*` on the worker |
| Computer Vision, OCR, Document Understanding | none | capability mapping Table 3 — not closable by a backend service |
