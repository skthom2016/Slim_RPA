# Loops and conditions

A Tracto script has no `if`, no loop and no retry, and one will never be added to it. Do not write one, and do not imitate one by copying steps (see "Why not copy the steps"). A script is a straight line; repetition and decisions live outside it, in Go. This file says where, and how to build them.

## The rule

- **Inside a script:** steps run top to bottom, once. The first failure stops the run. The `condition` field exists in the format but every runtime ignores it, so writing one changes nothing.
- **A loop ("for each row"):** a short **main job** asks the Go service (sheet-service, port 5090) to start a batch, then ends. sheet-service queues a **child job** for each pending row, one at a time, waits for it, and writes its results back into the row.
- **A decision ("if this, run that"):** made outside the script, by choosing which published script runs. See "Conditions".

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
| `outputs` | Name the child published -> the column it is written to. |
| `statusColumn` | Holds `Done` or `Failed` per row. A row is pending until it holds a `doneValues` entry (default `["Done"]`). Default column name `Status`. |
| `messageColumn` | Optional. Says why a row failed; cleared when a retry succeeds. |
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

## Conditions

There is no `if` in a script. The decision is made outside it, by choosing which published script runs. What exists today:

- **A column as the condition.** The batch only processes rows whose `Status` is not `Done`. Setting a row's `Status` to `Done` (or any `doneValues` entry) skips it, so a sheet can say which rows to do.
- **The caller chooses the script.** A person, a scheduler, or a main job submits one published script or another. Two scripts are two branches; each stays a straight line.
- **A technical check that stops the run.** The browser `assert` action fails the run when the page is not as expected (an element missing, text different). It stops the run; it does not pick a different path.
- **A reason a row failed.** A child that cannot do its work fails, and the row says why in `Message`; the rest carry on.

**Not built yet:** a rule evaluated in Go that picks between two child scripts per row (for example "if this value is above a limit run flow A, otherwise flow B"). Do not write a request shape for it and do not put the comparison in a script. If a task needs it, say so plainly: the row loop exists, the branch does not.

**How the branch must be built when it is.** Go decides *between* jobs; a child never calls Go to branch:

```
Go, for one row:
  run child A  -> A returns a value and ends          (A holds a worker only while it runs)
  Go checks its rule on that value                    (Go waits and holds no worker)
  run child B or child C                              (a worker is free again)
  write the result into the row
```

A child that calls Go and waits for a sub-child deadlocks (see the rule above). A child that calls Go and ends at once does not deadlock, but it has finished before the sub-child runs, so its row cannot receive the sub-child's result. A child that calls Go only for a quick answer cannot act on it, because a script has no `if`: the answer could only be typed as a value, which is a parameter, not a branch.

The browser runtime also refuses business predicates in `assert` by design (it checks the screen, it does not make business decisions), so a comparison such as "price above 100" cannot be written there.

## Why not copy the steps

Copying a block of steps N times looks like a loop but is not one. The number of copies is fixed in the file. If the real count is smaller, the extra copies still run, and an unresolved `${...}` is left in the step as literal text instead of failing, so a copy can search for `${param.invoiceId8}` and a read can return the "not found" page. The run succeeds with wrong data and nothing says so. Use the batch whenever the count can change. Copying steps is only right for a fixed, small, known count.

## Adding a new looping process

1. Write the child: one item, parameters in, `storeAs` out, no sheet access. Validate it, publish it.
2. Make the workbook: input column(s), one column per output, `Status`, `Message`.
3. Write the main job: the one `http.post` above, with the child's id and version and the column mappings. Validate and publish it.
4. Run the main job. Read `GET /process/<batchId>` and the sheet for the result.

## Verified and not verified

- **Run live** (real Orchestrator, browser worker and workbook): starting a batch, one child per row in order, results written back into the row, a failing row marked `Failed` with the runtime's error while the next row still ran, and a second start on the same sheet refused with `409`.
- **Covered by automated tests only:** cancelling a batch, a child overrunning its timeout, a restart marking the batch `Interrupted`, stopping when the Orchestrator is unreachable, an unpublished child being refused.
- **Not run:** the main job's own `http.post` step (it is validated only), and a desktop child.
- **Not built:** Go-side branching, running children in parallel, and handing a batch's result to a later step of another job.

The browser actions a child uses (`wait`, `fill`, `getText`, signing in through a profile) are in the `tracto-browser-rpa` skill.
