# SIS migration: stories and tasks

The work to move the UiPath payment-advice processes onto Tracto, as stories with acceptance criteria and tasks. The four flows are in [SIS-01](../SIS-01-bank-avail-check.md) to [SIS-04](../SIS-04-payment-advice-extraction.md); the International collection accounting run (`International_Bot1_SXP`) is in [SIS-05](../SIS-05-international-bot1.md). Each story cites the analysis it comes from in [the SIS documents](../README.md) and says whether the capability already exists in the Tracto code.

**42 stories.** Work is planned and delivered story by story.

## The story files

| File | Epics | Stories |
|---|---|---|
| [epic-A-G-H-platform-jobs-config.md](epic-A-G-H-platform-jobs-config.md) | A Platform capabilities, G Main jobs and schedules, H Configuration, documentation and secrets | 11 |
| [epic-B-C-bank-check-and-mail-dispatch.md](epic-B-C-bank-check-and-mail-dispatch.md) | B Flow 1 bank statement availability and the International collection accounting run, C Flow 2 mail dispatch | 9 |
| [epic-D-F-I-spreadsheet-pdf-flows-and-output-quality.md](epic-D-F-I-spreadsheet-pdf-flows-and-output-quality.md) | D Flow 3 spreadsheet advices, F Flow 4 PDF and image advices, I Output quality and hand-off | 13 |
| [epic-E-document-extraction.md](epic-E-document-extraction.md) | E Document extraction process | 9 |

## All stories

| ID | Story | Capability today |
|---|---|---|
| A1 | Mail reading | Partly |
| A2 | Mail sending | To build |
| A3 | File operations | To build |
| A4 | Program runner | To build |
| A5 | Work items | Partly |
| A6 | Appending write | Partly |
| B1 | Configuration and entity loop | Partly |
| B2 | Statement check and fetch | To build |
| B3 | Unavailable accounts and reports | Partly |
| B4 | Collection accounting run for the International entities | To build |
| C1 | Mailbox intake | To build (mailbox), partly (configuration) |
| C2 | Filing and routing | To build |
| C3 | Work items, tracker and replies | To build |
| C4 | Mail classification | To build |
| C5 | Dispatcher reporting | To build |
| D1 | Entity profiles and validation | Partly |
| D2 | Cleaning and mapping | To build |
| D3 | Workbook output and reporting | To build |
| E1 | Intake and file handling | Partly (module team) |
| E2 | Document splitting | Partly (module team) |
| E3 | LLM extraction | Partly (module team) |
| E4 | Cross-check | Partly (module team) |
| E5 | Result assembly | Partly (module team) |
| E6 | Operation and resilience | Partly (module team) |
| E7 | Accuracy evidence | To build |
| E8 | Entity profiles for the field set | To build (module team) |
| E9 | Character recognition for scans and images | Partly (module team) |
| F1 | Validation and customer rules | To build |
| F2 | Workbook and notifications | To build |
| F3 | International entity profile | To build |
| F4 | Review routing by confidence | To build |
| G1 | Main jobs | Partly |
| G2 | Schedules and monitoring | Partly |
| H1 | Configuration templates | To build |
| H2 | Operational documentation | Partly |
| H3 | Secrets and credentials | Partly |
| I1 | Output profiles per entity | To build |
| I2 | Date, amount and identifier normalisation | To build |
| I3 | Write once and duplicate guard | To build |
| I4 | Document outcomes and reporting | To build |
| I5 | Net amount derivation | To build |
| I6 | Parallel run and cutover | To build |

## Order of work

```
1  Foundations        A3 files, A6 appending write, A5 work items, A1 mail reading, A2 mail sending,
                      A4 program runner, H3 secrets, G1 main jobs
2  Extraction module  E1 intake, E8 profiles, E9 scans, E2 splitting, E3 extraction, E5 assembly, E6 operation
                      (the module team's work; runs in parallel with 1 and 3)
3  Output core        I1 profiles, I2 normalisation, I3 write once, I5 net amount, I4 outcomes
4  The flows          D1-D3 spreadsheet advices, C1-C3 dispatcher, B1-B3 bank availability, F1-F2 PDF advices,
                      B4 International collection accounting run
5  Quality            E4 cross-check, E7 accuracy, F3 International profile, F4 review routing, C4 classification
6  Run and hand over  G2 schedules and monitoring, C5 reporting, I6 parallel run and cutover, H1 templates, H2 documentation
```

B4 depends on flows 2 to 4 only for its timing: it starts after flow 4 has written the day's International workbook, because `bot_run.py` loads it.

## Gaps that stay open

| Gap | Affects | What closes it |
|---|---|---|
| Bot 3: which process runs the International matching, posting and reconciliation reports, and when | B4, G2 | The process owner |
| The live `run_SXP_bot.bat`, what increments `active_bot_run_id`, the switch values in `ca_with_pa_sxp`, and `MSS_SXP_BotRun.xlsx` | B4 | The collection accounting owner |
| The exact time zone of the 07:55 "Australian time" trigger | B4, G2 | The process owner |
| Whether the International entity belongs in the four flows and the loader at all | F3, I1, I6 | The process owner |
| Queue as a SQLite table or a sheet | A5, C3 | Re-confirmation; the table is the working default |
| The threshold for review routing | F4 | Re-extracting the 248 International workbooks that carry UiPath confidences |
| The release path for a reviewed document (the day workbook is never rewritten) | F4 | The proposal in F4.7 to be confirmed |
| The spreadsheet advice formats of the entities other than DTSS | D1, D2 | Samples of each entity's workbook and the profile sheet |
| The password source for locked PDFs, and mailbox credentials for each entity | E1, H3, C1 | The owner of the password list and the mailbox accounts |
| Volume: about 150 documents a working day in the samples | E6, G2 | Confirmation from the process owner |

## Findings against the code

Reading the code while writing the stories turned up places where the analysis documents and the code disagree. The stories follow the code; the analysis documents are to be corrected.

| # | Finding | Story |
|---|---|---|
| 1 | The sheet service's write is text-only, only on an existing file, only into columns already in the header row. It cannot create a workbook or a sheet, write typed dates or numbers, or append. SIS-03 says it exists | A6, I1 |
| 2 | The schedule's time zone is stored but never passed to the job; params come only from the schedule's own parameter JSON | B1, G2 |
| 3 | A script containing a password is not refused at publish; the validator only checks the `${secret:}` syntax | H3 |
| 4 | `${secret:NAME}` is resolved by the browser runtime only; the desktop runtime has no resolver and the Go services have no reader | H3 |
| 5 | Mail needs a wider Microsoft scope than the single file scope the service signs in with, and a new sign-in | A1, A2 |
| 6 | The Microsoft refresh token is stored unencrypted in the service's database, and the Go services log without masking | H3 |
| 7 | The one-step main job still runs on a desktop worker with an unlocked session | G1 |
| 8 | A main job that succeeds when a batch is accepted hides failed items; notification on partial failure needs a rule | G2 |
| 9 | The Python bank statement download deletes and recreates the entity's date folder before downloading, so an existing folder does not mean the statement is available | B2 |
| 10 | The fetch batch file, the `EntitySelected` cell and the check workbook are not in the supplied Python folder; the fetch bot is unverified | B2 |
| 11 | The Python date folder follows the machine clock. (The statement root matches: the live `api_download_path` is the root flow 1 checks; the code default on `D:` is not used) | B1, B2 |
| 12 | The Python download passes credentials as URL query parameters and truncates the statement, outstanding and unapplied tables on each run | B4 |
| 13 | The UiPath taxonomy has a `TDS` column in the invoice table; two analysis documents say it has none | E3, E8 |
| 14 | The extraction reply carries colours, not numeric confidences, and per-field colours are not in the reply; the comparison fields are the four utility fields | E4, F4 |
| 15 | A password-locked PDF probably ends as a plain 502 in the extraction service, and PDFs locked only against editing are rejected though UiPath opens them | E1 |
| 16 | Page ranges and splitter confidence are logged but not returned; a failed document inside a bundle is skipped without trace; a bundle of fewer than five advices likely comes back whole | E2, E5 |
| 17 | The error codes in the documents differ from the code (`INVALID_INPUT` against `INVALID_PDF`; model failures are an uncoded 502) | E6 |
| 18 | Character recognition in the extraction service serves boundary detection only; the model reads the PDF itself | E9 |
| 19 | The extraction service documents say most bank advices are locked; the samples show 2.5% locked International files and none in DTSS | E1 |
| 20 | The UiPath duplicate-removal workflow has no deduplication; it filters empty numbers and minus signs | I3 |
| 21 | SIS-03 appends every file into one workbook, which conflicts with writing the day workbook once | D3, I3 |
| 22 | The 24-column order in SIS-03 differs from the sample DTSS hand-off sheet; the loader selects by name, so the sample order is used | I1 |
| 23 | Adding the International entity to the loader's entity setting also changes what the Oracle Fusion download reads, since the same setting drives it | F3 |
| 24 | The Python service has no API and no API documentation. It is started as a program, `bot_run.py`, with no arguments; the "API" in the analysis documents is the service's own client for Oracle Fusion through the Magic xpi middleware | B4 |
| 25 | `bot_run.py` is the whole collection accounting run (load the advice workbooks, download, create receipts, match, post), so starting it is what consumes the hand-off workbook; the Indian runs are started by a person from the web page, the International runs by `International_Bot1_SXP` | B4, I6 |
| 26 | The Indian Python copy never writes `Response_Report.xlsx`, which is the only success signal `International_Bot1_SXP` reads; the International Bot 1 copy writes it (finding 34). `bot_run.py` exits with -1 only when no run is active, and otherwise ends normally whatever happened | B4 |
| 27 | `International_Bot1_SXP` signs in to Office 365 with an interactive token while supplying a mailbox password and an application secret from `MasterConfig.xlsx`, and ships a test workflow that mails a production report to a hard-coded address | H3 |
| 28 | The Indian run is prepared and started by the web page `ca_with_pa`: `bot-run/init` writes the entity, the download window, a `bot_run` row and `active_bot_run_id` to MySQL, then `run-bot.php` runs Python. The reset of `active_bot_run_id` at the end of a run is commented out, so the previous run's id stays active | B4 |
| 29 | The match step deletes every payment advice loaded more than four days earlier, matched or not, and the posting step deletes receipt, TDS and log rows older than four days. A document delivered again after four days is loaded as new | I3, I6 |
| 30 | The Python service reads `MasterConfig.xlsx` from `C:\Automation\Data\Config\`, the UiPath processes from `E:\Automation\Data\Config\` | H1 |
| 31 | The web page's run calls take no sign-in and build SQL from the query string; the database holds API and mailbox passwords in clear text, the MySQL root password is in the Python code, and the bank statement URL with credentials is written to `bot_run_log` | H3 |
| 32 | A repeated read of a workbook adds nothing to the database: the first advice insert fails on its existing `File name` and the loader does not roll its session back, so every later insert from that file fails too, including advices that are new. The supplied database (9 to 12 Nov) has no repeated invoice line; the analysis documents had said repeated reads insert the lines again | I3 |
| 33 | The International entities run their own copies of the code (Bot 1 and Bot 3) on their own database, `ca_with_pa_sxp`. Their loader reads 14 columns, all in today's 15-column International sheet, from the same `...\Statements\ExtractedPaymentAdvices\` tree, so the International hand-off loads as it is | F3, I1 |
| 34 | `Response_Report.xlsx` `B2` and `B3` are the row counts of the whole bank statement and outstanding tables, which Bot 1 empties at the start of each download; they are written only when the download switch is on, so with it off the "API down" mail goes out though nothing was tried | B4 |
| 35 | The International run needs no preparation: the entity is fixed in the database, the bank statement window comes from `MSS_SXP_BotRun.xlsx`, and SIS says `active_bot_run_id` is incremented when Python is called (no supplied code does it) | B4 |
| 36 | `BankStatement_Report.xlsx` is rewritten when the Python code is imported, from the previous run's tables, so deleting it before the run does not keep an old report out of the success mail | B4 |

## Conventions

- **ID.** Story `E3`; its tasks `E3.1`, `E3.2`. A task's **Notes** say which platform capability it uses and, in the extraction epic, which side owns it (**Module team** or **Tracto Go**).
- **Capability today.** Exists, Partly or To build, with the file path that is the evidence.
- **Done.** Every acceptance criterion passes, the story's own tests pass, and the open items of the story are either closed or recorded in the gap table above.
- **Decisions the stories take as fixed.** No child jobs and no browser runtime in any flow. The desktop runtime only runs a one-step main job that makes one HTTP call. Each flow is a main job plus a Go service that loops over items with its own status per item. The hand-off to collection accounting is the workbook file; the Python service is not modified and is started only as a program (B4). A reply to a sender goes to the sender only. DTSS skips spreadsheets. There is no review screen.
