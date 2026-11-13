# SIS flow 3: India.Excel.Extraction in Tracto

How the UiPath process `India.Excel.Extraction` 1.0.7 ("Standard Excel Extraction") is rebuilt on Tracto. It consumes what flow 2 files away, checks that each payment advice workbook is in the agreed format, and turns it into one standard table.

## In brief

**What it is for.** Some customers send their payment advice as a spreadsheet rather than a PDF. Those files follow an agreed eleven-column layout, so no reading of documents is needed — but they still have to be checked, cleaned and turned into the same standard table the PDF advices produce, so that everything downstream sees one format.

**What it does.** For each entity it finds the day's advice workbooks, checks that each one has the expected sheet and the expected column names, mails the customer when it does not, cleans the values, maps them onto a fixed twenty-four-column schema, and writes the result out for the collection-accounting stage to read.

**What it becomes in Tracto.** A schedule starts a one-step main job, which calls a Go process and ends. There is no script and no worker: every step is reading a sheet, comparing strings, writing a sheet, copying a file or sending a mail. This is the cleanest of the four.

**The heart of the rebuild** is that the UiPath version holds the same routine copied once per entity, in a single file of over 500 KB, and the differences between the copies are data — a folder, a file pattern, a sheet name, a column list. Two small sheets replace all of it, and adding an entity becomes adding a row.

Open questions are at the end of this document.

## What the UiPath process does

For each entity, for each payment-advice spreadsheet filed under today's date: check the file is in the expected shape, map its columns onto a fixed 24-column schema, write the rows to an output workbook, and mail someone when the shape is wrong.

**There are two engines in one package**, and they are not two halves of one flow:

- **The published entry point is `Main.xaml`**, a REFramework state machine. All of the business work happens in its `Init` state, which invokes `Entity wise extract.xaml` once with the input path, the sample template, the output path and the comma-separated entity list. Its queue loop then runs with an empty `Process.xaml`, so queue items are fetched and marked successful without anything being done to them.
- **`All queue.xaml` is a second driver that is not published.** It loops `PriorityEntity_1..N`, calls `CheckFileExists.xaml` per entity, and that calls `ExcelExtraction.xaml` for a single consolidated `PA_<Entity>*.xlsx` advice. `CheckFileExists` requires the entity folder to hold **exactly one** `.xlsx`; zero or several are skipped with no alert, and a leftover `Message Box` runs before the check, which would stop an unattended run until someone dismissed it.

So `Entity wise extract.xaml` handles the many standard-format advices, `ExcelExtraction.xaml` handles the one consolidated file, and the two write to different output trees on different drives.

**Validating the file.** Two checks, each with its own rejection mail:

- The workbook's sheets must include `Sheet1`, otherwise "`<Entity>` Sheet name is invalid".
- The eleven column names of `Sheet1` must match the expected set — `Sl No`, `Date of Credit/Value date`, `Total net amount credited in Bank`, `Customer Name`, `UTR Number`, `Invoice Date`, `Invoice Number`, `Invoice Amount`, `TDS`, `Other Deductions`, `Net Amount` — otherwise "`<Entity>` File format is invalid".

Each alert goes out twice: once from the production SMTP account on `mail.sisindia.com`, and once from a Gmail account left over from development whose body names DTSS regardless of which entity failed.

**Cleaning the data.** In every row: a cell in `TDS`, `Other Deductions`, `Net Amount` or `Invoice Amount` that is exactly `-` — the placeholder customers use for "nothing" — becomes blank; an empty `TDS` or `Other Deductions` becomes `0`; runs of whitespace are collapsed in the customer name and the total.

Reading the file, far more normalisation appears to happen than that. It does not: **of the fifty-seven value-rewriting assignments in `Entity wise extract.xaml`, fifty-three sit inside `Comment Out` blocks** and never run, including every one that strips `0` characters out of an amount. Only the four dash-placeholder rules are live, and each is guarded by a test that the whole cell equals `-`. Anyone reading this workflow to work out what it does to the figures has to check which half is switched on.

**Mapping the columns.** The eleven standard columns are read in their fixed order and mapped onto the output schema, with the amounts converted to numbers. A row whose amounts will not convert — empty or not numeric — is caught by a row-level handler and the loop moves on, so **the row simply disappears from the output with no log and no error row**. A file name is built as `<Sl No> <dd MM yyyy> <Entity> ExcelAdvice`.

**Writing the output.** The mapped rows go into the 24-column schema held in `Data\Payment advice header.xlsx`:

> ValueDate, BeneficiaryName, BeneficiaryBankAccountNumber, BeneficiaryBankName, BeneficiaryBankIFSCCode, CustomerName, CustomerBank, InvoiceNumber, InvoiceDate, InvoiceAmount, Retention, TDS, WCT_TDS, Other Deduction, NetAmount, TotalCreditedAmount, BOTGeneratedID, Status, UTR / TransactionReferenceNumber, GST Tax Amount, GST TDS, File name, AdviceNumber, AdviceDate

Most columns stay empty; the extractor fills about ten of them. The table is written to `<out>\<dd MMM yyyy>\<Entity>\Extracted Payment Advices.xlsx`, with a timestamped copy alongside, and the source file is copied onward.

## What is actually in this package

Worth saying plainly, because it shapes the rebuild.

- **`Process.xaml` is empty.** The REFramework scaffolding is all there — state machine, queue item as the transaction, retry, exception split — but the step that would do the work contains a comment reading "Invoke steps of the process". Queue items are therefore drained and marked successful without being processed, while the real work has already happened in `Init`.
- **`Entity wise extract.xaml` is over 500 KB of XAML** and contains the same routine copied once per entity: explicit branches for `DTSS`, `AEPL`, `Terminix` and `RARE`, plus a generic path. Each copy has its own variables, its own folder check, its own sheet check, its own column check and its own mail. The configured entity list carries nine entities, so five of them fall through to the generic path. Adding an entity with its own quirk means copying the block again.
- **There is a `- Copy.xaml` of the same file**, 376 KB, in the published package, along with a `debug.log`, a `temp.xlsx`, the test harness, an `OnedriveTest.xaml` that only sends a test mail, and a live copy of the estate `MasterConfig.xlsx`.
- **The paths span three drive roots.** `Main.xaml` reads its configuration from `E:\Automation\FM Excel Extraction\...`, `All queue.xaml` from `D:\Automation\Data\Config\...`, and `ExcelExtraction.xaml` writes to a `D:` tree while `Entity wise extract.xaml` writes under the configured `E:` paths.

The duplication is the important part. **The per-entity differences are data, not logic**: a folder, a file-name prefix, a sheet name, a list of expected columns. One routine driven by a table would replace all of the branches, and adding an entity would be adding a row.

## What this flow actually needs

Nothing here drives an application. Excel Application Scope opens workbooks, but only to read cells and write cells — there is no typing into a window, no clicking, no browser. Every step is: list files, read a sheet, compare strings, write a sheet, copy a file, send a mail.

**So this flow has no script at all beyond the one-step main job.** It is a Go process over sheet-service, and it is the clearest case of the three so far.

## The shape in Tracto

```
Schedule, or started when flow 2 finishes
   │
   ▼
Main job (desktop, one http.post) ──POST /excelextract/start──▶ Go service
   ends in seconds                                                │
                                                                  │ read the entity table and the format profiles   (exists)
                                                                  │ for each entity, for each pending file:
                                                                  │   list the folder                     (new: fs API)
                                                                  │   read the workbook's sheet names     (new: small addition)
                                                                  │   check the sheet and the columns     (Go)
                                                                  │     wrong → mail the rejection, mark the row Failed
                                                                  │   clean and map the rows              (Go)
                                                                  │   append to the consolidated workbook (exists: /update + append)
                                                                  │   copy the source onward              (new: fs API)
                                                                  ▼   mark the row Done
```

Each file is one item with its own status, so a workbook in the wrong format fails on its own and the rest of the entity's files still run.

## The format profile: turning the copy-paste into a table

This is the heart of the rebuild. One sheet describes every entity's expected format, and one Go routine reads it:

| Entity | InputSubfolder | FilePattern | SheetName | ExpectedColumns | RejectMailTo | Active |
|---|---|---|---|---|---|---|
| AEPL | AEPL | `PA_AEPL*.xlsx` | Sheet1 | `Sl No,Value Date,...` | … | Yes |
| RARE | RARE | `PA_RARE*.xlsx` | Sheet1 | … | … | Yes |
| DTSS | DTSS | `PA_DTSS*.xlsx` | Sheet1 | … | … | Yes |
| Terminix | Terminix | `PA_Terminix*.xlsx` | Sheet1 | … | … | Yes |

A second sheet maps source columns onto the standard schema, so a customer who renames a column is a row edit rather than a code change:

| Entity | SourceColumn | TargetColumn | Clean |
|---|---|---|---|
| AEPL | Invoice Amount | InvoiceAmount | `dashAsBlank` |
| AEPL | TDS | TDS | `dashAsBlank`, `emptyAsZero` |
| AEPL | Customer Name | CustomerName | `collapseSpaces` |

With those two sheets, `Entity wise extract.xaml` becomes a few hundred lines of Go that work for any entity, including ones that do not exist yet.

**Reading by position has to go.** The UiPath version validates column *names* and then reads the columns in their fixed order. A file that passes the name check but has its columns reordered is silently mapped wrong. Reading by header name, which `POST /batch` already returns, removes that whole failure.

**And no row may vanish.** A row that fails conversion today is dropped with no trace. In Tracto it belongs on the item's message and on an exception sheet, so the count going in matches the count coming out plus the count rejected.

## What exists today

| Need | What covers it |
|---|---|
| Read a workbook's rows with headers | sheet-service `POST /batch` |
| Write rows to a workbook | sheet-service `POST /update` |
| The entity table and the format profiles | ordinary sheets, read the same way |
| Run on a timetable | Orchestrator schedules |
| One item at a time with a status and a message per item | sheet-service `POST /process/start`, if the files are listed as rows |

## What has to be built

### 1. Filesystem operations (generic, shared with flows 1 and 2)

`/fs/list` with a glob, `/fs/exists`, `/fs/copy`, `/fs/mkdir`. All three flows want these; built once they serve all of them.

### 2. Reading a workbook's sheet names (small)

The sheet check needs the list of sheets in a workbook before reading one. The `.xlsx`, Graph and Google backends all know it internally; it needs exposing:

```
POST /sheets    { "source": { "path": "..." } }  →  { "sheets": ["Sheet1", "Summary"] }
```

Small, and it makes "is this the file we expect" answerable without reading anything.

### 3. Appending rows to a workbook (small, shared with flow 2)

The consolidated `Extracted Payment Advices.xlsx` is appended to, not written at fixed row numbers. Either an append mode on `/update` or a `POST /append`.

### 4. Sending mail (generic, shared with flows 1 and 2)

The two rejection mails. Same `/mail/send` endpoint.

### 5. The validate-and-map engine (flow-specific, but general in shape)

Read the profile, check the sheet, check the columns, clean and map, append, copy, record. The cleaning rules (`dashAsBlank`, `emptyAsZero`, `collapseSpaces`) are a small fixed vocabulary, deliberately not an expression language: a short list of named rules can be understood by whoever maintains the profile sheet, and cannot do anything surprising. Each rule should say what it does to a value in one line, so that nobody has to read code to find out whether a figure was altered.

### 6. A file as an item (generic)

The same item-source gap as the other flows. One queue item per found file gives every file its own status, message and retry, which is the queue decided in `SIS-02` — a table in SQLite rather than a sheet. It also means flow 2 can enqueue those files directly as it saves them, and flow 3 becomes a consumer of that queue rather than a second scan of the same folders.

## Security findings in this package

**The published package contains SMTP credentials in clear text.** `Entity wise extract.xaml` has four `Send SMTP Mail Message` activities with the `Email`, `Password`, `Server` and `Port` written as literal attribute values: two for a `@dtss.in` sender on port 587, two for a `@neelitech.com` sender on port 465, the latter in the shape of a Google app password.

**The package also ships a live copy of the estate `MasterConfig.xlsx`** inside its `Data` folder — fourteen sheets covering the whole estate, including the DTSS mailbox password and the `api.sisindia.com` report-API username and password, all in clear text. That one file exposes more than the hard-coded SMTP passwords do.

Everything above is in the `.nupkg` as published, so anyone who can read the package file can read all of it. No value is reproduced in this document.

What follows from it:

- **Those accounts' passwords and the API credential should be changed**, and the app password revoked, independently of any Tracto work.
- Any copy of the package should be treated as holding live credentials, and a configuration workbook should never be inside a published package in the first place.
- On Tracto this cannot recur in the same way: a script that contains a password is refused at validation, mail goes out on the service's own Microsoft sign-in, and an IMAP or SMTP account reads its password from the worker's environment, where it is never written to a log or a file.

All four packages have a finding of this kind: flow 1 keeps an Azure AD client secret in a shared workbook, flow 2 writes the mailbox password into the run log on every run, and flow 4 carries an OCR API key and a list of document passwords.

## What improves, and what to watch

**Improves**

- **The branches become one routine and a table.** Adding an entity stops being a copy-paste of a 100 KB block, and the five entities that fall through to the generic path today get the same treatment as the four that have their own.
- **One engine instead of two.** One path, one output tree, one place to look — rather than a published flow that does its work in `Init` and drains a queue without processing it, and an unpublished driver that handles a different file.
- **Columns are read by name.** A reordered file can no longer be mapped silently wrong.
- **One bad file does not stop the entity.** Each file is an item with its own status and message, and a rerun does only the files that are not done.
- **No row disappears quietly.** A row that will not convert is recorded and reported instead of dropped.
- **Nothing waits for a person.** There is no dialog to dismiss, so an unattended run cannot stop on one.
- **The rejection is traceable.** Which file, which check, which expected column against which found column, recorded on the row. The UiPath mail says only "File format is invalid".
- **No Excel on the machine.** sheet-service reads and writes `.xlsx` directly, so there is no Excel Application Scope to leave a process behind, no workbook left open to block the next write, and nothing that needs a desktop session.

**Watch**

- **The 24-column schema is mostly empty.** About ten of the columns are filled. Whether the rest are filled by flow 4 or are dead needs an answer before the schema is carried over as-is.
- **Two output files per entity per day.** `Extracted Payment Advices.xlsx` and a timestamped copy. The loader reads only the fixed-name file; the timestamped copy is not read by it.
- **`Today` is the machine's local date** in both the input path and the output path, as in flow 1, so a run near midnight can look in yesterday's folder and write into today's. The Orchestrator schedule names a time zone; the folder date should come from the same decision.
- **Half the normalisation in the workflow is switched off.** Fifty-three of the fifty-seven value-rewriting assignments are inside `Comment Out` blocks, among them every rule that would strip `0` characters out of an amount. They are inert, but they are also still in the file, so a future maintainer who re-enables a block without reading the guard would start altering figures. Carrying over only the four live rules, by name, removes that trap.
- **`Sl No` drives the generated file name.** If it is blank or duplicated the names collide. The queue item's own id is a safer key.
- **One file or many.** The two engines disagree about what an entity folder holds: one expects exactly one `PA_<Entity>` workbook, the other processes every `.xlsx` in the folder. Which is the real rule has to be settled before the rebuild, because "exactly one" silently skips the day a customer sends two.
- **Who reads the output.** The existing collection accounting service loads the workbook directly from a fixed folder tree, for the loader machine's date: `ExtractedPaymentAdvices\Excel\Extractor_ExtractedFiles\<dd MMM yyyy>\<entity>\Extracted payment advices.xlsx`. It reads the first sheet only and needs the twenty-two columns listed in [collection-accounting-handoff.md](collection-accounting-handoff.md). The output paths and names are part of that contract, not an internal detail.

## Build order

1. `/fs/list` and `/fs/copy` — shared with flows 1 and 2.
2. `POST /sheets` and the append write — both small.
3. The format profile and column map sheets, settled with a real file from each of the four entities in hand.
4. The validate-and-map engine.
5. The rejection mail, once `/mail/send` exists for flow 1.

Only the live cleaning rules should be carried into step 4, named explicitly, and the commented-out ones left behind.

## Open questions

These need answers before the Tracto flow can be built.

### For the process owner

1. **Are the three live cleaning rules the intended ones?** A cell that is exactly `-` becomes blank, an empty `TDS` or `Other Deductions` becomes `0`, and runs of whitespace collapse in the customer name and the total. That is all that currently runs. Worth a yes from the process owner before they are carried over, and worth asking whether the fifty-three disabled rules were switched off deliberately or abandoned half-finished — if any of them were meant to be live, that is a change to the figures that has not been happening.
2. **Which engine is the real one, and does an entity folder hold one file or many?** The package contains two: the published flow processes every `.xlsx` in the folder, while the second, unpublished driver requires **exactly one** file named `PA_<entity>` and silently skips the entity otherwise. These cannot both be right. The answer decides what the Tracto flow looks for, and "exactly one" would silently skip the day a customer sends two.
3. **A real sample file from each entity.** The format profile is built from the actual workbooks, not from the column names in the code. One genuine file per entity — ideally including one that would be rejected — is what makes the validation correct instead of plausible.
4. **Do the empty header columns matter for spreadsheet advices?** The workbook has twenty-four columns and this flow fills about ten. The loader reads twenty-two and ignores `BOTGeneratedID` and `Status`; for a spreadsheet advice the beneficiary details, advice number and advice date stay empty, which the loader accepts. Whether anything downstream relies on them for these advices is for the process owner.
5. **Is `Sl No` reliable?** It drives the generated file name, so a blank or duplicated value makes names collide. If it is not guaranteed unique we will key on the work row instead.
6. **Should a customer be mailed twice?** The format alert goes out from two accounts today, one of them a leftover from development whose text names DTSS regardless of which entity failed. Confirming the single right sender and the right wording is a small thing that is visible to customers.
7. **What should happen to a row that will not convert?** Today it is dropped with no trace, so the output can be short and look complete. The intention is to record it and report it — which means somebody will start seeing rejections that have been invisible.

### Not in Tracto yet

Listed under "What has to be built"; in short: `/fs/list` and `/fs/copy`, a way to read a workbook's sheet names, an appending write, `/mail/send`, and files as batch items. All shared with the other flows except the validate-and-map engine itself, which is ordinary Go over data already in memory.
