# Known faults in the current output

The faults visible in the files the UiPath processes write today and in the way the collection accounting service loads them, with the evidence for each, what it does downstream, and how Tracto avoids it. Evidence is from the DTSS sample (112 documents, 152 rows, 5 days) and the Southern Cross Protection sample (International: 856 documents, 2,973 rows, 7 folders), described in [payment-advice-samples.md](payment-advice-samples.md). Columns are explained in [output-workbook-reference.md](output-workbook-reference.md).

Severity: **High** puts wrong or repeated data into the accounting database or stops a load; **Medium** loses information or forces a manual step; **Low** is untidy.

## In the workbooks the processes write

| ID | Fault | Severity | Evidence | Effect downstream | Tracto |
|---|---|---|---|---|---|
| F1 | **Lines of auto-accepted documents are written twice.** Documents that did not go to a person for confirmation have every invoice line duplicated in `Output`; confirmed ones do not | High | 604 of 609 International documents outside `NeedValidation` doubled, 229 of 243 inside it once. 15 of 93 DTSS bank advices doubled. The process documents say duplicates are removed; identical lines remain | The loader inserts every row as an invoice line with no unique key, so each doubled line is a repeated invoice line | R5: a line is written once; identical lines inside a document collapse |
| F2 | **Day and month swapped.** A date that can be read as month-first is read that way; one whose first part is above 12 is left as text | High | 1,106 of 1,132 International `ValueDate` cells more than 20 days from the day's folder are explained by the swap. A value date of 5 Nov 2026 is stored as 11 May 2026. 46% of International value dates that hold a date are wrong. Same for invoice dates | Wrong value and invoice dates in the database. Matching does not use dates, but anything read from them (ageing, reports) is wrong | R2: parse once with the entity's day-first convention; write a true date |
| F3 | **Dates are a mix of true dates and text** in one column | Medium | International `ValueDate`: 2,449 dates, 341 text, 183 empty. `InvoiceDate`: 1,366 dates, 1,513 text, 94 empty. DTSS `ValueDate`: 109 dates, 43 text | The loader parses the text with `dateutil`, month-first, repeating F2 for ambiguous values | R2: a date is a date or empty; an unreadable date is listed |
| F4 | **The same documents are processed on a second day** | High | The 13 Nov folder is a copy of 12 Nov: all 98 files identical by content, the same 277 rows, `File name` still pointing at 12 Nov | The loader skips the advice (same `File name`) and inserts its invoice lines again | R5: a document already processed (same content) is not processed again |
| F5 | **`NetAmount` is empty for most DTSS lines** | High | 138 of 152 DTSS lines empty; International 19 of 2,973 | The loader sets the derived net of a row with an empty net to the advice's whole total, right only for a one-line advice. 20 of the 23 multi-line DTSS documents have an empty net on at least one line (55 lines) | R6: net is derived when it can be; never empty on a multi-line advice |
| F6 | **Leading zeros are lost** from BSB codes and account numbers | Medium | International: 64 of 277 documents with a BSB starting `0` lose it (`062000` becomes `62000`); 12 of 14 account numbers. 854 of 890 account numbers and 2,916 of 2,973 invoice numbers are stored as numbers | A BSB or an account number that no longer matches the bank's | R4: identifiers are written as text |
| F7 | **Some totals stay text** | Medium | 12 International `TotalCreditedAmount` values (comma and a trailing point) and 1 DTSS value with a rupee sign inside the digits | The line goes to `Errors`, or the loader's conversion fails and the total is empty, so the statement match by total is lost | R3: amounts are parsed to numbers |
| F8 | **Columns that are always empty.** DTSS: `AdviceNumber` (the bank advice prints it), `CustomerBank`, `GST Tax Amount`, `GST TDS`, `Retention`, `WCT_TDS`. International: `UTR / TransactionReferenceNumber`, `BeneficiaryBankName`, `CustomerBank` | Medium | Fill rates in [output-workbook-reference.md](output-workbook-reference.md) | With no UTR the match falls back to the total within 10 units, which is ambiguous for similar amounts | R9: advice number filled where printed; the rest stay empty where the document has none |
| F9 | **Documents that produce nothing are not reported** | Medium | 36 of 803 International files (4.5%): 20 encrypted, 4 images, 2 scans not read, 10 that are not advices. No row and no `Errors` line for any | A payment that never reaches the database and that nobody is told about | R7: every document gets an outcome and is listed |
| F10 | **Names are unreliable** in the weakest field | Medium | `customer_name` has the lowest confidence of any field (median 0.805); the beneficiary is the same entity under several spellings; 607 payer names in 856 documents | Customer alias matching has to absorb every spelling | R10: confidence decides what is posted |
| F11 | **Failed lines are in the hand-off sheet** | Medium | `Sheet1` is `Output` plus the lines of `Errors` (International: 2 of 3 on 6 Nov; 21 of 22 on 16 Nov) | Lines with no invoice number or a non-numeric amount reach the loader | R8: the first sheet holds accepted lines only |
| F12 | **`merchant_fees` is read and dropped** | Low | Extracted in the International table, never filled and not in the output | A deduction printed that way would be lost | International profile carries it to `Other Deduction` where present |
| F13 | **Mailbox noise is filed as work** | Low | 14 of 15 DTSS mail bodies are bounce notices; one is a password-expiry mail linking to a page to change a password (a phishing pattern) | Noise in the queue; the phishing mail sits in the same folder as real advices | R11: bounces and system reports are dropped and counted; a mail asking for a password is reported |
| F14 | **Negative TDS and floating-point noise** in the spreadsheet advice | Low | DTSS standard file: a `TDS` with fifteen decimals, one negative | The same values reach the loader | R3: amounts are rounded to two decimals; a negative deduction is listed |

## In the loader and the matching

Read from `pyment_advice_loader.py`, `PaymentAdvice/PaymentAdvice.py` and `CoreProcess/CaWithPa.py`. The service is not modified; these are what Tracto's files must be shaped around.

| ID | Fault | Severity | Where | Effect | Tracto |
|---|---|---|---|---|---|
| L1 | **Dates are parsed month-first** | High | `date_parse` calls `dateutil.parser.parse` with no `dayfirst` | Repeats F2 for any date that arrives as text and can be read both ways | R2: Tracto sends true dates |
| L2 | **International cannot be loaded** | High | The loader selects 22 columns by name; the International `Sheet1` has 15 and lacks `AdviceNumber`, `AdviceDate`, `BeneficiaryBankIFSCCode`, `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS`. The entity is also not in the configured list and its tree is under another root | `KeyError`, caught and printed; nothing is inserted | R1: an International profile, with the missing columns empty or the loader changed |
| L3 | **No unique key on invoice lines** | High | The invoice table's key is a row index; one advice row per `File name`, many invoice rows | Every repeated read, doubled line or repeated day adds repeated lines | R5 and R12: write once, never rewrite |
| L4 | **Derived net is overwritten by the total** | High | `calculate_net_amount` sets the net, then sets it again to `TotalCreditedAmount` for every row whose net is zero | A multi-line advice with an empty net gives each line the whole total | R6 |
| L5 | **A wrong total is written into the total column** | Medium | When the net is empty and invoice amount and TDS are set, the loader assigns `InvoiceAmount - TDS` to `TotalCreditedAmount` | The advice total of the line is replaced by one invoice's net | R6: net filled so this branch is not taken |
| L6 | **Errors are printed and swallowed** | Medium | `except Exception: print(...)` around each insert, conversion and the whole load | A row that fails is lost with no record; a failed load looks like a quiet run | Tracto validates the file before it is left for the loader |
| L7 | **The load runs twice per bot run** | Medium | The loader module runs its load on import and `bot_run.py` calls it again | Doubles the effect of L3 on every run | Out of Tracto's scope; flagged to the owner |
| L8 | **Match by total within 10 units, no date** | Medium | `bank_compare_by_total_credited` with a tolerance of 10 | Two receipts of similar size are an ambiguous match; every International advice depends on it (no UTR) | R1 and R9: more identifying fields where the document has them |

## What is not faulty

- Reconciliation inside a document mostly holds: on 16 Nov, 232 of 239 International documents have a table that adds up to the document's total.
- DTSS `TDS` is filled on every line and numeric. `InvoiceNumber`, `File name` and `TotalCreditedAmount` are filled on every line of both entities.
- Credit notes are separated and flagged (11 International lines across the days).

## Order of work

1. R5 (write once) and R2 (dates): they remove the faults that put wrong or repeated data into the database.
2. R1 (the International profile): without it nothing loads for that entity.
3. R6, R3 and R4: the amounts and identifiers.
4. R7, R9, R10 and R11: reporting and review.
