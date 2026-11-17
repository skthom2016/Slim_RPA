# Configuration workbooks: MasterConfig.xlsx and BankMaster.xlsx

The two workbooks the UiPath flows read their settings from: what each sheet holds, which flow reads it, and what Tracto carries over. They also hold credentials in clear text; **no credential value or address appears in this document**, only key names.

## MasterConfig.xlsx: only the `Settings` sheet is used

The workbook is the shared configuration of all the flows, but only the first sheet, `Settings`, is read. The other fourteen sheets (`Entities`, `EmailDetails`, `ExcelExtraction`, `EmailExtraction`, `PDF Extraction`, `InboxMonitor`, `Constants`, `Assets` and the six report-download sheets) were built to take everything from Excel and are not used. The mail bodies, including the exception mails, are hard-coded in the UiPath workflows, so the mail text in the workbook is never read. **Tracto does not read these sheets and carries none of them over.**

What `Settings` holds, as a list of `Name`, `Value` and `Description`:

| Group | Keys | Use |
|---|---|---|
| Queue names | `PDF_JPEG_Queue`, `XLSX_Queue`, `MailMessage_Queue` | The entity name is put in front of the key to give the UiPath Orchestrator queue of that entity: `DTSS_XLSX_Queue` for its Excel files, `DTSS_PDF_JPEG_Queue` for its PDF and image files. One queue of each kind exists per entity |
| Entities | `TotalNumberOfEntity`, `PriorityEntity_1` to `PriorityEntity_15` | The entities the flows loop over (fifteen today) |
| Folders and trackers | `PaymentAdvicesFolderPath`, `PaymentAdvicesExtractionFolderPath` (where the extracted output is written), `ReportFolderPath`, the log tracker and module tracker paths, and the customer list path | Where files are read from and written to; see [folders-and-files.md](folders-and-files.md) |
| Environment | `Environment` (Prod or Test), `SkipDownloadModule` | Run mode |
| Credential assets | The credential-asset suffixes | The names of Orchestrator assets, not values |

Tracto equivalent: the queue names disappear (the Tracto queue holds the entity as a field), the entity list becomes the entity table, and the folders become configured paths on the Tracto side.

## BankMaster.xlsx: every sheet is used

Sixteen sheets: one per entity, plus `EntitySelected`. Each entity has its own banks.

| Sheet | Content |
|---|---|
| One per entity: `TSIS`, `DTSS`, `SIS`, `SMC`, `SLV`, `Terminix`, `USSP`, `AMRS`, `RHSPL`, `ONESIS`, `SISCASH`, `SISCOCASH`, `APS`, `SSMS`, `PFS` | One column, `BANK_ACCOUNT_NAME`, with one row per expected bank account of that entity |
| `EntitySelected` | Header `EntitySelected` and one value: the entity flow 1 last wrote for the fetch bot to read |

Accounts per entity: SIS 10, SISCASH 5, DTSS 3, SLV 3, SMC 2, Terminix 2, USSP 2, RHSPL 2, SISCOCASH 2, and one each for TSIS, AMRS, ONESIS, APS, SSMS and PFS. **That is 37 accounts, and no entity has more than ten**, so the `A1:A40` window flow 1 reads never truncates anything today. There is no sheet for RARE or AEPL.

**What it is for.** Flow 1 checks, per entity, whether the statement of each of these bank accounts is available in Oracle Fusion. A separate bot fetches the statements. The existing Python collection-accounting service holds the Oracle Fusion access, so Tracto calls that Python service through its API and does not reach Oracle Fusion itself. The Python service's API document is awaited; until it is read, the calls, their inputs and their outputs are not fixed, and flow 1's fetch step (see [SIS-01-bank-avail-check.md](SIS-01-bank-avail-check.md)) is written against a placeholder.

## The entity lists disagree

Seven places in these workbooks, the packages and the Python service list entities, and no two are the same. Seventeen different entities appear in all.

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

Columns 1, 4 and 5 come from sheets other than `Settings`, which the flows do not read from the workbook; they show what the packages carry. The Tracto design takes this as a single entity table with one column per flow (enabled or not), the recipients, the mailbox and the accepted file types. Which entity belongs to which flow is a decision for the process owner; the lists above are what the estate does today, not necessarily what it should do.

## This workbook and the packages are from different moments

- The configuration copy shipped inside the flow 3 package has an `Entity` key of nine entities and `TotalNumberOfEntity` 8. The current workbook has no `Entity` key and `TotalNumberOfEntity` 15.
- `ExcelMappingPath` (`E:\Automation\EmailExcelExtraction\Mapping`) is in the current workbook, and version 1.0.7 of flow 3 never reads it. A mapping folder suggests column maps per entity in a newer version.
- The package copy puts `SourceDownloadsPath` and `DailyLogFile` on `D:`; the current workbook puts both on `E:`.
- The `Entities` sheet exists only in the current workbook.

## The International entity is in none of these lists

Southern Cross Protection (Australia, AUD) has a day folder, per-document workbooks and a day workbook in the sample ([payment-advice-samples.md](payment-advice-samples.md)), but no sheet in `BankMaster.xlsx` and no entry in any of the seven lists above. Whether it belongs to the collection accounting estate, and which of the lists it has to join, is for the process owner.

## What Tracto carries over

- **From `Settings`:** the entity list (as the entity table, with recipients, mailbox and accepted file types added), the folder paths and the tracker paths.
- **From `BankMaster.xlsx`:** the per-entity bank account lists, read through the sheet engine.
- **Not carried over:** the queue names, the other fourteen sheets of `MasterConfig.xlsx`, and the mail text (hard-coded in the UiPath workflows; Tracto keeps its own templates).
- **The `EntitySelected` cell** stays only if the fetch step cannot take the entity as an argument; a call to the Python service would pass it directly.
- **Credentials.** The workbook holds credentials in clear text under key names on the sheets that are not used (`DTSS_EmailPassword`, `password`, `Application Secret`, `APP ID`, `Tenant ID`, `senderID`, and the API `Username` and `Password`). They are not copied and should be rotated.

## Points to confirm

1. **Which entities each flow should cover.** The table above shows today's coverage; the intended coverage is for the process owner to say.
2. **Which version is live.** Whether the workbook or the packages are current, and the content of the `ExcelMappingPath` folder.
3. **Where each entity's mailbox credentials are kept.** They are Orchestrator assets named `{entity}_{AssetCredentialName}`; Tracto needs them as `${secret:NAME}` on the worker.
4. **Recipients for ONESIS,** which has none.
5. **The Python service API.** The calls the bank availability check needs (statement availability per entity and bank account, and the fetch), with their inputs and outputs.
