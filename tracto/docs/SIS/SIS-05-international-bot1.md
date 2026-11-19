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

### International entities: two copies of the code, one database

```
@echo off
C:\Users\botadmin\AppData\Local\Programs\Python\Python36\python.exe -W ignore E:\Automation\International_CAwithPA\Bot_1\ca_international_MSS\bot_run.py
```

That is the batch file text given for this process. `International_Bot1_SXP` runs `E:\Automation\International_CAwithPA\Bot_1\run_SXP_bot.bat`, which is not supplied. The `run_bot.bat` inside each supplied code folder runs a developer's virtual environment on `D:\Python\ca_international\bot_run.py` and is not the live one.

Two International copies of the code are supplied. They differ in what `bot_run.py` does, and each writes its reports under its own bot folder:

| Copy | Folder its code writes to | What its `bot_run.py` does |
|---|---|---|
| Bot 1 (`ca_international_SXP_1` as supplied) | `E:\Automation\International_CAwithPA\Bot_1\ca_international_SXP\` | Loads the advices, downloads from Oracle Fusion, writes the two counters, then receipt creation, matching and posting as switched |
| Bot 3 (`ca_international_SXP` as supplied) | `E:\Automation\International_CAwithPA\Bot_3\ca_international_SXP\` | Loads the advices again, matching, posting, and two reconciliation reports; no download and no receipt creation |

Both differ from the Indian copy in the same ways:

| | Indian copy | International copies |
|---|---|---|
| MySQL database | `collection_accounting` | `ca_with_pa_sxp`, on the same machine |
| `MasterConfig.xlsx` read by Python | `C:\Automation\Data\Config\` | `E:\Automation\International_CAwithPA\Config\` |
| Entity key | the entity code (`DTSS`, `SIS`, …) | `SXP_Group` or `MSS_Group`, mapped in `bot_run.py` to the folder names `Southern-Cross-Protection` and `MSS-Security` |
| Bank statement date window | `download_time_config`, set by the web page per run | `E:\Automation\International_CAwithPA\Config\MSS_SXP_BotRun.xlsx`: the row whose `Current Date` is today, columns `SXP_Group Start Date` and `SXP_Group End Date` (the SXP columns are read whatever the entity) |
| Run preparation | `bot-run/init` from the web page | None in the code. SIS says `active_bot_run_id` is incremented as soon as Python is called; no supplied code does that (both copies only read it, and the reset is commented out), so it happens outside the code, for example in the database. `selected_entities` is not written by any supplied code and stays as set in `ca_with_pa_sxp` |
| Purge | Advices older than four days at the match step; receipt, TDS and log rows older than four days at posting | At each download, when the outstanding download is on: the bank statement, outstanding, unapplied and receipt-created tables are emptied, and advices older than eight days are deleted. No purge at matching or posting |
| Advice loader | 22 columns; the first existing advice makes the whole file fail | 14 columns (below); each row is committed on its own, and an advice already present is skipped without stopping the rest |

**The International loader** reads the first sheet of the workbook and needs `ValueDate`, `BeneficiaryName`, `BeneficiaryBankAccountNumber`, `BeneficiaryBankName`, `CustomerName`, `CustomerBank`, `UTR / TransactionReferenceNumber`, `TotalCreditedAmount`, `File name`, `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount`, `Other Deduction` and `NetAmount`. All 14 are in today's 15-column International sheet; `BeneficiaryBankBSBCode` is not read. An invoice line is skipped as a duplicate on the same `InvoiceNumber` and `File name` only if the database has that unique key: the code declares it, but the table is read from the database, not created by this code, so whether the key exists depends on the `ca_with_pa_sxp` schema, which is not supplied. It reads the two day workbooks:

- `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\PDF_JPEG\ML_ExtractedFiles\{dd MMM yyyy}\Southern-Cross-Protection\Extracted payment advices.xlsx`
- `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\Excel\Extractor_ExtractedFiles\{dd MMM yyyy}\Southern-Cross-Protection\Extracted Payment Advices.xlsx`

That is the same tree as the Indian entities, not the `...\Input\International\...` root named in the International `MasterConfig.xlsx`.

### What a run does

**Bot 1**, the copy `International_Bot1_SXP` runs:

| Step | Code | Switched by |
|---|---|---|
| Write `BankStatement_Report.xlsx` from the current bank statement table, before anything else, including the check for an active run | `BankStatementDownload` runs on import | always |
| Load today's two advice workbooks (the paths above) | `PaymentAdvice.read_payment_advice` | `download_module_enabled` |
| Empty the bank statement, outstanding, unapplied and receipt-created tables; delete advices older than eight days; download the outstanding list. If it returns no rows the entity stops there and the bank statement is not downloaded | `Api.ApiDownloadManager`, `Api.Outstanding` | `download_module_enabled` and `outstanding_download_enable` |
| Download the bank statement for the window in `MSS_SXP_BotRun.xlsx`. With no row for today the download is skipped, with only a printed error | `Api.Bankstatement` | `bankstatement_download_enable` |
| Download unapplied receipts and the customer master | `Api.Unapplied`, `Api.CustomerMaster` | `unapplied_download_enable`, `customer_master_download_enable` |
| Write the counters into `E:\Automation\International_CAwithPA\Bot_1\Response_Report.xlsx`: `B2` = the number of rows in the bank statement table, `B3` = the number of rows in the outstanding table. Also write up to 500 advices loaded today into `...\Bot_1\PaymentAdvice.xlsx`, over the previous content without clearing it | `Api.CheckNumberofRecords` | `download_module_enabled` |
| Create receipts; find the customer by invoice and match; post | `ReceiptCreation`, `CaWithPa` | the three step switches |
| Write `BankStatement_Report.xlsx` again, from the tables after the run | `BankStatementDownload.bankdownload` | always |

**Bot 3:**

| Step | Code | Switched by |
|---|---|---|
| Write `...\Bot_3\...\Bank_Statement\BankStatement_Report.xlsx` and `...\Bot_3\...\Final_Report\FinalReport.xlsx` on import, before the check for an active run | `BankStatementDownload`, `FinalReportDownload` | always |
| Load today's two advice workbooks again (advices already loaded are skipped), find the customer by invoice, match | `PaymentAdvice`, `CaWithPa` | `comparison_module_enabled` |
| Post | `CaWithPa.posting` | `posting_module_enabled` |
| Write the bank statement report, `FinalReport.xlsx` (invoice lines of advices loaded since the previous working day and updated today, and a `Receipt Applied Details` sheet) and `FinalReconcilReport.xlsx` (sheets `KPI_Dashboard`, `Customer_Recon`, `Transaction_Details`, `Receipt_Applied`, `Receipt_Not_Created`, `PA_Not_Allocated`, `Invoice_Not_Applied`) | `BankStatementDownload`, `FinalReportDownload`, `FinalReport` | always |

Matching is the same in both: an advice is matched to a bank credit by UTR, else by total within 10, else to an unapplied receipt; each invoice is matched to the outstanding list by invoice number. Errors inside a step are logged to `bot_run_log` and the run continues; an unhandled error sets the `bot_run` status to -1. Apart from the -1 exit when no run is active, the program ends normally whether or not the run worked.

Which process runs Bot 3, and when, is not in the supplied material; `International_Bot1_SXP` runs only Bot 1.

### What SIS has confirmed

| Point | Answer |
|---|---|
| International entities | One today: Southern Cross Protection. Each International entity has its own copy of the code and its own database; the Indian entities share one copy and one database |
| Database | `ca_with_pa_sxp`, separate from the Indian one, with the same table structure |
| Trigger | `International_Bot1_SXP` is time-triggered at about 07:55 Australian time, after the International mail monitoring (05:45) and PDF extraction (05:49) |
| Hand-off folder | Indian and International both read from `...\Statements\ExtractedPaymentAdvices\` |
| Result of a run | The `bot_run` table holds the status |
| Running twice | Allowed, but receipt creation and receipt allocation are duplicated when the same data is run again, which is an issue |
| Indian and International at the same time | Allowed: different databases |
| Repeated invoice lines | Not stored; removed by invoice number |
| Recipients | From the `EmailDetails` sheet of the International `MasterConfig.xlsx` |
| Locked PDFs | International has no password-protected PDFs and no password file. The International sample has 20 password-protected PDFs ([payment-advice-samples.md](payment-advice-samples.md)) |

### What the two counters mean

`B2` and `B3` are counts of whole tables, not of what this run downloaded. They mean "downloaded today" only because the same run empties the tables first. Read with the steps above:

| Situation | `B2` | `B3` | Mail UiPath sends |
|---|---|---|---|
| Both downloads worked | rows in the statement | rows in the outstanding list | Success |
| Outstanding returned nothing (Oracle Fusion or the middleware down) | 0 (tables emptied, statement not fetched) | 0 | "Production API's are down." |
| No row for today in `MSS_SXP_BotRun.xlsx`, or the statement download failed | 0 | rows | "Bank Statement Report was not downloaded." |
| `download_module_enabled` is `no` | 0 (never written) | 0 | "Production API's are down.", though nothing was tried |
| No active run (`active_bot_run_id` -1) | 0 | 0 | "Production API's are down." |

Because `BankStatement_Report.xlsx` is written when the code is imported, deleting it before the run (UiPath step 2) does not stop an old report: the file is recreated at once from the previous run's table, then rewritten at the end.

### Who starts it

| Entities | Started by |
|---|---|
| Indian | A person, from the web page (Start Process, then confirm). No UiPath process is involved, and that stays as it is. |
| International, Bot 1 | `International_Bot1_SXP`, through `run_SXP_bot.bat` |
| International, Bot 3 | Not known |

Because `bot_run.py` loads the advice workbooks, starting it is what consumes the hand-off files flows 3 and 4 write ([collection-accounting-handoff.md](collection-accounting-handoff.md)). It starts at 07:55 Australian time, about two hours after the International PDF extraction (05:49). Only one run can use a database at a time: Bot 1 and Bot 3 share `ca_with_pa_sxp`, its single `active_bot_run_id` and its tables, and Bot 1 empties the tables Bot 3 matches against. An Indian run and an International run can overlap. A second run on the same data duplicates receipt creation and allocation, so a successful run is not repeated for the same day.

### The International configuration workbooks

`MasterConfig_International.xlsx`, as supplied (which of the two International `MasterConfig.xlsx` locations it comes from is not stated):

| Sheet | What it holds |
|---|---|
| `Settings` | The UiPath framework keys (queue and asset names); `Entity` = `MSS-Security,Southern-Cross-Protection`; `TotalNumberOfEntity` 1 and `PriorityEntity_1` = `Southern-Cross-Protection`; report and tracker paths under `E:\Automation\Tracker\International\`; `PaymentAdvicesFolderPath` = `E:\Automation\Data\Input\International\Payment Advices`; `PaymentAdvicesExtractionFolderPath` = `E:\Automation\Data\Input\International\Statements\ExtractedPaymentAdvices` |
| `EmailDetails` | Per entity, for `MSS-Security`, `Southern-Cross-Protection`, `Henderson-Security-Services` and `Henderson-Technologies`: `_senderID`, `_password`, `_receiverID`, `_receiverID_CC`, `_bank_issue_receiverID`, and `_api_down_receiverID` for all except `MSS-Security`; then `Application Secret`, `APP ID`, `Tenant ID` |
| `InboxMonitor` | The International dispatcher's download folder (`E:\Automation\International_CAwithPA\EmailPADispatcher-International\SourceDownload`), the "not processed" reply text, and a mail count for each of nine International entities: MSS-Security, MSS-Strategic-Medical&Rescue, Habitat-Security, Southern-Cross-Protection, Platform4Group, Triton-Security, The-Alarm-Centre, Henderson-Security-Services, Henderson-Technologies |
| `PDF Extraction` | `EntityNameForCurrentRun` = `Southern-Cross-Protection`, the header template, the PDF log folder |
| `ExcelExtraction`, `EmailExtraction`, `ProdApiSettings`, `TestApiSettings`, `Constants`, `Assets`, `Sheet1` | Copies of the Indian sheets (Indian entity names, `D:` paths, the Magic xpi URLs and API account), the framework constants, and the nine entities numbered 1 to 9 |

`BankMasterInternational.xlsx` has the same shape as `BankMaster.xlsx`: a sheet per entity listing `BANK_ACCOUNT_NAME` (MSS-Security two accounts, Southern-Cross-Protection four) and `EntitySelected` `A2` = `Southern-Cross-Protection`. That is the layout `BankAvailCheck` uses, so an International bank availability check probably exists; it is not among the supplied packages.

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

The International code and configuration add:

- **The International `MasterConfig.xlsx`** holds the application secret, four mailbox passwords and the Magic xpi API password in clear text.
- **Both International code copies** hold the MySQL credentials in `DBClasses\mysql_helper.py`.
- **Both code folders carry customer documents.** Their `test_data` folders hold customer payment advices, bank statements and outstanding reports, and they ship a bundled virtual environment.

These are listed for rotation in H3.

## What improves in Tracto

- **A failure is reported.** UiPath stops silently when the batch fails, the workbook is locked or a counter is blank. Tracto sends the failure mail and records the step that failed.
- **A timeout.** The batch can no longer hang the robot indefinitely.
- **The attachments are checked** before the success mail goes out, including that `BankStatement_Report.xlsx` was written after the program started, not left over from the previous run.
- **One run at a time per database, and no second successful run on the same day.** A repeat would duplicate receipt creation and allocation.
- **No credential** in a workbook, and no test workflow in what is deployed.

## Open questions

1. Which UiPath process runs Bot 3 (the code under `E:\Automation\International_CAwithPA\Bot_3\ca_international_SXP\`), at what time, through which batch file, and who receives `FinalReport.xlsx` and `FinalReconcilReport.xlsx`? Is there a Bot 2? Bot 3 is to move to Tracto as well if it is a UiPath process.
2. Please share `E:\Automation\International_CAwithPA\Bot_1\run_SXP_bot.bat` as it is today. The text given earlier names `ca_international_MSS`, but the Bot 1 code writes under `ca_international_SXP`.
3. Please share `E:\Automation\International_CAwithPA\Config\MSS_SXP_BotRun.xlsx`. Who adds the row for each day, and how far ahead? With no row for today, the bank statement is not downloaded and the "bank statement not downloaded" mail goes out.
4. SIS says `active_bot_run_id` is incremented as soon as Python is called. No code in either copy does that. Is it a database trigger, or something else?
5. Please share the structure (no data) of `ca_with_pa_sxp`, especially whether `payment_advice_invoice` has a unique key on `InvoiceNumber` and `file_name`, and the values of the `settings` keys other than the passwords: `selected_entities`, `api_download_path`, the four step switches and the four `*_download_enable` switches. Bot 1 and Bot 3 read the same switches, so with all of them on, Bot 1 already matches and posts.
6. When `download_module_enabled` is `no`, Bot 1 never writes the counters, so the "Production API's are down" mail goes out although nothing was tried. Is the download always on for Bot 1?
7. `Api\CheckNumberofRecords.py` also writes up to 500 of today's advices into `E:\Automation\International_CAwithPA\Bot_1\PaymentAdvice.xlsx` without clearing the rows already there. Does anyone use that file?
8. The supplied `MasterConfig_International.xlsx`: is it `E:\Automation\International_CAwithPA\Config\MasterConfig.xlsx` (read by Python) or `E:\Automation\International_CAwithPA\Bot_1\Data\Config\MasterConfig.xlsx` (read by UiPath)? Its `PaymentAdvicesExtractionFolderPath` is `E:\Automation\Data\Input\International\Statements\ExtractedPaymentAdvices`, while the loader reads `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices`.
9. "Australian time" for the 07:55 trigger: which time zone (for example Sydney, with daylight saving)?
10. The International sample has 20 password-protected PDFs, but SIS says International has none. Should those 20 be reported to the sender as unreadable?
11. `BankMasterInternational.xlsx` has the same layout as the workbook `BankAvailCheck` uses. Is there an International bank availability check, and is it to move to Tracto?
