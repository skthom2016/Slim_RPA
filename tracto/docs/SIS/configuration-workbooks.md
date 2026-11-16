# Configuration workbooks: MasterConfig.xlsx and BankMaster.xlsx

The two workbooks the UiPath flows read their settings from: what each sheet holds, which flow reads it, and what Tracto carries over. They also hold credentials in clear text; **no credential value or address appears in this document**, only key names.

## BankMaster.xlsx

Sixteen sheets: one per entity, plus `EntitySelected`.

| Sheet | Content |
|---|---|
| One per entity: `TSIS`, `DTSS`, `SIS`, `SMC`, `SLV`, `Terminix`, `USSP`, `AMRS`, `RHSPL`, `ONESIS`, `SISCASH`, `SISCOCASH`, `APS`, `SSMS`, `PFS` | One column, `BANK_ACCOUNT_NAME`, with one row per expected bank account |
| `EntitySelected` | Header `EntitySelected` and one value: the entity flow 1 last wrote for the fetch bot to read |

Accounts per entity: SIS 10, SISCASH 5, DTSS 3, SLV 3, SMC 2, Terminix 2, USSP 2, RHSPL 2, SISCOCASH 2, and one each for TSIS, AMRS, ONESIS, APS, SSMS and PFS. **That is 37 accounts, and no entity has more than ten**, so the `A1:A40` window flow 1 reads never truncates anything today. There is no sheet for RARE or AEPL.

## MasterConfig.xlsx

Fifteen sheets, each a list of `Name`, `Value` and `Description`.

| Sheet | What it holds | Read by |
|---|---|---|
| `Settings` | Queue-name suffixes (`PDF_JPEG_Queue`, `XLSX_Queue`, `MailMessage_Queue`), tracker paths, `PaymentAdvicesFolderPath`, `PaymentAdvicesExtractionFolderPath`, `ReportFolderPath`, `Environment` (Prod or Test), `SkipDownloadModule`, `TotalNumberOfEntity` and `PriorityEntity_1` to `_15`, the credential-asset suffixes | Flows 1, 2, 3 and 4 |
| `Entities` | `entities` (the list flow 1 loops), `{entity}_receiver_id` for each entity, `avail_receiver_id`, `cc_receiver_id`, and the five Office 365 keys | Flow 1 |
| `EmailDetails` | Mailbox settings for DTSS only: address, password, port 465, server `mail.sisindia.com`, notification address. The RARE rows are empty | Loaded by the drivers of flows 3 and 4. The dispatcher does not read it: its mailbox credentials are Orchestrator assets |
| `ExcelExtraction` | `ExcelMappingPath`, `SampleFilePath`, `OutPath`, `InputPath` | Flow 3 |
| `EmailExtraction` | `OrchestratorQueueName` (`DTSS_MailMessage_Queue`) and `PaymentAdviceTemplate` | The mail-body process, which is not one of the four |
| `PDF Extraction` | `EntityNameForCurrentRun` (the entity handed from the driver to `Main.xaml`), `PaymentAdviceHeader`, `DailyLogFile` | Flow 4 |
| `InboxMonitor` | `SourceDownloadsPath`, the text of the unsupported-type mail (`MailBody_NotProcessedFile`), and `{entity}_MailCount` | Flow 2 |
| `Constants` | `MaxRetryNumber` (2), timeouts, delays, accuracy thresholds and log message templates | The framework |
| `Assets` | Empty. Orchestrator assets overwrite the other sheets | The framework |
| `BankStatementApiSettings`, `OutstandingApiSettings`, `CustomerMasterApiSettings`, `UnAppliedApiSettings`, `ProdApiSettings`, `TestApiSettings` | Report-download settings: download folder, URLs, an API username and password, and per-entity last-download dates | **None of the four flows** |

**Keys that none of the four flows read:** `PaymentAdvicesExtractionFolderPath` (which is the base folder the Python loader reads), `ReportFolderPath`, `SkipDownloadModule`, `PaymentAdviceTemplate`, `PaymentAdviceHeader` (flow 4 opens a relative `Config\Payment advice header.xlsx` instead) and `ExcelMappingPath`. The six report-download sheets belong to the side that downloads the bank statement, outstanding, unapplied and customer-master reports, and need no migration here.

## The entity lists disagree

Seven places in these workbooks and the Python service list entities, and no two are the same. Seventeen different entities appear in all.

| Entity | 1 Flow 1 `entities` | 2 Flows 2 and 4 `PriorityEntity` | 3 BankMaster sheet | 4 Recipient key | 5 Mail count key | 6 Flow 3 (package) | 7 Python loader |
|---|---|---|---|---|---|---|---|
| AEPL | | | | x | x | x | x |
| AMRS | x | x | x | x | | x | x |
| APS | x | x | x | x | | | x |
| DTSS | x | x | x | x | x | x | x |
| ONESIS | x | x | x | | | | x |
| PFS | x | x | x | x | | | x |
| RARE | | x | | | x | | |
| RHSPL | x | | x | x | | x | x |
| SIS | x | x | x | x | x | x | x |
| SISCASH | x | x | x | x | x | | x |
| SISCOCASH | x | x | x | x | x | | x |
| SLV | x | x | x | x | x | x | x |
| SMC | x | x | x | x | x | x | x |
| SSMS | x | x | x | x | | | x |
| TSIS | x | x | x | x | | | x |
| Terminix | x | x | x | x | x | x | x |
| USSP | x | x | x | x | x | x | x |

Column 6 is the list in the configuration copy shipped inside the flow 3 package; the current workbook has no such key. Column 7 is the production list in the Python service's settings. Each column has fifteen entities except 5 (ten), 6 (nine) and 7 (sixteen).

What follows from it:

- **RARE** is monitored and extracted by flows 2 and 4 but is not in flow 1, has no bank sheet, is not in the Python loader's list and has no mailbox settings in `EmailDetails`.
- **AEPL and RHSPL** are in the Python loader's list, but neither is in the `PriorityEntity` list, so flows 2 and 4 never read their mailboxes.
- **ONESIS** is in flow 1 but has no recipient key.
- **Six entities** (TSIS, AMRS, ONESIS, APS, SSMS, PFS) have no `{entity}_MailCount` row, so flow 2 finds nothing to write for them.
- **Flow 1's list matches the BankMaster sheets exactly** today.

The Tracto design takes this as a single entity table with one column per flow (enabled or not), the recipients, the mailbox and the accepted file types. Which entity belongs to which flow is a decision for the process owner; the lists above are what the estate does today, not necessarily what it should do.

## This workbook and the packages are from different moments

- The configuration copy shipped inside the flow 3 package has an `Entity` key of nine entities and `TotalNumberOfEntity` 8. The current workbook has no `Entity` key and `TotalNumberOfEntity` 15.
- `ExcelMappingPath` (`E:\Automation\EmailExcelExtraction\Mapping`) is in the current workbook, and version 1.0.7 of flow 3 never reads it. A mapping folder suggests column maps per entity in a newer version.
- The package copy puts `SourceDownloadsPath` and `DailyLogFile` on `D:`; the current workbook puts both on `E:`.
- The `Entities` sheet exists only in the current workbook.

## What Tracto carries over

- **Carried over:** the entity table, per-entity recipients, the mailbox list, the paths (`PaymentAdvicesFolderPath`, `PaymentAdvicesExtractionFolderPath`, the trackers, `SourceDownloadsPath`), and `BankMaster.xlsx` as it is, read through the sheet engine.
- **The `EntitySelected` cell** stays only if the fetch bot cannot take the entity as an argument.
- **Not carried over:** the framework `Constants`, the empty `Assets` sheet, and the six report-download sheets.
- **Credentials.** The workbook holds them in clear text, under these key names: `DTSS_EmailPassword`, `password`, `Application Secret`, `APP ID`, `Tenant ID`, `senderID`, and the API `Username` and `Password` on the report-download sheets. They are not copied, and should be rotated.

## Points to confirm

1. **Which entities each flow should cover.** The table above shows today's coverage; the intended coverage is for the process owner to say.
2. **Which version is live.** Whether the workbook or the packages are current, and the content of the `ExcelMappingPath` folder.
3. **Where each entity's mailbox credentials are kept.** Only DTSS is in the workbook; the rest are Orchestrator assets named `{entity}_{AssetCredentialName}`.
4. **Recipients for ONESIS,** which has none.
