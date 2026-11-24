# Working defaults for the open questions

The build starts before every question in [open-questions.md](open-questions.md) is answered. For each open point that affects the build, this table gives the default the code follows. Each default is a setting or one isolated piece of code, so an answer that differs changes that setting or piece and nothing else. When a question is answered, its row here is updated and the question is closed.

## Where the code goes

| Topic | Default | Questions |
|---|---|---|
| Repository | Everything is built in this repository (`tracto`). The extraction service is called as it is through its API; its repository is not changed, and the extraction epic (E) stays with its team. Where a module-team feature is missing, Tracto flags the document for review instead of working around it inside the service | 96 to 105 |
| Shared capabilities | Files, mail reading and sending, the program runner, work items and the appending write (stories A1 to A6) are added to `sheet-service`, which already holds the Microsoft sign-in, the sheet engine and the batch store | Decisions |
| Flow logic | A new Go module, `sis-service`, holds the five start endpoints (`/bankcheck/start`, `/maildispatch/start`, `/excelextract/start`, `/padvice/start`, `/carun/start`) and calls `sheet-service` over HTTP | Decisions |
| Work queue | A PostgreSQL table in `sheet-service`, extending the batch store | Decisions |
| Machine | Both services run on the Windows server that runs the UiPath robot today, with the same `E:\Automation`, `C:\Automation` and `C:\xampp\htdocs` paths, Python 3.6 and batch files. Every path is a setting | 89 |
| Main jobs | One-step desktop scripts with `http.post`, as in story G1, on a worker with an unlocked session | Decisions |

## Credentials

| Topic | Default | Questions |
|---|---|---|
| Mail accounts | A `mail_accounts` table in `sheet-service`, one row per entity mailbox: kind (`graph-app` with tenant ID, client ID and client secret; `graph-delegated` through the existing sign-in; `imap` with host, port, user and password), mailbox address, secret expiry date. The secret column is encrypted (AES-GCM) with one key from the environment variable `TRACTO_SECRET_KEY`. Rows are added and changed through a write-only admin endpoint; the secret is never returned or logged. Tracto warns 30 days before a secret expires | 31, 33, 34 |
| Other secrets | Environment variables `TRACTO_SECRET_<NAME>` on the service machine: extraction API key, PDF password list | 63, 87 |
| Sending account | Each entity row names the account its mails are sent from; if empty, a service sender account set in `Settings` | Story A2 |
| UiPath credentials in workbooks | Not read. The `APP ID`, `Application Secret`, `Tenant ID` and password keys are ignored and never logged | 87 |

## Start times

| Topic | Default | Questions |
|---|---|---|
| Time zones | Indian times in `Asia/Kolkata`; International times in `Australia/Sydney`. The run date and every date folder come from the schedule's zone | 11, 90 |
| Days | Monday to Friday for every schedule | 114 |
| Flow 2, mail download | Indian 10:00, International 05:45 | 90 |
| Flows 3 and 4, Indian | No schedule: the SPOC presses Run now, as they start it by hand today | 91 |
| Flow 4, International | 05:49 | 90 |
| Flow 1, bank check | Indian 11:00 and 11:30, International 07:00. The 11:30 run is a rerun of the unfinished entities only, so an entity reported available at 11:00 is not mailed again | 90 |
| Flow 5, International run | 07:55, as today; it does not wait for flow 1. Its mail states flow 1's result for the entity | 107 |

## Flow 1, bank check

| Topic | Default | Questions |
|---|---|---|
| Entities | Indian: the `entities` key of `MasterConfig.xlsx`, as today. International: MSS-Security and Southern-Cross-Protection, as today. Each is a row of the entity table with its own flag | 13 |
| International retry | Every 5 minutes until 07:50 Sydney time, then the entity is `Unavailable` | 106 |
| Unavailable mail | Once per entity, after its last try, listing the missing accounts, to the entity's own recipients | 111 |
| Fetch bots | Started as they are (`run_Bank_bot.bat`, `run_Bank_bot_<entity>.bat`), one entity at a time, with the entity written to `EntitySelected!A2`. Timeout 15 minutes per fetch. The exit code is stored; the folder check decides | 19 to 22 |
| Fetch bots' calendar | Before a fetch, Tracto reads `Bot_run_Configurations.xlsx` as the bots do. If the first row is not the column names or today has no filled row, the fetch is not started and the entity's result says why | 115 to 117 |
| Missing check workbook | The entity's try is `Failed`; the other entities continue | 25 |
| Statement roots | Indian `C:\Automation\Data\Input\Statements1\BankStatements`, International `C:\xampp\htdocs\Statements1\BankStatements` | 112 |
| Success queue | Replaced by the entity's result in the run record | 107 |

## Flow 2, mail dispatch

| Topic | Default | Questions |
|---|---|---|
| Entities | The fifteen entities of the dispatcher today, plus Southern-Cross-Protection as an International row | 35, 113 |
| Customer list | `Customers List.xlsx`, one sheet per entity, as today; first matching row wins and every matched keyword is recorded | 42, 121 |
| Progress marker | A record of processed message ids; the read flag is not used | 40 |
| Reply wording | The texts the UiPath workflows send today | 43 |
| Accepted file types | Those set for DTSS, for every entity | 38 |
| `PA_` pre-download | Kept, because `India.Excel.Extraction` reads from that folder today | 36, 37 |
| Staging retention | Nothing is deleted, as today | 39 |

## Flows 3 and 4, extraction

| Topic | Default | Questions |
|---|---|---|
| Spreadsheets processed | The `PA_` copies, as `India.Excel.Extraction` does today | 37 |
| Locked PDFs | Tracto tries the password list on the service machine and sends the unlocked file; a file no password opens is flagged for review | 63, 97 |
| Images | Sent to the extraction service as they are; a reply with no fields is flagged for review | 98 |
| Review threshold | Every document the service does not mark as agreed by both readings goes to `NeedValidation`, as UiPath sends low-confidence documents today | F4 |
| International entity | Southern Cross Protection only | 12 |

## Collection accounting and cutover

| Topic | Default | Questions |
|---|---|---|
| Python code | Not changed. The Indian run and Bot 3 stay a person's click on the web page | 1, 14 |
| Calendar | Flow 5 reads `MSS_SXP_BotRun.xlsx` as Bot 1 does; it does not start without a filled row for today and warns when fewer than 30 days of rows are left | 3 |
| `active_bot_run_id` | Not touched by Tracto. A run that ends because no run is active is reported as failed with that reason | 4, 109 |
| Parallel run | Tracto writes to its own folder tree. UiPath stays the live side for every flow until cutover. The flow 5 schedule is created disabled, because only one side may start Bot 1 on a day | 92 to 94 |
| Cutover | Per flow, after 10 working days with no unexplained difference | 93 |
