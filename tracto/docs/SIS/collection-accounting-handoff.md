# The hand-off to collection accounting

How the extracted payment advices reach the existing collection accounting service. The UiPath processes hand them over as a file, the service loads that file into its MySQL database, and Tracto keeps exactly that arrangement. Nothing in it has to be built on the collection accounting side.

Source: the Python service `collectionaccounting` (`pyment_advice_loader.py` and `PaymentAdvice/PaymentAdvice.py`), read as the contract. None of it is modified.

## In brief

The UiPath flows never call an API and never touch MySQL. They write one workbook per source, entity and day. The Python service looks for those workbooks, reads them, and writes the advice and invoice rows to its database. **Tracto's flows 3 and 4 write the same workbooks in the same places, and that is the whole integration.**

Neither the loader nor the existing web pages over the database offer an endpoint for inserting advices, so there is no API to call. Tracto does not connect to MySQL.

## What the loader reads

For each selected entity, on each run, it looks for today's file in three trees under `ExtractedPaymentAdvices`:

| Tree | Written today by |
|---|---|
| `PDF_JPEG\ML_ExtractedFiles\<dd MMM yyyy>\<entity>\Extracted payment advices.xlsx` | Flow 4, PDF advice extraction |
| `Excel\Extractor_ExtractedFiles\<dd MMM yyyy>\<entity>\Extracted payment advices.xlsx` | Flow 3, spreadsheet advice extraction |
| `EmailBody\Extractor_ExtractedFiles\<dd MMM yyyy>\<entity>\Extracted payment advices.xlsx` | A process that is not one of the four; see below |

- **The date folder is the loader machine's date.** A workbook written under another date is not seen that day.
- **Only the first sheet is read.** The advice rows must be on the first sheet; any other sheet, such as errors or credit notes, is ignored by the loader. This is why the UiPath flow ends by deleting a sheet and renaming another to leave the advice rows first.
- **Dates are read as text and parsed.** `AdviceDate`, `ValueDate` and `InvoiceDate` are read as strings and converted, so any common date text works. Amounts are converted from text with thousands separators removed.
- **A file that does not exist is skipped silently.** There is no error if a flow did not run.
- **It inserts and does not replace.** The advice table's key is `File name`; the invoice table has no unique key. Doubled lines inside a workbook are inserted as repeated invoice lines on the first read. On a second read of a workbook, inserting the first advice fails on its existing `File name`; the loader does not roll the database session back, so every later insert from that workbook fails as well and nothing more is added, including any advice in it that is new.
- **It keeps advices for four days.** The match step deletes every advice loaded more than four days earlier, matched or not. A document delivered again after that is loaded as new.
- **The run is started by a person or a bot, not on a timer.** `bot_run.py` loads the workbooks at the start of every run. For the Indian entities a person starts the run per entity from the collection accounting web page, which first sets the entity and the download dates in the database; for the International entities `International_Bot1_SXP` runs it at about 07:55 Australian time, two hours after the International PDF extraction ([SIS-05](SIS-05-international-bot1.md)). The loader reads today's folder at that moment, so the workbook must exist before the run is started.

## The columns it needs

The workbook has twenty-four columns in the UiPath template. The loader uses **twenty-two** and ignores `BOTGeneratedID` and `Status`.

| Used | Columns |
|---|---|
| Advice header | `AdviceNumber`, `AdviceDate`, `ValueDate`, `BeneficiaryName`, `BeneficiaryBankAccountNumber`, `BeneficiaryBankName`, `BeneficiaryBankIFSCCode`, `CustomerName`, `CustomerBank`, `TotalCreditedAmount`, `UTR / TransactionReferenceNumber`, `File name` |
| Invoice line | `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount`, `GST Tax Amount`, `GST TDS`, `Retention`, `TDS`, `WCT_TDS`, `Other Deduction`, `NetAmount` |

Header names are matched exactly, including the spaces in `File name`, `GST Tax Amount`, `GST TDS`, `Other Deduction` and `UTR / TransactionReferenceNumber`. The loader renames `File name` and `UTR / TransactionReferenceNumber` itself.

**One row per invoice line.** The header columns repeat on every line of an advice. The loader keeps one advice per distinct `File name`, so that value is the advice's identity: two advices with the same `File name` collapse into one.

## What the sample files show about the hand-off

The sample day workbooks ([payment-advice-samples.md](payment-advice-samples.md), [output-workbook-reference.md](output-workbook-reference.md)) show what the loader would receive today:

- **Southern Cross Protection's hand-off sheet has 15 columns**, and eight of the 22 the Indian loader needs are missing. The International entities run their own copy of the service, whose loader reads 14 columns by name: `ValueDate`, `BeneficiaryName`, `BeneficiaryBankAccountNumber`, `BeneficiaryBankName`, `CustomerName`, `CustomerBank`, `UTR / TransactionReferenceNumber`, `TotalCreditedAmount`, `File name`, `InvoiceNumber`, `InvoiceDate`, `InvoiceAmount`, `Other Deduction`, `NetAmount`. All 14 are in today's 15-column International sheet (`BeneficiaryBankBSBCode` is not read), and it reads from the same `...\Statements\ExtractedPaymentAdvices\` tree as the Indian loader, under the folder `Southern-Cross-Protection` ([SIS-05](SIS-05-international-bot1.md)). The International loader commits each row on its own and skips an advice already loaded, instead of failing the whole file.
- **Dates reach the loader as true dates or as text.** A true date is already day and month swapped when it was ambiguous at the source; text is read month-first by the loader. Tracto writes true dates, parsed day-first.
- **Documents accepted without validation are written twice** in the sample, and the loader inserts every row as an invoice line. Tracto writes each line once.
- **`NetAmount` is empty for most DTSS lines.** The loader then sets the derived net of every such row to the advice's total, which is only right for a one-line advice.
- **Matching is by `TotalCreditedAmount` within 10 units when there is no UTR**, which is every International advice. The date is not used.

## What Tracto does

- Writes the workbook with exactly those headers, the advice rows on the first sheet, in the folder for the entity and day.
- Writes each advice once. The day's workbook is written after extraction finishes and is not updated afterwards, because a workbook changed after the loader has read it can leave duplicate invoice lines.
- Writes a unique `File name` per advice.
- Keeps errors and credit notes on later sheets, as the UiPath flows do.
- Leaves `BOTGeneratedID` and `Status` in the template, empty, so the layout stays identical to what UiPath writes.

## During the parallel run

The loader looks at one fixed path. If Tracto and UiPath both wrote there, the second would overwrite the first, and the comparison would be of whichever wrote last. So during the parallel run Tracto writes to a **separate tree with the same internal layout**, and the two are compared as files. At cutover the Tracto flow takes over the real path and the UiPath process stops writing to it.

## What is outside the four flows

The `EmailBody` tree is written by a process that is not among the four UiPath processes. The mail dispatcher raises work items for mails with no attachment, and something downstream consumes them and produces that workbook. It is not in the scope of this migration and stays as it is.

The customer alias sheets, which let collection accounting recognise a customer on a bank statement line, are uploaded by a SPOC through the web page into the `customer_alias` table. They stay outside Tracto ([configuration-workbooks.md](configuration-workbooks.md#the-customer-alias-sheets)).

## To confirm

1. **When does the loader run?** It reads whatever exists at that moment, for today's date. The extraction flows need to have written their workbooks first, so the schedule has to put them ahead of the loader.
2. **Is a unique `File name` per advice available?** For a PDF advice it can be derived from the document's name; for a spreadsheet advice it is built from a row number and the entity today, which is not unique if that number repeats.
3. **Do doubled lines reach the database?** The supplied database (9 to 12 Nov, seven Indian entities, 1,536 advices, 2,395 invoice lines) holds no repeated invoice line. The doubled lines of the samples (F1) would be inserted on a first read; whether the loader's workbooks still carry them is open. Tracto writes each line once either way.
4. **Do spreadsheet advices leave the header columns empty?** A workbook from flow 3 has no beneficiary details, advice number or advice date to fill. The loader accepts empty values; whether anything downstream relies on them for these advices is for the process owner.
