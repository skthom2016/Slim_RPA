# The output workbooks, column by column

Where each column of the day's workbook comes from, how it is cleaned, and how often it is filled in the samples described in [payment-advice-samples.md](payment-advice-samples.md). Fill rates are over all rows in the `Output` sheets of the sample days: **152 DTSS rows** (5 days) and **2,973 International rows** (7 folders, one of which repeats another).

## The three layers

```
source document  ->  per-document workbook            ->  the day's workbook
(PDF, image)         Simple Fields (1 row)                Output  (accepted lines + 2 flag columns)
                     Table (1 row per invoice line)       Credit Note, Errors
                     DocPath (the source path)            Sheet1  (the hand-off sheet)
```

A row of `Output` is one invoice line. The header values of the advice (payer, payee, dates, total) repeat on every line of it, and the source document's path in `File name` is what ties the lines of one advice together.

## DTSS: 26 columns in `Output`, 24 in `Sheet1`

`Sheet1` is `Output` without the last two columns. The Python loader reads 22 of the 24; `BOTGeneratedID` and `Status` are ignored.

| Column | Comes from | Observed rule | Filled | Stored as |
|---|---|---|---|---|
| `AdviceNumber` | `advice_number` | Not extracted although the bank advice prints it (`Advice reference no`) | 0.7% | text |
| `AdviceDate` | `advice_date` | The date the bank sent the advice | 77% | date |
| `ValueDate` | `value_date` | The credit date; for the bank advice with no value date the advice date is copied (a rule in the process, for file names containing `HSBC Advising Service`) | 100% | date 109, text 43 |
| `BeneficiaryName` | `beneficiaryName` | The entity, as the payer spells it; the bank advice prints it partly masked | 100% | text |
| `BeneficiaryBankAccountNumber` | `beneficiary_account_number` | As printed, including mask characters (`*****`, `XXXXXXXXXX`) | 97% | text |
| `BeneficiaryBankName` | `beneficiary_bank_name` | As printed | 76% | text |
| `BeneficiaryBankIFSCCode` | `beneficiary_IFSC_code` | Present on the customer-produced advices only | 4% | text |
| `CustomerName` | `customer_name` | The payer; the bank advice prints it partly masked | 100% | text |
| `CustomerBank` | none | Never filled | 0% | |
| `UTR / TransactionReferenceNumber` | `UTR_number` | On the bank advice the `Other reference` string; on the others the bank's payment number where printed | 73% | text |
| `InvoiceNumber` | `invoice_number` | Spaces removed | 100% | text |
| `InvoiceDate` | `invoice_date` | `2026-07-07`, `06/08/2026`, `06.09.2026` all occur at the source | 68% | date 79, text 24 |
| `InvoiceAmount` | `invoice_amount` | Thousands separators removed | 100% | number 150, text 2 |
| `GST Tax Amount`, `GST TDS`, `Retention`, `WCT_TDS` | `GSTtax_amount`, `GST_TDS`, `retention`, `wct_tds` | Columns exist in the model; none of the samples has a value | 0% | |
| `TDS` | `TDS` | `WHT=7` becomes 7; `1,746.00-` becomes 1746 (sign dropped); `13.89` stays | 100% | number |
| `Other Deduction` | `other_deduction` | `0.00` becomes 0 | 4% | number |
| `NetAmount` | `net_amount` | Only when the document's table has the column and the layout is mapped | 9% | number |
| `TotalCreditedAmount` | `total_credited_amount` | `INR71,191.84` becomes 71191.84 | 100% | number 151, text 1 |
| `File name` | `DocPath` | The full path of the source PDF | 100% | text |
| `BOTGeneratedID`, `Status` | the bot | Left empty | 0% | |
| `Error description` | the bot | Set when the line goes to `Errors` | 0% in `Output` | |
| `Credit Note` | the bot | `Yes` when the line is a credit note | 0% | |

## International: 17 columns in `Output`, 15 in `Sheet1`

| Column | Comes from | Observed rule | Filled | Stored as |
|---|---|---|---|---|
| `ValueDate` | `value_date` | Month-first reading of `d/m/yyyy`; see the dates section | 94% | date 2,449, text 341 |
| `BeneficiaryName` | `beneficiaryName` | The entity, spelled several ways | 95% | text |
| `BeneficiaryBankAccountNumber` | `beneficiary_account_number` | **Stored as a number when it is all digits** | 30% | number 854, text 36 |
| `BeneficiaryBankName` | `beneficiary_bank_name` | Never filled; Australian advices print a BSB, not a bank name | 0% | |
| `BeneficiaryBankBSBCode` | `beneficiary_bank_bsb_code` | `062-000` stays; `062000` becomes the number 62000 | 34% | text 805, number 201 |
| `CustomerName` | `customer_name` | The payer | 99% | text |
| `CustomerBank` | none | Never filled | 0% | |
| `UTR / TransactionReferenceNumber` | `UTR_number` | The advices carry no UTR | 0% | |
| `InvoiceNumber` | `invoice_number` | **Stored as a number when it is all digits**; `1047968A` stays text | 100% | number 2,916, text 57 |
| `InvoiceDate` | `invoice_date` | Mixed: dates and `28/09/2026`-style text | 97% | text 1,513, date 1,366 |
| `InvoiceAmount` | `invoice_amount` | Present only when the document shows an invoice total separate from the paid amount | 25% | number |
| `Other Deduction` | `other_deduction` | A discount or deduction column, where there is one | 3% | number |
| `NetAmount` | `net_amount` | The paid amount for the line; for a one-amount table this is the amount in the table | 99% | number |
| `TotalCreditedAmount` | `total_credited_amount` | `$1,234.50` becomes 1234.5; a comma-and-trailing-point shape stays text | 100% | number 2,961, text 12 |
| `File name` | `DocPath` | The full path of the source PDF | 100% | text |
| `Error description` | the bot | Set when the line goes to `Errors` | 0% in `Output` | |
| `Credit Note` | the bot | `Yes` on a credit note | 0.4% | text |

`merchant_fees` is read from the document and not carried to the output.

## Cleaning rules seen

These are what the files show. They are the starting point for the profile's rules.

| Rule | Examples at the source | Result |
|---|---|---|
| Currency symbol and code removed | `INR71,191.84`, `$440.00`, `₹1,234.00` | `71191.84`, `440`, `1234` |
| Thousands separator removed | `2,379,103.44` | `2379103.44` |
| Trailing minus read as a deduction | `1,746.00-` | TDS 1746 |
| Trailing minus on an invoice amount sends the line to `Errors` | `88.00-` | `Invoice Amount is not a number` |
| Deduction written as a label | `WHT=7` | TDS 7 |
| Leading and trailing spaces removed | | |
| `CN` prefix or a minus sign makes a credit note | `CN466774`, `-800.49` | Line also on the `Credit Note` sheet, `Credit Note` = `Yes`, amount negative |
| A line with no invoice number, or with neither invoice amount nor net amount, goes to `Errors` | | `Invoice Amount/Invoice number not present` |
| A total that is not a number goes to `Errors` | a total with a comma and a trailing point | `TotalCreditedAmount value is not a number` |

## Dates

The source documents write the same date in many ways (`12/11/26`, `12/11/2026`, `12.11.2026`, `12 Nov 2026`, `12. November 2026`, `Nov-12-2026`, `12-Nov.-2026`). The workbook ends up with three outcomes in the same column:

| Outcome | International `ValueDate` | International `InvoiceDate` | DTSS `ValueDate` |
|---|---|---|---|
| A true date | 2,449 | 1,366 | 109 |
| Text, as written | 341 | 1,513 | 43 |
| Empty | 183 | 94 | 0 |

A date that can be read as `m/d` **is** read as `m/d`: `5/11/2026` on a document paid on 5 November is stored as 11 May. A date whose first part is above 12 cannot be, and stays text. In the samples, 1,106 of the 1,132 International value dates that are more than 20 days from the day's folder are explained by exactly this swap. The Python loader then reads any remaining text with `dateutil` month-first, which gives the same outcome for ambiguous values.

Tracto writes a true date, from the entity's convention (day-first for both entities in the samples), or leaves the cell empty and lists the document.

## What the loader selects

The loader needs the columns below by exact name and reads them from the first sheet. International's `Sheet1` lacks the ones marked.

| Needed by the loader | In DTSS `Sheet1` | In International `Sheet1` |
|---|---|---|
| `AdviceNumber` | yes (empty) | **no** |
| `AdviceDate` | yes | **no** |
| `ValueDate` | yes | yes |
| `BeneficiaryName` | yes | yes |
| `BeneficiaryBankAccountNumber` | yes | yes |
| `BeneficiaryBankName` | yes | yes (empty) |
| `BeneficiaryBankIFSCCode` | yes | **no** (`BeneficiaryBankBSBCode` instead) |
| `CustomerName` | yes | yes |
| `CustomerBank` | yes (empty) | yes (empty) |
| `UTR / TransactionReferenceNumber` | yes | yes (empty) |
| `TotalCreditedAmount` | yes | yes |
| `File name` | yes | yes |
| `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount` | yes | yes |
| `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS` | yes | **no** (all five) |
| `Other Deduction`, `NetAmount` | yes | yes |

Eight of the 22 are missing for International. That table is the Indian loader. The International entities run their own copy of the service, whose loader reads 14 columns by name: `ValueDate`, `BeneficiaryName`, `BeneficiaryBankAccountNumber`, `BeneficiaryBankName`, `CustomerName`, `CustomerBank`, `UTR / TransactionReferenceNumber`, `TotalCreditedAmount`, `File name`, `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount`, `Other Deduction`, `NetAmount`. All 14 are in today's 15-column International sheet (`BeneficiaryBankBSBCode` is not read), and it reads from the same `...\Statements\ExtractedPaymentAdvices\` tree as the Indian loader, under the folder `Southern-Cross-Protection` ([SIS-05](SIS-05-international-bot1.md)).
