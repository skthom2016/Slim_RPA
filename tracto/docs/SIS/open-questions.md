# SIS migration: open questions

Every question still open for moving the UiPath payment-advice processes to Tracto. Each one can be read on its own. The UiPath processes are named by their package names:

| Package | What it does |
|---|---|
| `BankAvailCheck` | Checks each entity's bank statement folder for the day, runs a fetch bot for missing ones, mails which accounts have no statement |
| `EmailPADispatcher` | Reads the entity mailboxes and files each payment advice attachment into dated folders |
| `India.Excel.Extraction` | Validates and cleans spreadsheet payment advices into the standard table |
| `Payment.Advice.Extraction` | Reads PDF and image payment advices into the standard table |
| `International_Bot1_SXP` | Runs the collection accounting batch for Southern Cross Protection and mails the result |

"The collection accounting service" is the Python code in the `collectionaccounting` folder (`bot_run.py`, `pyment_advice_loader.py`, the `Api` and `CoreProcess` folders) with its MySQL database.

## 1. Collection accounting service and `International_Bot1_SXP`

1. Please share the International copy of the Python code (the folder `E:\Automation\International_CAwithPA\Bot_1\ca_international_SXP\`) and the batch file `E:\Automation\International_CAwithPA\Bot_1\run_SXP_bot.bat`. The Python folder we have is the Indian copy.
2. `International_Bot1_SXP` writes 0 into cells `B2` and `B3` of `E:\Automation\International_CAwithPA\Bot_1\Response_Report.xlsx` (sheet `Sheet1`) before it runs the batch file, then reads them back: `B2` is treated as "bank statement downloaded" and `B3` as "outstanding downloaded". Which Python file writes these two cells, and what value does it write: a count of rows downloaded, or a flag such as 1 for success?
3. The batch file text you shared runs `...\Bot_1\ca_international_MSS\bot_run.py`, but `International_Bot1_SXP` reads its report from `...\Bot_1\ca_international_SXP\Bank_Statement\`. Is there a separate copy of the Python code and a separate batch file for each International entity (MSS, SXP, others)? If yes, which International entities exist, and is there one UiPath bot per entity or one bot for all?
4. For the Indian entities, pressing Start Process on the collection accounting web page (`ca_with_pa`) first calls `bot-run/init`, which writes the entity into `settings.selected_entities`, the run dates into `download_time_config`, creates a `bot_run` row and sets `settings.active_bot_run_id`; only then is `bot_run.py` started. For an International run started by `International_Bot1_SXP` through the batch file, what does these four things? Nothing in the web page or the Python code we have does it for an International entity.
5. Does the International copy use the same MySQL database (`collection_accounting`) and `settings` table as the Indian copy, or its own? The database dump we have has no International entity in `settings` or `download_time_config`.
6. When is `International_Bot1_SXP` started today: at a fixed time from a UiPath trigger, or by hand? Does it have to run only after the day's International payment advice workbook (`Extracted payment advices.xlsx`) has been written by `Payment.Advice.Extraction`?
7. Which columns does the International copy of `pyment_advice_loader.py` read from the payment advice workbook? The Indian loader needs 22 columns by name (including `AdviceNumber`, `AdviceDate`, `BeneficiaryBankIFSCCode`, `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS`). The International workbook's first sheet has only 15 columns and none of those eight. Does the International loader read the 15, or was it changed in some other way?
8. Which folder does the International loader read the payment advice workbook from? The samples have the International files under `...\Input\International\Payment Advices\{date}\Southern-Cross-Protection\`, while the Indian loader reads under `...\Statements\ExtractedPaymentAdvices\`.
9. `bot_run.py` ends with exit code -1 only when no run is active; when a step fails part way (for example Oracle Fusion is down) it logs to `bot_run_log` and ends normally. Is the `bot_run` row's `status` (1 or -1) the right place to see whether a run worked?
10. Is it safe to run `bot_run.py` twice on the same day for the same entity? The second load of the payment advice workbook adds nothing (its first advice already exists), but receipt creation, matching and posting run again.
11. Can the Indian and International runs, or two International entities, run at the same time, or must they run one after the other?
12. The success mail of `International_Bot1_SXP` says "Please find the attached Bank Statement and ATB Report" and attaches `outstanding.xlsx`. Is "ATB Report" the name the recipients know, or should the mail say "outstanding report"?
13. `outstanding.xlsx` is read from `C:\xampp\htdocs\Statements1\OutstandingStatements\{dd MMM yyyy}\SXP_Group\`. Is `SXP_Group` fixed for Southern Cross Protection, and what is the group folder for the other International entities?
14. Where is the International `MasterConfig.xlsx` (`E:\Automation\International_CAwithPA\Bot_1\Data\Config\MasterConfig.xlsx`)? Please share it without the password and secret values, so we can see the recipient keys for each International entity.
15. The day workbooks in the samples have every invoice line written twice for documents that were not confirmed by a person (for example 25 Sep DTSS, and every Southern Cross Protection day). The database dump (9 to 12 Nov) has no repeated invoice line. Are the doubled lines removed somewhere before the loader reads `Extracted payment advices.xlsx`, or did the loader's workbooks on those days simply have none?
16. The reset of `active_bot_run_id` to -1 at the end of `bot_run.py` is commented out, so the last run's id stays active. Is that deliberate?
17. The match step deletes every payment advice loaded more than four days earlier, matched or not, and the posting step deletes receipt, TDS and log rows older than four days. Is four days deliberate, and how does anyone follow up an advice that was never matched?
18. `Southern Cross Protection Pty Ltd` appears in the Indian database only as a customer of the TSIS and AMRS entities. Is it the same company as the International entity whose payment advices are in `Payment Advices Int'l\...\Southern-Cross-Protection\`?
19. The Python service reads `MasterConfig.xlsx` from `C:\Automation\Data\Config\`, while the UiPath processes read it from `E:\Automation\Data\Config\`. Are these two copies of the same file, or different files?

## 2. `BankAvailCheck`

20. `BankAvailCheck` writes the entity name into `E:\Automation\Data\Config\BankMaster.xlsx`, sheet `EntitySelected`, cell `A2`, then runs `C:\xampp\htdocs\prod\run_Bank_bot.bat`. Please share `run_Bank_bot.bat`. Does it run `download.py` (the download step only)? `download.py` downloads for the entities in `settings.selected_entities` with the dates in `download_time_config`, and nothing in the Python code reads cell `A2`. What reads that cell, and what sets the entity and dates for the fetch?
21. Can that fetch bot take the entity name as a command-line argument instead of reading cell `A2`? If not, entities must be fetched one at a time.
22. When the fetch bot ends with exit code 0, does that mean the statements were downloaded, or only that the script finished?
23. How long may one entity's fetch take before it should be treated as failed?
24. Is it safe to run the fetch twice for the same entity on the same day? The Python download deletes and recreates the entity's date folder before it downloads.
25. The Python download writes the statement to `C:\Automation\Data\Input\Statements1\BankStatements\{dd MMM yyyy}\{entity}\` with today's date from its machine clock, whatever the download window in `download_time_config` is. `BankAvailCheck` checks the same folder for today. Is the statement for a day always downloaded on that same day, or can it arrive later under a later date folder?
26. Which process writes `{entity}_check\bank_statements_check.xlsx` inside the day's statement folder, and when? No file in the Python folder we have writes it.
27. The unavailable-accounts table is headed "Bank Account Number" but holds the value of `BANK_ACCOUNT_NAME`. Which one does the operations team want to see?
28. Today every unavailable-accounts mail goes to the DTSS recipients, whichever entity is missing statements. Should each entity's mail go to that entity's own recipients? Should the DTSS recipients stay in copy?
29. ONESIS has no recipient key in `MasterConfig.xlsx`. Who should receive its mails?
30. The availability mail is sent even when no entity is available. Should it still be sent in that case?
31. The check workbook is read only in the range `A1:E200`. Was 200 rows a real limit, or can the whole sheet be read?

## 3. `EmailPADispatcher`

32. Which mailbox does each entity use, and is each one Microsoft 365 or the in-house mail server (`mail.sisindia.com`)? `MasterConfig.xlsx` has mailbox settings for DTSS only.
33. For the in-house mail server: which IMAP port and TLS mode? The configuration shows port 465, which is the SMTP port.
34. Where are the mailbox passwords kept today? The packages read them from UiPath Orchestrator assets named `{entity}_{AssetCredentialName}`. Who owns these accounts and can give them to the new service?
35. Can the Microsoft 365 mailboxes be read through one account with shared-mailbox permission, or does each need its own sign-in? Will the tenant administrator grant `Mail.Read` and `Mail.Send` to the app registration?
36. Which entities should the dispatcher cover? Its list has fifteen entities. It leaves out AEPL and RHSPL, which the Python loader includes, and it includes RARE, which the loader does not.
37. Before the main loop, the dispatcher saves attachments whose first file name starts with `PA_{entity}` into a second folder. Is that still needed? `India.Excel.Extraction` reads from that folder.
38. Spreadsheets are filed into an `XLS\` folder, but `India.Excel.Extraction` reads only the `.xlsx` files at the top of the entity folder (the `PA_` copies). Which spreadsheets is `India.Excel.Extraction` meant to process?
39. Which attachment types should each entity accept, besides those set for DTSS?
40. How long should saved attachments, archives and mail bodies be kept in the staging folder? Nothing deletes them today.
41. A mail counts as handled when it is marked read. Can we instead record the IDs of the mails we processed, so that a person opening the mailbox does not hide a mail from the bot?
42. When a file cannot be processed, the sender is mailed and the file stays in staging with no record. Should it also be listed somewhere with a status, so the backlog is visible?
43. May the customer list (`Customers List.xlsx`) be changed to named columns with more than two aliases per customer? Today the first matching row wins, which goes wrong when a mail mentions two customers.
44. What wording should the three replies to senders use (failed archive, no attachment, unsupported file type)? `MasterConfig.xlsx` holds mail text that no process reads.
45. Does anything read cell `B2` of `ModuleTracker.xlsx`, or the `{entity}_MailCount` value the dispatcher writes?
46. Mails with no attachment are filed as work for a downstream process that writes the `EmailBody` workbook. Which process is that, and does it stay as it is?
47. In the DTSS sample, 14 of the 15 saved mail bodies are bounce notices. One is a password-expiry mail with a link to change a password, which looks like phishing. Where do these come from? Who should be alerted when such a mail arrives?

## 4. `India.Excel.Extraction`

48. Only three cleaning rules run today: a cell that is just `-` becomes blank, an empty `TDS` or `Other Deductions` becomes 0, and repeated spaces collapse in the customer name and total. Fifty-three other rules are commented out. Are the three the intended rules, and were the fifty-three switched off on purpose?
49. Which version is live? The current `MasterConfig.xlsx` has `ExcelMappingPath` (`E:\Automation\EmailExcelExtraction\Mapping`) and no `Entity` key, but version 1.0.7 reads `Entity` and not `ExcelMappingPath`. Please share the files in the mapping folder.
50. Can an entity folder hold several spreadsheet advices on one day, or exactly one named `PA_<entity>`? The published workflow processes every `.xlsx`, while the second driver in the package skips the entity unless there is exactly one.
51. Please send one real spreadsheet advice from each entity that sends them, and one that should be rejected.
52. Is the `Sl No` column unique in every spreadsheet advice? It is used to build the file name.
53. The format alert to customers goes out twice, from two accounts. One is a development leftover whose text always says DTSS. Which account and which wording are correct?
54. Today a row that cannot be converted is dropped with no record. Is it acceptable that these rows will now be listed and reported?
55. Does anyone use the timestamped copy of `Extracted Payment Advices.xlsx`, or only the file with the fixed name?
56. The International sample includes a four-column spreadsheet (`SlNo`, `Invoice Number`, `Amount`, `TotalCreditedAmount`) with no customer, date or bank details. Should it be processed, and how do we know the customer?

## 5. `Payment.Advice.Extraction` (PDF and image advices)

57. Today a person confirms the extracted figures on screen (Validation Station). The new process has no screen: a document it is unsure of is copied to a `NeedValidation` folder and listed in a mail. Is that acceptable? Who will handle those documents?
58. Which fields must be present for an advice to be accepted, for the Indian entities and for Southern Cross Protection?
59. When one invoice line of a multi-line advice is uncertain, should the whole advice be held back, or only that line?
60. When the extraction service marks a field amber (the two readings agree but one was unsure), should the advice pass or be held?
61. When the invoice lines do not add up to the total credited amount, how big a difference is allowed before the advice is held (the starting value is 0.05)? What should happen to an advice that shows no total?
62. The three notification mails (extraction failed, mandatory information missing, credit notes found) are commented out today. Who should receive them now?
63. Where is the list of about fifty PDF passwords kept, and who owns it? Which password belongs to which customer? Is the list for the 20 locked Southern Cross Protection PDFs the same as the Indian one?
64. Five customers have their own workflow today. Which of their rules are still needed? Please send a few real documents for each.
65. Two special rules exist: for files named as the HSBC advising service, an empty value date is filled from the advice date; and one customer's amounts use a European decimal format. Are both still needed?
66. Duplicate rows are dropped today with no record. Can one document legitimately pay the same invoice for the same amount twice?
67. The UiPath taxonomy has a `TDS` column in the invoice table. What fills `TDS` on DTSS advices today, and should the reader extract it directly?
68. For Southern Cross Protection advices there is no UTR. May an EFT or payment reference number be used as the UTR? The loader matches by UTR first, so a wrong value could cause a false match.
69. In the samples, documents that went through Validation Station have each invoice line once, while auto-accepted documents have every line twice. Is that known, and is the live database affected?
70. The 13 Nov International folder is an exact copy of 12 Nov. Was that a re-run, or did the mail run fetch the same mails again?
71. Should credit notes stay off the first sheet of the payment advice workbook (the sheet the loader reads)?
72. Should DTSS documents also be routed to review by confidence? The DTSS sample has no `NeedValidation` folder.
73. Should the new process accept `.png` and `.tiff` images, or only `.jpg` and `.jpeg` as today?

## 6. Output workbook and dates

74. Do all senders write dates day first (`5/11/2026` = 5 November)? Are there senders who write month first, and can `DD.MM.YY` appear?
75. In a deduction column, does a trailing minus (`1,746.00-`) mean a deduction (as the current TDS output shows), or a negative amount?
76. In your own calculation of the net amount, is `GST Tax Amount` deducted from the invoice amount, as the loader does?
77. When an advice is held back, should it still go to the loader with a flag? The loader's layout has no flag column.
78. Is Southern Cross Protection part of this migration (all four flows and the loader), or only the PDF extraction?
79. Which other entities need their own output layout (AEPL, RARE, Terminix, RHSPL appear in different lists)?
80. `India.Excel.Extraction` writes `Extracted Payment Advices.xlsx` (capital P) and the loader reads `Extracted payment advices.xlsx`. Are these the same file on the loader's machine?
81. After what time of day should the day's workbook be written, and when does the loader run? A spreadsheet advice that arrives after the workbook is written goes to the next day.

## 7. Entities, configuration, volume and security

82. Which entities should each process cover? Seventeen entities appear across the configuration lists and no two lists agree. RARE, AEPL, RHSPL, ONESIS and Southern Cross Protection each have gaps.
83. Who owns `MasterConfig.xlsx`? May keys be added to it, or should the new settings live in a separate workbook? Will it stay an `.xlsx` on the `E:` drive, or move to SharePoint or Google Sheets?
84. The estate uses `E:\Automation`, `C:\Automation` and, in the packages' copy of the configuration, `D:\Automation`. Which drive holds each live folder (staging, daily log, statements, payment advices)?
85. How many payment advice documents arrive on a working day? Earlier figures said about 45, but the samples show about 150 (DTSS 22, Southern Cross Protection up to 248).
86. May the payment advice documents be sent to an external LLM provider for extraction? What may be kept, and for how long?
87. Which account should send each process's mails, and who should receive failure alerts?
88. The UiPath packages and configuration hold clear-text credentials: an Azure AD client secret, mailbox passwords (one written to the UiPath log on every run), SMTP passwords, an OCR API key and the PDF passwords. Who will rotate them, and can the old UiPath logs that contain a password be deleted?
89. `International_Bot1_SXP` contains a test workflow (`Test.xaml`) that mails a production report to a hard-coded address. Can it be removed from the deployed package?

## 8. Run times and cutover

90. At what time should each process run, and in which time zone? The folder dates come from the run date.
91. Should the two extraction processes start on their own schedule, or when the dispatcher finishes?
92. During the parallel run, the new process writes to a separate folder tree. Can the loader be pointed at that tree for a test load on a test database without changing the Python code?
93. How many days of matching output are needed before switching over (the starting proposal is 10 working days with no unexplained difference)? Who stops the UiPath processes on the live path, and when?
94. During the parallel run, only one side may start `bot_run.py` for an International entity on a day. Which side should, until cutover?

## 9. Extraction module team

95. When will the extraction service changes be ready: PDF password unlocking, image input, the error codes, page ranges per document and colours per field in the reply?
96. Will the service unlock password-protected PDFs (a `password` field on the request), or should the caller unlock them before sending?
97. Images: converted to PDF before reading, or sent to the model as images?
98. Synchronous replies or a callback?
99. Who writes the payment-advice prompts (Indian and International), where are they stored, and how is a change versioned and tested?
100. Which model and provider, and what does one document cost?
101. Can the reply carry a numeric confidence for each field, not only a colour? The review threshold is calibrated on numeric scores.
102. The second reading (reviewer) always uses OpenAI. Is that acceptable if the main reader uses another provider?
103. How many documents can the service take at once, and how long does one take? About 150 to 250 a day must finish within the working day.
104. Where will the service run, and who may reach it?

## 10. Decisions for the Tracto team

- Queue for work items: a SQLite table (the working default) or a sheet.
- Which Go module hosts each start endpoint and the work-item store.
- Whether the one-step main jobs stay on the desktop runtime, which needs an unlocked desktop session.
- Whether a main job that succeeds while some items fail counts as a failure for alerts.
- Whether secrets stay as environment variables on the service machine, and whether the Microsoft token store is encrypted.
- Where the configuration templates live in the repository.
