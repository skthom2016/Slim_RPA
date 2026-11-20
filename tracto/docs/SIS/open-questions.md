# SIS migration: open questions

Every question still open for moving the UiPath payment-advice processes to Tracto. Each one can be read on its own. The UiPath processes are named by their package names:

| Package | What it does |
|---|---|
| `BankAvailCheck` | Checks each entity's bank statement folder for the day, runs a fetch bot for missing ones, mails which accounts have no statement |
| `InternationalBankAvailCheck` | The same check for the International entities (MSS-Security and Southern-Cross-Protection), retrying every five minutes until the statements are there |
| `EmailPADispatcher` | Reads the entity mailboxes and files each payment advice attachment into dated folders |
| `India.Excel.Extraction` | Validates and cleans spreadsheet payment advices into the standard table |
| `Payment.Advice.Extraction` | Reads PDF and image payment advices into the standard table |
| `International_Bot1_SXP` | Runs the collection accounting batch for Southern Cross Protection and mails the result |
| Bot 3 (International) | Python code that matches, posts and writes the International reconciliation reports; started from the web page through `run-bot-sxp.php`, not by a UiPath process |

"The collection accounting service" is the Python code in the `collectionaccounting` folder (`bot_run.py`, `pyment_advice_loader.py`, the `Api` and `CoreProcess` folders) with its MySQL database. The International entities run their own copies of that code (Bot 1 and Bot 3) with their own database, `ca_with_pa_sxp`, and two more download-only copies as the International fetch bots, one per entity, each with its own database.

## Blocking questions

These answers decide what is built, where it runs, or whether it can be tested. They are listed in full in the sections below.

| # | Topic | Questions |
|---|---|---|
| 1 | Bot 3: which page starts it, whether that page prepares the run in `ca_with_pa_sxp`, and who receives its reports | 1, 4 |
| 2 | International bank availability check: which entities, how long to keep retrying, and what reads its success queue | 13, 106, 107 |
| 3 | Entities each process must cover | 12, 81 |
| 4 | Whether Tracto runs on the UiPath robot's machine, with access to its folders and batch files | 89 |
| 5 | Mailboxes, mail server, and `Mail.Read` / `Mail.Send` for the app registration | 31, 34 |
| 6 | Test databases and a test Oracle Fusion / Magic xpi endpoint | 95 |
| 7 | `run_Bank_bot.bat` (the Indian fetch bot), and who extends `MSS_SXP_BotRun.xlsx` past 31 December 2026 | 3, 19 |
| 8 | The PDF password list and its owner | 63 |
| 9 | Which side is live per flow during the parallel run, and what decides the switch | 93, 94 |
| 10 | Which spreadsheets `India.Excel.Extraction` processes (`XLS\` folder or `PA_` copies) | 37 |

## 1. Collection accounting service and `International_Bot1_SXP`

1. Bot 3 (`E:\Automation\International_CAwithPA\Bot_3\ca_international_SXP\bot_run.py`, which matches, posts and writes `FinalReport.xlsx` and `FinalReconcilReport.xlsx`) is run by the web page's `run-bot-sxp.php` with `shell_exec`, the same way `run-bot.php` runs the Indian copy. Which page or button calls `run-bot-sxp.php`? Does that page first prepare the run in `ca_with_pa_sxp` (the date window, `selected_entities`, a new `bot_run` row and `active_bot_run_id`), as `bot-run/init` does for the Indian entities? At what time of day is it usually started, after `International_Bot1_SXP` at 07:55? Who receives the two reports? Is there a Bot 2? Bot 3 stays a person's click outside Tracto, like the Indian run, unless it is to be scheduled.
2. `run_SXP_bot.bat` runs `...\Bot_1\ca_international_SXP\bot_run.py` and returns Python's exit code (`exit /b %errorlevel%`). `bot_run.py` returns a non-zero code only when no run is active; a failed download, receipt creation or posting still ends with 0. Is the absence of an active run the only failure the exit code is meant to signal?
3. `MSS_SXP_BotRun.xlsx` is a calendar filled in advance from 1 November 2025 to 31 December 2026, one row per day, dates stored as text. Who extends it for 2027, and by when? From 1 January 2027 there is no row, so the bank statement is not downloaded and the "Bank Statement Report was not downloaded" mail goes out every day. The windows follow a rule: on Monday the previous Friday, on Tuesday Saturday to Monday, on other working days the day before; an empty row on weekends and on 25 and 26 December, 2 January and Easter Monday, while 1 January and Good Friday have one. 7 April 2026 repeats the window of 3 April (2 April only). Until 1 December 2025 Monday covered Friday to Sunday, and in December 2025 the MSS columns still did. Which rule is current, and which holiday calendar decides the empty days? Could Tracto compute the window from that rule and a holiday list instead of a hand-kept calendar?
4. You said `active_bot_run_id` is incremented as soon as Python is called. Neither International copy of the code does that: they only read it, and the reset to -1 at the end is commented out. What increments it: a trigger in the database, the page that runs Bot 3 (question 1), or something else? If it is that page, Bot 1 at 07:55 logs against the run Bot 3 last created.
5. Please share the structure (no data) of the International database `ca_with_pa_sxp`. In particular, does `payment_advice_invoice` have a unique key on `InvoiceNumber` and `file_name`? The International code skips a repeated invoice line only if the database refuses it.
6. What are the values in `ca_with_pa_sxp`'s `settings` table, apart from the passwords, for `selected_entities`, `api_download_path`, `download_module_enabled`, `receipt_creation_module_enabled`, `comparison_module_enabled`, `posting_module_enabled`, `outstanding_download_enable`, `bankstatement_download_enable`, `unapplied_download_enable` and `customer_master_download_enable`? Bot 1 and Bot 3 read the same switches, so if matching and posting are on, Bot 1 already matches and posts before Bot 3 runs.
7. Bot 1 writes the counters into `Response_Report.xlsx` only when `download_module_enabled` is `yes`. When it is `no`, both cells stay 0 and the "Production API's are down" mail goes out although nothing was tried. Is the download always on for Bot 1?
8. The counters are the number of rows in the whole bank statement table (`B2`) and outstanding table (`B3`), which Bot 1 empties at the start of each download. Is that the intended meaning, "rows downloaded in this run"?
9. Bot 1 also writes up to 500 of today's payment advices into `E:\Automation\International_CAwithPA\Bot_1\PaymentAdvice.xlsx`, over the rows already in the file without clearing them. Does anyone use that file?
10. Is the `MasterConfig_International.xlsx` you shared the file the Python code reads (`E:\Automation\International_CAwithPA\Config\MasterConfig.xlsx`) or the one `International_Bot1_SXP` reads (`E:\Automation\International_CAwithPA\Bot_1\Data\Config\MasterConfig.xlsx`)? Its `PaymentAdvicesExtractionFolderPath` is `E:\Automation\Data\Input\International\Statements\ExtractedPaymentAdvices`, while the loader reads `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices`, which you confirmed. Is that setting unused?
11. The 07:55 trigger is in "Australian time". Which time zone exactly (for example Sydney, with daylight saving)? The mail monitoring at 05:45 and the PDF extraction at 05:49 use the same zone?
12. The International `MasterConfig.xlsx` has mail settings for MSS-Security, Henderson-Security-Services and Henderson-Technologies, and mail counts for nine International entities. You said Southern Cross Protection is the only International entity today. Should Tracto set up only Southern Cross Protection?
13. The International bank availability check (`InternationalBankAvailCheck`) checks the entities in the `Entity` key of the International `MasterConfig.xlsx`: MSS-Security and Southern-Cross-Protection, each with its own fetch bot and batch file. Is it to move to Tracto as part of flow 1? Should Tracto check both entities, or only Southern-Cross-Protection?
14. The reset of `active_bot_run_id` to -1 at the end of `bot_run.py` is commented out in the Indian copy as well, so the last run's id stays active until the web page sets a new one. Is that deliberate?
15. In the Indian copy the match step deletes every payment advice loaded more than four days earlier, matched or not, and the posting step deletes receipt, TDS and log rows older than four days. Is four days deliberate, and how does anyone follow up an advice that was never matched? (The International copy instead deletes advices older than eight days at each download.)
16. `Southern Cross Protection Pty Ltd` appears in the Indian database only as a customer of the TSIS and AMRS entities. Is it the same company as the International entity?
17. The Indian Python service reads `MasterConfig.xlsx` from `C:\Automation\Data\Config\`, while the UiPath processes read it from `E:\Automation\Data\Config\`. Are these two copies of the same file, or different files?
18. You said repeated invoice lines are removed by invoice number before they reach MySQL. Where does that happen? In the sample day workbooks, documents not confirmed by a person have every invoice line twice, and the Indian invoice table has no unique key.

## 2. `BankAvailCheck`

19. `BankAvailCheck` writes the entity name into `E:\Automation\Data\Config\BankMaster.xlsx`, sheet `EntitySelected`, cell `A2`, then runs `C:\xampp\htdocs\prod\run_Bank_bot.bat`. The International fetch bots are copies of the collection accounting code that run only the download: they read the entity from cell `A2` of `BankMasterInternational.xlsx`, ask Oracle Fusion which accounts have a statement, write `{entity}_check\bank_statements_check.xlsx`, and download the statement only when every account has one. Is `run_Bank_bot.bat` the same kind of copy for the Indian entities? Please share it, the code folder it runs, and where that copy takes its date window from.
20. Can that fetch bot take the entity name as a command-line argument instead of reading cell `A2`? If not, entities must be fetched one at a time.
21. When the fetch bot ends with exit code 0, does that mean the statements were downloaded, or only that the script finished? The International fetch bots end with 0 and mark their run successful even after an error.
22. How long may one entity's fetch take before it should be treated as failed?
23. Is it safe to run the fetch twice for the same entity on the same day? The Python download deletes and recreates the entity's date folder before it downloads.
24. The Python download writes the statement to `C:\Automation\Data\Input\Statements1\BankStatements\{dd MMM yyyy}\{entity}\` with today's date from its machine clock, whatever the download window in `download_time_config` is. `BankAvailCheck` checks the same folder for today. Is the statement for a day always downloaded on that same day, or can it arrive later under a later date folder?
25. For the International entities the fetch bot writes `{entity}_check\bank_statements_check.xlsx` (one row per bank account with `AVAILABLE` `Y` or `N`) on every run. Is the Indian check workbook written the same way, by `run_Bank_bot.bat`?
26. The unavailable-accounts table is headed "Bank Account Number" but holds the value of `BANK_ACCOUNT_NAME`. Which one does the operations team want to see?
27. Today every unavailable-accounts mail goes to the DTSS recipients, whichever entity is missing statements. Should each entity's mail go to that entity's own recipients? Should the DTSS recipients stay in copy?
28. ONESIS has no recipient key in `MasterConfig.xlsx`. Who should receive its mails?
29. The availability mail is sent even when no entity is available. Should it still be sent in that case?
30. The check workbook is read only in the range `A1:E200`. Was 200 rows a real limit, or can the whole sheet be read?

## 3. `EmailPADispatcher`

31. Which mailbox does each entity use, and is each one Microsoft 365 or the in-house mail server (`mail.sisindia.com`)? `MasterConfig.xlsx` has mailbox settings for DTSS only.
32. For the in-house mail server: which IMAP port and TLS mode? The configuration shows port 465, which is the SMTP port.
33. Where are the mailbox passwords kept today? The packages read them from UiPath Orchestrator assets named `{entity}_{AssetCredentialName}`. Who owns these accounts and can give them to the new service?
34. Can the Microsoft 365 mailboxes be read through one account with shared-mailbox permission, or does each need its own sign-in? Will the tenant administrator grant `Mail.Read` and `Mail.Send` to the app registration?
35. Which entities should the dispatcher cover? Its list has fifteen entities. It leaves out AEPL and RHSPL, which the Python loader includes, and it includes RARE, which the loader does not.
36. Before the main loop, the dispatcher saves attachments whose first file name starts with `PA_{entity}` into a second folder. Is that still needed? `India.Excel.Extraction` reads from that folder.
37. Spreadsheets are filed into an `XLS\` folder, but `India.Excel.Extraction` reads only the `.xlsx` files at the top of the entity folder (the `PA_` copies). Which spreadsheets is `India.Excel.Extraction` meant to process?
38. Which attachment types should each entity accept, besides those set for DTSS?
39. How long should saved attachments, archives and mail bodies be kept in the staging folder? Nothing deletes them today.
40. A mail counts as handled when it is marked read. Can we instead record the IDs of the mails we processed, so that a person opening the mailbox does not hide a mail from the bot?
41. When a file cannot be processed, the sender is mailed and the file stays in staging with no record. Should it also be listed somewhere with a status, so the backlog is visible?
42. May the customer list (`Customers List.xlsx`) be changed to named columns with more than two aliases per customer? Today the first matching row wins, which goes wrong when a mail mentions two customers.
43. What wording should the three replies to senders use (failed archive, no attachment, unsupported file type)? `MasterConfig.xlsx` holds mail text that no process reads.
44. Does anything read cell `B2` of `ModuleTracker.xlsx`, or the `{entity}_MailCount` value the dispatcher writes?
45. Mails with no attachment are filed as work for a downstream process that writes the `EmailBody` workbook. Which process is that, and does it stay as it is?
46. In the DTSS sample, 14 of the 15 saved mail bodies are bounce notices. One is a password-expiry mail with a link to change a password, which looks like phishing. Where do these come from? Who should be alerted when such a mail arrives?

## 4. `India.Excel.Extraction`

47. Only three cleaning rules run today: a cell that is just `-` becomes blank, an empty `TDS` or `Other Deductions` becomes 0, and repeated spaces collapse in the customer name and total. Fifty-three other rules are commented out. Are the three the intended rules, and were the fifty-three switched off on purpose?
48. Which version is live? The current `MasterConfig.xlsx` has `ExcelMappingPath` (`E:\Automation\EmailExcelExtraction\Mapping`) and no `Entity` key, but version 1.0.7 reads `Entity` and not `ExcelMappingPath`. Please share the files in the mapping folder.
49. Can an entity folder hold several spreadsheet advices on one day, or exactly one named `PA_<entity>`? The published workflow processes every `.xlsx`, while the second driver in the package skips the entity unless there is exactly one.
50. Please send one real spreadsheet advice from each entity that sends them, and one that should be rejected.
51. Is the `Sl No` column unique in every spreadsheet advice? It is used to build the file name.
52. The format alert to customers goes out twice, from two accounts. One is a development leftover whose text always says DTSS. Which account and which wording are correct?
53. Today a row that cannot be converted is dropped with no record. Is it acceptable that these rows will now be listed and reported?
54. Does anyone use the timestamped copy of `Extracted Payment Advices.xlsx`, or only the file with the fixed name?
55. The International sample includes a four-column spreadsheet (`SlNo`, `Invoice Number`, `Amount`, `TotalCreditedAmount`) with no customer, date or bank details. Should it be processed, and how do we know the customer?

## 5. `Payment.Advice.Extraction` (PDF and image advices)

56. Today a person confirms the extracted figures on screen (Validation Station). The new process has no screen: a document it is unsure of is copied to a `NeedValidation` folder and listed in a mail. Is that acceptable? Who will handle those documents?
57. The `Validation Station.xlsx` you shared has 5,089 rows for MSS-Security and Southern-Cross-Protection only, with columns `Customer Name`, `Keyword` and `Entity`, a date in the fourth column and notes in the fifth and seventh. Is there a separate keyword list for the Indian entities? Is a row added each time a person corrects a customer name at the Validation Station, and is the customer name in the first column the one to put in the hand-off?
58. Which fields must be present for an advice to be accepted, for the Indian entities and for Southern Cross Protection?
59. When one invoice line of a multi-line advice is uncertain, should the whole advice be held back, or only that line?
60. When the extraction service marks a field amber (the two readings agree but one was unsure), should the advice pass or be held?
61. When the invoice lines do not add up to the total credited amount, how big a difference is allowed before the advice is held (the starting value is 0.05)? What should happen to an advice that shows no total?
62. The three notification mails (extraction failed, mandatory information missing, credit notes found) are commented out today. Who should receive them now?
63. Where is the list of about fifty PDF passwords kept, and who owns it? Which password belongs to which customer? You said International has no password-protected PDFs, yet 20 of the Southern Cross Protection sample PDFs are password-protected. Should those be treated as unreadable and reported to the sender?
64. Five customers have their own workflow today. Which of their rules are still needed? Please send a few real documents for each.
65. Two special rules exist: for files named as the HSBC advising service, an empty value date is filled from the advice date; and one customer's amounts use a European decimal format. Are both still needed?
66. Duplicate rows are dropped today with no record. Can one document legitimately pay the same invoice for the same amount twice?
67. The UiPath taxonomy has a `TDS` column in the invoice table. What fills `TDS` on DTSS advices today, and should the reader extract it directly?
68. For Southern Cross Protection advices there is no UTR. May an EFT or payment reference number be used as the UTR? The loader matches by UTR first, so a wrong value could cause a false match.
69. The 13 Nov International folder is an exact copy of 12 Nov. Was that a re-run, or did the mail run fetch the same mails again?
70. Should credit notes stay off the first sheet of the payment advice workbook (the sheet the loader reads)?
71. Should DTSS documents also be routed to review by confidence? The DTSS sample has no `NeedValidation` folder.
72. Should the new process accept `.png` and `.tiff` images, or only `.jpg` and `.jpeg` as today?

## 6. Output workbook and dates

73. Do all senders write dates day first (`5/11/2026` = 5 November)? Are there senders who write month first, and can `DD.MM.YY` appear?
74. In a deduction column, does a trailing minus (`1,746.00-`) mean a deduction (as the current TDS output shows), or a negative amount?
75. In your own calculation of the net amount, is `GST Tax Amount` deducted from the invoice amount, as the loader does?
76. When an advice is held back, should it still go to the loader with a flag? The loader's layout has no flag column.
77. Is Southern Cross Protection part of this migration (all four flows and the loader), or only the PDF extraction?
78. Which other entities need their own output layout (AEPL, RARE, Terminix, RHSPL appear in different lists)?
79. `India.Excel.Extraction` writes `Extracted Payment Advices.xlsx` (capital P) and the loader reads `Extracted payment advices.xlsx`. Are these the same file on the loader's machine?
80. After what time of day should the day's workbook be written, and when does the loader run? A spreadsheet advice that arrives after the workbook is written goes to the next day.

## 7. Entities, configuration, volume and security

81. Which entities should each process cover? Seventeen entities appear across the configuration lists and no two lists agree. RARE, AEPL, RHSPL, ONESIS and Southern Cross Protection each have gaps.
82. Who owns `MasterConfig.xlsx`? May keys be added to it, or should the new settings live in a separate workbook? Will it stay an `.xlsx` on the `E:` drive, or move to SharePoint or Google Sheets?
83. The estate uses `E:\Automation`, `C:\Automation` and, in the packages' copy of the configuration, `D:\Automation`. Which drive holds each live folder (staging, daily log, statements, payment advices)?
84. How many payment advice documents arrive on a working day? Earlier figures said about 45, but the samples show about 150 (DTSS 22, Southern Cross Protection up to 248).
85. May the payment advice documents be sent to an external LLM provider for extraction? What may be kept, and for how long?
86. Which account should send each process's mails, and who should receive failure alerts?
87. The UiPath packages and configuration hold clear-text credentials: an Azure AD client secret, mailbox passwords (one written to the UiPath log on every run), SMTP passwords, an OCR API key and the PDF passwords. Who will rotate them, and can the old UiPath logs that contain a password be deleted?
88. `International_Bot1_SXP` contains a test workflow (`Test.xaml`) that mails a production report to a hard-coded address. Can it be removed from the deployed package?
89. Will the Tracto services run on the same Windows machine as today's UiPath robot, the one with the `E:\Automation\` folders, `C:\xampp\htdocs\`, Python 3.6 and the batch files? If not, how will Tracto reach those folders and start `run_SXP_bot.bat` and `run_Bank_bot.bat`?

## 8. Run times and cutover

90. At what time should each process run, and in which time zone? The folder dates come from the run date.
91. Should the two extraction processes start on their own schedule, or when the dispatcher finishes?
92. During the parallel run, the new process writes to a separate folder tree. Can the loader be pointed at that tree for a test load on a test database without changing the Python code?
93. How many days of matching output are needed before switching over (the starting proposal is 10 working days with no unexplained difference)? Who stops the UiPath processes on the live path, and when?
94. During the parallel run, only one side may start `bot_run.py` for an International entity on a day. Which side should, until cutover?
95. Can we have a test copy of the MySQL databases `collection_accounting` and `ca_with_pa_sxp`, and a test Oracle Fusion / Magic xpi endpoint? Without them the collection accounting run can only be tested with stand-in batch files, never end to end before going live.

## 9. Extraction module team

96. When will the extraction service changes be ready: PDF password unlocking, image input, the error codes, page ranges per document and colours per field in the reply?
97. Will the service unlock password-protected PDFs (a `password` field on the request), or should the caller unlock them before sending?
98. Images: converted to PDF before reading, or sent to the model as images?
99. Synchronous replies or a callback?
100. Who writes the payment-advice prompts (Indian and International), where are they stored, and how is a change versioned and tested?
101. Which model and provider, and what does one document cost?
102. Can the reply carry a numeric confidence for each field, not only a colour? The review threshold is calibrated on numeric scores.
103. The second reading (reviewer) always uses OpenAI. Is that acceptable if the main reader uses another provider?
104. How many documents can the service take at once, and how long does one take? About 150 to 250 a day must finish within the working day.
105. Where will the service run, and who may reach it?

## 10. `InternationalBankAvailCheck`

106. When an entity's statements are missing, the International check waits five minutes and tries again, with no limit: each try runs the fetch bot again and mails the bank-issue recipients again. How long should Tracto keep trying (a number of tries, or until a time of day)? Should the "not available" mail go out on every try, or once when Tracto stops trying?
107. When an entity's statements are available, the check adds an empty item to the Orchestrator queue `{entity}_BankAvail_Success_Queue`. What reads that queue? Does it start `International_Bot1_SXP`, or anything else? In Tracto the same signal would be the entity's result in the bank check run.
108. Both International fetch bots read the `SXP_Group` columns of `E:\Automation\International_CAwithPA\Config\Bot_run_Configurations.xlsx`, so MSS-Security is fetched for Southern Cross Protection's window. Is that intended? Is `Bot_run_Configurations.xlsx` the same file as `MSS_SXP_BotRun.xlsx` (same columns), or a second copy kept in step by hand?
109. The two fetch bots use their own databases, `ca_with_pa_mss_bankcheck` and `ca_with_pa_sxp_bankcheck`, and stop at once when `active_bot_run_id` there is -1 or empty. What sets it in those two databases?
110. The fetch bot mails the bank statement lines it cannot identify, with `Unidentified.xlsx` attached, and writes that file into the Indian code folder `C:\xampp\htdocs\prod\collectionaccounting\`. Who receives that mail? Is the shared file location intended, given that the Indian and International bots can write it at the same time?
111. The International check's "not available" mail says only "{entity} bank statement is not available, kindly check." The table of missing accounts is built but not sent. Should Tracto send the list of missing accounts, as the Indian check does?
112. The International check reads its statements from `C:\xampp\htdocs\Statements1\BankStatements\`, the Indian one from `C:\Automation\Data\Input\Statements1\BankStatements\`. Do the International statements stay under the web server folder?

## 11. Decisions for the Tracto team

- Queue for work items: a SQLite table (the working default) or a sheet.
- Which Go module hosts each start endpoint and the work-item store.
- Whether the one-step main jobs stay on the desktop runtime, which needs an unlocked desktop session.
- Whether a main job that succeeds while some items fail counts as a failure for alerts.
- Whether secrets stay as environment variables on the service machine, and whether the Microsoft token store is encrypted.
- Where the configuration templates live in the repository.
