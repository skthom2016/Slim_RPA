# Folders and files across the four flows

Every folder and subfolder the UiPath processes create, every workbook they write, and where each one is saved. The flows hand work to each other through these folders, so the layout is part of what Tracto reproduces.

Paths are as configured in the UiPath estate. `{date}` is `dd MMM yyyy` from the machine's local clock, `{entity}` is the entity name (DTSS, AEPL, RARE, Terminix and the rest).

## The tree

```
E:\Automation\
  Data\
    Config\
      MasterConfig.xlsx                 settings for flows 1, 2, 3 and 4
      BankMaster.xlsx                   flow 1
      PasswordsPDF.txt                  flow 4 (PDF open passwords)
    SampleExtract.xlsx                  flow 3 template, copied per entity
    Input\
      Payment Advices\                  = PaymentAdvicesFolderPath = flow 3 InputPath
        {date}\
          {entity}\
            PA_{entity}...xlsx          flow 2 pre-download; flow 3 input
            Extracted Standard Excel Advices\   flow 3 creates; processed source files move here
            PDF_JPEG\                   flow 2 saves renamed PDFs and images; flow 4 reads them
            XLS\                        flow 2 saves renamed spreadsheets; no flow reads this folder
            MailBody\                   flow 2 saves .eml for mails with no attachment
            Output\
              ExtractedPDF\             flow 4 moves processed PDFs here
              PDF_JPEG\
                Extracted Files\        flow 4 per-document result workbooks
                Exception\              flow 4 documents that failed
                Extracted payment advice.xlsx      flow 4 consolidated workbook
      Statements\
        ExtractedPaymentAdvices\        the hand-off read by the Python loader (key PaymentAdvicesExtractionFolderPath)
          PDF_JPEG\ML_ExtractedFiles\{date}\{entity}\Extracted payment advices.xlsx     flow 4
          Excel\Extractor_ExtractedFiles\{date}\{entity}\Extracted Payment Advices.xlsx  flow 3
          EmailBody\Extractor_ExtractedFiles\...                                       not one of the four
  Tracker\
    ModuleTracker.xlsx                  flow 2: sheet per entity, cell B2 = Success or Fail
    Customers List.xlsx                 flow 2: sheet per entity
    LogTracker.xlsx                     flow 2: sheet "Email Tracker"
    Validation Station.xlsx             flow 4: keywords per entity

D:\Automation\   (in the packages' copy of the configuration; the current MasterConfig.xlsx has the first two on E:\Automation)
  EmailPADispatcher\SourceDownload\     flow 2 staging, plus a subfolder per ZIP while it is unpacked
  Tracker\PDF Extractor Logs\{date}.xlsx  flow 4 daily log of failed documents
  Data\Input\...                        flow 3's second path (see below)

C:\Automation\Data\Input\Statements1\BankStatements\
  {date}\{entity}\                      flow 1: the folder's existence means "available"
  {date}\{entity}_check\bank_statements_check.xlsx   flow 1: per-account check

C:\xampp\htdocs\prod\run_Bank_bot.bat  flow 1: the fetch bot
```

## Flow 1: BankAvailCheck

- **Reads** `MasterConfig.xlsx` (sheets Settings, Entities, Constants), `BankMaster.xlsx` (sheet per entity, `A1:A40`, column `BANK_ACCOUNT_NAME`), and `{entity}_check\bank_statements_check.xlsx` (`Sheet1`, `A1:E200`, columns `BANK_ACCOUNT_NAME` and `AVAILABLE`).
- **Writes** one cell: the entity name into `BankMaster.xlsx`, sheet `EntitySelected`, cell `A2`, which is how the fetch bot learns the entity.
- **Creates** no folders and no files. The result is two mails.

## Flow 2: EmailPADispatcher

- **Folders created per entity per run:** `{PaymentAdvicesFolderPath}\{date}\{entity}\` with `PDF_JPEG`, `XLS` and `MailBody` inside, and the staging folder.
- **Pre-download:** when a mail's first attachment name or its subject contains `PA_{entity}`, all of that mail's attachments are saved straight into `{PaymentAdvicesFolderPath}\{date}\{entity}\`, not into a subfolder. This is the folder flow 3 reads.
- **Staging:** every attachment is first saved to `SourceDownload`. A ZIP is extracted into a subfolder named after the archive, its files are moved on, and then both the archive and the subfolder are deleted.
- **Files created:** each attachment is moved to `PDF_JPEG` or `XLS` as `{customer}_{dd MMM yyyy hh mm ss}_{n}{ext}`. A mail with no attachment is saved to `MailBody` as `{customer or Others}_{dd MMM yyyy hh mm ss}_{n}.eml`.
- **Queue items** carry the full path of the moved file (`PDF_JPEG`, `XLS` or `MailBody` folder plus file name).
- **Workbooks updated:** `LogTracker.xlsx` (sheet `Email Tracker`, rows appended without headers), `ModuleTracker.xlsx` (cell `B2` of the entity's sheet), and `MasterConfig.xlsx` (the mail count written next to `{entity}_MailCount` in `InboxMonitor`, rows `A6:A16`).

## Flow 3: India.Excel.Extraction

**Standard path** (`Main.xaml`, then `Entity wise extract.xaml`):

- **Reads** `{InputPath}\{date}\{Entity}\*.xlsx`. The AEPL, RARE, DTSS and Terminix branches look for `PA_{Entity}*.xlsx`.
- **Creates** `{InputPath}\{date}\{Entity}\Extracted Standard Excel Advices\` and moves each processed source file into it.
- **Copies** the template `SampleExtract.xlsx` to `{OutPath}\{date}\{Entity}\Extracted Payment Advices.xlsx`, creating the folder first. Rows from every valid file of that entity are consolidated into this one workbook.
- **Also writes** a timestamped copy, `Extracted Payment Advices {ddMMMyyyy hhmmss}.xlsx`, in the same folder. DTSS also has `DTSS Extracted Payment Advices.xlsx` there.
- `OutPath` is `E:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\Excel\Extractor_ExtractedFiles`, which is exactly the Excel tree the Python loader reads.

**Second path** (`All queue.xaml`, `CheckFileExists.xaml`, `ExcelExtraction.xaml`), not published:

- Looks in `D:\Automation\Data\Input\Payment Advices\{date}\{entity}\` and proceeds only when there is exactly one `.xlsx` and its name contains `PA_{entity}`.
- Writes `D:\Automation\Data\Input\Statements\ExtractedPaymentAdvices\Excel\Extractor_ExtractedFiles\{date}\{entity}\Extracted Payment Advices.xlsx`, with the headers taken from `Data\Payment advice header.xlsx` (`A1:X1`), inside the package.

## Flow 4: Payment.Advice.Extraction

- **Reads** the queue `{entity}_PDF_JPEG_Queue`, `MasterConfig.xlsx` (sheets Settings, Constants, PDF Extraction, EmailDetails), `Validation Station.xlsx`, `PasswordsPDF.txt` and `Config\Payment advice header.xlsx`.
- **Per document:** a per-document results workbook with a sheet per table and a `DocPath` sheet, saved as `{docName}.xlsx` in `...\Output\PDF_JPEG\Extracted Files\`, plus `DocumentProcessing\Output\{docName}.xlsx` and `temp.xlsx` inside the robot's working folder. The processed PDF is moved to `...\Output\ExtractedPDF\`. A failed document is moved to `...\Output\PDF_JPEG\Exception\` and a row is added to `D:\Automation\Tracker\PDF Extractor Logs\{date}.xlsx`.
- **Once per entity, after the queue is empty:** the per-document workbooks are consolidated into `...\Output\PDF_JPEG\Extracted payment advice.xlsx` (sheets for header, `Table`, `Output`, `Errors` and `Credit Note`). It is then copied to `ExtractedPaymentAdvices\PDF_JPEG\ML_ExtractedFiles\{date}\{entity}\Extracted payment advices.xlsx` (folder created first), cleaned, and left with the advice rows on the first sheet.

## How the flows hand over

| From | To | Through |
|---|---|---|
| Flow 2 | Flow 3 | The `PA_{entity}` attachments saved at the top of `{PaymentAdvicesFolderPath}\{date}\{entity}\` |
| Flow 2 | Flow 4 | `PDF_JPEG\` and the queue `{entity}_PDF_JPEG_Queue` |
| Flow 3 | Python loader | `...\Excel\Extractor_ExtractedFiles\{date}\{entity}\Extracted Payment Advices.xlsx` |
| Flow 4 | Python loader | `...\PDF_JPEG\ML_ExtractedFiles\{date}\{entity}\Extracted payment advices.xlsx` |
| Flow 2 | Nothing | `XLS\` and the mail-body queue have no reader among the four flows |

## Points to confirm

1. **Flow 4's folder paths.** The reviewed process document puts the per-document workbooks and the exception folder under `{PaymentAdvicesFolderPath}\{date}\{entity}\Output\...`. In the workflow those paths are built from the argument `filePath`, which the queue item fills with the full path of the PDF, and a folder cannot sit inside a file path. Either the argument holds the entity folder in production or those steps fail. The folder layout above is the documented one; confirm it against a real run.
2. **`XLS\` has no reader.** Flow 3 lists `*.xlsx` at the top of the entity folder, so it sees the `PA_{entity}` pre-downloads and not the spreadsheets flow 2 files into `XLS\`. Confirm which spreadsheets flow 3 is meant to process.
3. **Two drives.** In the copy of the configuration shipped inside the packages, the second path in flow 3, the daily log of flow 4 and the dispatcher's staging folder are on `D:`. In the current `MasterConfig.xlsx` the staging folder (`SourceDownloadsPath`) and the daily log (`DailyLogFile`) are on `E:`. Tracto uses one configured root for each.
4. **Two output files in flow 3.** The workbook and its timestamped copy are both written. The loader reads only `Extracted payment advices.xlsx`; confirm that nobody downstream relies on the timestamped copy.
