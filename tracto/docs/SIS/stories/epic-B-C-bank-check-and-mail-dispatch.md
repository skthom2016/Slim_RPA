# Epics B and C: bank statement availability and mail dispatch

Scope: epic B is flow 1 (`BankAvailCheck`, [SIS-01](../SIS-01-bank-avail-check.md)) and the collection accounting run for the International entities (`International_Bot1_SXP`, [SIS-05](../SIS-05-international-bot1.md)), and epic C is flow 2 (`EmailPADispatcher`, [SIS-02](../SIS-02-email-pa-dispatcher.md)). Each flow is a one-step main job (`bankcheck-main`, `carun-main`, `maildispatch-main`, one `http.post`) plus a Go service that loops entities with its own status per item; no child job and no browser runtime.
Platform capabilities are referred to by ID: A1 mail reading, A2 mail sending, A3 file operations, A4 program runner, A5 work items, A6 appending write.

| ID | Title |
|---|---|
| B1 | Configuration and entity loop |
| B2 | Statement check and fetch |
| B3 | Unavailable accounts and reports |
| B4 | Collection accounting run for the International entities |
| C1 | Mailbox intake |
| C2 | Filing and routing |
| C3 | Work items, tracker and replies |
| C4 | Mail classification |
| C5 | Dispatcher reporting |

Conventions used in every story:
- "Run record" is the Go service's own record of one run: one row per entity (state `Done`, `Failed` or `Skipped`, a message, counters) plus the run's start and end. Nearest existing code: `process_batches` and `process_items` in `tracto/sheet-service/internal/process/store.go`, which are keyed by sheet row and child run id and are not reusable as they are.
- The services are new Go endpoints (`/bankcheck/start`, `/carun/start`, `/maildispatch/start`). A search of `tracto/sheet-service`, `tracto/orchestrator-go` and `tracto/orchestrator-ui` finds no `bankcheck`, `maildispatch`, `/fs/`, `/mail/` or program-runner code today.
- The one-step main job needs only the existing `http.post` action (`tracto/desktop-runtime/src/Tracto.Desktop.Actions/Handlers/HttpPostHandler.cs`). Main-job authoring and publishing belong to epic G.

---

## B1 — Configuration and entity loop
| Field | Value |
|---|---|
| Epic | B: Flow 1, bank statement availability |
| Type | Story |
| Depends on | none (A3 supplies the configured-root restriction the paths rely on) |
| Capability today | Partly. Sheet reads exist: `POST /batch`, `POST /cell`, `POST /update` in `tracto/sheet-service/internal/api/api.go`, `.xlsx` reader in `tracto/sheet-service/internal/sheet/xlsx.go`. The entity loop with a status per item exists only for sheet rows with a child job (`tracto/sheet-service/internal/process/runner.go`); a Go loop with no child job, the run record and `/bankcheck/start` are to build. The schedule time zone is stored (`orchestrator-go/internal/handlers/schedules.go`, `internal/scheduler/scheduler.go`) but is not passed to the job: params come only from `ParamsJson` (`scheduler.go` line 164). |

**User story.** As an operations lead, I want the bank check to read its entities, recipients and paths from configuration and give each entity its own outcome, so that one bad entity never stops the others and no path or recipient is hard-coded in a published script.

**Context.** UiPath reads `MasterConfig.xlsx` `Settings` (key `entities`, comma-separated), hard-codes the statement root `...\Statements1\BankStatements`, the `BankMaster.xlsx` path and the batch file path, derives `dd MMM yyyy` from the machine clock, and has no `Try`/`Catch` so one failure ends the run before any mail ([SIS-01, What the UiPath process does](../SIS-01-bank-avail-check.md)). In Tracto the entities come from one entity table with a flag per flow, paths come from `Settings`, the date comes from the schedule time zone, and each entity has a separate outcome.

**Acceptance criteria.**
1. Given an entity table with the column `Flow1Enabled` set for 15 entities, when `/bankcheck/start` runs, then exactly the flagged entities are processed, in sheet order, and each gets a run-record row.
2. Given `Settings` keys `statements_root`, `bankmaster_path`, `bank_fetch_command` and `time_zone`, when any is missing, then the run ends before the loop with state `Failed` and a message naming the missing key, and no mail is sent.
3. Given the schedule fires at 23:55 Asia/Kolkata (18:25 UTC) and `time_zone` is `Asia/Kolkata`, when the date folder is built, then it is that day's `dd MMM yyyy` in Asia/Kolkata, not the UTC or machine date.
4. Given the schedule passes the time zone as a job parameter (`params.timeZone`), when the main job posts to `/bankcheck/start`, then the body carries it and the service uses it over the `Settings` value.
5. Given entity 3 of 15 throws (unreadable folder, missing workbook), when the loop continues, then entities 4 to 15 are processed and entity 3 shows `Failed` with the message.
6. Given an entity flagged for flow 1 with no recipient on its row, when the loop reaches it, then its availability is still checked and its row records "no recipient"; the unavailable mail for it goes to the fallback recipient list from `Settings` if set, otherwise the row is `Failed` with the message.
7. Given a rerun on the same day after a partial run, when `/bankcheck/start` is called with `rerun=unfinished`, then only entities not `Done` are processed.
8. Given no Office 365 key (`APP ID`, `Application Secret`, `Tenant ID`, `senderID`, `password`) is present, when the service starts, then it runs; if any of the five is present it is ignored and never logged.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| B1.1 | Read entities, recipients and paths from the configuration sheets | `Settings` and the entity table through `POST /batch` |
| B1.2 | Entity loop with a separate outcome per entity | Includes the run record and `rerun=unfinished` |
| B1.3 | Statement date folder derived from the schedule time zone | Includes passing the zone as a schedule param, because the scheduler does not inject it |
| B1.4 | Entity flag and recipient handling: `Flow1Enabled`, missing-recipient outcome (ONESIS has none today) |  |
| B1.5 | Move hard-coded paths into `Settings` and drop the five Office 365 keys from the read |  |

**Test notes.** Needs a copy of `MasterConfig.xlsx` `Settings` and an entity table with 15 rows including one with no recipient, a `BankMaster.xlsx` copy, and a statements root with date folders. Checked: run-record rows per entity, the date at 23:55 and 00:05 in the schedule zone, behaviour when a key is removed, and a forced failure of one entity.

**Open items.**
- Entity lists differ per flow ([configuration-workbooks.md](../configuration-workbooks.md)); the flow 1 list matches the `BankMaster.xlsx` sheets today, and the intended list is for the process owner to confirm.
- Recipient for ONESIS, and whether the DTSS list stays in copy.
- Owner of `MasterConfig.xlsx` and whether keys may be added to it, or the entity table lives in a new workbook.
- Where the Go service runs. The statement root is the same for both sides: the live `api_download_path` setting is `C:\Automation\Data\Input\Statements1`, which is the root flow 1 checks.

---

## B2 — Statement check and fetch
| Field | Value |
|---|---|
| Epic | B: Flow 1, bank statement availability |
| Type | Story |
| Depends on | B1, A3 (exists/list), A4 (program runner) |
| Capability today | To build. No `/fs/exists`, `/fs/list` or `/run` exists in `tracto/sheet-service` or `tracto/orchestrator-go` (searched). The cell write for `EntitySelected!A2` exists: `POST /update` in `tracto/sheet-service/internal/api/api.go` writes by header name and row, with a per-file lock in `internal/sheet/xlsx.go` (inside one process only). |

**User story.** As an operations lead, I want the service to check each entity's statement folder, run the fetch bot when it is missing, and check again, so that an entity is reported available only when its statements are actually on disk.

**Context.** UiPath tests `{root}\{dd MMM yyyy}\{entity}` three times per entity, writes the entity name to `BankMaster.xlsx` sheet `EntitySelected` cell `A2`, runs `run_Bank_bot.bat` through PowerShell with no timeout and no exit-code check, then checks again ([SIS-01, steps 3 and 5](../SIS-01-bank-avail-check.md)). In Tracto Go checks the folder, writes the cell and runs the configured command with a timeout and exit code through A4. The Python service's `get_bankstatement` (`Api/Bankstatement.py`) deletes and recreates the entity's date folder before it downloads, so a folder that exists after a failed fetch can be empty; the existence check therefore also requires at least one file.

**Acceptance criteria.**
1. Given `{root}\{date}\{entity}` exists and holds at least one file, when checked, then the entity is `Available` and no fetch runs.
2. Given the folder is missing, when checked, then the entity name is written to `EntitySelected!A2`, the named command `bank-fetch` runs with the `timeoutSeconds` from `Settings`, and the folder is checked again.
3. Given the folder exists but is empty, when checked, then it counts as missing and the fetch runs.
4. Given the fetch exits with a non-zero code, when it returns, then the exit code and the last 2,000 characters of its output are stored on the entity's run-record row, and the folder is still checked once more.
5. Given the fetch exceeds the timeout, when the timeout is reached, then the process is killed, the row records `TimedOut`, and the entity continues to the unavailable-accounts comparison (B3) instead of waiting.
6. Given the fetch exits 0 but the folder is still missing or empty, then the entity is not reported available.
7. Given a command name not present in the runner's configured list, when requested, then the call is refused with an error and nothing runs.
8. Given the entity loop, when two entities need fetching, then they run strictly one after the other while the shared `EntitySelected` cell is in use.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| B2.1 | Daily statement folder check | Uses A3 `exists` |
| B2.2 | Run the fetch bot, wait, read the exit code and check again | Includes the `EntitySelected!A2` write through `POST /update`; uses A4 |
| B2.3 | Timeout handling |  |
| B2.4 | Empty-folder rule: a folder with no file counts as missing | Needs A3 `list` |

**Test notes.** A statements root with three entity folders (one full, one missing, one empty), a stub `bank-fetch` command that exits 0, exits 1, sleeps past the timeout, and one that creates the folder; a copy of `BankMaster.xlsx`. Checked: outcome per entity, the cell content after each fetch, the stored exit code and output, kill on timeout.

**Open items.**
- Whether exit 0 means "statements fetched" or only "script ran"; the post-fetch check stays either way.
- Timeout per entity (a business decision) and whether a second fetch for the same entity is harmless; the Python code removes the existing date folder first, so a repeat replaces earlier files.
- `run_Bank_bot.bat` is not in the supplied Python folder. The folder has `download.py`, which runs only the download step for the entities in `selected_entities` with the window in `download_time_config`; nothing in the Python code or the web page reads `BankMaster.xlsx` `EntitySelected`. Whether the batch file runs `download.py`, and what sets the entity and dates for it, is unverified.
- The statement folder is written by the Python download as `{api_download_path}\BankStatements\{dd MMM yyyy}\{entity}\bankstatement.xml`; the live `api_download_path` is `C:\Automation\Data\Input\Statements1`, the root flow 1 checks, and the date is the Python machine's clock. The Python download only runs for entities in `selected_entities`, and the download step is switched off in the supplied database (`download_module_enabled = no`).

---

## B3 — Unavailable accounts and reports
| Field | Value |
|---|---|
| Epic | B: Flow 1, bank statement availability |
| Type | Story |
| Depends on | B1, B2, A2 (mail sending) |
| Capability today | Partly. Reading both workbooks works through `POST /batch` (`tracto/sheet-service/internal/api/api.go`); the comparison, the HTML table and the mails are to build. No `/mail/send` and no `Mail.Send` scope exist: `MS_SCOPES` is `Files.ReadWrite offline_access` (`tracto/sheet-service/README.md`, `internal/msauth/msauth.go`). |

**User story.** As a bank operations user, I want each entity that is still missing statements to produce a table of the exact bank accounts with nothing, mailed to that entity's recipients, so that I know which accounts to chase.

**Context.** UiPath reads `{entity}_check\bank_statements_check.xlsx` (`Sheet1`, `A1:E200`) and the entity's `BankMaster.xlsx` sheet (`A1:A40`), keeps rows whose `BANK_ACCOUNT_NAME` is in the bank list and `AVAILABLE = "N"`, removes duplicates, builds an HTML table headed "Unavailable Bank List", always sends a plain-text availability mail to `avail_receiver_id` (CC `cc_receiver_id`) and sends the table only to the DTSS list whichever entity is missing ([SIS-01, steps 6 to 9](../SIS-01-bank-avail-check.md)). In Tracto the whole used range is read, a missing workbook fails only that entity, and each entity's own recipients receive its table.

**Acceptance criteria.**
1. Given a check workbook with 400 rows, when read, then all 400 rows are compared; a row beyond row 200 with `AVAILABLE = "N"` appears in the table.
2. Given the entity's `BankMaster.xlsx` sheet lists three accounts and the check file marks two of them `N`, when compared, then the table has two rows with the columns Entity and Bank Account Number, duplicates removed.
3. Given the same account appears twice with `N`, then it appears once.
4. Given `{entity}_check\bank_statements_check.xlsx` does not exist, when the entity is compared, then the entity row is `Failed` with the message "check workbook missing", the other entities continue, and the entity is listed in the summary mail under "check failed".
5. Given at least one entity is available, when the loop ends, then one availability mail goes to `avail_receiver_id` with CC `cc_receiver_id`, subject `MT940 statement status`, listing the available entities.
6. Given no entity is available and none is unavailable, then the availability mail is not sent (a run-record message states why); given none is available but some are unavailable, the availability mail is sent stating no entity is available.
7. Given entity X is unavailable, when the unavailable mail is built, then it goes to X's own recipient list, HTML body, with a subject that differs from the availability mail (`MT940 statement status: unavailable accounts`); the DTSS list is added in CC only if `Settings` key `unavailable_cc` is set.
8. Given the mail send fails, then the run ends `Failed` with the mail error and the entity outcomes remain recorded.
9. Given the header "Bank Account Number" carries the value of `BANK_ACCOUNT_NAME`, then the column heading is a `Settings` value (`report_account_heading`) so it can be changed without a release.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| B3.1 | Compare the check workbook with the bank master |  |
| B3.2 | Build the HTML table of unavailable accounts |  |
| B3.3 | Availability and unavailable-accounts mails | Uses A2 |
| B3.4 | Flow tests with sample statement folders |  |
| B3.5 | Whole-range read (no `A1:E200` / `A1:A40` window) and per-entity failure for a missing check workbook |  |
| B3.6 | Per-entity recipients, distinct subjects, empty-result rules, heading as a setting |  |

**Test notes.** Sample `{entity}_check\bank_statements_check.xlsx` files (full, 400 rows, missing), a `BankMaster.xlsx` copy (37 accounts, no entity above ten), and a mail account in a test tenant. Checked: table rows, dedupe, recipients, subject, behaviour with an empty result and with a failed send.

**Open items.**
- Which heading the operations team wants: account name or account number.
- Whether the availability mail goes out when nothing is available.
- Recipients for ONESIS and whether the DTSS list stays in copy.
- Whether `A1:E200` stood for a real limit.
- `bank_statements_check.xlsx` is not written by any file in the supplied Python folder (search for the file name finds nothing); which process writes it and when is unknown.

---

## B4 — Collection accounting run for the International entities
| Field | Value |
|---|---|
| Epic | B: Flow 1, bank statement availability |
| Type | Story |
| Depends on | A2 (mail sending), A3 (exists, delete), A4 (program runner), A6 (cell write), G1, H3 |
| Capability today | To build. Nothing in `tracto/` starts the collection accounting service. The pieces it needs are A2, A3 and A4 (all to build) and a cell read and write on `Response_Report.xlsx`: the read exists (`POST /cell` in `tracto/sheet-service/internal/api/api.go`); the write is text-only and by header name (finding 1), so writing a number into `B2` and `B3` needs A6. |

**User story.** As the International collection accounting team, I want Tracto to start the collection accounting run for each International entity, read how it ended and mail the right people, so that a failed run is reported instead of ending silently, and no mailbox password or application secret is kept in a workbook.

**Context.** UiPath does not call the Python service through an API, and the service has no API documentation. It is started by a batch file that runs `python.exe -W ignore ...\bot_run.py`, and `bot_run.py` is the whole collection accounting run: it loads the day's payment advice workbooks into MySQL, downloads bank statements, outstanding and unapplied data from Oracle Fusion, creates receipts, matches and posts, each step switched by a setting in the MySQL `settings` table. It takes no arguments: the entity, the download window and the run it logs against are set in the database beforehand. For the Indian entities a person starts it from the collection accounting web page (`ca_with_pa`), whose `bot-run/init` call prepares the run (entity, dates, `bot_run` row, `active_bot_run_id`) and whose `run-bot.php` then runs Python; that stays as it is. For the International entities a separate UiPath process, `International_Bot1_SXP`, starts it and reports the result ([SIS-05](../SIS-05-international-bot1.md)): it deletes the previous `BankStatement_Report.xlsx`, writes 0 into `Response_Report.xlsx` `B2` (bank statement) and `B3` (outstanding), runs `run_SXP_bot.bat` and waits, reads the two cells back and sends one of three mails. It has no `Try`/`Catch`, so a failed batch or an unreadable cell ends the run with no mail. In Tracto this is a one-step main job (`carun-main`, one `http.post` to `/carun/start`) and a Go service that loops over the International entities flagged for it.

**Acceptance criteria.**
1. Given the entity table with `CaRunEnabled` set for Southern Cross Protection, when `/carun/start` runs, then that entity is processed and gets its own run-record row; an entity not flagged is not touched.
2. Given an entity, before the run the service deletes that entity's `BankStatement_Report.xlsx` if it exists and writes the number 0 into `Response_Report.xlsx` `B2` and `B3`; if either step fails the entity is `Failed` with the message, the batch is not started, and the failure mail of criterion 8 is sent.
3. Given the named command for the entity (`ca-run-SXP`, configured in the program runner as the Python executable, `-W ignore` and the entity's `bot_run.py`), when it runs, then Go waits for it with the `timeoutSeconds` from `Settings`, and stores the exit code, the duration and the last 2,000 characters of its output on the entity's row.
4. Given the program exits, then `B2` and `B3` are read as whole numbers; a blank, missing or non-numeric cell makes the entity `Failed` with "result unreadable" and the cell values on the row, not an exception.
5. Given `B3` is 0, then the "API down" mail goes to `<entity>_api_down_receiverID` with CC `<entity>_receiverID_CC`.
6. Given `B3` is above 0 and `B2` is 0, then the "bank statement not downloaded" mail goes to `<entity>_bank_issue_receiverID` with CC `<entity>_receiverID_CC`.
7. Given both are above 0, then `BankStatement_Report.xlsx` and that day's `outstanding.xlsx` (`...\OutstandingStatements\{dd MMM yyyy}\SXP_Group\outstanding.xlsx` for Southern Cross Protection, the group folder a setting per entity, the date in the schedule's time zone) are checked to exist and be non-empty; if both are, the success mail goes to `<entity>_receiverID` with CC `<entity>_receiverID_CC` and both attached; if either is missing or empty, the "report missing" mail of criterion 8 is sent instead, naming the file.
8. Given the program times out, exits with a code the configuration marks as failure, or criterion 2 or 4 fails, then a failure mail goes to `<entity>_api_down_receiverID` with CC `<entity>_receiverID_CC`, stating the step that failed, and the entity row is `Failed`. A run never ends with no mail.
9. Given the three mails, then each has its own subject naming the entity and the outcome (not "Mail from Bot 1" for all), and the body text of each is a `Settings` value.
10. Given a run is still going, when the schedule fires again or the job is submitted again, then the second request is refused (one run at a time across all International entities: the service has a single `active_bot_run_id` and shares `Response_Report.xlsx`).
11. Given the parallel run (I6), then only one of UiPath and Tracto starts `bot_run.py` for an entity on a day; the switch is the entity's `CaRunEnabled` flag, and the runbook (H2) states which side is live.
12. Given `MasterConfig.xlsx` holds `APP ID`, `Application Secret`, `Tenant ID`, `<entity>_senderID` and `<entity>_password`, then the service reads none of them; mail goes through A2.
13. Given the answer to how an International run is prepared (open item), then the preparation is a step of the loop before criterion 2: either nothing (the International copy prepares itself), or the web page's `bot-run/init` call with the entity and the day's date window, whose `bot_run_id` is stored on the entity's row. Tracto never writes to the MySQL database directly.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| B4.1 | `/carun/start` with the entity loop, run record and `CaRunEnabled` flag | Reuses the loop and run record of B1 |
| B4.2 | Before the run: delete the stale report, write 0 into `B2` and `B3` | Uses A3 `delete` and the A6 cell write |
| B4.3 | Run the entity's named command with timeout; store exit code, duration and output tail | Uses A4. The command line is configuration, not a request field |
| B4.4 | Read and parse `B2` and `B3`; the outcome decision of criteria 4 to 8 | |
| B4.5 | The four mails (API down, statement not downloaded, success with two attachments, failure), recipients from the configuration keys, subjects and bodies as settings | Uses A2 |
| B4.6 | Attachment checks before sending; the dated `outstanding.xlsx` path from the schedule's time zone | |
| B4.7 | One run at a time; refusal of a second request | |
| B4.8 | `carun-main` main job and its schedule | With G1 and G2 |
| B4.9 | Tests with a stub batch file | |
| B4.10 | Run preparation through `bot-run/init`, if the International copy needs it | Depends on the open item on preparation |

**Test notes.** A stub `.bat` per case that writes `B2` and `B3` (both above 0, `B3` 0, `B2` 0, a blank cell, text in a cell), one that exits 1, one that sleeps past the timeout, and one that writes nothing; a copy of `Response_Report.xlsx`; sample `BankStatement_Report.xlsx` and `outstanding.xlsx`, and the same with one missing; a mail account in a test tenant. Checked: the mail chosen in each case, its recipients and attachments, the row state and stored output, refusal of an overlapping run, and that no secret key is read. The real `bot_run.py` is never run in a test: it changes MySQL and Oracle Fusion.

**Open items.**
- The International copy of the Python service (`ca_international_SXP`) and `run_SXP_bot.bat` are not supplied. The supplied Python folder is the Indian copy; it never writes `Response_Report.xlsx`, so which code sets `B2` and `B3`, and to what (a count, or 1 for success), is unverified.
- The batch file text shared for this process runs `...\Bot_1\ca_international_MSS\bot_run.py`, while the UiPath package reads `...\Bot_1\ca_international_SXP\Bank_Statement\`. Whether there is one copy per International entity (MSS, SXP) and therefore one named command per entity.
- How an International run is prepared. `bot_run.py` reads `active_bot_run_id`, `selected_entities` and the download window from MySQL; for the Indian entities the web page's `bot-run/init` sets them. Nothing supplied sets them for an International entity, and the reset of `active_bot_run_id` at the end of a run is commented out, so an unprepared run would log against the last run and process the last entity selected. The supplied database has no International entity at all, so the International copy may use its own database.
- `bot_run.py` exits with -1 only when no run is active; otherwise it ends normally whether or not the run worked, and the `bot_run` status in MySQL holds the result. The counters are the signal UiPath uses.
- A person pressing Start Process on the web page while a scheduled International run is going would collide with it on `active_bot_run_id` and `selected_entities`, if both use the same database. Tracto cannot see that from outside without reading the database.
- When `International_Bot1_SXP` runs (its trigger time) and whether it must run after flow 4 has written the day's International workbook.
- The success mail calls the second attachment the "ATB Report"; the file is `outstanding.xlsx`. Which name the recipients expect.

---

## C1 — Mailbox intake
| Field | Value |
|---|---|
| Epic | C: Flow 2, mail dispatch |
| Type | Story |
| Depends on | A1 (mail reading), A6 for the run record only if kept in a workbook; none otherwise |
| Capability today | To build for the mailbox; Partly for configuration. `POST /batch` reads the entity and customer sheets (`tracto/sheet-service/internal/api/api.go`). No mail reader exists: no Graph `Mail.Read`, no IMAP client (searched `tracto/sheet-service` and `tracto/orchestrator-go`; `MS_SCOPES` is `Files.ReadWrite offline_access`). |

**User story.** As a payment-advice operator, I want each entity's unread mail fetched in arrival order and matched to a customer, so that every advice is attributed to the right customer with the keyword that matched recorded.

**Context.** UiPath loops `PriorityEntity_1..N` (15 entities), reads unread `Inbox` over IMAP port 993 (`outlook.office365.com` for RARE, `mail.sisindia.com` for the rest), sorts by the `Date` header, lower-cases the plain-text body and takes the first customer row whose alias in columns 3 or 4 is contained, else `Others` ([SIS-02, steps 1 to 6](../SIS-02-email-pa-dispatcher.md)). Tracto reads entities as rows with a flag per flow, uses Graph for Microsoft 365 and IMAP for the in-house server (A1), records processed message ids instead of relying on the read flag, and records the matched keyword.

**Acceptance criteria.**
1. Given an entity table with `Flow2Enabled`, `MailPlatform` (`graph` or `imap`) and `MailAccount` columns, when `/maildispatch/start` runs, then only enabled entities are processed, in sheet order, each with its own run-record row.
2. Given an entity with 12 unread mails, when fetched, then they are returned oldest first by the `Date` header, each with message id, sender, subject, date, plain-text body and attachment names, sizes and types.
3. Given a message id already in the processed-message record (A1), when the mailbox is read again, then the mail is not handled again, whether or not the read flag is set.
4. Given a person has opened a mail (read flag set) that is not in the processed record, then it is still handled.
5. Given the customer sheet of the entity with a row whose alias matches the lower-cased body, then the customer is that row's name and the work row records the matched keyword; given two matching rows, the first in sheet order wins and the row records both matched keywords.
6. Given no match, or a mail with no plain-text part, then the customer is `Others`.
7. Given an entity whose mailbox cannot be opened (bad credential, server down), then that entity is `Failed` with the message, no password is written to any log, and the other entities continue.
8. Given the entity's mail count, then it is recorded on the run-record row (count read, count new).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| C1.1 | Entity list and per-entity mailbox selection |  |
| C1.2 | Fetch unread mail in arrival order | Uses A1 |
| C1.3 | Read the customer list | Sheet named after the entity in `Customers List.xlsx` |
| C1.4 | Keyword matching, recording the matched keyword |  |
| C1.5 | Entity table columns for flow 2: `Flow2Enabled`, `MailPlatform`, `MailAccount`, `AcceptedTypes`, recipients |  |

**Test notes.** One test Microsoft 365 mailbox and one IMAP test account, each with mails of: a single match, two customers in one body, no match, no plain-text part, already read by a person; a customer sheet with aliases. Checked: order, matched keyword, `Others`, rerun after a crash between filing and recording (no loss, no repeat).

**Open items.**
- Entity-to-mailbox list and the platform of each (the workbook holds mailbox settings for DTSS only).
- Which entities flow 2 covers; AEPL and RHSPL are in the Python loader list but not in the dispatcher list, RARE is the reverse.
- Whether the customer sheet may take named columns and more than two aliases.
- IMAP password source on the worker (`${secret:NAME}`).

---

## C2 — Filing and routing
| Field | Value |
|---|---|
| Epic | C: Flow 2, mail dispatch |
| Type | Story |
| Depends on | C1, A1 (save attachments and `.eml`), A3 (folders, move, safe extract) |
| Capability today | To build. No `/fs/*`, `/mail/save` or archive extraction exists in `tracto/sheet-service` or `tracto/orchestrator-go` (searched). Routing, naming and the per-entity accepted-types rule are Go code over those. |

**User story.** As a payment-advice operator, I want every attachment filed under a standard name in a folder by date, entity and file type, with archives unpacked safely, so that the extraction flows find each file where they expect it.

**Context.** UiPath creates `{PaymentAdvicesFolderPath}\{dd MMM yyyy}\{entity}\` with `PDF_JPEG`, `XLS`, `MailBody` and a staging folder, routes by extension matched with `Contains`, skips `.xls` and `.xlsx` for DTSS, extracts `.zip` and routes inner files, renames every file `{customer}_{dd MMM yyyy hh mm ss}_{n}{ext}`, and saves no-attachment mails as `.eml` in `MailBody` ([SIS-02, steps 4 to 9](../SIS-02-email-pa-dispatcher.md), folder map in [folders-and-files.md](../folders-and-files.md)). Tracto matches extensions exactly and case-insensitively, reads accepted types from the `AcceptedTypes` column of the entity row, deletes an archive only after every inner file has an outcome, and refuses archive paths that leave the destination.

**Acceptance criteria.**
1. Given entity DTSS and date 13 Nov 2026, when the entity is processed, then `{root}\13 Nov 2026\DTSS\PDF_JPEG`, `XLS` and `MailBody` exist, and a second run does not fail on existing folders.
2. Given `AcceptedTypes` for an entity is `pdf,jpg,jpeg,xls,xlsx,zip`, then `.pdf`, `.jpg`, `.jpeg` go to `PDF_JPEG` and `.xls`, `.xlsx` go to `XLS`; given DTSS's `AcceptedTypes` is `pdf,jpg,jpeg,zip`, then a `.xlsx` is not filed and is handled as an unsupported type (C3).
3. Given `report.pdfx` or `a.PDF`, then `.pdfx` is unsupported and `.PDF` is routed as `pdf`.
4. Given a filed file, then its name is `{customer}_{dd MMM yyyy hh mm ss}_{n}{ext}` with `n` increasing across the whole run, and the work row carries the message id.
5. Given an attachment name with path separators, `..` or more than 150 characters, then the stored name uses only the rename pattern and the original name is kept on the work row.
6. Given a `.zip` with three files (a pdf, an xlsx, a `.docx`), then the two accepted files are routed as above, the `.docx` is unsupported, and the archive is deleted only after all three have an outcome.
7. Given a `.zip` containing an entry with the path `../x.pdf`, then extraction refuses it, no file is written outside the destination, the archive is not deleted, and the sender is replied to (C3).
8. Given a mail with no attachment that is not dropped by C4, then its body is saved as `{customer or Others}_{dd MMM yyyy hh mm ss}_{n}.eml` in `MailBody`.
9. Given a mail whose subject or first attachment name contains `PA_{entity}` and the setting `preDownloadPass=on`, then its attachments are also saved to the top of the entity folder; with the setting off, nothing extra is written.
10. Given a file operation outside the configured roots, then it is refused.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| C2.1 | Create date, entity and file-type folders | Uses A3 |
| C2.2 | Route by extension with per-entity accepted types |  |
| C2.3 | Standard file naming |  |
| C2.4 | Archive extraction and routing of inner files | Uses A3 safe extract |
| C2.5 | Save mail bodies as `.eml` | Uses A1 |
| C2.6 | Routing tests with a sample mailbox |  |
| C2.7 | Name safety: path separators, `..`, length, original name kept on the work row |  |
| C2.8 | `PA_{entity}` pre-download pass behind a setting, default off until confirmed | Flow 3 reads this folder |

**Test notes.** A test mailbox with: pdf, jpg, `.PDF`, xlsx for DTSS and for another entity, `.pdfx`, a zip of three, a zip with a traversal entry, a zip that is corrupt, a mail with a 300-character attachment name, a `PA_DTSS` pre-download mail. Checked: resulting tree, names, `AcceptedTypes` effect, nothing outside the roots.

**Open items.**
- Whether the `PA_{entity}` pre-pass is deliberate; flow 3 reads the files it saves, so dropping it affects flow 3.
- `XLS` has no reader among the four flows ([folders-and-files.md](../folders-and-files.md)); which spreadsheets flow 3 is meant to process.
- Retention of staged files and archives.
- Which `AcceptedTypes` value each entity gets besides DTSS.

---

## C3 — Work items, tracker and replies
| Field | Value |
|---|---|
| Epic | C: Flow 2, mail dispatch |
| Type | Story |
| Depends on | C2, A5 (work items), A6 (appending write), A2 (reply) |
| Capability today | To build. No work-item table or endpoints, no append, no reply exist in `tracto/` (searched). `POST /update` writes to known rows (`tracto/sheet-service/internal/api/api.go`) and `POST /batch` reports `nextStartRow` at end of data, so an append is possible only with a read-then-write that is not atomic (`POST /apply` has the same read, release, write gap noted in SIS-02); A6 closes it. |

**User story.** As a payment-advice operator, I want a work item for every filed file, a tracker row for every artefact, and a reply to the sender when a file cannot be handled, so that nothing is lost silently and the sender is told once.

**Context.** UiPath adds one queue item per file with `PAFileName`, `EntityName`, `From`, `TimeDate`, `AttachmentType` (or `CustomerName` for the mail-body queue) and `FilePath`; appends rows (ID, Entity Name, Attachment Name, From, Subject, Time/Date, DownloadStatus, FileName) to sheet `Email Tracker` in `LogTracker.xlsx`; writes `Success` or `Fail` into cell `B2` of the entity sheet in `ModuleTracker.xlsx`; and replies by SMTP for three cases (archive will not extract, attachment missing after save, unsupported type), copying the original CC and BCC ([SIS-02, steps 7 to 12](../SIS-02-email-pa-dispatcher.md)). In Tracto work items live in the A5 table (SQLite, to be re-confirmed against a sheet), a reply goes to the sender only, and the matched keyword and message id are stored on the work item.

**Acceptance criteria.**
1. Given a file filed to `PDF_JPEG`, then one work item exists with entity, sender, mail date, customer, attachment type, full path, matched keyword, message id and status `Pending`; the same file is never given two items on a rerun.
2. Given a `.eml` filed to `MailBody`, then one work item exists of type `MailBody`.
3. Given three files filed from one mail, then three work items and three tracker rows exist.
4. Given the tracker, when rows are appended, then they land below the existing rows with no header repeated, and two entities finishing at once do not write the same row.
5. Given an archive that will not extract, an attachment missing on disk after save, or an unsupported type, then one reply goes to the sender only, subject `Re: {original subject}`, with the text of the template for that case; the original CC and BCC are not copied.
6. Given the reply send fails, then the work row records the failure and the entity run continues.
7. Given the same mail seen on a rerun, then no second reply is sent (the reply is recorded per message id).
8. Given a file with an outcome of unsupported or failed, then the tracker row carries `DownloadStatus` `Failed` and the reason.
9. Given the run ends, then the entity's run-record row is `Done` or `Failed`, and with the setting `moduleTracker=on` cell `B2` of the entity's sheet in `ModuleTracker.xlsx` holds `Success` or `Fail`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| C3.1 | Create one work item per file | Uses A5 |
| C3.2 | Append to the email tracker | Uses A6 |
| C3.3 | Reply for a failed archive, a missing attachment or an unsupported type | Uses A2 reply |
| C3.4 | Reply tests |  |
| C3.5 | Module Tracker `B2` write behind a setting |  |

**Test notes.** Test mailbox with a corrupt zip, an unsupported `.docx`, a good pdf; a copy of `LogTracker.xlsx` and `ModuleTracker.xlsx`; a second sender mailbox to receive replies. Checked: item count, tracker rows, reply recipients and text, no CC or BCC, no repeat on rerun.

**Open items.**
- Sheet or SQLite table for the work items (default table, to be re-confirmed).
- Whether a file nobody can process also becomes a work item with status, so the backlog is visible.
- Wording of the three reply templates; the workbook has mail text that no flow reads.
- Whether anything reads `ModuleTracker.xlsx` `B2` or the mail count next to `{entity}_MailCount`.

---

## C4 — Mail classification
| Field | Value |
|---|---|
| Epic | C: Flow 2, mail dispatch |
| Type | Story |
| Depends on | C1, C2, A1, A2 (owner alert) |
| Capability today | To build. No classification exists in the UiPath process (it files every mail without an attachment: `EmailMonitor/EachMailProcess.xaml`, "MailBodySequence") nor in `tracto/`. |

**User story.** As a payment-advice operator, I want bounce notices and system reports dropped and counted and a mail that asks for a password reported to the process owner, so that noise does not become work and a phishing mail is never opened as an advice.

**Context.** The 15 `.eml` files in the DTSS `MailBody` sample are not advices: 14 are bounce notices and one is a "Warning: system report" saying a password will expire and linking to a page to change it, a credential-phishing pattern ([payment-advice-samples.md, section 7](../payment-advice-samples.md); finding F13 in [known-faults-in-current-output.md](../known-faults-in-current-output.md); requirement R11). Tracto classifies mails with no attachment before filing, drops the first two kinds with a count, reports the third, and keeps the rest as `.eml` bodies.

**Acceptance criteria.**
1. Given a mail with no attachment whose sender is a mail-system address (`postmaster`, `mailer-daemon`) or whose subject matches the bounce list (`Undeliverable`, `Delivery Status Notification`, `Mail delivery failed`, `Returned mail`), then its class is `Bounce`, it is not saved to `MailBody`, no work item is created, and the entity's run-record `bouncesDropped` counter rises by one.
2. Given a mail with no attachment whose subject matches the system-report list (`system report`, `automatic reply`, `out of office`), then its class is `SystemReport`, it is dropped and counted in `systemReportsDropped`.
3. Given a mail with no attachment whose body asks the reader to enter, confirm, reset or change a password, or whose body links to a page for that purpose, then its class is `PasswordRequest`: it is not filed as an advice, no reply goes to the sender, the process owner receives one alert mail naming the entity, sender address, subject and date, and the run-record counter `passwordRequestsReported` rises.
4. Given any mail body containing a link, then the service does not request, resolve or follow the link, does not render the HTML, and the alert text shows the link as plain text with its scheme made inert (`hxxp`).
5. Given a mail with no attachment that matches no rule, then it is saved as `.eml` in `MailBody` and gets a work item as in C2 and C3.
6. Given the bounce and system-report patterns and the process-owner address are `Settings` values, when a pattern is added, then it takes effect on the next run without a release.
7. Given a mail with an attachment whose body gives a password for that attachment, then it is not classified `PasswordRequest` and is filed normally.
8. Given the process-owner address is not set, then a `PasswordRequest` mail is not filed, the run ends `Failed` with the message "process owner address missing", and the other mails continue.
9. Given the 15 sample `.eml` files, then 14 are `Bounce`, 1 is `PasswordRequest`, none is filed.
10. Given every classified mail, then its message id is recorded in the processed-message record so a rerun does not count or alert twice.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| C4.1 | Classification rules: bounce and system-report sender and subject patterns from `Settings`, class per mail |  |
| C4.2 | Password-request detection and the owner alert mail | Uses A2 |
| C4.3 | Link safety (no fetch, no render, inert links in alerts) and counters in the run record |  |
| C4.4 | Tests with the 15 sample `.eml` bodies and constructed cases |  |

**Test notes.** The 15 sample `.eml` files from the DTSS `MailBody` folder (read as test data; do not copy contents into documents), plus constructed mails: a real advice body with no attachment, a body giving a password for an attachment, a body with a link, a bounce with an attachment. Checked: class per mail, counters, alert content, no outbound request to any link (check with the network blocked).

**Open items.**
- Final pattern lists: bounce, system report and password wording (the sample is one mailbox and one day).
- Process-owner address for alerts.
- Whether a mail with an attachment and a password-change link should also be reported; as written only mails with no attachment are checked.
- Where the password-expiry mail originates; it reached a mailbox the dispatcher reads.

---

## C5 — Dispatcher reporting
| Field | Value |
|---|---|
| Epic | C: Flow 2, mail dispatch |
| Type | Story |
| Depends on | C1, C2, C3, C4, A2 |
| Capability today | To build. UiPath has no summary; the only records are the `Email Tracker` sheet and the queue. Tracto has the schedules list and completed runs (`tracto/orchestrator-ui/public/app.js`, `loadCompletedSchedules`) but no per-entity business summary and no mail. |

**User story.** As a payment-advice supervisor, I want one daily summary per entity mailed to the configured recipients, so that I can see what arrived, what was filed, what was rejected and why, without opening the folders.

**Context.** Flow 2's results are spread over the run record, the `Email Tracker` sheet and the work items. A daily summary is derived from them, not kept as a third record. Counts only: no customer names, amounts or sender addresses in the body.

**Acceptance criteria.**
1. Given a day with several dispatcher runs, then the summary has one block per enabled entity with: mails read, files filed split by type (`pdf`, `jpg`, `xls`, `zip` inner, `eml`), work items raised, files rejected with a count per reason (unsupported type, archive failed, attachment missing), replies sent, bounces dropped, system reports dropped, password requests reported.
2. Given the totals, then for each entity the filed plus rejected plus dropped plus reported count equals the files and mails seen, and a mismatch is shown as a line "unaccounted: n".
3. Given an entity that failed (mailbox error), then its block shows `Failed` and the message, and the other blocks are unaffected.
4. Given the day's end time `Settings` `summary_time` in the schedule time zone, when it is reached, then one summary mail is sent to the entity-level recipients and the supervisor list, HTML body, subject `Mail dispatch summary {dd MMM yyyy}`.
5. Given the summary was already sent for the date, then a rerun of the summary trigger does not send a second mail unless `force=true`.
6. Given a day with no mail read for any entity, then the summary still goes out stating zero for each entity.
7. Given the mail send fails, then the failure is recorded and the summary is retried at the next trigger.
8. Given the figures, then they are derived from the run record and the tracker; no counter is held separately.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| C5.1 | Per-entity counters in the run record (read, filed by type, work items, rejected by reason, replies, dropped, reported) |  |
| C5.2 | Summary builder from the run record, tracker and work items, with the reconciliation line |  |
| C5.3 | HTML summary mail to the configured recipients | Uses A2 |
| C5.4 | Trigger and once-a-day guard (`summary_time`, `force`) |  |
| C5.5 | Tests: multi-run day, failed entity, empty day, repeat trigger |  |

**Test notes.** A day of several runs against the test mailboxes from C1 to C4, with one failing entity. Checked: counts against the tracker and work items, the reconciliation line, one mail per day, content free of customer data.

**Open items.**
- Recipient lists per entity and for the supervisor.
- Summary time and whether a second summary at end of day is wanted.
- Whether flows 3 and 4 add their own lines to this summary or send their own.
- The volume figure (about 150 documents a day in the samples against 45 assumed) sets the size of the summary.

---

## Cross-epic dependencies

- A1 mail reading: C1, C2, C4. A2 mail sending: B3, B4, C3, C4, C5. A3 file operations: B2, B4, C2 (including safe archive extraction and the configured-root restriction). A4 program runner: B2, B4. A5 work items: C3. A6 appending write: B4 (cell write), C3 (tracker) and C5 if counters are also written to a workbook.
- The collection accounting service has no API; it is started by a batch file that runs `bot_run.py` (B4). The statement fetch bot of B2 is a batch file as well.
- C2 and C3 produce what epics D (flow 3, spreadsheets in the entity folder and `PA_{entity}` files) and F (flow 4, `PDF_JPEG` and the work items) consume; the work-item fields and file names are fixed with those epics.
- C2 depends on `AcceptedTypes` on the entity row (C1.5); epics D and F read the same entity table.
- B1 and C1 share one entity table with a flag per flow; H1 (configuration templates) holds its layout.
- B1 and C5 rely on the schedule time zone; G2 (schedules) passes `timeZone` as a job parameter, which the scheduler does not do today.
- G1 publishes the two one-step main jobs; G2 schedules them (flow 1 before statement processing, flow 2 every 15 minutes, summary at `summary_time`).
- The queue choice (SQLite table or sheet, to be re-confirmed) affects A5 and C3 only.
