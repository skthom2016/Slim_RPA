# SIS flow 5: International_Bot1_SXP in Tracto

How the UiPath process `International_Bot1_SXP` 1.0.3 is rebuilt on Tracto: what it does, how it starts the collection accounting service, and the shape it takes. The stories are B4 in [stories/epic-B-C-bank-check-and-mail-dispatch.md](stories/epic-B-C-bank-check-and-mail-dispatch.md).

## In brief

**What it is for.** For an International entity (Southern Cross Protection, `SXP`), it starts the day's collection accounting run and tells the business how it went: whether the Oracle Fusion data came down, and if it did, sends the bank statement report and the outstanding report.

**What it does.** It resets two counters in a workbook, runs a batch file that runs the Python collection accounting service, reads the two counters back and sends one of three mails.

**What it becomes in Tracto.** A schedule starts a one-step main job (`carun-main`), which calls a Go process (`/carun/start`) and ends. Go deletes the stale report, resets the counters, runs the Python program with a timeout, reads the counters, checks the attachments and sends the mail. Like flow 1, it needs no worker for the work itself.

## How the collection accounting service is started

The Python collection accounting service has **no API and no API documentation**. Its `Api` folder is the service's own client for the Oracle Fusion reports and receivables services, reached through the Magic xpi middleware at `api.sisindia.com` ([payment-advice-samples.md](payment-advice-samples.md), section 9.3). Nothing calls the service: it is started as a program, `python.exe ...\bot_run.py`.

### What a run needs before it starts

`bot_run.py` takes no arguments. Everything it needs is in the MySQL database `collection_accounting` (MariaDB 10.4 on the same machine, 24 tables), read through the `settings` table and overlaid on the `Settings` sheet of `C:\Automation\Data\Config\MasterConfig.xlsx`:

| Setting or table | What it decides |
|---|---|
| `settings.active_bot_run_id` | The `bot_run` row the run belongs to. If it is -1 or empty, `bot_run.py` stops at once with exit code -1. The statement that resets it to -1 at the end of a run is commented out in `bot_run.py`, so it keeps the last run's id, and a run started without a new id logs against the previous run's row. |
| `settings.selected_entities` | Which entities the run processes (a comma-separated list; the web page sets one). |
| `download_time_config` | Per entity and report (`bankstatement`, `outstanding`, `unapplied`, `customer_master`), the date window of the download. |
| `settings.download_module_enabled`, `receipt_creation_module_enabled`, `comparison_module_enabled`, `posting_module_enabled` | Which of the four steps run. In the supplied database download is `no` and the other three `yes`. |
| `settings.api_download_path` | Where downloaded statements go: `C:\Automation\Data\Input\Statements1`, so a statement lands in `C:\Automation\Data\Input\Statements1\BankStatements\{dd MMM yyyy}\{entity}\` with the date from the machine clock. |
| `settings.python_path`, `settings.app_path` | The Python install (`C:\Users\botadmin\AppData\Local\Programs\Python\Python36`) and the code folder (`C:\xampp\htdocs\prod\collectionaccounting`). |

### Indian entities: the collection accounting web page

The web page is a PHP (Yii) application, `ca_with_pa`, under XAMPP on the same machine (`http://localhost:8080/prod/ca_with_pa/web/`). Its bot-run page has one row per entity (DTSS, SIS, ONESIS, RHSPL, SMC, SLV, USSP, AMRS, SISCASH, SISCOCASH, TSIS, Terminix), each with a "run from" and "run till" date and a **Start Process** button. Confirming starts the run in two calls:

1. **`bot-run/init?run_from=…&run_till=…&entity=…`** (`BotRunController::actionInit`) writes the two dates into `download_time_config` for that entity's `bankstatement` and `outstanding` rows, sets `settings.selected_entities` to the entity, creates a `bot_run` row named `Bot Run {entity} - {from} - {till}` with status 0, and sets `settings.active_bot_run_id` to its id.
2. **`web/run-bot.php`** runs `C:\Users\botadmin\AppData\Local\Programs\Python\Python36\python.exe C:\xampp\htdocs\prod\collectionaccounting\bot_run.py` with `shell_exec` and waits.

The page then shows the run's `bot_run_log` rows as they are written. The four step switches are toggles on the same page. The calls take no sign-in, and `actionInit` builds its SQL from the query string.

### International entities: a batch file

```
@echo off
C:\Users\botadmin\AppData\Local\Programs\Python\Python36\python.exe -W ignore E:\Automation\International_CAwithPA\Bot_1\ca_international_MSS\bot_run.py
```

That is the batch file text given for this process: the same Python install the web page uses, and a separate copy of the code under `E:\Automation\International_CAwithPA\Bot_1\`. `International_Bot1_SXP` runs `run_SXP_bot.bat`. Nothing in the web page or in the supplied Python code prepares a run for an International entity (sets `active_bot_run_id`, `selected_entities` or the download window), and nothing in either writes `Response_Report.xlsx`; the International copy is not supplied, so how it does both is open. The supplied database has no International entity in any setting or in `download_time_config`; Southern Cross Protection Pty Ltd appears in it only as a customer of the TSIS and AMRS entities.

### What a run does

| Step | Code | Switched by |
|---|---|---|
| Load the day's payment advice workbooks into MySQL | `pyment_advice_loader.data_load_from_excel`, from the three trees under `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\` (also run on import and a second time when download is on) | always |
| Download bank statements, outstanding, unapplied and customer data | `Api.ApiDownloadManager.init_download`; the bank statement, outstanding and unapplied tables are emptied first when the outstanding download is on | `download_module_enabled` |
| Create a receipt per bank credit, receipt number = UTR | `Api.ReceiptCreation` | `receipt_creation_module_enabled` |
| Match each advice with a bank credit (UTR, else total within 10), else an unapplied receipt; match each invoice with the outstanding list by invoice number | `CoreProcess.CaWithPa.compare` | `comparison_module_enabled` |
| Apply the receipt to each invoice and post the TDS adjustment | `CaWithPa.posting` | `posting_module_enabled` |

The match step first deletes every payment advice older than four days; the posting step deletes receipt, TDS and log rows older than four days. Errors inside a step are logged to `bot_run_log` and the run continues; an unhandled error sets the `bot_run` status to -1. Apart from the -1 exit when no run is active, the program ends normally whether or not the run worked.

Only one run can be active: there is one `active_bot_run_id`. Two runs at the same time (a person on the web page and a scheduled International run) would log against the same row and change each other's entity and dates.

**Who starts it.**

| Entities | Started by |
|---|---|
| Indian | A person, from the web page (Start Process, then confirm). No UiPath process is involved, and that stays as it is. |
| International | This UiPath process, `International_Bot1_SXP`, through `run_SXP_bot.bat`. |

Because `bot_run.py` begins by loading the advice workbooks, starting it is what consumes the hand-off file flows 3 and 4 write ([collection-accounting-handoff.md](collection-accounting-handoff.md)). It must start after flow 4 has written the day's workbook for the entity. An advice still unmatched four days after it was loaded is deleted by the match step.

## What the UiPath process does

1. **Read the configuration.** `Framework\InitAllSettings.xaml` reads `E:\Automation\International_CAwithPA\Bot_1\Data\Config\MasterConfig.xlsx`, sheets `EmailDetails`, `Settings` and `Constants`, into a key/value dictionary.
2. **Remove the previous report.** If `...\Bot_1\ca_international_SXP\Bank_Statement\BankStatement_Report.xlsx` exists, it is deleted, so an old report is never sent again.
3. **Reset the counters.** `E:\Automation\International_CAwithPA\Bot_1\Response_Report.xlsx`, `Sheet1`: write 0 into `B2` (bank statement) and `B3` (outstanding).
4. **Run the batch.** `Invoke PowerShell` runs `E:\Automation\International_CAwithPA\Bot_1\run_SXP_bot.bat` and waits for it. There is no timeout and no exit-code check. A commented-out older version started the batch, waited five minutes and then polled for `BankStatement_Report.xlsx` once a minute for 25 minutes.
5. **Read the counters.** `B2` into `bank_number` and `B3` into `outstanding_number`, each parsed as a whole number. The same two cells are read in two identical Excel scopes, one after the other.
6. **Build the outstanding path.** The entity name is the literal `Southern-Cross-Protection`; today as `dd MMM yyyy` from the machine clock gives `C:\xampp\htdocs\Statements1\OutstandingStatements\<date>\SXP_Group\outstanding.xlsx`.
7. **Send one mail** through an Office 365 scope (Microsoft Graph), subject `Mail from Bot 1`, plain text:

| Condition | To | CC | Body | Attachments |
|---|---|---|---|---|
| `B3` = 0 | `<entity>_api_down_receiverID` | `<entity>_receiverID_CC` | "Production API's are down." | none |
| `B3` > 0 and `B2` = 0 | `<entity>_bank_issue_receiverID` | `<entity>_receiverID_CC` | "Bank Statement Report was not downloaded." | none |
| both > 0 | `<entity>_receiverID` | `<entity>_receiverID_CC` | "Please find the attached Bank Statement and ATB Report." | `BankStatement_Report.xlsx`, `outstanding.xlsx` |

`<entity>` is `Southern-Cross-Protection`; recipient values are comma-separated lists.

The package carries the REFramework files, but `Main.xaml` invokes only `InitAllSettings.xaml`; the transaction loop, retry, screenshot and clean-up workflows are never run. There is no `Try`/`Catch` anywhere in `Main.xaml`.

## UiPath steps and their Tracto equivalent

| # | UiPath | Tracto | Story |
|---|---|---|---|
| 1 | `MasterConfig.xlsx` with paths, recipients and credentials | The entity table and `Settings` hold paths and recipients; no credential is read | B4, H1, H3 |
| 2 | Delete `BankStatement_Report.xlsx` | A3 `delete` inside a configured root | B4 |
| 3 | Write 0 into `B2` and `B3` | The A6 cell write, as numbers | B4 |
| 4 | `Invoke PowerShell` on the batch file, no timeout | A4 runs a named command (the Python executable and the entity's `bot_run.py`) with a timeout, and keeps the exit code and output | B4 |
| 5 | Read `B2` and `B3`, `Integer.Parse` | Read and parse; a blank or non-numeric cell is a failed run with a mail, not an exception | B4 |
| 6 | Literal entity name, machine-clock date | Entity from the table, date in the schedule's time zone, group folder (`SXP_Group`) a setting | B4 |
| 7 | Three mails with the same subject | Four mails (adding "the run failed"), each with its own subject; attachments checked before sending | B4, A2 |

## What has to be built

Nothing that is specific to this process beyond the B4 loop: it uses the program runner (A4), file operations (A3), the cell write (A6) and mail sending (A2) that flows 1 and 2 need anyway.

## Security findings in this package

- **Credentials in the configuration workbook.** `APP ID`, `Application Secret`, `Tenant ID`, `<entity>_senderID` and `<entity>_password` are read from `MasterConfig.xlsx` in clear text. The `*password*` log exclusion of the project covers the log, not the file.
- **Interactive sign-in on an unattended robot.** The Office 365 scope is set to `InteractiveToken` while also given an application secret and a mailbox password.
- **A test workflow in the production package.** `Test.xaml` loads the production configuration and mails a production `outstanding.xlsx` to a hard-coded mailbox address.

These are listed for rotation in H3.

## What improves in Tracto

- **A failure is reported.** UiPath stops silently when the batch fails, the workbook is locked or a counter is blank; Tracto sends the failure mail and records the step that failed.
- **A timeout.** The batch can no longer hang the robot indefinitely.
- **The attachments are checked** before the success mail goes out.
- **One run per entity at a time**, because the run shares MySQL state and `Response_Report.xlsx`.
- **No credential** in a workbook, and no test workflow in what is deployed.

## Open questions

1. The International copy of the service (`ca_international_SXP`) and `run_SXP_bot.bat` are not supplied. The supplied Indian copy never writes `Response_Report.xlsx`. Which code sets `B2` and `B3`, and do they hold a count or a flag?
2. The batch file text names `ca_international_MSS`, the package reads under `ca_international_SXP`. Is there one copy and one batch file per International entity?
3. What prepares an International run? For the Indian entities the web page's `bot-run/init` sets `selected_entities`, the download window in `download_time_config`, a new `bot_run` row and `active_bot_run_id`. Neither the web page nor the supplied code does this for an International entity, and `active_bot_run_id` is never reset, so an unprepared run logs against the last Indian run and uses the last Indian entity's settings, unless the International copy has its own database or its own preparation.
4. Does the International copy load the 15-column International hand-off sheet? The Indian loader needs 22 columns that the International sheet does not have ([known-faults-in-current-output.md](known-faults-in-current-output.md), L2).
5. When is `International_Bot1_SXP` triggered today, and must it wait for the day's International payment advice workbook?
7. Does the International copy use the same MySQL database (`collection_accounting`) as the Indian one? The supplied database has no International entity in any setting or download window.
6. The success mail calls the second attachment the "ATB Report" and the file is `outstanding.xlsx`. Which name do the recipients expect?
