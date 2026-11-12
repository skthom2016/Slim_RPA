# Loops and conditions

A Tracto script has no `if`, no loop and no retry, and one will never be added to it. Do not write one, and do not imitate one by copying steps (see "Why not copy the steps"). A script is a straight line; repetition and decisions live outside it, in Go. This file says where, and how to build them.

## The rule

- **Inside a script:** steps run top to bottom, once. The first failure stops the run. The `condition` field exists in the format but every runtime ignores it, so writing one changes nothing.
- **A loop ("for each row"):** a short **main job** asks the Go service (sheet-service, port 5090) to start a batch, then ends. For any source other than a sheet, the same shape needs a matching Go API; see "Any other source of items". sheet-service queues a **child job** for each pending row, one at a time, waits for it, and writes its results back into the row.
- **A decision ("if this, run that"):** made outside the script, by a Go API that checks the condition and queues the appropriate child. See "Conditions".

The main job is not the loop and does not wait for it. It only starts it.

## Loop: one child job per row

```
Main job (desktop)  --POST /process/start-->  sheet-service (Go, 5090)
 ends in seconds                                for each row whose Status is not Done, in order:
                                                  queue the child job with that row's value(s)
                                                  wait for it to end
                                                  write its outputs, Status and Message into the row
```

Build three things.

**1. A workbook** (or Google Sheet / SharePoint file) with one row per item. Row 1 is the header. It needs the input column(s), one column for each value the child returns, a `Status` column and, usefully, a `Message` column. Nothing else.

| Id | InvoiceItem | Total | Date | Status | Message |
|---|---|---|---|---|---|
| 494559 | | | | | |
| 815068 | | | | | |

**2. A child script**: one unit of work, for one item. It receives the row's value as a parameter, returns values with `storeAs`, and never touches the sheet. It is published on its own. This one is a browser child (verified):

```json
{
  "id": "acme-child-fetch-invoice",
  "name": "ACME Child: Fetch One Invoice",
  "version": "1.0.0",
  "params": { "invoiceId": "349236" },
  "segments": [
    {
      "name": "Search one invoice on ACME",
      "runtime": "browser",
      "profile": "default",
      "steps": [
        { "stepNumber": 1, "action": "navigate", "parameters": { "url": "https://acme-test.uipath.com/invoices/search" } },
        { "stepNumber": 2, "action": "wait", "parameters": { "css": "#invoiceNumber", "timeoutMs": "15000" } },
        { "stepNumber": 3, "action": "fill", "parameters": { "css": "#invoiceNumber", "value": "${param.invoiceId}" } },
        { "stepNumber": 4, "action": "click", "parameters": { "role": "button", "name": "Search" } },
        { "stepNumber": 5, "action": "wait", "parameters": { "css": "table.table td:nth-child(3)", "timeoutMs": "15000" } },
        { "stepNumber": 6, "action": "getText", "parameters": { "css": "table.table td:nth-child(3)", "storeAs": "invItem" } },
        { "stepNumber": 7, "action": "getText", "parameters": { "css": "table.table td:nth-child(4)", "storeAs": "invTotal" } },
        { "stepNumber": 8, "action": "getText", "parameters": { "css": "table.table td:nth-child(5)", "storeAs": "invDate" } }
      ]
    }
  ]
}
```

The `params` default is only used when the child is run on its own; the batch overrides it for every row. A child can equally be a desktop script.

**3. A main script**: one `http.post` step. It is a desktop script because only the desktop runtime has `http.post`. Give it `params` so the workbook path can be overridden when the job is submitted:

```json
{ "stepNumber": 1, "action": "http.post", "description": "Start the batch; this job then ends",
  "parameters": {
    "url": "http://localhost:5090/process/start",
    "body": "{\"source\":{\"path\":\"${param.excelPath}\",\"sheet\":\"${param.sheetName}\"},\"child\":{\"artifactId\":\"acme-child-fetch-invoice\",\"version\":\"1.0.0\"},\"params\":{\"invoiceId\":\"Id\"},\"outputs\":{\"invItem.value\":\"InvoiceItem\",\"invTotal.value\":\"Total\",\"invDate.value\":\"Date\"},\"statusColumn\":\"Status\",\"messageColumn\":\"Message\",\"timeoutSeconds\":180}",
    "storeAs": "batch" } }
```

### The request

| Field | Meaning |
|---|---|
| `source` | The sheet: `path` and `sheet` (tab name). Forward slashes in the path. |
| `child` | `artifactId` and `version` of the published child script. |
| `params` | Child parameter name -> the column that supplies it. The child reads `${param.<name>}`. |
| `outputs` | Optional. Name the child published -> the column it is written to. Leave it out for a child that only does something, such as sending an email: its row still gets `Status`. |
| `statusColumn` | Holds `Done` or `Failed` per row. A row is pending until it holds a `doneValues` entry (default `["Done"]`). Default column name `Status`. |
| `messageColumn` | Optional. Says why a row failed; on success it is cleared, or set to `successMessage`. |
| `successMessage` | Optional, needs `messageColumn`. Text written there when a row succeeds, for example `Message sent`. |
| `timeoutSeconds` | How long one child may run before it is cancelled. Default 300. |

**Output names are `storeAs` plus the field.** A `getText` stored as `invItem` publishes `invItem.value`, so the request says `"invItem.value"`. `getTexts` publishes `.value` and `.count`. A `http.post` publishes `.statusCode`, `.body` and the top-level fields of a JSON reply.

The reply is `{"batchId": "...", "state": "Running", "total": 9}`, readable in a later step as `${batch.batchId}`.

### What it does, row by row

- Rows run **one at a time, in sheet order**.
- A child that succeeds but lacks a requested output is recorded `Failed`, never written half-empty.
- A child that fails or overruns `timeoutSeconds` is recorded `Failed`; an overrunning child is cancelled so it does not hold the worker. `Message` carries the runtime's own error line.
- **A failed row does not stop the others.**
- **Run the batch again to retry:** only rows whose `Status` is not `Done` are processed.
- Every child is queued with the batch id as its `businessJobId`, so a batch's runs can be traced together in the Orchestrator.

### Following and stopping a batch

```
GET  http://localhost:5090/process/<batchId>          state, counts, each row with its child run id and message
POST http://localhost:5090/process/<batchId>/cancel   cancels the child in flight and stops
```

Batch states: `Running`, `Completed`, `CompletedWithErrors`, `Cancelled`, `Stopped` (gave up; see its message), `Interrupted` (sheet-service restarted mid-batch; start it again to carry on).

**The main job succeeding means the batch started, not that it finished.** The result is in the batch, not in the main job's state. Anything that needs to know the outcome reads `GET /process/<batchId>` or the sheet.

### Rules to follow

- **Publish the child first.** Starting a batch for an unpublished child is refused (`400`), so the main job fails with the reason.
- **No job may start another job and wait for it. Only Go waits.** Workers run one job at a time. A job that waits for another holds its worker, and if the job it waits for needs the same kind of worker, the run hangs for ever with no error. This applies to the main job and to every child alike: a main job that waited for its children, or a child that called Go to start a sub-child and waited for it, deadlocks the same way (a chain of waiting jobs holds one worker per link). The main job therefore starts the batch and ends at once, and a child never starts another job. Go (sheet-service) waits for each job, and it holds no worker while it does.
- **One batch per sheet.** A second start on the same sheet is refused (`409`, with the running `batchId`).
- **Children run sequentially.** A signed-in browser profile can only be used by one run at a time, so do not run a second browser worker on the same profile to speed it up.
- **A child must be safe to run again**, because a retry runs it again for the rows that failed.
- **Every column named in the request must be in row 1, and none may be used twice** (as a param, an output, the status or the message column). Otherwise the start is refused.
- **Close the workbook in Excel.** A result that cannot be written stops the batch (`Stopped`) rather than losing work.
- sheet-service needs to reach the Orchestrator (default `http://127.0.0.1:5080`, setting `SHEET_SERVICE_ORCHESTRATOR_URL`). Three rows in a row that cannot reach it stop the batch.

Full API: `tracto/sheet-service/README.md`, section "Running a child job for each row".

### Changing the child later

The main job does not hold a job id. It names the child by **artifact id and version**, written as text in its `http.post` body (`"child":{"artifactId":"...","version":"1.0.0"}`); sheet-service queues a new run of that published script for each row.

- A published version can never be replaced with different content: publishing it again returns `VERSION_CONFLICT`. A changed child therefore needs a **new version number** (`1.0.1`), published under the same id.
- The main job keeps running the old child until its body names the new version. That edit changes the main script, so it gets a new version too.
- So a change to the child is three edits: bump and publish the child; point the main body at the new child version; bump and publish the main. Keep the id the same.
- A version of `latest` is not supported; the version is required.

### A child that returns nothing

`outputs` is optional. A child that only does something, such as sending an email, returns no value, and its row still gets `Status`. To show something in the sheet, set `messageColumn` and `successMessage` (for example `"Message sent"`); that text is written to the message column when the row succeeds. `successMessage` is a fixed text, not something the child read from the page.

### Any other source of items: build the API that calls the child

`/process/start` reads the items from a sheet. The pattern is not tied to sheets: the items could be the invoice numbers in a PDF, the files in a folder, the records an API returns, or a list in a database. **Whenever a task has to do something once per item, build it as a main job and a child job, with a Go API in between.** Never loop inside the script, and never copy steps.

1. **Is there an API that already reads this kind of source?** Today only sheets are covered (`/process/start`: `.xlsx`, Google Sheets, SharePoint/OneDrive). If the items are in a sheet, use it.
2. **If not, a matching API has to be created before the flow can be written.** Tell the developer this plainly, name the source, and add the API as a piece of work of its own (a Go endpoint, in sheet-service or another Go service). Do not work around it with a script. A PDF with many invoices needs an endpoint that extracts the invoice ids and then runs the child once per id.
3. **Then write the child and the main job as usual.** The main job is the same single `http.post` to the new endpoint, and the child takes one item as `${param.<name>}`.

What every such API must do, so that it behaves like `/process/start`:

- **Read the items once, at start, in Go.** The script never sees the whole list.
- **Refuse a request that cannot run, before anything starts:** the child is not published (`400`), a named field or column is missing, or a batch is already running on the same source (`409`).
- **Reply at once** with a batch id (`202`) and do the work in the background, so the main job ends in seconds.
- **Run the child once per item, one at a time:** queue it through the Orchestrator (`POST /jobs` with the child's artifact id and version, the item's values as `params`, the batch id as `businessJobId` and `batchId:itemKey` as `eventId`), poll `GET /jobs/{runId}` until it ends, and cancel it (`POST /jobs/{runId}/cancel`) if it overruns its timeout. Go waits; no job waits for another job.
- **Record each item's result where a person can see it:** `Done` or `Failed`, the child's outputs if any, and the runtime's own error line for a failure. For a sheet that is the row; for a PDF or an API it is a result sheet, a file or a store.
- **A failed item does not stop the others.** A rerun does only the items that are not `Done`. A result that cannot be recorded, or an Orchestrator that cannot be reached three times in a row, stops the batch.
- **Offer `GET /<route>/<batchId>` for progress and `POST /<route>/<batchId>/cancel`**, and mark a batch `Interrupted` if the service restarts mid-batch.
- **Test it against a fake Orchestrator**, as `internal/process/runner_test.go` does: order, failure of one item, rerun, timeout and cancel, refusal before start.

What can be reused: `tracto/sheet-service/internal/process/orch.go` is the Orchestrator client (submit, get, cancel, check a script is published, first error line) and works for any source. The batch loop in `runner.go` and the store in `store.go` are written around sheet rows; a source whose items have no row number needs them generalised to an item key, or a record of its own.

## Conditions

There is no `if` in a script. The decision is made outside it, in Go. What exists today:

- **A column as the condition.** The batch only processes rows whose `Status` is not `Done`. Setting a row's `Status` to `Done` (or any `doneValues` entry) skips it, so a sheet can say which rows to do.
- **The caller chooses the script.** A person or a scheduler submits one published script or another. Two scripts are two branches; each stays a straight line. When the choice depends on a value, a Go branch API makes it (below).
- **A technical check that stops the run.** The browser `assert` action fails the run when the page is not as expected (an element missing, text different). It stops the run; it does not pick a different path.
- **A reason a row failed.** A child that cannot do its work fails, and the row says why in `Message`; the rest carry on.

**A decision that depends on a value (if this, run that) is also a main job and a child job with a Go API between them.** The script never compares anything. The main job passes the values to a Go API and ends; the Go API checks the condition and queues the appropriate child.

```
Main job (desktop or browser)  --POST /<branch route>-->  Go API
 passes the values, then ends                               checks the rule on the values it was given
                                                            queues child A, B or C (Orchestrator POST /jobs)
                                                            waits for it, records which branch ran and the result
```

The values come from the flow: a parameter of the main job, or a value an earlier segment read (`${param.price.value}`), or, for a loop, the row's cells or an earlier item's result. The last segment of the main job is the desktop `http.post` that hands them over.

**Not built yet.** No branch endpoint exists today. When a task needs one, tell the developer plainly that the branch API is missing and that creating it is a piece of work of its own (a Go endpoint, in sheet-service or another Go service); then write the main job and the children against it. Do not put the comparison in a script, and do not invent a request shape for an endpoint that does not exist: design the endpoint first.

What a branch API must do:

- **Take the values in the request**, not read them from the screen. The rule is part of the request (a field, an operator, a value, the child to run) or of the endpoint's own code. A script never receives the answer and acts on it, because it has no `if`.
- **Always say what happens when no rule matches** (a default child, or refuse the request). A silent no-match must not run nothing and report success.
- **Refuse before anything runs** when a named child is not published (`400`).
- **Reply at once** (`202` with a batch id) and run the chosen child in the background, like `/process/start`; the main job ends.
- **Queue the chosen child through the Orchestrator** (`POST /jobs`, the values as `params`, the batch id as `businessJobId`), poll it, cancel it on timeout. Go waits; no job waits for another job.
- **Record which branch ran, why and what came of it**, where a person can see it, so a wrong choice can be traced.
- **Offer progress and cancel routes**, and test it against a fake Orchestrator.

**A decision inside a loop** is the same API called per item. Go runs child A for the row, checks its rule on A's returned value or on the row's cells, then runs child B or C and writes the result into the row:

```
Go, for one row:
  run child A  -> A returns a value and ends          (A holds a worker only while it runs)
  Go checks its rule on that value                    (Go waits and holds no worker)
  run child B or child C                              (a worker is free again)
  write the result into the row
```

A script never calls Go and waits for a sub-job (see the rule above: it deadlocks). A child that calls Go and ends at once does not deadlock, but it has finished before the chosen child runs, so its row cannot receive that child's result. So the decision is always made where Go already owns the loop: in the batch, or in a branch API called by a main job that ends.

The browser runtime also refuses business predicates in `assert` by design (it checks the screen, it does not make business decisions), so a comparison such as "price above 100" cannot be written there.

## Why not copy the steps

Copying a block of steps N times looks like a loop but is not one. The number of copies is fixed in the file. If the real count is smaller, the extra copies still run, and an unresolved `${...}` is left in the step as literal text instead of failing, so a copy can search for `${param.invoiceId8}` and a read can return the "not found" page. The run succeeds with wrong data and nothing says so. Use the batch whenever the count can change. Copying steps is only right for a fixed, small, known count.

## Adding a new looping process

1. Write the child: one item, parameters in, `storeAs` out, no sheet access. Validate it, publish it.
2. Make the workbook: input column(s), one column per output, `Status`, `Message`.
3. Write the main job: the one `http.post` above, with the child's id and version and the column mappings. Validate and publish it. When the child changes later, see "Changing the child later".
4. Run the main job. Read `GET /process/<batchId>` and the sheet for the result.

## Verified and not verified

- **Run live** (real Orchestrator, browser worker and workbook): starting a batch, one child per row in order, results written back into the row, a failing row marked `Failed` with the runtime's error while the next row still ran, and a second start on the same sheet refused with `409`.
- **Covered by automated tests only:** cancelling a batch, a child overrunning its timeout, a restart marking the batch `Interrupted`, stopping when the Orchestrator is unreachable, an unpublished child being refused.
- **Not run:** the main job's own `http.post` step (it is validated only), and a desktop child.
- **Not built:** Go-side branching, running children in parallel, and handing a batch's result to a later step of another job.

The browser actions a child uses (`wait`, `fill`, `getText`, signing in through a profile) are in the `tracto-browser-rpa` skill.
