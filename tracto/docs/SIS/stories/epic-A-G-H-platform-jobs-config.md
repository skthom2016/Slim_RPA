# Epics A, G and H: platform capabilities, main jobs and schedules, configuration and secrets

Scope: the generic platform pieces the SIS flows share (A), the five one-step main jobs with their schedules and monitoring (G), and the configuration templates, documentation and secrets handling (H). Flow-specific stories sit in the other epic files in this folder.
Paths in the evidence column are relative to `tracto/`.

| ID | Title |
|---|---|
| A1 | Mail reading |
| A2 | Mail sending |
| A3 | File operations |
| A4 | Program runner |
| A5 | Work items |
| A6 | Appending write |
| G1 | Main jobs |
| G2 | Schedules and monitoring |
| H1 | Configuration templates |
| H2 | Operational documentation |
| H3 | Secrets and credentials |

---

## A1 — Mail reading
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | none |
| Capability today | Partly. The Microsoft sign-in exists (`sheet-service/internal/msauth/msauth.go`: device-code flow, refresh token kept in table `ms_tokens` of `sheet-service.db`, renewed 5 minutes before expiry). Its default scope is `Files.ReadWrite.All offline_access`, so no mail permission is requested; `MS_SCOPES` can override it. No mail reading code exists anywhere under `tracto/` (no `/mail/*` route in `sheet-service/internal/api/api.go`, no IMAP client). To build |

**User story.** As a flow developer, I want one endpoint that lists unread mail with its attachments and saves attachments and mail bodies to a folder, for every entity mailbox whatever its kind (a Microsoft 365 mailbox in the entity's own tenant, a mailbox reached through the service's sign-in, or an IMAP mailbox), so that the dispatcher flow can read every entity mailbox without a desktop session.

**Context.** EmailPADispatcher reads each entity mailbox through a UiPath mail activity with a plain-text password and marks mail read as its progress marker ([SIS-02, "What the UiPath process does"](../SIS-02-email-pa-dispatcher.md)). In Tracto, each mailbox is a row of the mail account store (H3) with one of three kinds: `graph-app`, Microsoft Graph with the entity's own tenant ID, client ID and client secret (application permission `Mail.Read` on that mailbox); `graph-delegated`, Graph through the service's existing sign-in with `Mail.Read` added to the scope; `imap`, an IMAP client with host, port, user and password. With 50 to 60 entity mailboxes, each may sit in a different tenant. Mail is never used as its own progress marker; a processed-message record keyed by message id is. The endpoints are `POST /mail/fetch` and `POST /mail/save` ([SIS-02, "What has to be built", item 1](../SIS-02-email-pa-dispatcher.md)).

**Acceptance criteria.**
1. Given a Microsoft 365 account configured with `MS_SCOPES` containing `Mail.Read`, when `POST /mail/fetch {account, folder:"Inbox", unreadOnly:true, markRead:false, max:100}` is called, then the reply holds one record per message with `id`, `from`, `subject`, `date`, plain-text body, and per attachment `name`, `size` and `type`, oldest first.
2. Given the saved sign-in has no `Mail.Read` permission or the refresh token was revoked, when fetch is called, then the reply is an error with code `LOGIN_REQUIRED` or `SCOPE_MISSING` and a message naming `sheet-service login`; no partial list is returned.
3. Given an IMAP account whose password is held in the mail account store (H3), when fetch is called, then the same record shape is returned; when the password is wrong, the error is `AUTH_FAILED` and no log line contains the password.
4. Given `max` is 100 and the mailbox holds 250 unread messages, when fetch is called twice with the processed-message record updated between the calls, then the second call returns the next messages and never one already recorded.
5. Given `POST /mail/save {messageId, what:"attachments", folder}`, when the message has three attachments, then three files are written and the reply lists their paths; given `what:"eml"`, one `.eml` file is written holding the whole message.
6. Given an attachment name containing `..\`, a path separator, a reserved Windows name or more than 150 characters, when it is saved, then it is written under a sanitised name inside the target folder and the original name is returned alongside the path.
7. Given the same message id is recorded as processed, when `/mail/fetch` runs again with `unreadOnly:true`, then the message is not returned, even if a person has since marked it unread, and a message a person has read but that is not recorded is still returned when `unreadOnly:false`.
8. Given a mailbox that is unreachable (network down), when fetch is called, then the reply is `MAIL_UNREACHABLE` within the configured timeout and nothing is recorded as processed.
9. Given a `graph-app` account row with its own tenant ID, client ID and secret, when fetch is called, then the service obtains an application token for that tenant (client credentials), caches it until shortly before expiry, and reads that mailbox only; a wrong secret gives `AUTH_FAILED` naming the account, not the secret.
10. Given 50 account rows in different tenants, when the dispatcher fetches them in turn, then each uses its own token and a failure in one account does not affect the others.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A1.1 | Microsoft Graph sign-in scope and token handling for mail | Add `Mail.Read` (and `Mail.Send`, see A2) to `MS_SCOPES`; a second `sheet-service login` is needed once |
| A1.2 | Graph unread listing with body and attachment list |  |
| A1.3 | IMAP client: connect, list and fetch for the in-house mailbox | Password from the mail account store, see H3 |
| A1.8 | Graph application sign-in per account (client credentials, token cache per tenant) | Tenant, client and secret from the mail account store |
| A1.4 | Save attachments and .eml bodies to a target folder |  |
| A1.5 | Processed-message record so a rerun never repeats a message | Table keyed by account and message id; written after the work item exists, not before |
| A1.6 | Mail reader tests against test mailboxes |  |
| A1.7 | Attachment name sanitising: separators, reserved names, length, collisions | SIS-02 "Watch": attachments arrive from outside |

**Test notes.** One Microsoft 365 test mailbox and one IMAP test mailbox, each holding: a mail with a PDF, a mail with a ZIP, a mail with no attachment, a mail with an attachment named with path characters, a read mail and 250 unread mails for the paging case. Checks: record shape, order, rerun idempotence, sanitised names, error codes, and that no log line holds a password.

**Open items.**
- Which mailboxes exist, on which platform each runs and whether each is Microsoft 365 or IMAP is not known ([SIS-02, open question 2](../SIS-02-email-pa-dispatcher.md)); the configuration workbook holds mailbox settings for DTSS only.
- Which kind each mailbox is. The UiPath dispatcher reads every mailbox over IMAP with a password; the entity mailboxes are also reported to have their own tenant ID and client secret each. The store holds either, per row ([build-defaults.md](../build-defaults.md)).
- The in-house IMAP server's TLS mode and port (the workbook shows port 465, which is SMTP over TLS, not IMAP).
- Retention of saved attachments in the staging folder ([SIS-02, open question 6](../SIS-02-email-pa-dispatcher.md)).

---

## A2 — Mail sending
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | A1 (shared sign-in and scope) |
| Capability today | To build. No send code exists under `tracto/`; only the sign-in in `sheet-service/internal/msauth/msauth.go` can be reused |

**User story.** As a flow developer, I want to send an HTML mail with CC and to reply to a message so that the reply goes to the sender only, so that the flows can report results and chase senders without passing a password or copying strangers.

**Context.** All four flows end in mail. Flow 1 sends an availability mail and an HTML table of unavailable accounts; flow 2 replies to the sender when a file is unusable; flows 3 and 4 send rejection and summary mails ([SIS-01, "What has to be built", item 2](../SIS-01-bank-avail-check.md); [SIS-02, item 3](../SIS-02-email-pa-dispatcher.md)). UiPath sent through SMTP accounts whose passwords sit in the packages. Tracto sends through Graph `sendMail` with the sending account's row in the mail account store (H3): an application token for a `graph-app` row, the service's sign-in for a `graph-delegated` row. A reply goes to the sender only; the original CC and BCC are not copied.

**Acceptance criteria.**
1. Given `POST /mail/send {to, cc, subject, body, isHtml:true}` with an HTML table body, when it is sent, then the received mail renders the table and `cc` receives a copy.
2. Given `POST /mail/send` with `attachments:[{path}]` inside a configured root, when it is sent, then the attachment arrives; given a path outside the configured roots (A3.4), then the call is refused with `PATH_NOT_ALLOWED` and no mail is sent.
3. Given `POST /mail/reply {account, messageId, body}`, when the original mail had other To, CC and BCC recipients, then the reply's To is the sender only, CC and BCC are empty, and it appears in the original thread.
4. Given a recipient that is not a syntactically valid address, when send is called, then the reply is `INVALID_ADDRESS` before any call to Graph.
5. Given Graph answers with an error (throttling, 403 for a missing `Mail.Send` permission), when send is called, then the reply carries the failure and never reports success; a throttled call is retried up to the configured count.
6. Given a send that succeeds, then the reply holds the account used and a timestamp, so the caller can record it on its item.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A2.1 | Graph send with HTML body and CC |  |
| A2.2 | Reply to a message, addressed to the sender only |  |
| A2.3 | Send tests, including an HTML table body |  |
| A2.4 | Address validation and a send result that is never a silent success | Covers criteria 4 to 6 |

**Test notes.** A test mailbox for sending and a second for receiving. Checks: HTML table renders in the received mail, CC delivered, reply recipients, refusal of a path outside the roots, error mapping with the permission removed.

**Open items.**
- Which account sends each flow's mail. Default: the entity row names a sending account in the mail account store, else the service sender in `Settings` ([build-defaults.md](../build-defaults.md)).
- Mail text templates: the UiPath mail bodies are hard-coded in the workflows; the wording to keep is for the process owner, supplied through H1.
- Whether the SIS tenant allows `Mail.Send` and `Mail.Read` to be consented for the existing app registration.

---

## A3 — File operations
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | none |
| Capability today | To build. The desktop runtime handlers are `key`, `type`, `click`, `delay`, `scroll`, `navigate`, `focus`, `app.launch`, `http.get`, `http.post`, `notepad.type`, `file.saveAs`, `file.read`, `clipboard.get` (`desktop-runtime/src/Tracto.Desktop.Actions/Handlers/`); none checks, lists, copies, moves or deletes. No `/fs/*` route exists in `sheet-service/internal/api/api.go` |

**User story.** As a flow developer, I want Go endpoints to check, list, create, copy, move, delete and unpack files inside configured root folders, so that every flow can file its documents without a desktop session and without a path escaping its root.

**Context.** The flows create dated folder trees, rename and move attachments, unpack ZIP archives and test whether a statement folder exists ([folders-and-files.md](../folders-and-files.md); [SIS-01, "What has to be built", item 1](../SIS-01-bank-avail-check.md); [SIS-02, item 2](../SIS-02-email-pa-dispatcher.md)). UiPath did this with workflow activities and a PowerShell call. In Tracto the endpoints are `POST /fs/exists`, `/fs/list`, `/fs/mkdir`, `/fs/copy`, `/fs/move`, `/fs/delete` and `/fs/unzip`, all confined to roots set in the service configuration. Archives arrive from outside, so `unzip` refuses any entry that would land outside the destination.

**Acceptance criteria.**
1. Given `POST /fs/exists {path, type:"folder"}` on an existing folder, then `{exists:true, type:"folder", modifiedAt}`; on a missing path, `{exists:false}` with status 200.
2. Given `POST /fs/list {path, glob:"PA_DTSS*.xlsx"}`, then only matching entries are returned, each with name, size, type and modified time, sorted by name.
3. Given `POST /fs/mkdir` for `<root>\13 Nov 2026\DTSS\PDF_JPEG` where no parent exists, then every missing level is created and a second call succeeds without error.
4. Given any path whose normalised form is outside the configured roots (`..` segments, absolute path on another drive, a junction that resolves outside), then the call is refused with `PATH_NOT_ALLOWED` and nothing is read or written.
5. Given `copy` or `move` onto an existing target without `overwrite:true`, then the call fails with `TARGET_EXISTS` and neither file changes.
6. Given a ZIP holding an entry named `..\..\x.pdf` or an absolute path, when `POST /fs/unzip` runs, then the call fails with `UNSAFE_ARCHIVE`, no file from the archive is written, and nothing outside the destination is touched.
7. Given a ZIP above the configured limit of entries or total unpacked size, then the call fails with `ARCHIVE_TOO_LARGE` before extracting.
8. Given a corrupt or password-protected archive, then the call fails with `ARCHIVE_UNREADABLE` and the source file is left in place.
9. Given a file held open by another process, when `move` or `delete` is called, then the reply is `FILE_IN_USE` and no partial copy remains at the target.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A3.1 | Exists, list and create folder |  |
| A3.2 | Copy, move and delete |  |
| A3.3 | Safe archive extraction that refuses paths leaving the destination |  |
| A3.4 | Configured root-folder restriction | Applies to every route, including mail save and the program runner's working directory |
| A3.5 | File operation tests |  |
| A3.6 | Collision rule for copy and move (refuse unless `overwrite`) | Criterion 5 |
| A3.7 | Archive limits: entry count and total unpacked size | Criterion 7, hostile archives |

**Test notes.** A scratch root with a dated tree; archives built for the tests: a normal ZIP, a ZIP with a traversal entry, an oversized-entry ZIP, a corrupt ZIP and a protected ZIP. Checks: every refusal leaves the disk unchanged, and the root restriction holds for junctions.

**Open items.**
- The configured roots per machine: the estate uses `E:\Automation`, `C:\Automation` and, in the packages' copy, `D:\Automation`; Tracto uses one root per kind ([folders-and-files.md, "Points to confirm", item 3](../folders-and-files.md)).
- The limits for criterion 7 (entry count, unpacked size) are to be set from real archives.
- Staged-file retention is open ([SIS-02, open question 6](../SIS-02-email-pa-dispatcher.md)).

---

## A4 — Program runner
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | A3 (root restriction for the working directory) |
| Capability today | To build. Nothing in `sheet-service/` or `orchestrator-go/` starts a process; the desktop runtime has `app.launch` only for windowed applications and `docs` state "no `shell.run`" (`.claude/skills/tracto-desktop-rpa/references/limits.md`) |

**User story.** As a flow developer, I want to start a configured console program from Go, wait for it with a timeout and read its exit code and output, so that flow 1 can run the statement fetch bot and B4 can run the collection accounting batch, and each knows how it ended.

**Context.** Flow 1 runs `run_Bank_bot.bat` through PowerShell after writing the entity name into `BankMaster.xlsx`, then checks the folder again ([SIS-01, "What the UiPath process does", step 3](../SIS-01-bank-avail-check.md)). The International collection accounting run is the same pattern: `run_SXP_bot.bat` runs `python.exe -W ignore ...\bot_run.py` ([SIS-05](../SIS-05-international-bot1.md)); the named command holds the Python executable and the script path directly, so no batch file is needed. The bot is a console script, so Go starts it directly as `POST /run {command, args, timeoutSeconds}`. The caller names a command from configuration; it never supplies a path or a shell line.

**Acceptance criteria.**
1. Given a named command `bank-bot` in the service configuration, when `POST /run {command:"bank-bot"}` is called and the program exits 0, then the reply holds `exitCode:0`, captured stdout and stderr (each capped at a configured size) and the duration.
2. Given a program that exits non-zero, then the reply is a result with that exit code, not a service error.
3. Given a program that runs past `timeoutSeconds`, then the whole process tree is stopped, the reply is `TIMEOUT` with whatever output was captured, and no child process is left running.
4. Given `command` is not in the configuration, then the reply is `UNKNOWN_COMMAND` and nothing starts.
5. Given arguments containing spaces, quotes or `&`, then they reach the program as single arguments (no shell parsing).
6. Given the configured program path does not exist, then the reply is `NOT_FOUND` and the entity item can be marked Failed with that message.
7. Given the captured output contains text matching a secret pattern, then it is masked before it is returned or logged (H3).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A4.1 | Named-command configuration |  |
| A4.2 | Run with timeout, exit code and output capture |  |
| A4.3 | Runner tests |  |
| A4.4 | Arguments passed without a shell; working directory inside a root; process-tree stop on timeout | Criteria 3 and 5 |

**Test notes.** A test `.bat` that exits 0, exits 3, sleeps beyond the timeout and prints a line with spaces. Checks: exit code mapping, timeout kill with no orphan, unknown command.

**Open items.**
- The fetch bots take no argument; `EntitySelected!A2` stays shared state and the entity loop stays sequential unless the code is changed ([SIS-01, open question 2](../SIS-01-bank-avail-check.md)).
- The fetch bots always end with exit code 0, so the exit code is stored but the folder check decides; how long Go may wait is open ([SIS-01, open question 1](../SIS-01-bank-avail-check.md)).
- `bot_run.py` catches its own errors and exits normally, so for B4 the exit code says only that the program ended; the result is read from `Response_Report.xlsx`.

---

## A5 — Work items
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | none |
| Capability today | Partly. `sheet-service/internal/process/store.go` already keeps batch items with `state`, `message`, `run_id` and `updated_at` in tables `process_batches` and `process_items`, but items are keyed by sheet row (`PRIMARY KEY (batch_id, row)`), there is no attempt count, no claim and no way to add an item from an API. `/process/start` and `/process/{id}` are in `sheet-service/internal/api/process.go`. The Orchestrator's own queue is the run queue only. To build |

**User story.** As a flow developer, I want a work-item store with add, claim, complete and fail operations, an attempt count and a list view, so that flow 2 can hand each file to flows 3 and 4 and every item keeps its own status and reason.

**Context.** Flow 2 produces one work item per filed file (a pointer and a state, not the file); flow 4 consumes them one at a time and uses the attempt count to hold an item on `ANALYZER_BUSY` instead of failing it ([SIS-02, "What a queue is for" and "Where the queue lives"](../SIS-02-email-pa-dispatcher.md); [SIS-04, "The shape in Tracto"](../SIS-04-payment-advice-extraction.md)). The working default is a SQLite table; a sheet is the simpler alternative at about 45 to 150 documents a day and the choice is re-confirmed before this story starts. Fields: `PAFileName`, `EntityName`, `From`, `TimeDate`, `CustomerName`, `AttachmentType`, `FilePath`, `Status`, `Message`, plus message id, matched keyword, attempts and timestamps.

**Acceptance criteria.**
1. Given `POST /workitems {queue, entity, ...fields}`, then a row is stored with `Status:"Pending"` and `attempts:0`, and the reply holds its id; adding the same `(queue, message id, file name)` twice returns the existing id and stores one row.
2. Given two callers calling `POST /workitems/claim {queue, entity}` at the same time with one Pending item, then exactly one receives the item (`Status:"Running"`, `attempts:1`) and the other receives an empty result.
3. Given `POST /workitems/{id}/complete`, then the status is `Done`, the message is cleared or set to the supplied text and a second complete call changes nothing.
4. Given `POST /workitems/{id}/fail {message, retry:true}` with attempts below the configured maximum, then the status returns to `Pending` and the message holds the reason; at the maximum, the status becomes `Failed`.
5. Given the service restarts while an item is `Running`, then on start it is returned to `Pending` (attempts kept) and logged, never left `Running`.
6. Given `GET /workitems?queue=&entity=&status=&limit=&offset=`, then items are returned newest first with status, message and attempts; `GET /workitems/{id}` returns one item.
7. Given an item whose `Message` would exceed the configured length, then it is stored cut with a marker, as the sheet batch store does (500 characters).
8. Given a work item carries a file path outside the configured roots (A3.4), then add is refused with `PATH_NOT_ALLOWED`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A5.1 | Work-item table and status model | Statuses `Pending`, `Running`, `Done`, `Failed`; follows the `process_items` pattern |
| A5.2 | Add, claim, complete and fail endpoints | Claim as a single atomic update |
| A5.3 | Attempt count and retry handling |  |
| A5.4 | List and read endpoint for the console view |  |
| A5.5 | Work-item tests | Includes the concurrent claim |
| A5.6 | Return `Running` items to `Pending` on service start | Criterion 5. The batch store already marks batches Interrupted on start (`sheet-service/main.go`) |
| A5.7 | Work Items page in the console (list with filters) | A list view, not a review screen; `orchestrator-ui/public/app.js` has the pattern in `loadJobs` |

**Test notes.** A temporary database; a loop that claims from 4 goroutines over 50 items. Checks: no item claimed twice, attempts counted, restart recovery, list filters. If the sheet alternative is chosen, this story becomes making `POST /apply` atomic (it reads under the file lock, releases it and writes under it again, `sheet-service/internal/api/api.go` `apply`), and A5.1 to A5.3 shrink accordingly.

**Open items.**
- Sheet or table for the queue: working default is the SQLite table, to be re-confirmed before the story starts ([SIS-02, "What decides it"](../SIS-02-email-pa-dispatcher.md)).
- Which service hosts the store (the existing sheet-service module or a new Go module) and therefore which database file.
- Daily volume: the earlier figure was 45 a working day, the samples show about 150 ([payment-advice-samples.md](../payment-advice-samples.md)).
- Work items for mails with no attachment: the consumer is outside the four flows ([collection-accounting-handoff.md, "What is outside the four flows"](../collection-accounting-handoff.md)).
- Maximum attempts and back-off for `ANALYZER_BUSY`.

---

## A6 — Appending write
| Field | Value |
|---|---|
| Epic | A: Tracto platform capabilities |
| Type | Story |
| Depends on | none |
| Capability today | Partly. `POST /update` writes cells at known row numbers (`UpdateRequest` in `sheet-service/internal/api/api.go`); a per-file mutex (`fileLocks` in `sheet-service/internal/sheet/xlsx.go`) and a changed-since-read refusal (`ErrConflict` in `sheet-service/internal/sheet/sheet.go`) exist. There is no append mode and no `/append` route. To build |

**User story.** As a flow developer, I want to append rows to the end of a workbook sheet by header name, so that trackers and advice workbooks can grow without the caller knowing the next row number.

**Context.** The Email Tracker (`LogTracker.xlsx`, sheet `Email Tracker`) and the advice workbooks are written by appending ([SIS-02, "What has to be built", item 6](../SIS-02-email-pa-dispatcher.md); [folders-and-files.md](../folders-and-files.md)). `POST /update` needs a row number, which two entities finishing at once could claim twice. The new route is `POST /append {source, rows:[{header: value}]}`.

**Acceptance criteria.**
1. Given a sheet with a header row and 10 data rows, when `POST /append` adds 3 rows, then they occupy rows 12 to 14, cells are placed by header name, and the reply holds the first row written and the count.
2. Given a header named in a row that the sheet does not have, then the call fails with `UNKNOWN_COLUMN` and writes nothing.
3. Given two `POST /append` calls of 20 rows each at the same instant on the same file, then the file ends with 40 new rows, none lost and none overwritten.
4. Given the workbook is open in Excel (the file cannot be written), then the call fails with `FILE_LOCKED` and the file is unchanged.
5. Given a sheet with no header row, then the call fails with `NO_HEADER`; given the sheet does not exist, `NO_SUCH_SHEET`.
6. Given a cell value that is a true date or number, then it is written with that type rather than as text.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| A6.1 | Append rows to a workbook |  |
| A6.2 | Concurrent-write safety check | Reuses the per-file lock; the lock is inside one process, so two service instances on one file are not covered |
| A6.3 | Append tests |  |

**Test notes.** A scratch `.xlsx` with a header; 2 goroutines appending. Checks: row positions, column placement, typed cells, error codes.

**Open items.**
- Two sheet-service instances on one file have no coordination; a single instance per file is assumed.
- Whether Google Sheets and SharePoint sources need append in this migration (workbooks are local `.xlsx` in all four flows).
- The advice workbook writers (D3, F2) own the template, sheet order and file naming; this story owns only the generic append.

---

## G1 — Main jobs
| Field | Value |
|---|---|
| Epic | G: Main jobs, schedules and monitoring |
| Type | Story |
| Depends on | A5 (for the flows that claim items); the five start endpoints from epics B to F |
| Capability today | Partly. `http.post` exists with `statusCode`, `body`, `json` outputs and a 30000 ms default timeout (`desktop-runtime/src/Tracto.Desktop.Actions/Handlers/HttpPostHandler.cs`); `${param.name}` is supported (`desktop-runtime/src/Tracto.Desktop.Host/ScriptExecutionEngine.cs`, line 157); publishing is `PUT /artifacts/{id}/versions/{version}` (`orchestrator-go/internal/handlers/artifacts.go`); a validator exists (`.claude/skills/tracto-desktop-rpa/scripts/validate.js`). The five scripts and the five `/…/start` endpoints do not exist. To build |

**User story.** As an operator, I want one published one-step main job per flow that starts the flow's Go service and ends, so that a schedule or a person can run each flow as an ordinary Tracto job with a run record.

**Context.** Each flow is a main job plus a Go service that loops over its items with its own status per item. There is no child job and no browser runtime; the desktop runtime runs only the main job, a single `http.post` ([README.md, "Conversion at a glance"](../README.md)). The jobs are `bankcheck-main` to `/bankcheck/start`, `carun-main` to `/carun/start`, `maildispatch-main` to `/maildispatch/start`, `excelextract-main` to `/excelextract/start` and `padvice-main` to `/padvice/start`. The Go service replies at once (as `/process/start` does with 202) and carries on in the background.

**Acceptance criteria.**
1. Given each of the five scripts, when it is run through the validator, then it reports no error and no warning, has exactly one step, the action `http.post`, and contains no password, token or address.
2. Given the script is published with `PUT /artifacts/{id}/versions/{version}` inside the envelope `{"schemaVersion":"1","script":{...}}`, then the reply is success and `GET /artifacts` lists it; publishing with a mismatching id or version is refused with `422 INVALID_SCRIPT`.
3. Given a job is submitted for `bankcheck-main` and the Go service answers 202 with a batch id, then the run ends `SUCCEEDED` within seconds and the batch id is in the run's outputs.
4. Given the Go service answers 409 because a run of the same flow is active, then the run ends `FAILED` with `EXECUTION_FAILED` and the response body in its message, and no second batch starts.
5. Given the Go service is not running, then the run ends `FAILED` with the connection error and the schedule (G2) records it.
6. Given the job is submitted with `params {runDate:"13 Nov 2026"}` or `{entities:"DTSS,SIS"}`, then the value is placed in the request body through `${param.runDate}` and the Go service uses it; with no params the service uses today in the schedule's time zone and all enabled entities.
7. Given a param the service does not accept, then the service answers 400 with the name of the param and the run fails with that message.
8. Given the service URL differs between machines, then it is a default `param` of the script, overridable in the schedule, and not a literal in several steps.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| G1.1 | Five one-step main jobs | Script JSONs saved under `Downloads\rpa-scripts`, not in the repository |
| G1.2 | Script validation and publishing |  |
| G1.3 | Parameter handling | Run date, entity filter, service URL |
| G1.4 | Common start contract for the five services: 202 with batch id, 409 busy, 400 invalid | The services are built in epics B to F, the contract is fixed here |

**Test notes.** The Orchestrator, a desktop worker with an unlocked session and a stub service answering 202, 409, 400 and not at all. Checks: run states and messages for each answer, validator output, publish round trip.

**Open items.**
- The main job needs a desktop worker with an unlocked, logged-in session to run its single step, which the scheduling notes require for any desktop job (`docs/scheduling.md`); the flows need no worker for the work itself. Whether the one-step job should stay on the desktop runtime or move to another runtime is not decided here.
- Which Go module hosts each `/…/start` endpoint. Default: a new module `sis-service`; the shared capabilities go into `sheet-service` ([build-defaults.md](../build-defaults.md)).
- Whether flows 3 and 4 are started by their own schedule or by flow 2 finishing; see G2.4.

---

## G2 — Schedules and monitoring
| Field | Value |
|---|---|
| Epic | G: Main jobs, schedules and monitoring |
| Type | Story |
| Depends on | G1 |
| Capability today | Partly. Schedules exist: `POST/GET/PATCH/DELETE /admin/schedules` with cron in a named time zone, `overlapPolicy`, `graceSeconds`, compare-and-swap firing and collapse of missed fires (`orchestrator-go/internal/handlers/schedules.go`, `orchestrator-go/internal/scheduler/scheduler.go`, `docs/scheduling.md`); the console has a Schedules page with upcoming and completed views (`orchestrator-ui/public/app.js`, `loadSchedules`, `loadCompletedSchedules`) and a Jobs page and per-run Logs (`loadJobs`, `openLogs`). Failure notifications do not exist: no mail, SMTP or webhook code in `orchestrator-go`. Per-item status of a Go service's batch is not visible in the console. To build for the gaps |

**User story.** As an operator, I want each flow on a schedule in its entity group's time zone (Asia/Kolkata for the Indian entities, the Australian zone for the International ones), a mail when a scheduled run fails or is skipped, and the flow's outcome visible in the console, so that a missed day is noticed the same morning and the run can be inspected without logging in to the service.

**Context.** UiPath ran the processes from Orchestrator triggers. In Tracto a schedule queues the main job; a run needs a worker and the scheduler only queues (`docs/scheduling.md`). The statement date folder of flow 1 and the output date folder of the loader tree use the schedule's time zone, so the zone is fixed to the estate's. The loader reads whatever exists for today when it runs, so the extraction flows must be scheduled ahead of it ([collection-accounting-handoff.md, "To confirm", item 1](../collection-accounting-handoff.md)).

**Acceptance criteria.**
1. Given `POST /admin/schedules` for each of the five main jobs with the group's `timeZone` (`Asia/Kolkata` or the Australian zone), `overlapPolicy:"skip"` and `graceSeconds`, then each reply carries `nextRunLocal` in `Asia/Kolkata`, and a schedule with `Local` or an empty zone is refused.
2. Given a flow's run is still active when its next fire is due and the policy is `skip`, then no second run starts and the schedule's `lastStatus` is `Skipped`.
3. Given the Orchestrator was down across a fire and restarts within `graceSeconds`, then exactly one run starts; beyond it, the fire is recorded as skipped and not replayed.
4. Given a scheduled run ends `FAILED`, `TIMEDOUT` or never starts (`lastStatus` `Failed` or `Skipped`), then a mail is sent within 5 minutes to the configured operations recipients with the script id, schedule id, run id, state and the failure message, and no credential.
5. Given the same run fails, then only one notification is sent for it, and a later successful run sends none.
6. Given a flow's batch has 12 entities of which 2 failed, when the operator opens the run in the console, then the run record shows the batch id and the count done, failed and pending, and a link or call to read the per-item list from the service.
7. Given `GET /jobs?limit=&state=` and the Completed view, then runs triggered by a schedule appear with `triggerType:"Schedule"` and a failed one is flagged.
8. Given flow 3 or 4 is scheduled after flow 2, then the schedule times leave the configured gap, or flow 2's completion starts them (G2.4), and the schedule list shows the order.
9. Given the schedules, then they match today's UiPath triggers: flow 2 Indian at 10:00 and International at 05:45 Australian time; flow 4 International at 05:49; flow 1 Indian at 11:00 and 11:30 and International at 07:00 Australian time; flow 5 at 07:55 Australian time.
10. Given the Indian extraction (flows 3 and 4), then it has no schedule: the SPOC starts it with Run now, as they start it by hand in UiPath Assistant today. The Indian run of collection accounting has no schedule either: a person starts it from the web page.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| G2.1 | Schedules with time zone, overlap and grace settings | Configuration of five schedules on the existing API; no code change expected |
| G2.2 | Failure notifications | New in the Orchestrator or in a Go service that polls `/admin/schedules` and `/jobs`; uses A2 |
| G2.3 | Run-record visibility in the console | Console shows the batch summary from the run's outputs; per-item list read from the service |
| G2.4 | Order of the flows: fixed gap between schedules or a start-on-completion call | Flows 3 and 4 consume what flow 2 files, the loader consumes flows 3 and 4 |

**Test notes.** An Orchestrator with the five schedules at a one-minute cron, a stub main job that fails, a mailbox for notifications and a Go service stub returning a batch summary. Checks: skip and grace behaviour, one mail per failure, console fields.

**Open items.**
- The operations recipients for failure notifications and the sending account.
- Run times per flow: the process owner sets them; the loader's run time is not known ([collection-accounting-handoff.md, "To confirm", item 1](../collection-accounting-handoff.md)).
- A completed schedule view lists finished runs; whether a batch with failed items but a `SUCCEEDED` main job counts as a failure for notification purposes (the main job succeeds when the service accepts the batch).
- Event-triggered starts are not built (`docs/scheduling.md`, "Not covered").
- The time zone of the Indian times, the days of the week of each trigger, and the exact Australian zone ([open-questions.md](../open-questions.md), q90, q114, q11). Whether the Indian extraction stays a Run now or follows flow 2 (q91), and the SPOC's console account.
- The International mail monitoring and PDF extraction packages, if they are not the Indian ones with International configuration (q113).

---

## H1 — Configuration templates
| Field | Value |
|---|---|
| Epic | H: Configuration templates and documentation |
| Type | Story |
| Depends on | none (the sheets are consumed by epics B to F) |
| Capability today | To build. The sheets are described in [configuration-workbooks.md](../configuration-workbooks.md) and [SIS-03, "The format profile"](../SIS-03-india-excel-extraction.md); no template workbook exists in the repository |

**User story.** As a process owner, I want ready-made configuration workbooks with the right sheets, headers and sample rows, so that I can add an entity, a customer, a column mapping or a recipient by adding a row.

**Context.** The flows read configuration a person edits: the `Settings` sheet of `MasterConfig.xlsx`, one sheet per entity in `BankMaster.xlsx` (column `BANK_ACCOUNT_NAME`), the customer list, the entity profile and column map for spreadsheet advices, the customer rules for PDF advices, and the recipients ([configuration-workbooks.md](../configuration-workbooks.md)). The queue names, the other fourteen `MasterConfig.xlsx` sheets and the credential keys are not carried over. The seventeen entities across the seven current lists become one entity table with a flag per flow.

**Acceptance criteria.**
1. Given the entity table template, then it has one row per entity and the columns `Entity`, `BankCheck`, `MailDispatch`, `ExcelExtract`, `PdfExtract`, `Mailbox`, `AcceptedTypes`, `Recipients`, `Active`, filled with the seventeen entities and the flags of today's coverage, with `AcceptedTypes` for DTSS excluding spreadsheets.
2. Given the entity profile template, then it has the columns `Entity`, `InputSubfolder`, `FilePattern`, `SheetName`, `ExpectedColumns`, `RejectMailTo`, `Active`, with rows for AEPL, RARE, DTSS and Terminix; the column map template has `Entity`, `SourceColumn`, `TargetColumn`, `Clean`.
3. Given the customer rules template, then it has one row per customer rule with the keys the F1 rules read, and the customer list template has named alias columns (not two fixed columns).
4. Given the settings template, then it holds the paths (`PaymentAdvicesFolderPath`, `PaymentAdvicesExtractionFolderPath`, `ReportFolderPath`, tracker paths, staging folder, daily log folder), `Environment`, the schedule time zone and the extraction service URL, and no credential, password, key or address of a person.
5. Given any template cell, then a mailbox, password, key or token value is not present; credential columns, where needed, hold a secret name (H3).
6. Given a template is loaded by a flow with a required header missing, then the flow reports the header name and fails that sheet only (the check is in the flow stories; this story supplies the header list).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| H1.1 | Entity profile and column map templates |  |
| H1.2 | Customer rules and recipient templates |  |
| H1.3 | Entity table and settings templates | One flag per flow, paths, time zone, extraction service URL |

**Test notes.** Open each template through the sheet engine (`POST /batch`) and confirm headers read by name. Check that no cell holds an address or a credential.

**Open items.**
- Which entity belongs to which flow is for the process owner; the table in [configuration-workbooks.md](../configuration-workbooks.md) shows today's coverage only. RARE, AEPL, RHSPL, ONESIS and the International entity (Southern Cross Protection) have gaps.
- Whether configuration stays as `.xlsx` on a share or moves to SharePoint or Google Sheets (all three are readable).
- The spreadsheet advice formats per entity and the customer rules content are supplied by the owner; the templates carry sample rows only.
- Recipients for ONESIS (none exist).
- Where the templates live in the repository (proposed `tracto/docs/SIS/templates/`).

---

## H2 — Operational documentation
| Field | Value |
|---|---|
| Epic | H: Configuration templates and documentation |
| Type | Story |
| Depends on | G1, G2, H1 (and the flow stories for the content) |
| Capability today | Partly. Design documents exist for all four flows and the hand-off in `docs/SIS/`; `docs/scheduling.md` covers schedules. No runbook or configuration guide exists. To build |

**User story.** As an operator, I want a runbook per flow and a configuration guide, so that I can run, rerun, diagnose and configure the flows without the developer.

**Context.** The runbooks cover the four flows, the International collection accounting run and the extraction process: what runs when, what each status means, how to rerun only unfinished items, what to look at when a flow fails, and what the output folders are ([folders-and-files.md](../folders-and-files.md)). The configuration guide explains each sheet in H1 and how to add an entity, customer, mapping or recipient.

**Acceptance criteria.**
1. Given each of the six runbooks, then it states the schedule, the main job id, the service endpoint, the input and output folders, the per-item statuses, the rerun procedure and the failure notification it triggers.
2. Given an operator follows a runbook's rerun steps on a test day, then only unfinished items run and finished items are not repeated.
3. Given the configuration guide, then every sheet and header in the H1 templates is described with an example row, and "add an entity", "add a customer rule" and "change a recipient" are step lists that need no code change.
4. Given the hand-off section, then it states that the loader reads only the first sheet of `Extracted payment advices.xlsx` for today's date, that a workbook is written once after extraction finishes, and where the parallel-run tree is.
5. Given any document in this set, then it contains no credential, address of a person, customer name, bank account or amount.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| H2.1 | Runbooks for the four flows, the International collection accounting run and the extraction process |  |
| H2.2 | Configuration guide |  |
| H2.3 | Parallel-run and cutover section: separate output tree with the same layout, file comparison, cutover of the real path | [collection-accounting-handoff.md, "During the parallel run"](../collection-accounting-handoff.md) |

**Test notes.** A person who did not write them follows each runbook on the test environment; failures of the steps are corrections.

**Open items.**
- Which side (UiPath or Tracto) starts `bot_run.py` for each International entity during the parallel run, recorded in the B4 runbook.
- Flow 4's folder paths against a real run ([folders-and-files.md, "Points to confirm", item 1](../folders-and-files.md)).
- Whether the queue is a SQLite table or a sheet changes the rerun and inspection steps in three runbooks.

---

## H3 — Secrets and credentials
| Field | Value |
|---|---|
| Epic | H: Configuration templates and documentation |
| Type | Story |
| Depends on | none; A1 and A2 consume it |
| Capability today | Partly. `${secret:NAME}` is resolved by the browser runtime only: it reads environment variable `TRACTO_SECRET_<NAME in capitals>` on the worker and never logs it (`browser-runtime/src/TractoRuntime.Browser.Host/BrowserRunEngine.cs`, `ResolveSecrets`; `browser-runtime/src/TractoRuntime.Browser.Host/.env.example`). The desktop runtime has no secret reference: `Tracto.Desktop.Security` holds `SecretRef`, `ISecretResolver` and `DpapiSecretResolver`, which no action handler uses, and `limits.md` states there is no mechanism. In the Orchestrator, `redactSecrets` masks Warning and Error run logs (`orchestrator-go/internal/handlers/logs.go`), `containsSecret` rejects a result payload or output holding secret material (`orchestrator-go/internal/handlers/result.go`), and the audit middleware masks `password` and `secretRefs` in request bodies (`orchestrator-go/internal/middleware/audit.go`). The sheet-service reads credentials only from environment variables or a `.env` file next to the executable, with the real environment winning (`sheet-service/main.go`, `sheet-service/internal/envfile/envfile.go`). Not there: publish-time refusal of a script that holds a credential (`orchestrator-go/internal/handlers/artifacts.go` `Publish` checks only base64 and the segment structure), a `${secret:NAME}` reader for Go services, masking in the Go services' own logs (they use the standard `log` package), and protection of the Microsoft token store (`ms_tokens` in `sheet-service.db`, `sheet-service/internal/msauth/store.go`, holds the refresh token in an ordinary SQLite file). Partly |

**User story.** As a security owner, I want no credential in any configuration sheet, script or log, with the mailbox credentials of every entity held encrypted in one store that developers can add to without seeing existing values, and every other secret supplied as a named secret from the service environment, so that the credentials exposed by the UiPath packages are not re-created in Tracto.

**Context.** All five packages carry credentials in clear text or write them to a log, each described under "Security findings" in the flow documents: [SIS-01](../SIS-01-bank-avail-check.md) (an Azure AD client secret and a mailbox password in a shared workbook, outside the log masking), [SIS-02](../SIS-02-email-pa-dispatcher.md) (a mailbox password written to the run log at `Info` on every run), [SIS-03](../SIS-03-india-excel-extraction.md) (SMTP credentials as literals in four mail activities and a live copy of the estate workbook in the package) and [SIS-04](../SIS-04-payment-advice-extraction.md) (an OCR API key, about fifty PDF passwords hard-coded in two workflows, an SMTP credential, and run artefacts and screenshots in the package) and [SIS-05](../SIS-05-international-bot1.md) (an Azure AD client secret and a mailbox password in `MasterConfig.xlsx`, and a test workflow in the package that mails a production report to a hard-coded address). In Tracto, configuration sheets hold secret names only; values come from the environment of the worker or service as `${secret:NAME}` (`TRACTO_SECRET_<NAME>`); Microsoft mail uses the service's own device-code sign-in so there is no mail secret for those accounts. Rotation of the existing credentials is a separate checklist that does not wait for the build.

**Secrets that move (names only).**

| Source key or asset | In Tracto | Name |
|---|---|---|
| `Application Secret`, `APP ID`, `Tenant ID`, `senderID`, `password` (flow 1 workbook keys), and `APP ID`, `Application Secret`, `Tenant ID`, `<entity>_senderID`, `<entity>_password` (the International Bot 1 `MasterConfig.xlsx`) | Not read from the workbooks. A sending account that needs a tenant, client and secret is a row of the mail account store | store row |
| `DTSS_EmailPassword`, the Orchestrator assets `{entity}_{AssetCredentialName}`, and each entity mailbox's tenant ID, client ID and client secret | One row per entity mailbox in the mail account store, referenced from the entity table by account name | store row |
| API `Username` and `Password` of the report API (unused sheets) | Not carried | none |
| SMTP accounts in flow 3 (four activities) and flow 4 (`Notifications\Missing Information.xaml`) | Not carried; mail goes out through Graph (A2) | none |
| OCR API key (flow 4, `DU Extract.xaml`) | Not carried; replaced by the extraction service. The caller's key for that service | `${secret:EXTRACTION_API_KEY}` |
| LLM provider key (E3) | Service environment | `${secret:LLM_API_KEY}` |
| About fifty PDF open passwords (hard-coded in two workflows and in `PasswordsPDF.txt`) | One secret holding the list, read by the Go step that unlocks PDFs; location of the list is open | `${secret:PDF_PASSWORD_LIST}` |

**Acceptance criteria.**
1. Given any template or live configuration sheet (H1), when a flow loads it, then a cell value matching a credential pattern (the patterns in `orchestrator-go/internal/handlers/result.go`: `password=`, `secret`, `api_key`, `token`, `bearer`, `-----BEGIN`) makes the load fail with the sheet, row and column named and the value not printed.
2. Given a column that names a secret (for example `Mailbox` password reference), when the value is `${secret:DTSS_IMAP_PASSWORD}`, then the Go service reads environment variable `TRACTO_SECRET_DTSS_IMAP_PASSWORD`; when the variable is not set the item fails with `SECRET_NOT_SET` naming the variable, and no other item is affected.
3. Given a Go service resolves a secret, then the value is held only for the call that needs it, is never returned in a response, never written to a work item, `Message`, tracker, workbook or error text, and `GET /health` and any config echo show names only.
4. Given a script is published with a literal `password`, `secret`, `token`, `api_key` or bearer value in a step parameter or in `params`, then `PUT /artifacts/{id}/versions/{version}` refuses it with `422 SECRET_IN_SCRIPT` naming the step and parameter, and the validator reports the same.
5. Given the five main-job scripts and any schedule `params`, then none contains a credential (the main job's only input is a service URL and non-secret filters).
6. Given a Go service logs a request, a response, a command's output or an error that contains a configured secret value or text matching the patterns in criterion 1, then the logged text shows `[redacted]`; request and response bodies of mail, IMAP and extraction calls are not logged at all.
7. Given a test that runs each flow end to end against test mailboxes with known dummy secret values, then a search of every log file, the work-item store, the tracker and output workbooks, the run records, the Orchestrator's `/admin/logs` and its audit table finds none of the dummy values.
8. Given the Microsoft token store, then `sheet-service.db` is readable only by the service account (file permissions set at creation and checked at start-up), and start-up fails with a clear message when it is world-readable.
9. Given the rotation checklist, then it lists every credential in the table above and every credential in the five packages and in the collection accounting service (counts and names, no values: the MySQL root password in `DBClasses\mysql_helper.py`, the report API credentials and six mailbox passwords in the `settings` table, the API credentials written into `bot_run_log` with each statement URL; in the International copies, the MySQL credentials in `DBClasses\mysql_helper.py`, and in the International `MasterConfig.xlsx` the application secret, four mailbox passwords and the API password): the Azure AD client secret, the mailbox passwords, the logged mailbox password and the old log retention, the SMTP passwords and the revoked app password, the report-API credential, the OCR API key and the PDF passwords; it names an owner and a done date per row and is completed independently of the build.
10. Given the repository, then `.env` files are ignored by version control, and no published package or script bundle contains a configuration workbook, a `SourceDownload` folder, a debug log or screenshots.
11. Given the mail account store (table `mail_accounts` in `sheet-service.db`), then each row holds the account name, kind (`graph-app`, `graph-delegated`, `imap`), mailbox address, tenant ID, client ID or host, port and user in plain columns, the client secret or password in a column encrypted with AES-GCM under the key in `TRACTO_SECRET_KEY`, the secret's expiry date, and who changed it and when.
12. Given `PUT /admin/mail-accounts/{name}` with a secret, then the secret is encrypted before it is stored; `GET /admin/mail-accounts` returns every field except the secret, showing its last four characters and expiry date only; no endpoint returns a secret.
13. Given the service starts without `TRACTO_SECRET_KEY`, or with a key that does not decrypt a stored row, then it refuses to start and names the problem without printing any value.
14. Given a stored secret expires within 30 days, then the operations recipients receive one warning mail naming the account and the date, repeated weekly until the row is updated.
15. Given a copy of `sheet-service.db` alone, then no secret can be read from it.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| H3.1 | Secret inventory: map each key above to a `${secret:NAME}` or to "not carried", confirm names with the process owner |  |
| H3.2 | Go-side secret reader: `${secret:NAME}` to `TRACTO_SECRET_<NAME>`, same convention as the browser runtime, `.env` loading through `envfile` |  |
| H3.3 | Configuration guard: reject credential-looking cell values; secret columns hold names only |  |
| H3.4 | Log masking in the Go services: value-based and pattern masking, no body logging |  |
| H3.5 | Publish-time refusal of a script holding a literal credential, and the same rule in the validator | `orchestrator-go/internal/handlers/artifacts.go`, `.claude/skills/tracto-desktop-rpa/scripts/validate.js` |
| H3.6 | Token store protection: file permission at creation and check at start |  |
| H3.7 | Tests: dummy secrets through each flow, search of logs, stores and workbooks |  |
| H3.8 | Rotation checklist (independent of the build) | Produced and executed outside the build schedule |
| H3.9 | Mail account store: table, AES-GCM encryption, key from `TRACTO_SECRET_KEY` | Used by A1 and A2 |
| H3.10 | Write-only admin endpoint for mail accounts, with audit of changes | |
| H3.11 | Expiry warning for stored secrets | Uses A2 |

**Test notes.** Dummy secrets with unmistakable values set as environment variables on the test service; test mailboxes; a script and a configuration sheet seeded with credential-looking text. Checks: criteria 1 to 8 by search of logs, stores, workbooks and Orchestrator tables; the rotation checklist is reviewed by the security owner and is not a test of code.

**Open items.**
- The final secret names, and which mailbox accounts use IMAP (each needs one `${secret:...}`).
- Where the PDF password list lives: held by Tracto and sent one at a time, or by the extraction API ([extraction-api-exists-and-additions.md](../extraction-api-exists-and-additions.md)).
- Mailbox credentials are in the encrypted store; the remaining secrets stay as environment variables on the service machine (the platform has no vault integration; `docs/UIPATH-TRACTO-CAPABILITY-MAPPING.md` lists vault integration as not built). The store reads secrets through one interface, so a vault can replace it later.
- Whether the desktop runtime needs `${secret:NAME}` at all: the five main jobs carry no secret; the existing `DpapiSecretResolver` is unused.
- The retention of existing UiPath logs that already hold a mailbox password; rotating does not remove them ([SIS-02, "Security findings"](../SIS-02-email-pa-dispatcher.md)).
- Whether `ms_tokens` should be encrypted (DPAPI) rather than only permission-restricted.

---

## Cross-epic dependencies

- A1, A2, A3, A5 and A6 are needed by flow 2 (epic C); A2, A3 and A4 by flow 1 (epic B); A3, A6 and A2 by flow 3 (epic D); A3, A5, A6 and A2 by flow 4 (epic F) and, through the work-item queue, by the extraction process (epic E).
- A5 (work items) is written first with the queue choice re-confirmed; C3 adds items and F-epic consumers claim them.
- A3.4 (root restriction) applies to A1.4, A4 and A5; build A3 before them.
- A2 and A1 share the sign-in scope change in A1.1; one consent covers both.
- G1 needs the `/…/start` endpoints of epics B, C, D and F; G2 needs A2 for notifications and G1 for the jobs to schedule.
- H1 supplies the sheets read by B1, C1, D1, D2 and F1; H3.3 depends on H1 for the cell rules.
- H3.2 is consumed by A1.3 (IMAP password) and E3 (provider key, extraction service key) and by the PDF unlock step in E1.
- H2 follows every flow story and is finished last.
- A2, A3, A4 and A6 are needed by the International collection accounting run (B4).
