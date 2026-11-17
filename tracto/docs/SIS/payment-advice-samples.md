# Payment advice samples: the documents, the outputs and what the data shows

What the sample folders of two entities hold, how a source document becomes a row in the day's workbook, what quality the current output has, and what Tracto has to do about each finding. Column-by-column detail is in [output-workbook-reference.md](output-workbook-reference.md).

**No customer name, bank account, invoice number or amount from the samples is copied here.** Counts, layouts, field names and the shape of values are.

## 1. What was studied

| Sample | Entity | Country, currency | Days | Source documents | Output |
|---|---|---|---|---|---|
| `DTSS` | DTSS | India, INR | 5 (23, 24, 25, 28 and 30 Sep) | 112 PDF | 112 per-document workbooks, 5 day workbooks, 5 spreadsheet advices, 15 mail bodies |
| `Payment Advices Int'l` | Southern Cross Protection (the folder is `Southern-Cross-Protection`) | Australia, AUD | 7 folders, 6 distinct (6, 9, 10, 11, 12, 13 and 16 Nov; 13 Nov repeats 12 Nov, see section 8) | 901 files: 894 PDF, 5 `.jpg`, 2 `.jpeg` | 856 per-document workbooks, 7 day workbooks, 6 spreadsheet advices |

The two are not the same shape of work:

- **DTSS is concentrated.** 112 documents come from seven customers; the five largest account for 98% of them, and 93 documents (83%) are one bank's remittance advice with a fixed layout.
- **Southern Cross Protection is a long tail.** 856 documents come from **607 distinct customers**; 433 customers sent exactly one document, and the five largest account for 5%. Nearly every document has a layout the process has not seen before.
- **International is not in any list the flows read.** Its folder tree is `...\Input\International\Payment Advices\<date>\Southern-Cross-Protection\` (DTSS is `...\Input\Payment Advices\<date>\DTSS\`), it has no sheet in `BankMaster.xlsx` and no entry in the entity lists of [configuration-workbooks.md](configuration-workbooks.md), and its output has a different set of columns (section 5).

Volume in the samples: International averages **131 new documents on each of its six distinct days** (the largest day has 248), DTSS **22 a day**. Together that is about 150 a working day, against the 45 a day recorded earlier. A week of samples is a short basis; the figure needs confirming before sizing (section 11).

## 2. One day's folder

```
<root>\<dd MMM yyyy>\<entity>\
  PDF_JPEG\                              every PDF and image the dispatcher filed, named <customer or Others>_<time stamp>_<n>.<ext>
    NeedValidation\                      copy of each document a person had to confirm, with its workbook (International only)
  MailBody\                              mails that carried no attachment, saved as .eml (DTSS only)
  XLS\                                   spreadsheet advices as received (present, empty in the samples)
  Extracted Standard Excel Advices\      spreadsheet advices after flow 3 has processed them
  Output\PDF_JPEG\
    Extracted Files\                     one workbook per document, same name as the document
    Extracted payment advice.xlsx        the day's consolidated workbook
```

- **The name carries the sender.** The prefix is the customer the dispatcher matched from the mail, or `Others`. In DTSS 93 of 112 documents are prefixed `HSBC Advising Service` (the bank's own advice); in International **all 856 are `Others`**, because no customer list matches 607 senders. The prefix is not the customer on the document.
- **`<n>` is the position in that mail run**, so a number can be missing from the sequence when a file was not a payment advice.
- **`NeedValidation` copies, it does not move.** All 74 PDFs there on 16 Nov are also in `PDF_JPEG`.
- **The spreadsheet advices are inputs.** The folder `Extracted Standard Excel Advices` is where flow 3 files the workbooks it has processed (see [SIS-03](SIS-03-india-excel-extraction.md)). Their formats are in section 6.

## 3. The source documents

### 3.1 What arrives

| | DTSS | International |
|---|---|---|
| PDF | 112 | 894 |
| Pages | 111 one page, 1 two pages | 839 one page, 25 two, 2 three, 3 four, 1 five |
| File size | 15 to 57 KB | 1 KB to 2 MB, median 57 KB |
| Password protected | 0 | 24 files; 20 on the six distinct days (2.5%) |
| Image | 0 | 7 files; 4 on the six distinct days |
| PDF with no text layer (scanned) | 0 | 5 |
| Invoice lines per document | 92 with one, 9 with two, the rest 3 to 10 | 597 with one (70%), 135 with two, 33 with three, 33 with four, 20 with ten or more |

- **A table that runs over pages is rare**, so almost every document is read as one page.
- **Documents that produce no workbook.** On the six distinct International days, 36 of 803 files (4.5%) have none: **20 encrypted PDFs, 4 images, 2 PDFs without a text layer and 10 documents that are not payment advices** (the entity's own account statements, a service notice, a subcontractor's statement, an alarm docket, a supplier's invoice). Three other PDFs without a text layer were read, so the process has a text recognition step that works on scans and fails on some. The day's workbook does not mention any of the 36: its `Errors` sheet only holds rows that were extracted.
- **How the documents were made.** The PDF producer field shows the document families: for International, Aspose (178 or more), Crystal Reports (88), PDFlib/PHP (73), Microsoft Reporting Services (34), DevExpress (25), StrataMax (24), SAP NetWeaver, BFO and 76 with none. These are the accounting systems of the customers, which is why layouts do not repeat.

### 3.2 Layout families

A document is a remittance advice with a header block and a table. The wording differs by sender, but the field set is small.

| Header information | Where it appears | Notes |
|---|---|---|
| Payer | Letterhead or "From" block | The `customer_name`. Often a trading name, sometimes a group; the beneficiary's name appears in a second block |
| Payee (the entity) | "Pay to", "Southern Cross Protection Pty Ltd" | In International often only a name; account number and BSB appear in about a third |
| Payment date | "Payment Date", "Date Paid", "Date:" | Formats seen: `12 Nov 2026`, `12/11/26`, `12/11/2026`, `12.11.2026`, `12. November 2026`, `Nov-12-2026`, `12-Nov.-2026` |
| Total paid | "Total AUD paid", "Payment Amount", "Amount Paid" | With or without `$`, `INR`, `₹`, thousands separators |
| Reference | "EFT No.", "Payment Number", "Document" | **Not a bank UTR.** Australian advices carry no UTR: the `UTR / TransactionReferenceNumber` column is empty in every one of the 2,973 International rows |
| Table | One row per invoice | Invoice number, invoice date, invoice amount or amount paid, discount, still owing |

The **table columns differ** and the extractor has to choose among them:

- one amount column only, which is the paid amount (the large majority);
- invoice total, amount paid and still owing;
- gross, deductions, tax and net;
- the invoice number inside a description ("Invoice 1051221").

A page can also carry a *document* number next to the *invoice* number (SAP-style advices list "your document", "document" and "payment document" together); only one of them is the invoice the entity issued.

DTSS has fewer shapes. The bank advice (93 documents) is a fixed template with the labels `Advice sending date`, `Advice reference no`, `Beneficiary's name`, `Beneficiary's account`, `Value date`, `Remitter's name`, `Other reference` and a table `Invoice Number | Amount | Date | TDS | Cash Discount | Net Amount`. The other 19 are customer-produced advices with `Inv. No.`, `Inv. Date`, `Gross Amt`, `Deductions`, `TDS`, `Net Amt`, and a header that names the beneficiary's PAN, GSTN, account and IFSC.

### 3.3 What is on the page and not in the output

- **DTSS bank advice:** `Advice reference no` is on every page and `AdviceNumber` is empty in all 152 rows. `Customer reference` and `Instruction reference` are not captured. The `Other reference` (the bank's own `HSBCN5...` string) *is* captured, as the UTR.
- **DTSS:** the beneficiary's account number is printed masked by the bank (`*****`), and arrives masked.
- **International:** `Still owing`, discounts and `merchant_fees` are on some documents. `merchant_fees` is extracted but is **never filled and not in the output**, so a deduction shown that way would be lost.

## 4. What the current process extracts

Per document, the extraction writes one workbook with the same name as the document and these sheets:

| Sheet | Content |
|---|---|
| `Simple Fields` | One row of header fields |
| `Table` | One row per invoice line |
| `Simple Fields - Formatted`, `Table - Formatted` | A twin of each; identical to the first two in every sample |
| `DocPath` | One cell, the full path of the source document |

Header fields (`Simple Fields`):

| DTSS (12 columns) | International (9 columns) |
|---|---|
| `advice_number`, `advice_date`, `value_date`, `customer_name`, `beneficiaryName`, `beneficiary_account_number`, `beneficiary_bank_name`, `beneficiary_IFSC_code`, `UTR_number`, `total_credited_amount`, `Table`, `One line/Two line` | `UTR_number`, `value_date`, `beneficiaryName`, `beneficiary_account_number`, `beneficiary_bank_name`, `beneficiary_bank_bsb_code`, `customer_name`, `total_credited_amount`, `Table` |

Table fields:

| DTSS (10 columns) | International (6 columns) |
|---|---|
| `invoice_number`, `invoice_date`, `invoice_amount`, `TDS`, `retention`, `other_deduction`, `net_amount`, `GST_TDS`, `wct_tds`, `GSTtax_amount` | `invoice_number`, `invoice_date`, `invoice_amount`, `other_deduction`, `net_amount`, `merchant_fees` |

International is therefore a **different extraction model** from the one shipped in the Indian process: it has a BSB code instead of an IFSC code, no advice number or advice date, and none of the Indian tax columns. The package studied for the Indian flows (`Payment.Advice.Extraction` 1.0.57) has the DTSS field set; the International field set is not in any of the four packages.

### 4.1 Documents that a person confirmed

248 International workbooks sit in `NeedValidation` (29% of 856). They carry a **confidence** next to each field (`value_date - Confidence`, `net_amount - Confidence` and so on) and one more for the table as a whole. The process documents say a document goes to the validation screen when the classifier matches a known customer keyword and a credited amount was extracted.

| Field | Median confidence | Lowest tenth |
|---|---|---|
| `invoice_amount` | 0.999 | 0.996 |
| `invoice_date` | 0.995 | 0.967 |
| `invoice_number` | 0.994 | 0.933 |
| `net_amount` | 0.988 | 0.585 |
| `total_credited_amount` | 0.966 | 0.807 |
| `beneficiaryName` | 0.957 | 0.768 |
| `value_date` | 0.955 | 0.738 |
| `beneficiary_account_number` | 0.885 | 0.665 |
| `beneficiary_bank_bsb_code` | 0.845 | 0.617 |
| `customer_name` | 0.805 | 0.546 |
| Table (whole) | 0.868 | 0.539 |

The lowest value in a document has a median of 0.64. **The weak fields are the payer's name, the net amount where several columns compete and the whole table where its shape is unusual;** invoice number and date are dependable.

## 5. The day's consolidated workbook

`Extracted payment advice.xlsx` has four sheets in both entities:

| Sheet | Rows | Content |
|---|---|---|
| `Output` | 28 to 681 | Every accepted line, one row per invoice line, header fields repeated on each line, **26 columns for DTSS and 17 for International** |
| `Credit Note` | 0 to 8 | The lines with a negative amount or a `CN` invoice number; these lines are also in `Output` and `Sheet1` |
| `Errors` | 0 to 22 | Lines with no invoice number, no amount, or an amount or total that is not a number |
| `Sheet1` | Output plus errors | `Output` without its last two columns (`Error description`, `Credit Note`), with the `Errors` lines added: **24 columns for DTSS, 15 for International** |

- `Sheet1` is what ends up as the hand-off file, because the process deletes a sheet and renames another so the advice rows are alone (see [collection-accounting-handoff.md](collection-accounting-handoff.md)). It therefore **includes the lines that failed**, with their empty invoice number or non-numeric amount.
- **The two schemas.** The hand-off file for DTSS has the 24 columns the Python loader reads. The one for International has 15, and **eight of the columns the loader needs are missing**: `AdviceNumber`, `AdviceDate`, `BeneficiaryBankIFSCCode`, `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS`. Section 9 follows this through.
- **Errors seen:** `Invoice Amount/Invoice number not present` 25 (International) and 1 (DTSS), `TotalCreditedAmount value is not a number` 16, `Invoice Amount is not a number` 1. The 16 are one currency-formatted total; the one amount is a trailing minus (`88.00-`).
- **Credit notes:** 11 International lines across the days; none in DTSS.

## 6. Spreadsheet advices

Both entities also receive advices as spreadsheets. Their processed copies are in `Extracted Standard Excel Advices`.

| | DTSS | International |
|---|---|---|
| File name | `PA_DTSS_<ddMMyy>.xlsx`, one per day | `StandardExcelAdvice_<dd.MM.yyyy>.xlsx`; one file covers a weekend (`07-09.11.2026`) |
| Columns | 11: `Sl No`, `Date of Credit/Value date`, `Total net amount credited in Bank`, `Customer Name`, `UTR Number`, `Invoice Date`, `Invoice Number`, `Invoice Amount`, `TDS`, `Other Deductions`, `Net Amount` | 4: `SlNo`, `Invoice Number`, `Amount`, `TotalCreditedAmount` |
| Rows | 35 to 142 a day (362 in five days); 17 to 51 customers a day | 46 to 190 a day (615 in six files) |
| One advice | Rows sharing a `Sl No`; the total repeats on each row | Rows sharing a `SlNo` |
| UTR | Present on every row but one | None |

- **The DTSS file is one person's compilation of many customers' advices** (up to 51 customers on a day, one customer on 30 Sep), with the bank's UTR per advice, which is why no extraction is needed for it.
- **The International file has no customer, no date and no bank details at all**: an invoice number, an amount and the advice total. It cannot be matched to a customer or a date, only to the invoice and the total. The `Date`, `Customer` and `UTR` columns that the hand-off file needs come out empty.
- `TDS` in the DTSS file is a computed figure with floating-point noise (fifteen decimal places) and, in one row, a negative value; `Other Deductions` is empty in nearly every row.

## 7. Mail bodies

The 15 `.eml` files in DTSS `MailBody` (mails with no attachment) are **not payment advices**: 14 are bounce notices ("Undeliverable Message", a relay refusal on the address a mail was sent to) and one is a "Warning: system report" saying a password will expire and linking to a page to change it, which is a credential-phishing pattern. None has an attachment. The dispatcher files every mail without an attachment, so noise and a phishing mail sit beside real work.

## 8. Findings in the sample output

These are the faults visible in the files as they are today; the full list, with the loader's own faults, is in [known-faults-in-current-output.md](known-faults-in-current-output.md). Each is a requirement for Tracto (section 10), not a description of what Tracto does.

| # | Finding | Evidence |
|---|---|---|
| F1 | **Auto-accepted documents are written twice.** Documents that did not go to validation have every invoice line duplicated in `Output`; documents that went to validation do not | 604 of 609 International documents outside `NeedValidation` have their lines doubled; 229 of 243 inside it have them once. In DTSS 15 of 93 bank advices are doubled. The identical lines are in the file the loader reads, and the loader's invoice table has no unique key |
| F2 | **Day and month are swapped for dates of the form `d/m/yyyy`.** A date that can be read both ways is read month-first; a date with a day above 12 is left as text | In International, 1,106 of the 1,132 `ValueDate` cells that are more than 20 days from the folder date become correct when day and month are swapped. A value date of 5/11/2026 (5 November) is stored as 11 May. 46% of International `ValueDate` cells that hold a date are wrong in this way |
| F3 | **Dates are a mix of true dates and text.** True dates are written as dates; the rest are left as written | International `ValueDate`: 2,449 dates, 341 text, 183 empty. `InvoiceDate`: 1,366 dates, 1,513 text, 94 empty. DTSS `ValueDate`: 109 dates, 43 text. Text shapes: `dd/mm/yyyy`, `dd.mm.yyyy`, `dd/mm/yy`, `12. November 2026`, `Nov-12-2026` |
| F4 | **The same document is processed on a second day.** The 13 Nov folder is a copy of 12 Nov: all 98 files are identical and the day's workbook has the same 277 rows, whose `File name` still points at the 12 Nov documents | The hash of each of the 98 files matches one from 12 Nov. The loader skips an advice whose `File name` exists but inserts the invoice lines again |
| F5 | **`NetAmount` is empty for most DTSS lines** (138 of 152) and non-empty for nearly all International lines (2,954 of 2,973) | The table of the bank advice holds a `Net Amount`, but the net column is only mapped for some layouts. The loader derives the net from the amounts (section 9) |
| F6 | **Leading zeros are lost** from the BSB code and the account number | 64 of 277 International documents with a BSB code starting with `0` (`062000`) lose it (`62000`); 12 of 14 account numbers do |
| F7 | **Some totals stay text** | 12 International `TotalCreditedAmount` values have a comma and a trailing point (shape `123,45.`) and one DTSS value has a rupee sign inside the digits; they are routed to `Errors` or fail the loader's conversion |
| F8 | **Columns that are always empty:** `AdviceNumber` and `CustomerBank` in DTSS; `UTR / TransactionReferenceNumber`, `BeneficiaryBankName` and `CustomerBank` in International | `AdviceNumber` is on the bank advice and not captured; the others are not on the documents |
| F9 | **Documents that produce nothing are not reported.** Encrypted PDFs, images, some scans and documents that are not advices leave no row and no `Errors` line | 36 of 803 International files (4.5%): 20 encrypted, 4 images, 2 scans, 10 non-advices |
| F10 | **Header values are unreliable in the weak fields.** `customer_name` has the lowest confidence of any field, and the beneficiary and payer names vary (`SOUTHERN CROSS PROTECTION-`, `Southern X Protection`) | 607 distinct payer names in 856 documents; the beneficiary is the same entity under several spellings |
| F11 | **Reconciliation mostly holds.** On 16 Nov, 232 of 239 International documents have a table whose amounts add up to the document's total | The seven that do not: two sums differ, two have gaps and three have no total |
| F12 | **The bank advice can state a negative TDS or a rounding difference.** TDS values such as a small negative number appear in the spreadsheet advice | One row in the DTSS standard file |

## 9. What the collection accounting service does with it

Read from the Python service `collectionaccounting` (`pyment_advice_loader.py`, `PaymentAdvice/PaymentAdvice.py`, `CoreProcess/CaWithPa.py`). It is not modified.

### 9.1 Loading

- The loader reads the first sheet of `Extracted payment advices.xlsx` for each entity in a configured list (the commented list is `AEPL, DTSS, RARE, Terminix`) from three fixed trees under `...\Statements\ExtractedPaymentAdvices\`. The International tree in the sample is under `...\Input\International\`, a different root, and the International hand-off path is not in the samples.
- It converts dates with `dateutil.parser.parse`, which reads `5/11/2026` **month-first**, so the loader repeats the swap of finding F2 for any date that arrives as text and can be read both ways. A date that arrives from UiPath already as a true date is not parsed again.
- It converts `TotalCreditedAmount` and `NetAmount` by removing commas and calling `float`; a value with `$`, `₹` or a trailing minus fails, the error is printed and the field becomes empty.
- It selects its 22 columns by name. **For International's 15-column file, `AdviceNumber` is the first one missing and the load stops there with a `KeyError`; nothing is inserted.** International therefore cannot be loaded today, even if the entity were in the list.
- It inserts one advice row per distinct `File name` and **one invoice row per line without a unique key**, so finding F1 and finding F4 each write repeated invoice lines.

### 9.2 What the matching needs

| Step | Fields used | How |
|---|---|---|
| Match the advice to a bank statement | `UTR / TransactionReferenceNumber`, else `TotalCreditedAmount` | If a UTR is present, the statement whose UTR contains it, one match required. Otherwise the statement whose deposit is within **10 units** of the total, one match required. No date is used |
| Match to unapplied receipts | The same two | The same rule against the unapplied list |
| Match each invoice to an outstanding invoice | `InvoiceNumber` (letters and digits only), `derived_net_amount` | One outstanding invoice with the same number for the entity; the derived net must fall within a band of the balance (2% below for SIS). The TDS is then recomputed as the difference |
| Compute the net | `NetAmount`, `InvoiceAmount`, the deduction columns, `TotalCreditedAmount` | `derived_net_amount` is `NetAmount` when it is not zero, otherwise `InvoiceAmount` less the deductions, then overwritten by `TotalCreditedAmount` for any row whose `NetAmount` is zero, so **a multi-line advice with an empty net gives every line the whole advice total** |

What follows:

- **`TotalCreditedAmount` and the invoice number are the fields that decide a match.** The date, the beneficiary, the bank and the IFSC code are stored and not used to match.
- **A missing UTR makes the match depend on the amount alone.** The International advices have no UTR (finding F8), so every International advice would be matched by total within 10 AUD and any two receipts of similar size are an ambiguous match that the service refuses.
- **The extracted TDS is overwritten.** Matching sets the TDS from the balance, so a wrong or empty `TDS` column matters less than a wrong `InvoiceNumber`.
- **`NetAmount` empty on a multi-line DTSS advice is harmful** (finding F5): the bank advice is mostly single-line, but 20 of the 23 multi-line DTSS documents have an empty `NetAmount` on at least one line, 55 lines in all.
- **The word `ExcelAdvice` in a file name** switches off the SIS band check; a spreadsheet advice must keep its name pattern for that to stay true.

### 9.3 "The Python API"

The service has no web interface. Its `Api` folder (`Bankstatement.py`, `Outstanding.py`, `Unapplied.py`, `CustomerMaster.py`, `ReceiptCreation.py`, `PaToBankPosting.py`, `ApiDownloadManager.py`, `RequestHelper.py`) holds the **calls it makes to Oracle Fusion** to download bank statements, outstanding invoices, unapplied receipts and customer master data, and to create and apply receipts. The bank statement availability check is a separate bot that uses the same code. Tracto reaches Oracle Fusion only through this service, which is why its documentation is awaited ([configuration-workbooks.md](configuration-workbooks.md)).

## 10. What Tracto must do

| # | Requirement | Why |
|---|---|---|
| R1 | **One output profile per entity.** DTSS keeps the 24-column hand-off layout. International gets its own profile, and either the Python service learns it or the profile adds the eight missing columns (empty where the document has none) | Finding F8 and 9.1; the layout is the only contract |
| R2 | **Dates are parsed once, with the entity's convention, and written as true dates.** International and DTSS read `d/m/y` day-first; an unparseable date is left empty and reported, never text | F2, F3, 9.1 |
| R3 | **Amounts are parsed to numbers:** strip currency symbols and thousands separators, read a trailing minus as negative, read `WHT=7` and `7%` as 7, treat `(123.00)` as negative | F5, F7 and the quirks in section 4 |
| R4 | **Identifiers keep their text:** BSB, account numbers and invoice numbers are written as text so leading zeros survive | F6 |
| R5 | **A line is written once.** Within a document, identical lines (same invoice number and amounts) are collapsed; a document already processed on an earlier day (same file content) is not processed again | F1, F4 |
| R6 | **`NetAmount` is filled when it can be derived** (invoice amount less the deductions, or the amount paid when only one amount is on the document); on a multi-line advice it is never left empty | F5, 9.2 |
| R7 | **Every document gets an outcome.** A document that cannot be read (encrypted with no known password, image, no text layer) or that is not a payment advice is listed in the day's `Errors` sheet and in the mail with its file name and the reason | F9 |
| R8 | **Credit notes and errors stay on their own sheets**, as today, and are also excluded from the first sheet where the loader must not read them | Sections 5 and 9 |
| R9 | **The advice number is filled** where the document has one (the bank advice) | F8 |
| R10 | **Confidence decides, not a person.** A document whose weakest field is below a threshold is written to a review folder and listed in the daily mail rather than posted; there is no review screen (a recorded decision). The threshold needs a set of validated documents to set; the 248 in `NeedValidation`, with the corrections a person made, are that set | Section 4.1 |
| R11 | **Mail without an attachment is classified, not filed:** bounces and system reports are dropped and counted; a mail with a link asking for a password is reported to the process owner | Section 7 |
| R12 | **The day's workbook is written once and not touched afterwards** | [collection-accounting-handoff.md](collection-accounting-handoff.md) |

## 11. Fit with the extraction service

| Question | Answer from the samples |
|---|---|
| Can a template do it? | For DTSS's bank advice, yes (one fixed layout, 93 of 112). For International, no: 607 customers, 433 of them seen once. A layout-free reader is required, which is what the existing extraction API is |
| Does it have to see images? | Yes: 4 images and 5 PDFs without a text layer in International (about 1%); two of the scans were not read today. The API as it stands is PDF with a text layer only ([extraction-api-exists-and-additions.md](extraction-api-exists-and-additions.md)) |
| Does it have to open locked files? | Yes: 20 International PDFs are password protected (2.5%). The password list must be applied before the document is sent to a model |
| What does it have to return? | The fields in section 4 for the entity's profile, with a confidence per field, so R10 can work. The International profile needs `beneficiary_bank_bsb_code` and no IFSC, and the Indian profile needs `TDS`, `GST_TDS`, `wct_tds`, `retention` and `GSTtax_amount` |
| Does it have to tell an advice from another document? | Yes: 10 of 803 International files (1.2%) are not advices, and a reader that always returns fields would turn them into rows. It needs a "not a payment advice" answer |
| How many a day? | About 150 across the two samples, against the 45 a day assumed in [SIS-04](SIS-04-payment-advice-extraction.md). The time per document is to be measured on the service; the daily total is more than three times what the design assumed |
| Where is it weakest? | The payer's name, the net amount where columns compete, and a table of unusual shape (section 4.1) |

## 12. To confirm

1. **Is Southern Cross Protection in the scope of the four flows,** and does the collection accounting service post its receipts at all (the entity has no `BankMaster.xlsx` sheet and is not in the loader's list)? If it is, the International profile and the loader change are in scope; if it is not, only the extraction is.
2. **Which columns does the International hand-off need** (R1)? The eight the loader requires, or the loader changed to the 15?
3. **Which date convention does each entity's source use**, and are there senders that use the other? The samples show day-first for both.
4. **Is the volume right?** About 150 documents a working day in the samples against 45 recorded; International's largest day is 248.
5. **What is the password source** for the 20 locked International PDFs, and is the list the same as the Indian one?
6. **Why were the documents outside `NeedValidation` written twice** (F1)? The process documents say duplicates are removed; the files show they are not. Tracto avoids the fault either way (R5), but the live data may already hold repeated invoice lines.
7. **What happened on 13 Nov?** The folder is a repeat of 12 Nov: a re-run, or a mail run that fetched the same mails.
8. **What does the mail with a password link come from?** It reached the mailbox the dispatcher reads.
