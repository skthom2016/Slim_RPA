# Next tasks

Scheduled after the UiPath migration method is finalised
(`tracto/docs/UIPATH-MIGRATION-OPTIONS.md`).

---

## Run diagnostics: make a failed run explain itself

The Orchestrator UI Logs tab (localhost:4000) shows nothing useful today. Three separate
causes, found by inspecting the UI, the Go handlers and the live `orchestrator.db`.

### Task 1 — Point the Logs tab at the table that holds the data

`public/app.js:247` calls `GET /admin/telemetry/{runId}`, which reads `TelemetryEvents`.
That table holds only terminal events, so the timeline renders exactly one dot per run.
The full lifecycle is in `JournalEntries`, which has **no read endpoint**.

Measured on the live database:

| | `TelemetryEvents` (what the UI reads) | `JournalEntries` (where the data is) |
|---|---|---|
| Rows | 24 | 240+ |
| Event types | 2: `RUN_SUCCEEDED`, `RUN_FAILED` | 10: `RUN_CREATED`, `RUN_QUEUED`, `LEASE_OFFERED`, `RUN_RUNNING`, `SEGMENT_QUEUED`, `SEGMENT_SUCCEEDED`, `SEGMENT_FAILED`, `RUN_SUCCEEDED`, `RUN_FAILED`, `WORKER_REGISTERED` |

A real failed run (`cdd16aa2-3bd9-4e37-bd40-1c65b85d5b1a`):

```
JournalEntries:   RUN_CREATED -> RUN_QUEUED -> LEASE_OFFERED -> RUN_RUNNING -> RUN_FAILED
TelemetryEvents:                                                              RUN_FAILED
```

**Do:** add a journal read endpoint (`GET /admin/journal/{runId}`, following the shape of
`internal/handlers/telemetry.go` `GetByRun`), register it in `main.go` beside
`/admin/telemetry/{runId}`, and have the Logs tab render the merged timeline. The existing
timeline UI and `tlDotClass` colour mapping already handle these event names.

### Task 2 — Stop the Logs tab opening blank

The tab is a per-run lookup that requires a pasted Run ID, so arriving from the sidebar
shows an empty box with no indication of what to do. `GET /admin/telemetry` already returns
the last 200 events across all runs and is never called by the UI.

**Do:** on tab open with no Run ID, load recent activity across runs, each row clickable
through to that run's timeline. Keep the Run ID box for direct lookup.

### Task 3 — Carry the failure reason out of the worker

This is the task that matters; tasks 1 and 2 only make the panel honest about what it has.

Every failed run in the database carries the same metadata and a null category:

```json
{"terminalState":"Failed"}        errorCategory: null
```

The plumbing exists and is empty:

- `desktop-runtime/src/Tracto.Desktop.Host/HttpOrchestratorBridge.cs:183` sends
  `errorCategory = run.ErrorCategory?.ToString()`, which arrives null — the failing step's
  category is not propagated into the run result.
- Line 185 hardcodes `evidenceRefs = (string[]?)null`.
- Nothing carries the failing **step number** or an error message.
- `POST /jobs/{runId}/result` already accepts `errorCategory`, `unknownReason`, `payload`
  and `evidenceRefs`; `internal/handlers/result.go:172` writes only
  `{"terminalState": …}` into telemetry metadata.
- The desktop runtime already has a rich error model that is not being used:
  `DesktopActionError` with `TARGET_NOT_FOUND`, `ENVIRONMENT`, `desktop.elementDisabled`,
  `desktop.elementOffscreen`, `desktop.unsupportedControl`.

**Do:**

1. Propagate the failing step's `DesktopActionError` (category, detail code, message) and
   its step number into the run result the bridge posts.
2. Include them in the telemetry/journal metadata written by `result.go`, so the reason is
   queryable, not only visible in the worker console.
3. Populate `evidenceRefs` — the browser runtime already saves a screenshot to
   `bin/Debug/net10.0/evidence/<runId>-<stepId>.png`.
4. Do the equivalent in the browser runtime bridge.

**Why it is sequenced with the migration work:** migrating a flow means running a script
that fails several times before it is right. Without the failure reason stored, each cycle
requires reading the worker console on the machine that ran it. This is the dominant cost
of a migration project, not writing the script JSON.
