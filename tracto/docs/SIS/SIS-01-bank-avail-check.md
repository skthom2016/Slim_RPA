# SIS flow 1: BankAvailCheck in Tracto

How the UiPath process `BankAvailCheck` 1.0.4 is rebuilt on Tracto: what exists today, what has to be built, and the shape the flow takes.

## In brief

**What it is for.** Every working morning, before the bank-statement processing starts, somebody needs to know whether today's statement files have arrived for every company in the group. This process answers that question, tries to fix it where the answer is no, and mails the result.

**What it does.** For each entity it looks for today's statement folder. If the folder is there, that entity is fine. If it is not, the process starts a second bot that downloads the statements from the bank, looks again, and if they are still missing, opens the entity's check workbook and works out exactly which bank accounts have nothing. It then mails one list of the entities that are fine and, when anything is missing, a table of the missing accounts.

**What it becomes in Tracto.** A schedule starts a one-step main job, which calls a Go process and ends. Go does the whole loop: folder checks, the spreadsheet reads, the comparison, the HTML table and the two mails. The fetch bot is a console script, so Go starts it directly and waits for it — **there is no script and no worker anywhere in this flow.**

**The short version of what is missing:** a way to ask whether a folder exists, a way to send mail, and a way to treat a list of entities as items. All three are shared with the other flows. Open questions are at the end of this document.

## What the UiPath process does

Every run checks whether each entity's MT940 bank statements have arrived for today, triggers a second bot for the ones that are missing, and mails two summaries.

1. **Read the configuration.** `Framework\InitAllSettings.xaml` reads `E:\Automation\Data\Config\MasterConfig.xlsx`, sheets `Settings`, `Entities` and `Constants`, into a key/value dictionary. Keys used later: `entities` (a comma-separated list), `avail_receiver_id`, `cc_receiver_id`, `DTSS_receiver_id`, and the five Office 365 keys `APP ID`, `Application Secret`, `Tenant ID`, `senderID` and `password`.
2. **Prepare.** Build an empty table with columns `Entity` and `Bank Account Number`. Set today's date as `dd MMM yyyy`, and the statement root to `C:\Automation\Data\Input\Statements1\BankStatements`.
3. **For each entity**, check whether the folder `<root>\<today>\<entity>` exists.
   - **It exists:** the entity is available. Add it to the available list.
   - **It does not exist:** write the entity name into `BankMaster.xlsx`, sheet `EntitySelected`, cell `A2`; run `C:\xampp\htdocs\prod\run_Bank_bot.bat` through PowerShell, which is a second bot that fetches the statements; then check the folder again.
     - **It exists now:** add the entity to the available list.
     - **It still does not exist:** read `<root>\<today>\<entity>_check\bank_statements_check.xlsx` (`A1:E200`, columns include `BANK_ACCOUNT_NAME` and `AVAILABLE`) and the entity's own sheet in `BankMaster.xlsx` (`A1:A40`, a list of bank account names). For every bank in that list, every row of the check file with the same `BANK_ACCOUNT_NAME` and `AVAILABLE = "N"` is added to the unavailable table.
4. **Build an HTML table** of the unavailable rows (after removing duplicates), with a heading "Unavailable Bank List" and the columns Entity and Bank Account Number.
5. **Mail the available list** to `avail_receiver_id`, copying `cc_receiver_id`, subject `MT940 statement status`, plain text. This mail is sent unconditionally, so a run where nothing is available still sends "The following entities  bank statements are available."
6. **If anything was unavailable,** mail the HTML table to `DTSS_receiver_id`, same subject, HTML body. The recipient key is built from a literal `ent = "DTSS"` in the workflow, so whichever entity is missing statements, the mail goes to the DTSS list.

Both mails go through an Office 365 Scope that reaches Microsoft Graph with `APP ID`, `Application Secret`, `Tenant ID`, `senderID` and `password` all read from the configuration workbook, with `AuthenticationType` set to `InteractiveToken` — an application registration and a user password supplied together, only one of which can be the one in use.

There is no `Try`/`Catch` anywhere in `Main.xaml`. A missing check workbook, a failed PowerShell call or a Graph authentication failure ends the run with an unhandled exception, part-way through the entity list and before either mail is sent.

Although the project carries the REFramework folders, `Main.xaml` does not use them: there is no transaction queue, no retry and no `SetTransactionStatus`. It is one sequence with nested loops and conditions.

## What this flow actually needs

Almost none of it is UI automation. There is no browser, no desktop application, and nothing typed into a window. What it really does is:

| Work | Kind |
|---|---|
| Read a key/value configuration sheet | spreadsheet |
| Test whether a folder exists | filesystem |
| Write one cell | spreadsheet |
| Run a batch file that starts another bot | a process on a machine |
| Read two sheets and match rows between them | spreadsheet |
| Build an HTML table | text |
| Send two mails, one of them HTML | mail |

In Tracto the first, third and fifth are sheet-service work that exists today. The others are the gap. The batch file is a console script rather than something that drives a desktop session, so **nothing in this flow needs a worker at all** — Go can start the process itself, wait for it and read its exit code.

This matters for the shape: the flow is not a script with a loop around it. It is a Go process from end to end, and the only thing Tracto's job system contributes is the schedule, the run record and the audit trail.

## The shape in Tracto

```
Schedule (Orchestrator, e.g. 0 7 * * 1-5 Asia/Kolkata)
   │  queues
   ▼
Main job (desktop, one http.post)  ──POST /bankcheck/start──▶  Go service
   ends in seconds                                               │
                                                                 │ reads MasterConfig.xlsx  (sheet engine, exists)
                                                                 │ for each entity, in order:
                                                                 │   folder exists?            (new: filesystem check)
                                                                 │   no → write BankMaster!EntitySelected!A2  (exists)
                                                                 │        run run_Bank_bot.bat and wait   (new: run a program)
                                                                 │        folder exists now?
                                                                 │        no → read both sheets, match rows     (exists)
                                                                 │ build the HTML table                          (new: in Go)
                                                                 │ send the available mail                       (new: mail API)
                                                                 ▼ send the unavailable mail if any
                                                          record the batch result
```

The main job starts the process and ends, so it holds no worker while Go works — and since the fetch bot is a console script Go can start itself, no worker is used at any point. This is the rule the skills state, in its simplest form: no job waits for another job, only Go waits.

## What exists today

| Need | What covers it |
|---|---|
| Read a sheet range into rows | sheet-service `POST /batch` (`.xlsx`, Google Sheets, SharePoint/OneDrive) |
| Read one cell by header and row | sheet-service `POST /cell` |
| Write cells back | sheet-service `POST /update` |
| Queue a child job per item and wait for it | sheet-service `POST /process/start` (per sheet row) |
| Run the whole thing on a timetable | Orchestrator schedules, `POST /admin/schedules`, cron in a named time zone |
| Microsoft sign-in without a password in any file | `internal/msauth`: device-code sign-in, token stored and refreshed |

## What has to be built

Five pieces. Four of them are generic and will serve the other SIS flows; one is specific to this flow.

### 1. Filesystem checks in Go (generic)

The desktop runtime has no `file.exists`, and this flow turns on that question three times per entity. A Go endpoint answers it without a worker, without a desktop session, and with no window to steal focus.

```
POST /fs/exists    { "path": "C:/Automation/.../13 Nov 2026/ENTITY", "type": "folder" }
                   → { "exists": true, "type": "folder", "modifiedAt": "..." }
POST /fs/list      a folder's entries, optionally filtered by a glob
```

A path allow-list in the service configuration keeps it from being a general-purpose file reader for anything that can reach the port. Restricting it to configured roots is part of the work, not an extra.

Deciding against the alternatives: a desktop `file.exists` action would make the main job wait for a worker just to call `os.Stat`, and `cmd /c if exist` through the Run dialog cannot return an answer to anything.

### 2. Sending mail from Go (generic)

All four SIS flows end in a mail, and this one needs an HTML body. Driving Outlook on the web with a browser child is wrong for this: a typed compose box cannot carry an HTML table, every run costs a browser and a signed-in profile, and a profile serialises every mail behind one run.

```
POST /mail/send    { "to": ["..."], "cc": ["..."], "subject": "...",
                     "body": "<html>…", "isHtml": true,
                     "attachments": [ { "path": "..." } ] }
```

Backed by Microsoft Graph `sendMail`, reusing `internal/msauth` exactly as the SharePoint reader does: the same device-code sign-in, the same token store, with `Mail.Send` added to `MS_SCOPES`. No password anywhere, which also removes `Config("password")` from the configuration sheet.

### 3. A list as a source of items (generic)

`POST /process/start` iterates the rows of a sheet. Here the items are the entities from one configuration value. This is the item-source gap already recorded in the capability mapping: the same batch behaviour (one item at a time, a result per item, rerun only what is unfinished, progress and cancel) over a different source.

Either a `source` that accepts a literal list and a key/value sheet lookup, or a sibling endpoint. The entity list could equally be modelled as the rows of the `Entities` sheet, which `POST /process/start` already handles — that is worth considering, because it makes each entity's status and message visible in a sheet instead of only in a batch record.

### 4. A decision between child jobs in Go (generic)

The entity loop is: check, act, check again, and only then compare. That is two conditions per entity, and a script can hold neither. This is the branch API the skills describe as not built: Go evaluates a rule on a value it holds and queues child A, B or C, with a default when nothing matches.

For this flow the rule is just "does this folder exist", so a general rule language is not needed on day one. What is needed is the step shape: **run a child, look at the result, decide what happens next**, inside the Go loop.

### 5. Running a local program from Go (small, generic)

Start a configured command, wait for it with a timeout, capture its exit code and its output.

```
POST /run    { "command": "bank-fetch", "args": ["DTSS"], "timeoutSeconds": 600 }
             → { "exitCode": 0, "stdout": "...", "stderr": "...", "timedOut": false }
```

**Named commands, never a command line from a caller.** The service holds a short list of programs it is allowed to run, given in its configuration, and a request names one of them. An endpoint that runs whatever string it is handed is a remote shell on the machine holding the finance files, and nothing in these four flows needs that.

### 6. The BankAvailCheck process itself (flow-specific)

The per-entity sequence, the matching of the two sheets on `BANK_ACCOUNT_NAME` with `AVAILABLE = "N"`, the HTML table and the two mails.

The matching step deserves attention: it is a join between two sheets, read-only, with no UI. In Go it is a few lines over rows already in memory; attempted in a script it is impossible, because a script has no loop, no comparison and no list. Writing it as a reusable `POST /sheet/match` (rows of A whose column X appears in column Y of B, optionally filtered) would serve other flows, and the next three SIS flows will show whether that is worth generalising.

## The scripts to write

**One, and it does nothing but start the process.** `bankcheck-main` is a desktop main job with a single `http.post` to `/bankcheck/start`, exactly like the email batch main job. It ends as soon as Go replies.

Everything else is Go, including the fetch bot: `run_Bank_bot.bat` is a console script, so Go starts it as a child process, waits for it to finish and reads its exit code. That is better than the alternative in every way that matters — an exit code instead of a guess, no 30-second cap on how long it may take, no window to steal focus, and no machine that has to be logged in.

What Go still cannot learn from an exit code is whether the bot actually *fetched* anything, so the folder check after it runs stays.

## What the configuration sheet becomes

`MasterConfig.xlsx` stays where it is and keeps its shape; sheet-service reads it. Two changes:

- **All five Office 365 keys are removed.** `APP ID`, `Application Secret`, `Tenant ID`, `senderID` and `password` go, because mail goes out on the service's own Microsoft sign-in. The client secret in particular does not belong in a workbook that is opened by a robot and shared across the estate; see the security finding below.
- **The hard-coded paths move into it.** `C:\Automation\Data\Input\Statements1\BankStatements`, `E:\Automation\Data\Config\BankMaster.xlsx` and the batch file path are literals inside the UiPath workflow today, including a drive letter that differs between them. In Tracto they belong in `Settings` next to the rest, so the flow can run on another machine without an edit to a published script.

## What improves, and what to watch

**Improves**

- **No password in a file.** Mail uses a token that refreshes itself.
- **A failed entity does not end the run.** The batch records each entity separately, so one bad folder or one unreadable check file leaves the others alone. The UiPath version has no exception handling at all, so a missing `bank_statements_check.xlsx` on the first entity ends the run before any mail goes out.
- **The right people are told.** Each entity's unavailable list goes to that entity's recipients, instead of every entity's gaps landing on the DTSS list because of a literal in the workflow.
- **Nothing is mailed when there is nothing to say.** The summary is sent because there is a result, not unconditionally.
- **The run is inspectable.** Each entity's outcome, each child run id, and why a row failed, through the batch endpoint instead of only in the robot's log.
- **Rerunnable.** Running the batch again does only the entities that did not finish.

**Watch**

- **The second bot is outside Tracto.** `run_Bank_bot.bat` is someone else's process. Tracto can start it and watch for its output folder, but cannot see its state, and whatever it writes is trusted blindly. How long Go waits for the folder to appear has to be a setting, and a sensible timeout per entity.
- **The date folder is local time.** `dd MMM yyyy` from the machine's clock. Which time zone decides "today" needs to be stated, especially with a scheduled run near midnight, and the Orchestrator schedule already names a zone.
- **`A1:E200` and `A1:A40` are fixed windows.** A check file with more than 200 rows silently loses the rest. In Tracto, `POST /batch` pages to the end of the data, so the limit can go, but only if it was a UiPath convenience and not a real bound.
- **Writing `EntitySelected!A2` is shared state.** Two entities processed at once would overwrite each other's cell, which is a reason the loop stays sequential, and a reason to ask whether the second bot could take the entity as an argument instead.

- **Read ranges and column names.** The report column is called "Bank Account Number" but carries the value matched from `BANK_ACCOUNT_NAME`, an account name. Worth settling which it should be before the same heading is carried into Tracto.

## Security finding in this package

**The Office 365 credentials live in a shared configuration workbook.** `MasterConfig.xlsx` holds `APP ID`, `Application Secret`, `Tenant ID`, `senderID` and `password`, and `Main.xaml` reads all five and hands them to the Office 365 Scope. An Azure AD client secret in an Excel file on a file share is readable by everyone who can open the file, and the project's log masking only covers keys matching `*password*`, so the other four are not even excluded from the logs.

The secret should be rotated and moved out of the workbook independently of any Tracto work. On Tracto the same mail goes out on the service's own Microsoft sign-in through `internal/msauth`, so there is no secret to store and nothing to mask.

## Build order

1. `/fs/exists` — small, self-contained, needed by other flows.
2. `/mail/send` on Graph — needed by all four flows, reuses the existing sign-in.
3. The list or sheet source for the entity loop, and the child-then-decide step inside it.
4. `POST /run` for the fetch bot.
5. The BankAvailCheck process that composes them.

The first two are worth building before the remaining three SIS flows are designed, because every one of them will want both.

## Decisions taken

| Question | Decision |
|---|---|
| Does the fetch bot need a desktop session? | No. `run_Bank_bot.bat` is a console script, so Go starts it directly and reads its exit code. This flow has no child job and no worker. |

## Open questions

These need answers before the Tracto flow can be built. They are not blockers for designing it, but each one changes something concrete.

### For the process owner

1. **How long may Go wait for the fetch bot, and does its exit code mean anything?** It is a console script, so Go gets an exit code — but whether a zero means "statements fetched" or only "script ran" needs checking, which is why the folder check after it stays. The timeout is a business decision: how long is "the bank is slow" before it is "the bot is broken"? Also needed: whether running it twice for the same entity is harmless, in case a run is repeated.
2. **Can the fetch bot take the entity as an argument?** Today the entity name is written into one cell of a shared workbook and the bot reads it. If the bot can accept it on the command line instead, the shared cell disappears and entities could eventually run in parallel. If it cannot, the loop stays strictly sequential and we should say so deliberately.
3. **Which time zone decides "today"?** The folder name is `dd MMM yyyy` from the machine's clock. A run near midnight, or a machine in another region, looks in the wrong folder. The schedule names a zone; the folder date should come from the same answer.
4. **Should each entity's missing accounts go to that entity's people?** Today every unavailable list goes to the DTSS recipients because of a literal in the workflow. If that is a bug, the entity table needs a recipient column. If it is deliberate because DTSS operations handle all of it, that should be written down.
5. **Are `A1:E200` and `A1:A40` real limits?** An entity with more than forty accounts, or a check file with more than two hundred rows, silently loses the rest today. In Tracto the limit can simply go — unless those bounds mean something.
6. **What should the report column be?** It is headed "Bank Account Number" but carries an account *name*. Which one the operations team actually wants decides what we extract.
7. **Should the summary mail go out when nothing is available?** It is sent unconditionally today, including the empty case.
8. **Where will the configuration live?** `MasterConfig.xlsx` is shared with the other processes and sits on `E:`, while the statements are on `C:`. For Tracto we need to know who owns that file, whether we may add keys to it, and whether it stays an `.xlsx` on a share or moves to SharePoint or Google Sheets — all three are readable, but the choice affects locking and concurrent access.

### Not in Tracto yet

Listed in full under "What has to be built"; in short: `POST /fs/exists` and `/fs/list`, `POST /mail/send` on Graph with an HTML body, a list or sheet as a source of batch items, and the ability for Go to run a child job and then decide what happens next based on the result. Nothing here needs a new concept — it is all Go work over patterns that already exist.
