# SIS flow 2: EmailPADispatcher in Tracto

How the UiPath process `EmailPADispatcher` 1.0.10 is rebuilt on Tracto. This is the flow that feeds the others: everything it produces is work for a later process to pick up, so the decision it forces is what a work queue is in Tracto.

## In brief

**What it is for.** Customers mail their payment advices in — as PDFs, as photos, as spreadsheets, as ZIP files, or as the text of the mail itself. Somebody has to open every mailbox, work out which customer sent what, file it where the processing robots will find it, and chase the sender when a file cannot be opened. This process is that first stage; everything downstream works from what it files.

**What it does.** For each entity in turn it reads the unread mail in that entity's mailbox, works out the customer from words in the mail body, saves each attachment under a standard name in a folder by date, entity and file type, unpacks any archives, and raises one work item per file for the processing flows to pick up. If a file cannot be handled it replies to the sender asking for it again. It records every artefact in a tracker spreadsheet.

**What it becomes in Tracto.** A schedule starts a one-step main job, which calls a Go process and ends. There is no script and no worker in this flow at all: it is mail, files and spreadsheets from end to end. In UiPath every one of those steps had to run inside a robot holding a desktop session.

**The decision this flow forces:** what a work queue is in Tracto, since everything this flow produces is work for a later process. The working default is **a queue-items table in SQLite**, with a sheet as the simpler alternative that is still used for configuration and output; which of the two is confirmed before the queue is built. **The biggest thing missing is a mail reader** — Tracto cannot read a mailbox at all today. Open questions are at the end of this document.

## What the UiPath process does

It watches entity mailboxes, files every incoming payment advice into a dated folder by customer and file type, and raises a queue item for each one.

**The transaction loop (REFramework).** Unlike flow 1 this is a real state machine: Init, Get Transaction Data, Process Transaction, End Process, with a business/system exception split. The transactions are entity names, read one at a time from the configuration as `PriorityEntity_1`, `PriorityEntity_2` and so on, as many as `TotalNumberOfEntity` says.

The retry, however, does not exist: `SetTransactionStatus.xaml` has been emptied to a shell with no status update and no retry bookkeeping, and the transaction number advances only on success. An entity either works or fails once per run, and the next run is the retry.

**Per entity (`Process.xaml`):**

1. `ReadMails.xaml` fetches the mail. The mailbox credential is resolved by a chain — an Orchestrator credential asset first, then the Windows Credential Store, then an interactive prompt that stores whatever is typed — and the password is converted out of a `SecureString` into a plain string that is then passed on to every workflow below it. **Get IMAP Mail Messages** reads unread mail from `Inbox` over SSL on port 993, marks it read, and sorts it by the `Date` header. The server is `outlook.office365.com` for entity `RARE` and `mail.sisindia.com` for the rest.
2. If any mail arrived, `EachMailProcess.xaml` runs.
3. `WriteLogData.xaml` appends what happened to an Excel `Email Tracker` sheet.
4. `Success` is written into cell `B2` of the entity's sheet in `ModuleTrackerPath`, or `Fail` if anything threw. The number of mails fetched is also written next to the `<entity>_MailCount` key in the configuration workbook, found by scanning `A6:A16`.

**Per mail (`EachMailProcess.xaml`):**

1. Make the folders `<PaymentAdvicesFolderPath>\<dd MMM yyyy>\<entity>` with `PDF_JPEG`, `XLS` and `MailBody` inside, plus a temporary download folder.
2. Read the customer list from `CustomersListPath`, sheet named after the entity.
3. **Identify the customer** by lower-casing the mail's plain-text body and testing whether it contains either alias held in columns 3 and 4 of a customer row. The first row that matches wins, and column 2 of that row becomes the customer name. No match, or a mail with no plain-text part at all, means `Others`.
4. **If the mail has attachments,** save them to the temporary folder and route each one by extension:
   - `.pdf`, `.jpg`, `.jpeg` → the `PDF_JPEG` folder, and a queue item on `<entity>_<OrchestratorPDF_JPEGQueueName>`
   - `.xls`, `.xlsx` → the `XLS` folder, and a queue item on `<entity>_<OrchestratorXlsxQueueName>`, **except for entity `DTSS`**, which skips spreadsheets
   - `.zip` → extract it, route every file inside by the same rules, then delete the archive and the emptied folder
5. **If it has no attachments,** save the mail itself as an `.eml` in `MailBody` and raise a queue item on `<entity>_<OrchestratorMailMessageQueueName>`.
6. Every file is renamed `<customer>_<dd MMM yyyy hh mm ss>_<n><extension>`, with `n` counting up across the whole run.
7. Each queue item carries the file name, the entity, the sender address, the mail's `Date` header, the customer name or the attachment type, and the full file path. The mail-body queue carries the customer name; the two attachment queues carry the attachment type instead.
8. **Reply to the sender when something could not be handled.** Three cases send a mail back: a ZIP that would not extract, an attachment that was not on disk after the save, and a file type nobody processes. All three go out from the entity's own mailbox over SMTP on port 465, addressed to the original sender with the original CC and BCC copied in, subject `Re: <original subject>`.

There is also a first pass over the mails in parallel: when a mail's **first** attachment name or its subject contains `PA_<entity>`, **all** of that mail's attachments are saved straight to a hard-coded folder, separate from the one the main loop uses.

## What a queue is for, and what goes in it

Worth being plain about this before the decision below, because the word carries different meanings.

**A queue is the list of work still waiting to be done.** It is not a log of what happened. Flow 2 writes both and they are easy to confuse:

| | Email Tracker | The queue |
|---|---|---|
| Written | after the fact | before the work |
| Read by | a person asking "was that mail handled?" | the next process, to decide what to do |
| Changes | never, append-only | every item moves through its states |
| If it disappeared | the automation still runs | the chain stops |

**Why there has to be one at all.** Reading the mail and reading the documents are two different jobs running at different speeds. A mail run takes forty mails and pulls sixty PDFs out of them in about a minute. Extracting those sixty takes half an hour, because each one goes to a model, sometimes twice. They cannot be one pass: the mail stage would hold a mailbox connection open for half an hour, and a failure on document thirty-seven would leave nobody knowing which of the sixty were done. So stage one stops once the files are on disk and leaves a list; stage two picks that list up, on its own schedule, at its own pace.

**What is actually stored.** Not the PDF, and not its contents — a pointer and a state:

| PAFileName | EntityName | From | TimeDate | CustomerName | AttachmentType | FilePath | Status | Message |
|---|---|---|---|---|---|---|---|---|
| ACME_13 Nov 2026 10 15 30_5.pdf | DTSS | ap@acme.com | 13 Nov 2026 | ACME | pdf | `C:\...\PDF_JPEG\ACME_....pdf` | Pending | |

The file sits in a folder. The entry says that it exists and that nobody has read it yet.

**Why the folder is not enough.** A folder tells you a file is there. It cannot tell you whether it has already been extracted, so a second run does it twice; whether it was tried and failed, so you could retry those four instead of all sixty; whether something is working on it right now; that forty-three of sixty are done; or *why* one of them failed. Representing all that by moving files between folders is how this was done before queues existed, and it loses the reason for the failure.

So the list is not optional. The only decision is where it is kept.

## Where the queue lives

Tracto has a job queue — runs waiting for a worker, a table in the Orchestrator's SQLite — but no business-data queue, nothing that holds work items with their own status and attempt count. Two ways to provide one.

### Option A: a sheet is the queue

Each dispatched file becomes a row in a work sheet with the fields above. The consumer is then what `POST /process/start` already does: a child job per pending row, the result written back, failed rows left for the next run. It needs no new engine, and `POST /batch` already filters pending rows.

**What it gives:** every item visible in a spreadsheet, a stuck item fixable by typing in a cell, a rerun that picks up only what is unfinished.

**What it costs:**

- **Nothing can read it quickly except through the file.** Every read and write opens the whole workbook, and a person with the file open in Excel holds their own lock — the write fails, or their save overwrites what the service wrote.
- **No natural place for an attempt count or an approval record** without adding columns that only the machine reads.
- **`POST /apply` is not atomic.** It reads under the file lock, releases it, then writes under it again, so two callers at once can be handed the same rows. A small fix, but a fix to make.
- **One writer per sheet.** The per-file mutex is inside one sheet-service process; two instances on the same file have no coordination.

### Option B: queue items in SQLite — the default

A `QueueItems` table and a small API over it: add an item, claim the next, move it through `Pending`, `Running`, `Done` and `Failed`, with an attempt count. It follows the SQLite and handler patterns the Orchestrator already uses, and **sheet-service's batch store is already most of it** — items with states, each carrying its child run id and how it ended — so what is new is that items arrive from an API instead of from sheet rows.

**What it gives:** items that Tracto's own flows can claim and update without touching a file; a claim two callers cannot both win; and an attempt count, which flow 4 uses because the extraction service answers `ANALYZER_BUSY` when it is saturated and that means hold the item, not fail it.

**What it costs:** a table and a handful of endpoints. Unlike a sheet, a table is not its own user interface, so seeing into it needs a view — the Tracto console can list the items with their status and message.

### What decides it

**Not the review screen, and not the volume.** An earlier version of this section chose the table because flow 4's review screen needed a store it could read and write item by item. There is no review screen in this migration: the existing web pages over the collection accounting database read their own data and are not touched, and Tracto hands advices over as a workbook. The volume is about a thousand documents a month, roughly 45 on a working day, so parallel extraction and priority ordering are not reasons either.

Three smaller arguments for the table remain:

- **An attempt count has nowhere natural to live** in a sheet that a person also reads.
- **A claim and a completion are two full workbook rewrites per document.** That cost grows with the size of the sheet and not with the amount of work.
- **The file is shared with people.** A write fails, or is overwritten, when somebody has the workbook open in Excel.

Against that, a sheet needs nothing new, every item is visible, and `POST /apply` made atomic covers the claim. At this volume, with nothing that needs two consumers, the sheet is sufficient and is the simpler build.

**Status.** The table stays as the working default so that the estimate and the proposal stay consistent, but it is no longer the clear answer. Confirm sheet or table before the queue story starts.

**Where sheets stay.** Configuration a person edits — the entity table, the format profiles, the customer rules — and the output workbooks the collection accounting service reads. `POST /process/start` keeps working as it does for sheet-row batches; nothing already built stops working.

## The shape in Tracto

```
Schedule (every 15 min, say)
   │
   ▼
Main job (desktop, one http.post) ──POST /maildispatch/start──▶ Go service
   ends in seconds                                                │
                                                                  │ read config + entity list          (sheet engine, exists)
                                                                  │ for each entity, in order:
                                                                  │   fetch unread mail                (new: mail reader)
                                                                  │   read the customer list sheet     (exists)
                                                                  │   for each mail:
                                                                  │     match the customer by keyword  (Go, in memory)
                                                                  │     save attachments or the .eml   (new: mail reader)
                                                                  │     unzip, route by extension      (new: fs API)
                                                                  │     rename and move                (new: fs API)
                                                                  │     add a queue item               (new: queue table)
                                                                  │   append to the Email Tracker      (exists)
                                                                  ▼   mark the entity Success          (exists)
```

No child job at all. Nothing here drives a window, a browser or an application: it is mail, files and spreadsheets. **This flow has no script except the one-step main job**, and even that exists only so the run appears in the Orchestrator alongside the others and can be scheduled, audited and traced like any job.

That is worth stating plainly, because it is the opposite of the UiPath shape, where every one of these steps had to be an activity inside a robot holding a desktop session.

## What exists today

| Need | What covers it |
|---|---|
| Read the configuration and the customer list | sheet-service `POST /batch` |
| Append the work rows and the tracker rows | sheet-service `POST /update` |
| Mark an entity's status | sheet-service `POST /update` |
| Run on a timetable | Orchestrator schedules, cron in a named time zone |
| A consumer that runs one child job per pending item | sheet-service `POST /process/start` — for sheet rows today; the same loop over queue items |
| Microsoft sign-in with no password in a file | `internal/msauth`, device code, token stored and refreshed |

## What has to be built

### 1. Reading a mailbox (new, and the largest piece)

Nothing in Tracto reads mail. This needs a Go mail reader that can, for one configured account:

```
POST /mail/fetch   { "account": "rare", "folder": "Inbox", "unreadOnly": true,
                     "markRead": false, "max": 100 }
   → one record per message: id, from, subject, date, plain-text body,
     and for each attachment its name, size and type
POST /mail/save    { "account": "rare", "messageId": "...", "what": "attachments" | "eml",
                     "folder": "C:/.../temp" }
   → the paths written
```

Two protocols are in play. `outlook.office365.com` is Microsoft 365, where **Graph is the right door**: the same `internal/msauth` device-code sign-in the SharePoint reader uses, with `Mail.Read` added to the scopes, and no password anywhere. `mail.sisindia.com` is a plain IMAP server, which needs an IMAP client and a password from the worker's environment as `${secret:NAME}` or the service's own settings. Both behind one endpoint, chosen per account by configuration.

**Do not mark mail read as the progress marker.** The UiPath flow reads unread mail and marks it read, so a crash between marking and filing loses that mail silently. Writing the work row first and recording the processed message id second makes a rerun safe. A `processedMessages` record keyed by message id is a small table and removes the whole class of problem.

### 2. Filesystem operations in Go (generic, shared with flow 1)

Flow 1 needs `exists`; this flow needs the rest:

```
POST /fs/exists    POST /fs/list      POST /fs/mkdir
POST /fs/move      POST /fs/delete    POST /fs/unzip
```

All restricted to configured root folders. `unzip` must refuse paths that escape the destination folder, because the archives arrive by mail from outside.

Routing by extension (`.pdf`, `.jpg`, `.xls`, `.zip`) is a few lines in Go over the names `/fs/list` returns. Writing it as a script would need a branch per file, which a script cannot hold.

### 3. Sending mail (generic, shared with flow 1)

This flow sends mail itself: three replies to the original sender when a file could not be handled. The same `/mail/send` endpoint flow 1 needs covers it, on the same accounts and the same sign-in, with two additions this flow forces — replying to a message so the thread holds, and carrying the original CC and BCC.

Those recipient lists come from an incoming mail, so they are not a list Tracto chose. Copying them on is the current behaviour and worth keeping, but a reply that goes to everyone a stranger put on a mail should be a decision in configuration rather than a default, and the recipients belong on the work row so it is visible who was written to.

### 4. A list of entities as a source of items (generic)

The same item-source gap as flow 1. Here there is a neater fit than a literal list: the entities with their priority order are already a sheet (`PriorityEntity_1`, `PriorityEntity_2` …), so modelling them as rows of an `Entities` sheet lets `POST /process/start` drive them and gives each entity a visible status and message.

### 5. The queue itself (new, shared with flows 3 and 4)

A `QueueItems` table and the endpoints over it — add, claim, complete, fail, list. It is used only by the Tracto flows and the Tracto console. The closest existing thing is sheet-service's batch store, which already holds items with states and child run ids; this is that, fed from an API. (If the sheet is chosen instead, this item shrinks to making `POST /apply` atomic.)

### 6. An appending write (small)

`POST /update` writes to known row numbers. The Email Tracker wants "add these rows at the end", which needs either an append mode on `/update` or a small `POST /append`. Concurrency matters here: two entities finishing at once must not claim the same row. (The queue no longer needs this, since it lives in SQLite; the tracker and the output table still do.)

### 7. The dispatcher process itself (flow-specific)

The per-entity and per-mail sequence, the customer keyword match, the file naming with its counter, the three sender replies, and the `DTSS` exception for spreadsheets.

## What the configuration becomes

The sheets stay where they are and keep their shape. Three changes:

- **No password is read into a variable.** Microsoft accounts sign in through the token store; an IMAP account reads its password from the worker's environment, never from a sheet and never into a log.
- **The entity list becomes rows,** not `PriorityEntity_1`, `PriorityEntity_2` keys, so adding an entity is adding a row.
- **The hard-coded `E:\Automation\Data\Input\Payment Advices\` in the parallel pre-pass** moves into the settings sheet, where the rest of the paths already live.

## Security findings in this package

**The mailbox password is written to the log on every run.** `ReadMails.xaml` logs `"LoginId: " + loginId + " ..... LoginPwd: " + loginPwdAsString` at `Info` level. The project's masking covers keys matching `*password*`, which this line is not, so the credential sits in the robot logs in clear text for as long as the logs are kept. The affected mailboxes should be rotated independently of any Tracto work, and the retention of the existing logs is worth a thought too, since rotating does not remove what is already written.

**The password is a plain string for most of its life.** It is converted out of a `SecureString` in `ReadMails.xaml` and passed on through `Process.xaml` and `EachMailProcess.xaml` into the SMTP activities, so the log line is the visible symptom of a wider exposure.

**The published package carries real runtime data.** A `SourceDownload` folder ships inside the `.nupkg` with artefacts from earlier runs — mails, payment advices, spreadsheets, images and archives — which is customer data travelling with every deployment of the package.

On Tracto none of this recurs in the same shape: a Microsoft account signs in through the token store with no password to hold, an IMAP account reads its password from the worker's environment, a secret is never written to a log, and nothing is published except the script.

This is the fourth of the four packages with a credential problem; the pattern is worth addressing as its own piece of work, separate from the migration.

## What improves, and what to watch

**Improves**

- **The password stops being logged.** There is no password to log for the Microsoft accounts, and a secret is never written to a log for the rest.
- **A mail cannot be lost by being marked read.** The row is written before the message is recorded as processed.
- **One bad mail does not end the entity.** Each mail is its own item with its own status. The UiPath version has several empty catch blocks — the attachment name read, the attachment save, the FIFO ordering, the inner files of an archive — so those failures leave no trace at all and surface later as a file operation that cannot find what it expects.
- **A failed entity is retried.** The framework's retry is an empty shell today, so an entity that fails is simply not done until the next run. A batch item carries its own retry.
- **Extensions are matched, not searched for.** The current test asks whether the extension *contains* `pdf`, which also accepts `.pdfx`, and two of the comparisons carry a typographic quote in the literal so they can never match anything.
- **No desktop session needed.** The whole flow runs headless, so it does not compete with a signed-in user for the keyboard and is not blocked by a locked screen.
- **The work items are visible.** A queue item in UiPath is inspectable only through the Orchestrator. Here each item has a status, a message and an attempt count that the Tracto console can show, and a rerun picks up only what is unfinished.

**Watch**

- **Customer matching is a substring test on the body.** `body.Contains(keyword)` with the first match winning is order-dependent and will mismatch a mail that mentions two customers, or one whose keyword appears inside another word. Carrying the behaviour over exactly is the safe first step; it is worth knowing it is approximate, and the matched keyword belongs in the work row so a wrong match is traceable.
- **Attachments arrive from outside.** Names can collide, contain path separators or be absurdly long, and archives can be hostile. The rename to `<customer>_<timestamp>_<n>` already removes most of that, and the unzip needs the escape check.
- **`DTSS` skips spreadsheets.** A rule embedded in two conditions deep inside the workflow. In Tracto it belongs in the entity's row as a column, for example `AcceptedTypes`, rather than a name tested in code.
- **The second counter resets per mail, the timestamp per run.** File names are `<customer>_<run timestamp>_<n>`, so two mails for the same customer in the same run are distinguished only by `n`. It works, but it means the file name cannot be traced back to a mail; the work row carries the message id, which is a better link.
- **The parallel pre-pass overlaps the main loop.** It saves `PA_<entity>` attachments to a different folder from the one the main loop uses, and then the main loop saves them again. It also triggers on the *first* attachment's name, so a mail whose second attachment is the advice is judged on the wrong file. Whether any of this is deliberate needs an answer from whoever owns the process before it is rebuilt.
- **The archive cleanup can delete unprocessed files.** The extraction folder is removed when it holds no files **or** no subfolders, where both conditions were presumably meant, so a folder whose inner files were not handled can be deleted right after the sender was told to resend. In Tracto nothing should be deleted until every inner file has a recorded outcome.
- **Mails read by a person are skipped.** The robot takes unread mail only and marks it read, so anyone opening the mailbox to look at a message removes it from the robot's view. Recording processed message ids rather than relying on the read flag fixes this as well as the crash case.

## Build order

1. `/fs/*` and `/mail/send` — shared with flow 1, build them once.
2. `/mail/fetch` and `/mail/save` on Graph for the Microsoft accounts, IMAP second.
3. The processed-message record, so a rerun is safe.
4. The queue table and its endpoints, with something that can show what is in it.
5. The dispatcher process that composes them.

Flows 3 and 4 consume what this one produces, so the item's fields should be settled with those two in view rather than fixed now.

## Decisions taken

| Question | Decision |
|---|---|
| What is the queue? | A queue-items table in SQLite as the working default; a sheet is the simpler alternative at this volume and is to be re-confirmed. Sheets stay for configuration and output. |
| How much volume? | About 1,000 documents a month, roughly 45 a working day. One at a time is enough. |
| Who do replies to a sender go to? | The sender only. The original CC and BCC are not copied on. |
| Does DTSS still skip spreadsheets? | Yes. It stays, held as an `AcceptedTypes` column on the entity row rather than a name in code. |

## Open questions

These need answers before the Tracto flow can be built.

### For the process owner

1. **Sheet or table for the queue?** With no review screen and about 45 documents a working day, the sheet is sufficient and simpler; the table is the working default. Confirm one.
2. **Which mailboxes, and on which platform?** We need the actual entity list with, per entity, the mailbox and whether it is Microsoft 365 or the in-house IMAP server. Microsoft accounts sign in through the token store with no password anywhere; an IMAP account needs a password in the worker's environment. The split decides how much of the flow is password-free.
3. **Is the `PA_<entity>` pre-pass deliberate?** It saves those attachments to a second folder before the main loop saves them again, and it decides by looking only at the *first* attachment's name. It looks like something added for one case and left in. If it is still needed we will carry it over; if not, dropping it removes a whole duplicate copy of the files.
4. **May the customer list be restructured?** Today the aliases are in two fixed columns and the first row that matches wins, which mismatches any mail that mentions two customers. We can keep it exactly as is, but if the sheet can take named columns and more than two aliases, the matching becomes both clearer and better.
5. **What should happen to a file nobody can process?** Today the sender is mailed and the file is left in a staging folder with no record. Should it also appear as a work item with a status, so somebody can see the backlog?
6. **How long do staged files and archives live?** Nothing cleans the staging folder, and payment advices are customer financial documents. A retention rule belongs in the design rather than being discovered later.
7. **What counts as "already handled"?** Today it is the mail's read flag, which means a person who opens the mailbox to look at something removes it from the robot's view, and a crash after marking loses it. The recommendation is to record processed message ids instead — this needs confirming because it changes what a rerun does.

### Not in Tracto yet

The largest piece in all four flows is here: **nothing in Tracto reads a mailbox.** That means Graph for the Microsoft accounts and an IMAP client for the rest, behind one endpoint, plus saving attachments and `.eml` bodies. Beyond that: `/fs/*` including a safe unzip, `/mail/send` with reply and CC/BCC, an appending write, and a record of processed messages. Full detail under "What has to be built".
