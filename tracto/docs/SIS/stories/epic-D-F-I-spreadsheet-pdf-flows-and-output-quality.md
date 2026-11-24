# Epics D, F and I: spreadsheet advices, PDF and image advices, and output quality

Epic D is flow 3 (spreadsheet advice extraction), epic F is flow 4 (PDF and image advice flow) and epic I holds the output-quality and hand-off stories that both flows share. Each flow is a one-step main job plus a Go service that loops items with its own status per item; there are no child jobs and no browser runtime, and the hand-off to collection accounting is the workbook file.

Evidence paths are relative to `tracto\`. "sheet-service" is `sheet-service\`; the flow services (`/excelextract/start`, `/padvice/start`) do not exist yet: no route, handler or package for them is in `sheet-service\` or `orchestrator-go\`.

| ID | Title |
|---|---|
| D1 | Entity profiles and validation |
| D2 | Cleaning and mapping |
| D3 | Workbook output and reporting |
| F1 | Validation and customer rules |
| F2 | Workbook and notifications |
| F3 | International entity profile |
| F4 | Review routing by confidence |
| I1 | Output profiles per entity |
| I2 | Date, amount and identifier normalisation |
| I3 | Write once and duplicate guard |
| I4 | Document outcomes and reporting |
| I5 | Net amount derivation |
| I6 | Parallel run and cutover |

## What the code gives today

| Need | State | Evidence |
|---|---|---|
| Read a sheet by header name, bounded rows | Exists | `sheet-service\internal\api\api.go` (`POST /batch`, `POST /cell`), `internal\sheet\xlsx.go` `ReadRows` |
| Write cells to an existing workbook | Partly: only existing files, only columns already in the header row, only `SetCellStr` (every value is text), no new sheet, no new workbook, no append helper | `internal\sheet\xlsx.go` `Write`; routes in `api.go` are `/health`, `/batch`, `/update`, `/apply`, `/cell` |
| List a workbook's sheets | To build | no route in `api.go`; `Source` interface in `internal\sheet\sheet.go` has `ReadRows` and `Write` only |
| One status per item, retry | To build (A5). `sheet-service\internal\process\runner.go` runs a child job per sheet row, which is the child-job model these flows do not use; `orchestrator-go\internal\db\schema.go` has no work-item table | |
| File list, copy, move, hash | To build (A3); no `os.ReadDir`, glob or copy code in either service; `sha256` appears only in artifact and worker hashing | `orchestrator-go\internal\handlers\artifacts.go` |
| Send mail | To build (A2); no mail code in either service | |
| Schedule with a named time zone | Exists | `orchestrator-go\internal\scheduler\scheduler.go`, `handlers\schedules.go` (`TimeZone` column) |
| `.xlsx` library with typed cells | Exists as a dependency | `sheet-service\go.mod` (`github.com/xuri/excelize/v2`) |

The loader contract used throughout is in `collection-accounting-handoff.md`. Read from the Python service (not modified): `PaymentAdvice\PaymentAdvice.py` reads the first sheet with `dtype=str` for `AdviceDate`, `ValueDate` and `InvoiceDate`, parses them with `dateutil.parser.parse` (month-first), converts `TotalCreditedAmount` and `NetAmount` with `float` after removing commas, and `calculate_net_amount` sets the derived net to `TotalCreditedAmount` for every row whose `NetAmount` is 0. `pyment_advice_loader.py` builds the three tree paths from a fixed root and the `selected_entities` setting, and reads the file only if it exists.

---

## D1 — Entity profiles and validation
| Field | Value |
|---|---|
| Epic | D — Flow 3: spreadsheet advice extraction |
| Type | Story |
| Depends on | A2, A3, A5, C2, C3, I1 |
| Capability today | Partly. Profile and entity sheets are readable with `POST /batch` (`sheet-service\internal\api\api.go`). Sheet names are not exposed (`sheet-service\internal\sheet\xlsx.go` has no listing). File listing, mail and work items are to build (A3, A2, A5). |

**User story.** As the SIS operations team, I want every spreadsheet advice checked against its entity's agreed sheet and column names before any row is read, so that a file in the wrong format is rejected with the exact reason and the rest of the day still runs.

**Context.** UiPath checks `Sheet1` and eleven column names per entity inside one 500 KB workflow copied per entity, mails a rejection twice (once from a leftover development account) and reads columns by position afterwards ([SIS-03](../SIS-03-india-excel-extraction.md), "What the UiPath process does"). Tracto holds the differences as a profile sheet row per entity (`Entity`, `InputSubfolder`, `FilePattern`, `SheetName`, `ExpectedColumns`, `RejectMailTo`, `Active`), one routine reads it, each file is a work item, and one rejection mail goes to the configured recipients. DTSS has no active row: spreadsheets for DTSS are skipped.

**Acceptance criteria.**
1. Given the profile sheet has an active row for an entity, when the flow runs, then the entity's folder `{InputPath}\{dd MMM yyyy}\{InputSubfolder}\` is listed with that row's `FilePattern`, and each match becomes one work item.
2. Given an entity row has `Active` = `No` or is DTSS, when the flow runs, then no file of that entity is listed and the run record shows the entity as `Skipped: not enabled for spreadsheets`.
3. Given the entity folder does not exist, when the flow runs, then the entity outcome is `Skipped: folder missing` and the other entities still run.
4. Given a workbook without the sheet named in the profile, when it is checked, then the item is `Rejected: sheet name`, the found sheet names are on the item, and one mail goes to `RejectMailTo` naming the entity, file and sheets found.
5. Given the sheet exists and its header differs from `ExpectedColumns`, when it is checked, then the item is `Rejected: columns`, the mail lists expected against found names (missing, extra, case or spacing differences), and no row is read.
6. Given the header matches in a different column order, when the file is read, then it is accepted and rows are read by header name.
7. Given a rejection, then exactly one mail is sent per file and none comes from any other account; a mail failure leaves the item `Rejected` with `mailError` recorded and does not stop the loop.
8. Given the schedule fires at 23:50 in the schedule's time zone, when the folder date is derived, then it is the date in that time zone, not the machine's local date.
9. Given the entity folder holds two or more matching files, when it runs, then every file is processed (no "exactly one" rule), each with its own status.
10. Given a rerun on the same day, when items are already `Done` or `Rejected`, then they are not processed again.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| D1.1 | Find the day's advice workbooks per entity | Uses A3 list with the profile's pattern |
| D1.2 | Read the entity profile sheet | `POST /batch` on the profile workbook |
| D1.3 | Sheet-name check |  |
| D1.4 | Column-name check | Exact, case-sensitive, reports missing and extra |
| D1.5 | Rejection mail to the configured recipients | A2 |
| D1.6 | Skip rule for entities not enabled for spreadsheets (DTSS) and for a missing folder, recorded per entity |  |
| D1.7 | Day folder date from the schedule time zone | Same decision as B1 |
| D1.8 | Expose sheet names: `POST /sheets {source}` returning `{sheets:[...]}` for xlsx, Graph and Google sources | Required by D1.3 |

**Test notes.** One genuine workbook per entity plus one rejected file per check (wrong sheet, wrong header, reordered header, extra column). The DTSS spreadsheet sample (five daily files of eleven columns) is the "skipped" case; the International spreadsheet sample (four columns) is the "different profile" case. Check run records, mail text and that no value is read from a rejected file.

**Open items.**
- Whether an entity folder holds one file or many: the two UiPath engines disagree ([SIS-03](../SIS-03-india-excel-extraction.md), question 2). The criteria above assume many.
- Which version is live: the current configuration has `ExcelMappingPath` and no `Entity` key.
- Whether the International four-column spreadsheet (`SlNo`, `Invoice Number`, `Amount`, `TotalCreditedAmount`) is processed by this flow; it carries no customer, date or bank details. It needs its own profile row and F3's hand-off layout.
- A real sample from each active entity, including one that would be rejected.

---

## D2 — Cleaning and mapping
| Field | Value |
|---|---|
| Epic | D — Flow 3: spreadsheet advice extraction |
| Type | Story |
| Depends on | D1, I1, I2 |
| Capability today | To build. `POST /batch` returns rows keyed by header (`sheet-service\internal\api\api.go`); no cleaning or mapping code exists in either service. |

**User story.** As the SIS operations team, I want spreadsheet rows cleaned by three named rules and mapped by header name onto the entity's output columns, so that no figure is altered by an undocumented rule and a reordered file cannot be mapped wrong.

**Context.** Only four value-rewriting assignments in `Entity wise extract.xaml` run (fifty-three sit inside `Comment Out` blocks); the live rules are `-` becomes blank, empty `TDS` or `Other Deductions` becomes 0, and spaces collapse in the customer name and total. Columns are read by position after a name check ([SIS-03](../SIS-03-india-excel-extraction.md), "Cleaning the data"). Tracto carries the live rules by name (`dashAsBlank`, `emptyAsZero`, `collapseSpaces`), reads by header, and keeps the mapping in a `ColumnMap` sheet (`Entity`, `SourceColumn`, `TargetColumn`, `Clean`). Date and amount conversion is the shared normaliser of I2.

**Acceptance criteria.**
1. Given `ColumnMap` rows for an entity, when a file passes D1, then each source column is mapped to its `TargetColumn` by header name and every target column of the profile that has no source is written empty.
2. Given a cell in `TDS`, `Other Deductions`, `Net Amount` or `Invoice Amount` that is exactly `-`, when `dashAsBlank` runs, then the value is empty; a cell `-5` or `- ` containing other text is not changed by this rule.
3. Given an empty `TDS` or `Other Deductions`, when `emptyAsZero` runs, then the value is 0 and no other column receives a 0.
4. Given runs of spaces in `Customer Name` or the total, when `collapseSpaces` runs, then each run becomes one space and the value is trimmed.
5. Given a `Clean` entry that is not one of the three rule names, when the profile is loaded, then the load fails with the row and rule named and no file is processed.
6. Given a `SourceColumn` named in `ColumnMap` but absent from the file header, then the item is `Rejected: columns` with that name.
7. Given the same rows in two files with different column order, then the mapped output is identical.
8. Given a row that fails date or amount conversion, then the row is not dropped here: it is handed to D3 with a reason (rows read = rows mapped + rows rejected).
9. Each cleaning rule has a one-line description in the profile documentation, and a change to any cell is attributable to a named rule.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| D2.1 | Cleaning rules | The three live rules only; conversion calls I2 |
| D2.2 | Header-based mapping to the twenty-four-column schema | DTSS layout; other entities by their I1 profile |
| D2.3 | Column map sheet |  |
| D2.4 | Mapping tests with sample workbooks |  |

**Test notes.** DTSS-shaped workbook (eleven columns, 35 to 142 rows a day in the sample), one with reordered columns, one with `-` placeholders, one with a floating-point `TDS` of fifteen decimals. Compare input row count with mapped plus rejected.

**Open items.**
- The three live rules need confirmation as the intended ones, and whether the fifty-three disabled rules were switched off deliberately ([SIS-03](../SIS-03-india-excel-extraction.md), question 1).
- Whether anything downstream needs the empty beneficiary, advice number and advice date columns for spreadsheet advices.
- `Sl No` reliability (drives the file name; see I3.4).

---

## D3 — Workbook output and reporting
| Field | Value |
|---|---|
| Epic | D — Flow 3: spreadsheet advice extraction |
| Type | Story |
| Depends on | D2, A3, A6, I1, I3, I4 |
| Capability today | To build. `sheet-service\internal\sheet\xlsx.go` `Write` cannot create a workbook or a sheet and writes text only; file copy and move do not exist (A3). |

**User story.** As collection accounting, I want one workbook per entity and day in the Excel hand-off tree with the exact headers the loader reads, so that the existing loader loads Tracto's spreadsheet advices unchanged.

**Context.** UiPath copies a template to `{OutPath}\{dd MMM yyyy}\{Entity}\Extracted Payment Advices.xlsx`, appends every valid file into it, writes a timestamped copy beside it and moves each source to `Extracted Standard Excel Advices` ([SIS-03](../SIS-03-india-excel-extraction.md) steps 7 to 9; [folders-and-files.md](../folders-and-files.md)). In Tracto the rows of the entity's files are collected in the run and the workbook is written once at the end of the entity's run (I3.3), because a workbook changed after the loader reads it duplicates invoice lines (`collection-accounting-handoff.md`). The append write of A6 is not used for the hand-off workbook.

**Acceptance criteria.**
1. Given mapped rows from several files of one entity, when the entity's items are finished, then one `Extracted Payment Advices.xlsx` is written to `...\Excel\Extractor_ExtractedFiles\{dd MMM yyyy}\{Entity}\` containing the rows of all accepted files, with the I1 header names and the advice rows on the first sheet.
2. Given a row that cannot be converted, then it appears on the `Errors` sheet with file, source row number, column and reason, and is not on the first sheet; the entity's result record shows rows read, written and rejected, and read = written + rejected.
3. Given a file name `{Sl No} {dd MM yyyy} {Entity} ExcelAdvice`, then the token `ExcelAdvice` is present in the `File name` of every spreadsheet advice.
4. Given all of an entity's files are rejected or empty, then no workbook is written and the result record says so.
5. Given the workbook was written, then each processed source is moved to `Extracted Standard Excel Advices` and the timestamped copy `Extracted Payment Advices {ddMMMyyyy hhmmss}.xlsx` exists beside the workbook; a rejected file stays where it is.
6. Given a write failure (path unavailable, file locked), then items stay `Pending`, sources are not moved, and the entity outcome is `Failed: write`; a rerun writes the workbook.
7. Given the written file, when a contract test reads it, then the 22 column names the loader selects are present by exact name on the first sheet (see I1.6).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| D3.1 | Report rows that cannot be converted | Errors sheet and result counts |
| D3.2 | Advice workbook writer: twenty-four-column schema, sheets and file naming | Uses the writer core of I1.5 |
| D3.3 | Write to the folder tree for the entity and day, with a result record |  |
| D3.4 | Load test: the existing collection accounting loader reads the Tracto workbook | Run by SIS against a test database; the loader is not modified |
| D3.5 | Move sources to the archive folder and write the timestamped copy | A3 move and copy |

**Test notes.** DTSS-shaped sample files for two entities in one day. Check the tree, the headers, the counts, the archive move, and a rerun after a forced write failure.

**Open items.**
- Whether anything downstream reads the timestamped copy; the loader reads only the fixed name.
- Whether `Extracted Payment Advices.xlsx` (capital P, flow 3) and `Extracted payment advices.xlsx` (the loader's spelling) are the same file on the loader machine (Windows is case-insensitive).
- A spreadsheet advice arriving after the day's workbook is written (see I3.5).

---

## F1 — Validation and customer rules
| Field | Value |
|---|---|
| Epic | F — Flow 4: PDF and image advice flow |
| Type | Story |
| Depends on | A5, E1, E3, E4, E5, E6, I1, I2, I5 |
| Capability today | To build. No `/padvice` route or extraction client exists in `sheet-service\` or `orchestrator-go\`; the extraction service is outside the repository (`H:\Neelitech\apexcos-poc\backend`, `POST /api/extract`). |

**User story.** As the SIS operations team, I want each extracted advice validated and corrected by customer rules held as data, so that a wrong figure never reaches the hand-off workbook now that nobody confirms figures on screen.

**Context.** UiPath branches on text in a 180 KB post-processing workflow into five per-customer workflows, handles two-line invoices and parentheses as negatives per customer, and relies on a person at `Present Validation Station` ([SIS-04](../SIS-04-payment-advice-extraction.md), "What the UiPath process does"). Tracto drives one routine from a `CustomerRules` table (`Customer`, `MatchText`, `Rule`, `AppliesTo`), claims one document at a time from the work-item queue, and validates with its own checks whether or not the service returns colours. `POST /api/extract` returns either a single `extractor` result or a `multi_invoice` result with an `invoices` array; both are handled.

**Acceptance criteria.**
1. Given a pending work item with a PDF, when it is claimed, then the PDF is unlocked (E1), the prompt id of the entity's profile is sent, and the raw reply is stored against the item.
2. Given a reply with an `invoices` array, then each entry becomes its own advice with its own validation and outcome and none are merged.
3. Given HTTP 503 `ANALYZER_BUSY` or 504 `ANALYZER_TIMEOUT`, then the item is held and retried with its attempt count increased (A5) and is not marked failed; after the configured attempts it becomes `Failed: extraction busy`.
4. Given dates and amounts in the reply, then they are converted by I2 and an unparseable date or amount is written empty, never as text, and listed with a reason.
5. Given the profile's required fields, when one is empty after extraction, then the advice is not written to the first sheet and the reason `MISSING_REQUIRED:<field>` is recorded.
6. Given invoice lines and a stated total, then the reconciliation of I5.3 runs and a disagreement beyond tolerance holds the advice back with the sums on the record; with no total the check is recorded as `NoTotal`.
7. Given a `CustomerRules` row whose `MatchText` is contained in the document text, then its named rule (`parenthesesAsNegative`, `twoLineInvoice`, `creditNote`, `valueDateFromAdviceDate`) is applied to the listed fields only; a rule name that is not in the vocabulary fails the table load.
8. Given amounts in parentheses on an invoice amount or net amount, then they become negative numbers for every customer, not only those with a rule.
9. Given an advice with a negative amount or an invoice number starting `CN`, then the line is marked as a credit note.
10. Given the reply is entirely `unreviewed`, then validation still runs and the outcome is decided without colours; given a field is red, then it holds the advice back (no value is returned for it).
11. Given a two-line invoice marker (`One line/Two line`), then the lines are merged by the `twoLineInvoice` rule only where the customer rule says so; the marker is not used alone to branch.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| F1.1 | Date and amount parsing | Wires the I2 normaliser into the flow |
| F1.2 | Invoice lines against the total credited amount | Wires the I5.3 check into the flow |
| F1.3 | Required-field check | Field list from the profile |
| F1.4 | Customer rules table |  |
| F1.5 | Rules for negatives in parentheses, two-line invoices and credit notes |  |
| F1.6 | Map the extraction reply (single and `invoices` array) onto the entity profile columns |  |
| F1.7 | Item loop for `/padvice/start`: claim, unlock, call with prompt id, hold on 503 and 504, store the raw reply | Retry bookkeeping is A5 |

**Test notes.** Sample bundle PDFs from E2, a bank advice (fixed layout, 93 of 112 DTSS documents), a customer-produced advice with `Inv. No.` and `Gross Amt`, an advice with amounts in parentheses and one with a credit note. A stub of the extraction service returning a single result, an `invoices` array, a 503, and an all-`unreviewed` reply.

**Open items.**
- The required-field list per entity.
- Whether amber advices pass or are held (see F4).
- Which of the five per-customer workflows are real rules; a handful of documents per customer is needed.
- Whether the bank-advice rule (empty value date filled from the advice date, for files named as that bank's advising service) and the one European decimal rule are still needed.
- Whether the table has a `TDS` column from the prompt (the UiPath taxonomy has none; the loader reads one).

---

## F2 — Workbook and notifications
| Field | Value |
|---|---|
| Epic | F — Flow 4: PDF and image advice flow |
| Type | Story |
| Depends on | F1, A2, A3, I3, I4 |
| Capability today | To build. Workbook creation with named sheets and typed cells is not in `sheet-service\internal\sheet\xlsx.go`; mail and file move are not in either service. |

**User story.** As collection accounting, I want the PDF advices written to the PDF hand-off tree and the operations team told about failures, missing information and credit notes, so that the loader reads accepted advices and nothing fails silently.

**Context.** UiPath writes per-document workbooks to `Output\PDF_JPEG\Extracted Files\`, moves processed PDFs to `Output\ExtractedPDF\`, failed ones to `Output\PDF_JPEG\Exception\`, consolidates in the `End Process` state without retry, copies to `ExtractedPaymentAdvices\PDF_JPEG\ML_ExtractedFiles\{date}\{entity}\Extracted payment advices.xlsx`, and the three notification mails are commented out ([SIS-04](../SIS-04-payment-advice-extraction.md) steps 9 to 15). In Tracto the consolidation is part of the batch with its own status and a rerun resumes it; the three mails are sent.

**Acceptance criteria.**
1. Given accepted advices, when the entity's items are finished, then `Extracted payment advices.xlsx` is written to the PDF hand-off tree for the entity and day with the sheets of I4.3 and the advice rows on the first sheet.
2. Given a processed advice, then a per-document workbook named like the document is saved in `Output\PDF_JPEG\Extracted Files\` and the PDF is moved to `Output\ExtractedPDF\`.
3. Given an advice that failed validation, then its PDF is copied to `Output\PDF_JPEG\Exception\`, an exceptions record (reason, raw reply, PDF path) is kept, and the advice is not in the first sheet.
4. Given extraction failed for any document, then a "extraction failed" mail lists file name and reason; given required information missing, a "mandatory information missing" mail lists file, field and advice; given credit notes, a "credit notes found" mail lists them; each is sent only when it has rows.
5. Given the consolidation step fails, then items stay `Done` individually, the entity outcome is `Failed: consolidation`, and a rerun writes the workbook without reprocessing documents.
6. Given a mail send failure, then the failure is recorded and the workbook and archive moves are unaffected.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| F2.1 | Write the advice workbook (Output, Errors and Credit Note sheets) to the PDF output tree | Sheet set extended by I4.3 |
| F2.2 | Exceptions record for advices that fail validation |  |
| F2.3 | Three notification mails | A2, HTML tables |
| F2.4 | File the per-document workbook, the processed PDF and the exception copy | A3 |

**Test notes.** A day with accepted, failed, credit-note and locked documents. Check the three trees, the three mails, and a forced consolidation failure followed by a rerun.

**Open items.**
- Recipients of the three mails (the mails are off today, so a backlog will appear on first use).
- Flow 4's per-document paths: the reviewed document puts them under the entity folder, but the workflow builds them from the PDF path; confirm against a real run ([folders-and-files.md](../folders-and-files.md), point 1).
- Whether the three mails and the I4.4 daily mail merge into one.

---

## F3 — International entity profile
| Field | Value |
|---|---|
| Epic | F — Flow 4: PDF and image advice flow |
| Type | Story |
| Depends on | E1, E3, I1, I2, F1, F2 |
| Capability today | To build. No prompt, field set, profile or folder root for the entity exists in `tracto\`; the field set exists only in the sample workbooks (`Simple Fields` sheets, nine columns) and is in none of the four UiPath packages. |

**User story.** As the SIS operations team, I want Southern Cross Protection (AUD) processed with its own field set, folder root and hand-off layout, so that its advices are extracted and written correctly without Indian-tax columns and can be loaded.

**Context.** International has a different extraction model from the Indian one: `beneficiary_bank_bsb_code` instead of an IFSC code, no advice number or date, no UTR, a six-column table (`invoice_number`, `invoice_date`, `invoice_amount`, `other_deduction`, `net_amount`, `merchant_fees`), `merchant_fees` read and never carried over, day-first dates, and 856 documents from 607 payers in the sample ([payment-advice-samples.md](../payment-advice-samples.md) sections 3 and 4). Its tree is `...\Input\International\Payment Advices\{date}\Southern-Cross-Protection\`. Its day workbook `Sheet1` has 15 columns. The International entities run their own copy of the collection accounting service ([SIS-05](../SIS-05-international-bot1.md)), whose loader reads 14 of them by name (all but `BeneficiaryBankBSBCode`) from the same `...\Statements\ExtractedPaymentAdvices\` tree as the Indian loader, under the folder `Southern-Cross-Protection`; the Indian loader's 22 columns do not apply. Its bank accounts are in `BankMasterInternational.xlsx`. It is the only International entity today.

**Acceptance criteria.**
1. Given the entity table has a row for the International entity with the flow-4 flag on, when the flow runs, then it reads `{International root}\{dd MMM yyyy}\Southern-Cross-Protection\PDF_JPEG\` and writes under that day's `Output\PDF_JPEG\` as the Indian entities do under their root; no path of another entity is touched.
2. Given the International prompt id, then the reply fields are the nine header fields (`UTR_number`, `value_date`, `beneficiaryName`, `beneficiary_account_number`, `beneficiary_bank_name`, `beneficiary_bank_bsb_code`, `customer_name`, `total_credited_amount`, `Table`) and the six table fields; none of `advice_number`, `IFSC`, `GST_TDS`, `wct_tds`, `retention` or `TDS` is required.
3. Given `merchant_fees` on a line, then it is added to `Other Deduction` (sum when both are present) and appears in no other column.
4. Given a BSB or account number, then it is written as text with leading zeros kept (`062000` stays `062000`; `062-000` stays as printed).
5. Given a date such as `5/11/2026`, then it is read day-first as 5 November 2026 and written as a date.
6. Given no UTR on the document, then `UTR / TransactionReferenceNumber` is empty and the document is not rejected for it.
7. Given the International profile, then the first sheet of `Extracted payment advices.xlsx` has today's 15 columns by name, and a contract test finds the 14 names the International loader reads (`ValueDate`, `BeneficiaryName`, `BeneficiaryBankAccountNumber`, `BeneficiaryBankName`, `CustomerName`, `CustomerBank`, `UTR / TransactionReferenceNumber`, `TotalCreditedAmount`, `File name`, `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount`, `Other Deduction`, `NetAmount`).
8. Given the day's workbook, then it is written to `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\PDF_JPEG\ML_ExtractedFiles\{dd MMM yyyy}\Southern-Cross-Protection\`, the date in the entity's time zone, before the 07:55 collection accounting run (B4).
9. Given the profile, then the hand-off folder name (`Southern-Cross-Protection`) and the loader's entity key (`SXP_Group`) are both profile values, and the folder name is the one the International `bot_run.py` maps the key to.
10. Given a document the extraction marks as not a payment advice, then it is listed with outcome `not_advice` (I4) and no row is written.
11. Given the six distinct International sample days, then the extraction output has, per document, the same line count as the sample per-document workbook within an agreed tolerance recorded by E7.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| F3.1 | International field schema and prompt (9 header fields, 6 table fields) in the prompt store, with the "not a payment advice" answer | E3's schema is the Indian ten-field set |
| F3.2 | Map the reply to the profile: BSB text, merchant fees into Other Deduction, no UTR, day-first |  |
| F3.3 | Entity folder root, day layout, `NeedValidation` and hand-off root as profile values |  |
| F3.4 | The 15-column hand-off layout with the contract test on the 14 loader names | Rows of the profile are built in I1.3 |
| F3.5 | Entity table row: flags, recipients, entity key, date order |  |
| F3.6 | Tests on the International sample documents (counts only) |  |

**Test notes.** The 856 documents and 7 day workbooks of the International sample (customer data: use counts and field names only), including the 20 locked PDFs, 4 images, 2 scans and 10 non-advices. Check line counts, text identifiers, date order, and the 15-column sheet by name.

**Open items.**
- Whether the International database refuses a repeated invoice line (`InvoiceNumber` and `file_name`); SIS says repeated lines are removed by invoice number, and Tracto writes each line once either way.
- The International `MasterConfig.xlsx` names `...\Input\International\Statements\ExtractedPaymentAdvices` as the extraction path; the loader reads `...\Input\Statements\ExtractedPaymentAdvices`, which SIS confirmed. The setting is taken to be unused.
- Date convention of other senders (the sample shows day-first throughout).
- SIS says International has no password-protected PDFs and no password file; the sample has 20. Whether they are reported to the sender as unreadable.

---

## F4 — Review routing by confidence
| Field | Value |
|---|---|
| Epic | F — Flow 4: PDF and image advice flow |
| Type | Story |
| Depends on | E3, E4, E7, F1, F3, I3, I4 |
| Capability today | To build. The extraction service returns colours per field, not numeric confidences (`extraction-service.md`); no routing code exists in `tracto\`. |

**User story.** As the SIS operations team, I want documents whose weakest field is below a configured threshold held in a review folder with their workbook and listed in the daily mail instead of posted, so that low-confidence figures are checked by a person without a review screen.

**Context.** UiPath shows a document at `Present Validation Station` when the classifier matches a known keyword and a credited amount was extracted; in the International sample 248 of 856 workbooks (29%) sit in `NeedValidation` with a confidence next to every field ([payment-advice-samples.md](../payment-advice-samples.md) section 4.1). Tracto has no review UI; a document below threshold is copied to `PDF_JPEG\NeedValidation\` with its per-document workbook, is not on the first sheet, and is listed in the daily mail. The threshold comes from the 248 workbooks. For the three fields that decide a match (`value_date`, `total_credited_amount`, `Table`) the lowest confidence in a document has quartiles of 0.62, 0.755 and 0.927 (counts below 0.6, 0.7, 0.8, 0.9: 56, 102, 142, 172 of 248); over all fields the median lowest is 0.64. Those are ML scores and the 248 workbooks hold one value per field, which is the value after a person's confirmation, not a before-and-after pair.

**Acceptance criteria.**
1. Given a profile with `ScoredFields` and `ReviewThreshold`, when an advice is extracted, then its review score is the minimum confidence over the scored fields present in the document; a scored field absent from the document is ignored and does not count as zero.
2. Given the score is below `ReviewThreshold`, then the PDF and the per-document workbook are copied (not moved) to `{day}\PDF_JPEG\NeedValidation\`, the outcome is `needs_review` with the weakest field and its score, and no line of the advice is on the first sheet.
3. Given the score is at or above the threshold, then the advice follows the normal path.
4. Given the service returns colours only, then red is withheld (no value), amber routes to review, green passes, and `unreviewed` is never routed by score (only Tracto's own validation applies); the profile records which mode is active.
5. Given the daily mail, then it has a "Needs review" section with file name, weakest field and score for each routed document, and the mail is sent when the section has rows.
6. Given `customer_name` (median confidence 0.805, lowest tenth 0.546 in the sample) is not in `ScoredFields`, then a low payer-name score alone does not route a document.
7. Given a person moves a checked per-document workbook to `NeedValidation\Approved\`, when the next run starts, then its lines are included in that day's hand-off with outcome `extracted (reviewed)` and the document is not rejected by the duplicate guard.
8. Given a threshold change in the profile sheet, then it takes effect on the next run and the value used is on each document's record.
9. Calibration: given the 248 documents re-extracted through E3 and their workbook values as reference, the report lists for each candidate threshold the share of documents routed and the number of documents with a wrong field that would pass; the chosen threshold and the figures are recorded in the profile documentation.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| F4.1 | Review score from the reply: numeric confidence or colour mode, scored fields only |  |
| F4.2 | `ScoredFields` and `ReviewThreshold` in the profile sheet, with validation |  |
| F4.3 | Route: copy PDF and workbook to `NeedValidation`, outcome `needs_review`, excluded from the first sheet |  |
| F4.4 | "Needs review" section in the daily mail | Rendering in I4.4 |
| F4.5 | Threshold calibration run over the 248 documents with the E7 scoring harness |  |
| F4.6 | Tests: below, at and above threshold; colour-only mode; absent scored field |  |
| F4.7 | Release path: approved workbooks in `NeedValidation\Approved` join the next hand-off |  |

**Test notes.** The 248 `NeedValidation` PDFs and workbooks (counts and field names only). A stub reply with numeric confidences and one with colours. Check the folders, the mail section, the first sheet and the release path.

**Open items.**
- E3 or E4 has to return a numeric confidence per field; today only colours are described. Without it the calibration of ML scores does not transfer, and colour mode is the fallback.
- The acceptable share routed to review and the acceptable number of wrong fields that pass; they are the inputs to F4.5.
- Which documents the 248 represent: they are those UiPath showed to a person, not a random sample.
- Whether release by folder move is accepted, or reviewed documents are handled by owner re-run.
- Whether DTSS gets the same routing (the sample has no `NeedValidation` folder for it).

---

## I1 — Output profiles per entity
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | A3, D1 |
| Capability today | Partly. Profile sheets can be read with `POST /batch` (`sheet-service\internal\api\api.go`); the writer that applies a profile does not exist: `sheet-service\internal\sheet\xlsx.go` `Write` is text-only, existing-file-only. |

**User story.** As collection accounting, I want one output profile per entity that fixes the columns, their order, names, types and required fields, so that each entity's workbook has exactly the layout the loader reads and a layout change is a row edit.

**Context.** UiPath writes the DTSS 24-column layout from a template inside the package and International's 15-column layout from a second template; nothing else describes them ([output-workbook-reference.md](../output-workbook-reference.md), requirement R1). The loader selects 22 columns by exact name from the first sheet, including the spaces in `File name`, `GST Tax Amount`, `GST TDS`, `Other Deduction` and `UTR / TransactionReferenceNumber`. Tracto reads an `OutputProfile` sheet (`Entity`, `Order`, `Column`, `Type`, `Required`, `Source`) and an entity row (date order, hand-off mode, root) and writes by it. The DTSS Sheet1 of the sample has the order `AdviceNumber, AdviceDate, ValueDate, BeneficiaryName, BeneficiaryBankAccountNumber, BeneficiaryBankName, BeneficiaryBankIFSCCode, CustomerName, CustomerBank, UTR / TransactionReferenceNumber, InvoiceNumber, InvoiceDate, InvoiceAmount, GST Tax Amount, GST TDS, Retention, TDS, WCT_TDS, Other Deduction, NetAmount, TotalCreditedAmount, File name, BOTGeneratedID, Status`; the `Output` sheet adds `Error description` and `Credit Note`.

**Acceptance criteria.**
1. Given an `OutputProfile` for an entity, when it is loaded, then columns are ordered by `Order`, and a duplicate order, duplicate column name or unknown `Type` (`text`, `number`, `date`) fails the load naming the row.
2. Given the DTSS profile, then the first sheet has the 24 columns above by exact name (including `BOTGeneratedID` and `Status`, empty) and `Output` adds the two flag columns.
3. Given the International profile, then the first sheet has `ValueDate, BeneficiaryName, BeneficiaryBankAccountNumber, BeneficiaryBankName, BeneficiaryBankBSBCode, CustomerName, CustomerBank, UTR / TransactionReferenceNumber, InvoiceNumber, InvoiceDate, InvoiceAmount, Other Deduction, NetAmount, TotalCreditedAmount, File name`, and in mode A the eight loader columns are added (23 columns).
4. Given a profile for an entity the loader reads, when it lacks any of the 22 loader names (and the mode is not B), then the flow refuses to write into the live tree and reports the missing names.
5. Given a `date` column, then cells are written as true dates; a `number` column as numbers; a `text` column as text (so `062000` and `0123456789` keep their zeros).
6. Given a column marked `Required` with no value for a line, then the line goes to `Errors` with `MISSING_REQUIRED:<column>`.
7. Given a new entity, then adding profile rows and an entity row is enough to produce its workbook without a code change.
8. Given the written workbook, when a contract test opens it, then the header row equals the profile by exact string, including spaces.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I1.1 | `OutputProfile` sheet design and reader; entity row fields (date order, hand-off mode, roots) |  |
| I1.2 | DTSS profile: 24 columns and the 26-column `Output` layout |  |
| I1.3 | International profile: 15 columns and the mode-A eight added columns | Decision and contract test in F3.4 |
| I1.4 | Profile validation at load, including the 22-name loader check |  |
| I1.5 | Workbook writer core: create workbook, named sheets, header by profile, typed cells (date, number, text) with excelize | `Write` in `xlsx.go` is `SetCellStr` only and refuses new columns |
| I1.6 | Contract test: header names of a written workbook against the 22 loader names |  |

**Test notes.** Profile sheets for DTSS and International; a deliberately broken profile for each validation. Open the written workbook with an independent reader and compare header strings and cell types.

**Open items.**
- The 24-column order printed in SIS-03 (starting `ValueDate`) differs from the sample day workbook (starting `AdviceNumber`); the loader selects by name, so order does not affect it. The sample order is used here.
- Which other entities need their own profile (AEPL, RARE, Terminix, RHSPL appear in different lists).
- Whether `BeneficiaryBankBSBCode` stays in mode A (the loader ignores it).

---

## I2 — Date, amount and identifier normalisation
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | I1 |
| Capability today | To build. No parsing code in `sheet-service\` or `orchestrator-go\`; `sheet-service` returns every cell as a string (`internal\sheet\sheet.go` `Row.Values`). |

**User story.** As collection accounting, I want dates written as true dates read day-first, amounts as numbers and identifiers as text, so that the loader stores correct dates and amounts and identifiers keep their leading zeros.

**Context.** The current workbooks mix true dates and text (International `ValueDate`: 2,449 dates, 341 text, 183 empty), read `5/11/2026` month-first (1,106 of 1,132 distant value dates are explained by the swap), keep some totals as text (12 International, 1 DTSS), and store BSB codes, account numbers and invoice numbers as numbers, losing leading zeros (64 of 277 documents with a BSB starting `0`) (F2, F3, F6, F7, F14 in [known-faults-in-current-output.md](../known-faults-in-current-output.md)). The loader reads dates as text and parses them month-first; ISO text and true dates are unambiguous to it. Tracto parses once with the entity's order and writes typed cells.

Shapes and expected output (the shapes are those seen; values are invented):

| Column kind | Input shape | Expected output |
|---|---|---|
| Date, day-first | `12/11/26`, `12/11/2026`, `12.11.2026` | true date 12 Nov 2026 |
| Date | `5/11/2026` | 5 Nov 2026, not 11 May |
| Date | `13/11/2026` | 13 Nov 2026 |
| Date | `12 Nov 2026`, `12. November 2026`, `12-Nov.-2026`, `Nov-12-2026` | 12 Nov 2026 |
| Date | `2026-07-07` | 7 Jul 2026 |
| Date | a true date cell | unchanged |
| Date | `31/02/2026`, `tomorrow`, `` | empty; the first two listed with `DATE_UNPARSEABLE` |
| Amount | `1,234.50`, `$1,234.50`, `INR1,234.50`, `₹1,234.50` | 1234.5 |
| Amount, invoice or net | `(123.00)`, `88.00-`, `-800.49` | -123, -88, -800.49 (credit note) |
| Amount, deduction column | `1,746.00-`, `(13.89)` | 1746, 13.89 (a deduction) |
| Amount, deduction column | `-13.89` | -13.89 kept and listed `NEGATIVE_DEDUCTION` |
| Amount | `WHT=7`, `7%` | 7 |
| Amount | `0.00` | 0 |
| Amount | `1746.0000000000001` | 1746 |
| Amount | `123,45.` (comma not followed by three digits, trailing point) | empty, listed `AMOUNT_UNPARSEABLE` |
| Amount, decimal-comma profile | `12.345,67` | 12345.67 |
| Amount | `-` alone | empty |
| Identifier | `062000`, `062-000` (BSB) | text, unchanged |
| Identifier | BSB from a number cell `62000` | `062000` (padded to six digits) |
| Identifier | `0123456789` (account) | text, unchanged |
| Identifier | `1047968`, `1047968A`, ` 104 7968 ` (invoice number) | text; spaces removed per profile |

**Acceptance criteria.**
1. Given an entity with `DateOrder = DMY`, then every table row above under "Date" yields the stated output and no date is read month-first.
2. Given a two-digit year, then years 00 to 69 are 2000 to 2069 and 70 to 99 are 1970 to 1999.
3. Given an unparseable or impossible date, then the cell is empty (never text), the document stays in the output and a reason is on the `Errors` sheet and the `Documents` sheet.
4. Given an amount in the table above, then the output equals the stated number, rounded to two decimals, written as a number cell.
5. Given a trailing minus or parentheses in an invoice or net amount, then the number is negative and the line is a credit note; in a deduction column the same notation is a positive deduction.
6. Given a negative value in a deduction column, then it is written as read and listed `NEGATIVE_DEDUCTION`.
7. Given an amount shape not in the rules, then the cell is empty, the document is reported, and no partial figure is written.
8. Given BSB, account number and invoice number cells, then they are written as text cells; given a number-typed BSB the profile's `BsbPad` pads it to six digits and an account number is never padded.
9. Given the sample shapes, when the table-driven test runs, then every row of the table passes.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I2.1 | Date parser with per-entity order and the format list |  |
| I2.2 | Amount parser: symbols, thousands separators, trailing minus, parentheses, `WHT=7`, `7%` |  |
| I2.3 | Decimal-comma profile option and BSB padding option |  |
| I2.4 | Round to two decimals; list negative deductions |  |
| I2.5 | Identifiers kept as text; spaces rule per profile |  |
| I2.6 | Unparseable values empty, with reason codes on the record |  |
| I2.7 | Table-driven tests from the shapes table |  |

**Test notes.** The table above as a test file; plus the shapes of the DTSS and International sample columns (counts only).

**Open items.**
- Whether a trailing minus in a deduction column is a deduction (as the current TDS output shows) or a negative; the table assumes a deduction.
- Date convention of senders other than the two sampled.
- Whether `DD.MM.YY` can occur on spreadsheet advices.

---

## I3 — Write once and duplicate guard
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | A3, A5, I1 |
| Capability today | To build. No hash, register or atomic write exists; `sha256` in `orchestrator-go\internal\handlers\artifacts.go` hashes script artifacts only. |

**User story.** As collection accounting, I want each invoice line written once and each document processed once, with the day workbook never touched after it is written, so that the loader, which has no unique key on invoice lines, never stores repeated lines.

**Context.** In the sample, 604 of 609 International documents outside `NeedValidation` have every line doubled; the 13 Nov folder repeats 12 Nov (98 files identical by content, the same 277 rows); the loader inserts doubled lines as repeated invoice lines on a first read, and on a later read of the same workbook it adds nothing once the first advice is found, including advices that are new (F1, F4, L3, L7 in [known-faults-in-current-output.md](../known-faults-in-current-output.md)). Advices are deleted from the database after four days (L9), so a document delivered again later is loaded as new. Tracto therefore never changes a file after writing it, and keeps its own register of processed documents beyond four days.

**Acceptance criteria.**
1. Given a document whose lines are identical on every column of the line (same invoice number, dates and amounts), then they are collapsed to one line and the count of collapsed lines is on the `Documents` sheet; lines that differ in any column are all kept.
2. Given a file whose SHA-256 is already in the register for the same entity, then the document is not processed, its outcome is `duplicate` with the date and path of the first processing, and it is listed in the daily mail.
3. Given the 13 Nov sample folder (98 files identical to 12 Nov), then 98 documents are `duplicate` and the day workbook has no lines.
4. Given a document in `needs_review` whose reviewed workbook is later approved (F4.7), then it is not rejected as a duplicate.
5. Given the entity's items are finished, then the day workbook is written to a temporary name in the same folder and renamed, so a reader never sees a partial file.
6. Given the hand-off file for the entity and day already exists, then the flow does not overwrite or append: outcome `HandoffExists`, nothing written, documents stay unprocessed for the next day's folder.
7. Given two advices, then their `File name` values differ: for a PDF it is the full path of the source document; for a spreadsheet advice it is `{Sl No} {dd MM yyyy} {Entity} ExcelAdvice`, with ` #2`, ` #3` added when the name was already used in the day, and `ExcelAdvice` is always present.
8. Given a document arriving after the day's workbook was written, then it is moved to the next day's input folder (or left in place and listed) and appears in the next day's hand-off; it is never added to the written file.
9. Given a rerun after a crash before the rename, then the temporary file is discarded and the workbook is written once.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I3.1 | Collapse identical lines inside a document, with a profile switch and a count |  |
| I3.2 | Content-hash register (PostgreSQL table: hash, entity, first date, path, outcome) and the skip |  |
| I3.3 | Day workbook written once: temporary name then rename, refuse to overwrite |  |
| I3.4 | Unique `File name` per advice |  |
| I3.5 | Late-document handling |  |
| I3.6 | Tests: doubled-line fixture, repeated-day folder, rerun, late file |  |

**Test notes.** A document whose table is doubled, a copy of a day folder, a forced crash before rename, a second run on the same day.

**Open items.**
- Whether one document can legitimately pay the same invoice for the same amount twice; the collapse switch is per profile for that reason.
- The cut-off time after which a day's workbook is written, and the loader's run time (to be placed after it).
- The supplied database (9 to 12 Nov, seven Indian entities) holds no repeated invoice line; whether older data does is open.
- How long the register of processed documents keeps a hash: the loader forgets an advice after four days, so the register is the only guard after that.
- `Sl No` uniqueness in spreadsheet advices.

---

## I4 — Document outcomes and reporting
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | A2, A5, E1, E3, I1, I3 |
| Capability today | To build. No outcome model, mail or `Errors` writer exists in `tracto\`; work-item status is A5. |

**User story.** As the SIS operations team, I want every document to end with a recorded outcome and a daily mail listing those not extracted, so that no payment vanishes without being listed.

**Context.** On the six distinct International days, 36 of 803 files (4.5%) leave no row and no `Errors` line: 20 encrypted PDFs, 4 images, 2 scans and 10 documents that are not advices; the `Errors` sheet holds only extracted lines; `Sheet1` includes the lines that failed; `AdviceNumber` is empty in all 152 DTSS rows although the bank advice prints it (F9, F11, F8 in [known-faults-in-current-output.md](../known-faults-in-current-output.md)). Tracto records one outcome per document and writes accepted lines only to the first sheet.

**Acceptance criteria.**
1. Given any document, then it ends with exactly one outcome from `extracted`, `extracted (reviewed)`, `error`, `locked`, `image_unreadable`, `not_advice`, `duplicate`, `needs_review`, with a reason code and the file name.
2. Given an encrypted PDF with no matching password, then the outcome is `locked`, the reason `ENCRYPTED_PDF`, and the document is in the `Documents` sheet and the daily mail.
3. Given an image or a PDF with no text layer that extraction cannot read, then the outcome is `image_unreadable`.
4. Given the extraction answers that the document is not a payment advice, then the outcome is `not_advice` and no row is written.
5. Given a line that fails validation, then it is on `Errors` with `Error description`, and not on `Sheet1`.
6. Given the day workbook, then the sheets are in this order: `Sheet1` (accepted lines, hand-off columns only), `Output` (accepted lines plus `Error description` and `Credit Note`), `Credit Note`, `Errors`, `Documents` (one row per document: file name, outcome, reason, advice, line counts).
7. Given the profile flag `CreditNotesOnFirstSheet` is `No`, then credit-note lines are not on `Sheet1`; when `Yes` (the current behaviour) they are on `Sheet1` and on `Credit Note`.
8. Given the entity-day is finished, then one mail is sent with counts per outcome and tables for `locked`, `image_unreadable`, `not_advice`, `error`, `duplicate` and `needs_review`; with no non-extracted documents it says so.
9. Given a bank advice that prints an advice reference, then `AdviceNumber` carries it on every line of the advice.
10. Given the mail fails to send, then the failure is recorded and the workbook is unaffected.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I4.1 | Outcome model and reason codes on the work item |  |
| I4.2 | `Errors` rows and the `Documents` sheet |  |
| I4.3 | First sheet accepted-only; sheet order; credit-note flag |  |
| I4.4 | Daily mail with the outcome tables | A2 HTML table |
| I4.5 | Advice number carried to every line | Needs `advice_number` in the prompt (E3) |
| I4.6 | `not_advice` mapping from the extraction answer | Needs the "not a payment advice" answer (E3) |
| I4.7 | Tests: one fixture per outcome (the sample's five kinds of no-row file) |  |

**Test notes.** A day with an encrypted PDF, an image, a scan, a non-advice, a duplicate, a failing line, a credit note and a normal advice. Check the sheet order, counts and mail.

**Open items.**
- Whether credit notes must stay off the first sheet for the loader.
- The recipients of the daily mail and whether it merges with the F2.3 mails.
- Mailbox noise (bounces, the password-link mail) is flow 2 (C epic), not here.

---

## I5 — Net amount derivation
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | I2, F1, F3 |
| Capability today | To build. No derivation or reconciliation code exists in `tracto\`. |

**User story.** As collection accounting, I want `NetAmount` filled on every line and the lines reconciled with the advice total, so that the loader never gives each line of a multi-line advice the whole total.

**Context.** `NetAmount` is empty for 138 of 152 DTSS lines; 20 of 23 multi-line DTSS documents have it empty on at least one line (55 lines). `calculate_net_amount` sets the derived net to `NetAmount` when it is not 0, otherwise to `InvoiceAmount` less `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS` and `Other Deduction`, then overwrites it with `TotalCreditedAmount` for every row whose `NetAmount` is 0; when the net is empty and invoice amount and TDS are set, the loader sets `TotalCreditedAmount` to `InvoiceAmount - TDS` (F5, L4, L5). On 16 Nov 232 of 239 International documents add up; of the seven that do not, two sums differ, two have gaps and three have no total.

**Acceptance criteria.**
1. Given a line with `NetAmount` printed, then it is kept.
2. Given no printed net and an invoice amount, then `NetAmount = InvoiceAmount - (GST Tax Amount + GST TDS + Retention + TDS + WCT_TDS + Other Deduction)`, empty deductions counting 0.
3. Given a one-amount table (International: only the paid amount), then `NetAmount` is that amount and `InvoiceAmount` stays empty.
4. Given a multi-line advice where any line has no derivable net, then no line of that advice is written to `Sheet1`, the advice is on `Errors` with `NET_NOT_DERIVABLE` and the line numbers.
5. Given a derived or printed net of exactly 0, then it is written as 0 and listed `NET_ZERO`, because the loader reads 0 as empty.
6. Given the lines of an advice and its `TotalCreditedAmount`, then the sum of net amounts is compared with the total using the profile's `ReconTolerance` (absolute, initial value 0.05 per advice); when the difference is larger the advice is held back with both sums on the record (`RECONCILE_MISMATCH`).
7. Given an advice with no total, then the check is recorded `NoTotal`, the advice is not held back for that reason, and it is listed in the daily mail.
8. Given a single-line advice, then net and total are compared in the same way.
9. Given the DTSS multi-line fixtures (20 documents with an empty net on some line), then each ends with every line net filled or the advice on `Errors`; given the seven International non-adding documents, then they are held back or recorded `NoTotal` as stated.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I5.1 | Derivation rules: printed net, invoice less deductions, one-amount table |  |
| I5.2 | Never-empty rule for a multi-line advice; `NET_NOT_DERIVABLE`; `NET_ZERO` |  |
| I5.3 | Reconciliation check with tolerance, held-back flag and `NoTotal` | F1.2 wires it into flow 4 |
| I5.4 | Tests: multi-line DTSS shapes, non-adding International shapes, zero net |  |

**Test notes.** Shapes of the 20 DTSS multi-line documents and the seven International documents that do not add up (counts only); the loader's three SQL statements read from `PaymentAdvice\PaymentAdvice.py` as the reference for what a filled net must survive.

**Open items.**
- The right tolerance per entity and currency (0.05 is an initial value to confirm against the sample).
- Whether a held-back advice may go forward with a flag instead (there is no flag column in the loader layout).
- Whether `GST Tax Amount` is deducted from the invoice amount in the business's own arithmetic (the loader does).

---

## I6 — Parallel run and cutover
| Field | Value |
|---|---|
| Epic | I — Output quality and hand-off |
| Type | Story |
| Depends on | D3, F2, F3, I1, I2, I3, I4, I5, E7, G2 |
| Capability today | To build. No comparison tool or parallel-tree setting exists in `tracto\`. The loader's root is fixed in `pyment_advice_loader.py` and is not configurable per path. |

**User story.** As the SIS operations team, I want Tracto to write to a separate tree with the same layout, compared with the UiPath workbooks each day against stated criteria, with a way back, so that the loader moves to Tracto only when the output is proven.

**Context.** The loader reads one fixed path; if both processes wrote there, the second would overwrite the first. During the parallel run Tracto writes to a separate tree with the same internal layout and the files are compared; at cutover Tracto takes the real path and UiPath stops writing it ([collection-accounting-handoff.md](../collection-accounting-handoff.md), "During the parallel run"). UiPath's workbooks carry known faults, so a difference is not always a Tracto error: doubled lines, day and month swapped, text dates, text totals, lost leading zeros, failed lines on `Sheet1`.

**Acceptance criteria.**
1. Given `HandoffRoot` set to the parallel tree, then Tracto writes `...\{flow tree}\{dd MMM yyyy}\{entity}\Extracted payment advices.xlsx` under it with the same relative layout; given the mode is `parallel` and the configured path equals the live root, then the flow refuses to write.
2. Given an entity-day with a Tracto workbook and a UiPath workbook, when the comparison runs, then it reads both first sheets by header and reports rows, advices (distinct `File name` basenames), sum of `TotalCreditedAmount` per advice, sum of `NetAmount`, and differences per field per line.
3. Given UiPath's doubled lines, then identical lines in its workbook are collapsed before comparison and the number collapsed is reported as `UiPathDuplicateLines`.
4. Given a difference, then it is classified `Expected` (UiPath day and month swapped, UiPath text date or text amount, lost leading zero, UiPath failed line on `Sheet1`, UiPath doubled line) or `Unexpected`; the classification rule for each is documented, and `Unexpected` is the only class that blocks cutover.
5. Given a line present on one side only, then it is listed with its side, and for a Tracto-only document the reason is on the `Documents` sheet.
6. Given each day, then a `Comparison {dd MMM yyyy}.xlsx` workbook (sheets `Summary`, `Differences`, `OnlyInTracto`, `OnlyInUiPath`) is written and a summary mail is sent.
7. Cutover is allowed when, over 10 consecutive working days that include both entities, there are zero `Unexpected` differences in `InvoiceNumber`, `TotalCreditedAmount`, `NetAmount` and `ValueDate`; every document UiPath produced a line for has a Tracto line or a listed reason; the E7 accuracy report meets the owner's figures; and a test load of the Tracto workbook succeeds on a test database.
8. Given cutover, then `HandoffRoot` is switched to the live root, UiPath is stopped from writing the live path (owner action), and the first live day is compared with the loader's load result.
9. Given rollback, then `HandoffRoot` returns to the parallel tree, the live file of the day is removed before the loader's next run (if it has not run), and UiPath resumes the live path; lines already loaded from a Tracto file stay loaded and are listed for SIS to correct.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| I6.1 | Parallel and live `HandoffRoot` setting with the refuse-to-overwrite guard |  |
| I6.2 | Comparison tool: read both workbooks, key, totals, per-field differences, result workbook |  |
| I6.3 | Expected-difference rules for the known UiPath faults |  |
| I6.4 | Daily comparison mail and run record |  |
| I6.5 | Cutover switch and first-day check |  |
| I6.6 | Rollback switch and procedure |  |
| I6.7 | Tests: sample workbooks of both entities against copies with injected differences |  |

**Test notes.** The DTSS (5 days) and International (7 folders, one repeated) UiPath day workbooks as the UiPath side; Tracto-side workbooks from the same documents. Counts only in any report that leaves the team.

**Open items.**
- The cutover figures (10 working days, zero unexpected) are initial values to confirm.
- Who stops UiPath on the live path, and when the loader runs relative to Tracto.
- Whether the loader's fixed `E:` root can be pointed at the parallel tree for a test load without changing the Python code.
- Because UiPath's key `File name` is the full path, the comparison keys on the document file name; documents renamed by Tracto's filing would not match (flow 2 naming, C2).

---

## Cross-epic dependencies

- A2 mail sending: D1.5, F2.3, F4.4, I4.4, I6.4.
- A3 file operations: D1.1, D3.5, F2.4, F4.3, I3.2, I3.5.
- A5 work items: D1, F1.7, I3.2, I4.1 (status, attempt count, retry).
- A6 appending write: not used for the hand-off workbook (written once by I3.3); available for trackers.
- C2, C3 (flow 2 filing and work items): D1 reads the `PA_{entity}` files and F1 reads `PDF_JPEG\` items; mailbox noise (R11) is theirs.
- E1 (locked, image and scan handling), E2 (splitting), E3 (International schema, `advice_number`, the "not a payment advice" answer, numeric confidence), E4 (colours), E5 (assembly), E6 (503 and 504 codes), E7 (scoring harness used by F3.6, F4.5 and I6.5).
- G1 (main jobs `excelextract-main`, `padvice-main`), G2 (schedules with time zone: D1.7; ordering ahead of the loader). Indian runs are started by the SPOC with Run now; flow 4 International is scheduled at 05:49 Australian time.
- H1 (profile, column map, customer rules and recipient templates): the sheets named in D1, D2, F1, I1 and F4.
