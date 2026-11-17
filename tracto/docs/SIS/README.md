# SIS on Tracto

Analysis of the SIS payment-advice automation and how each part of it is rebuilt on Tracto. One document per flow: what the process does today, what shape it takes in Tracto, what has to be built, and what still has to be answered before it can be.

No code here. Each document ends with an **Open questions** section — those are the points where building would mean guessing.

| Document | The flow | Shape in Tracto |
|---|---|---|
| [SIS-01](SIS-01-bank-avail-check.md) | **BankAvailCheck** — check each entity's bank statements have arrived, start a fetch bot for the ones missing, mail two reports | Go process, no script |
| [SIS-02](SIS-02-email-pa-dispatcher.md) | **EmailPADispatcher** — read the entity mailboxes, file every advice by customer and type, raise a work item per file, chase the sender when a file is unusable | Go process, no script |
| [SIS-03](SIS-03-india-excel-extraction.md) | **India.Excel.Extraction** — validate the spreadsheet advices against the agreed layout, clean them, map them onto the standard table | Go process, no script |
| [SIS-04](SIS-04-payment-advice-extraction.md) | **Payment.Advice.Extraction** — read the PDF advices, correct them per customer, produce the same standard table | Go process over the extraction service, no script |
| [configuration-workbooks.md](configuration-workbooks.md) | **Configuration workbooks** — which sheets are used (`Settings` of `MasterConfig.xlsx`, every sheet of `BankMaster.xlsx`), and how the entity lists differ | Reference for the stories |
| [payment-advice-samples.md](payment-advice-samples.md) | **Payment advice samples** — what the DTSS and Southern Cross Protection documents and outputs show: layouts, extracted fields, the faults in today's output, what the loader does with them, and what Tracto must do | Reference for the stories |
| [output-workbook-reference.md](output-workbook-reference.md) | **Output workbooks, column by column** — source field, cleaning rule, fill rate and storage type of every column, for both entity layouts | Reference for the stories |
| [known-faults-in-current-output.md](known-faults-in-current-output.md) | **Known faults in the current output** — each fault in the files and in the loader, with evidence, effect and how Tracto avoids it | Reference for the stories |
| [folders-and-files.md](folders-and-files.md) | **Folders and files** — every folder and subfolder the four flows create, and where each sub-workbook and merged workbook is saved | Reference for the stories |
| [collection-accounting-handoff.md](collection-accounting-handoff.md) | **The hand-off** — how extracted advices reach the existing Python collection accounting service | Reference: the workbook layout flows 3 and 4 write |
| [extraction-api-exists-and-additions.md](extraction-api-exists-and-additions.md) | **Extraction API: what exists and what to add** — current features, what is missing, and what each attachment type in the UiPath flow needs | Handed to the module team |
| [extraction-module-updates.md](extraction-module-updates.md) | **Extraction module updates** — what the existing module still needs: PDF password management and image extraction | Handed to the module team |
| [extraction-service.md](extraction-service.md) | **The extraction service** — the existing Go + Python service flow 4 calls instead of UiPath Document Understanding | Already exists; reference only |

## Conversion at a glance

| Flow | Main job | Child jobs | Go service | Browser runtime | What it creates |
|---|---|---|---|---|---|
| 1 BankAvailCheck | `bankcheck-main`: one `http.post` to `/bankcheck/start`, then ends | None. Go runs the fetch bot as a program | `/bankcheck/start`; uses file checks, mail send, the program runner and the sheet engine | Not needed | Nothing except cell `A2` of `BankMaster.xlsx` and two mails |
| 2 EmailPADispatcher | `maildispatch-main` to `/maildispatch/start` | None | `/maildispatch/start`; uses mail read and send, file operations, the appending write and work items | Not needed | The dated folder tree with `PDF_JPEG`, `XLS` and `MailBody`, the `PA_` pre-download, the trackers |
| 3 India.Excel.Extraction | `excelextract-main` to `/excelextract/start` | None | `/excelextract/start`; uses file operations, sheet reads, the appending write and mail send | Not needed | One consolidated `Extracted Payment Advices.xlsx` per entity per day, its timestamped copy and the `Extracted Standard Excel Advices` archive folder |
| 4 Payment.Advice.Extraction | `padvice-main` to `/padvice/start` | None. Each document is one step in the Go loop | `/padvice/start`; calls the document extraction process (LLM), file operations, the appending write and mail send | Not needed | Per-document workbooks, the consolidated workbook, the hand-off workbook, the `Exception` and `ExtractedPDF` folders and the daily log |

- **No child flow is created for any of the four.** Each flow is a main job and a Go service that loops over entities, files or documents, with its own status per item.
- **The desktop runtime is used only to run the one-step main job**, which is a single HTTP call. No step drives a window or an application.
- **The browser runtime is not needed anywhere.** No UiPath flow automates a web page; the fetch bot is a console script.

The full folder map, with the subfolders each flow creates and where the sub-workbooks and merged workbooks are saved, is in [folders-and-files.md](folders-and-files.md).

## How the flows fit together

```
mailboxes ──▶ 02 dispatcher ──┬──▶ 03 spreadsheet advices ──┐
                              │                             ├──▶ standard table ──▶ collection accounting
                              └──▶ 04 PDF advices ──────────┘                        (Python + MySQL, posts
                                        │                                             receipts and TDS to the ERP)
                                        └── extraction service

01 bank availability runs alongside, ahead of the bank-statement processing
```

## What they have in common

Across all four, the work that has to be built is almost the same handful of pieces: filesystem operations, sending and reading mail, an appending write to a sheet, and a source of batch items that is not sheet rows. Build those once and three of the four flows are mostly plumbing after that.

**Not one step in the four flows needs a worker.** The fetch bot in flow 1 was the last candidate and it turns out to be a console script, which Go can start itself. Everything else is files, spreadsheets, mail and documents. In UiPath all of it had to run inside a robot holding a desktop session; on Tracto each flow is a schedule, a one-line main job and a Go process.

What that leaves Tracto's job system doing here is worth saying: the schedule, the run record, the per-item status and the audit trail. That is not nothing — it is what makes the work observable and rerunnable — but no script drives an application in any of the four.

**The hand-off to collection accounting is a file.** The UiPath flows never call an API or touch MySQL: they write `Extracted payment advices.xlsx` into fixed folders, and the existing Python service loads it. Tracto keeps exactly that. See [collection-accounting-handoff.md](collection-accounting-handoff.md) for the layout the loader reads and what flows 3 and 4 must write.

**The queue is a working default, not a settled point.** Everything flow 2 produces is work for a later process, so these documents needed an answer to what a work queue is in Tracto. The working default is a queue-items table in SQLite, decided in [SIS-02](SIS-02-email-pa-dispatcher.md). There is no review screen in this migration, so the reason that first favoured the table no longer applies, and at about 45 documents a working day a sheet is sufficient and simpler. Sheet or table is to be confirmed. Sheets stay for configuration a person edits and for the output workbooks.

**The entity lists differ from one flow to the next.** Seventeen entities appear across the configuration and the Python service, and no two lists match: RARE is dispatched and extracted but has no bank sheet and is not loaded by collection accounting; AEPL and RHSPL are loaded but never dispatched; ONESIS has no recipient. Tracto holds one entity table with a flag per flow. The comparison is in [configuration-workbooks.md](configuration-workbooks.md).

**Two flows are mostly copy-paste today.** Flow 3 is one routine duplicated per entity in a single 500 KB file; flow 4 has five per-customer workflows behind a 180 KB dispatcher. In both the differences are data, so a profile sheet and a rules table replace them, and adding a customer becomes adding a row.

## Before any of this is built

Every one of the four packages carries credentials in clear text or writes them to a log — an Azure AD client secret in a shared workbook, a mailbox password logged at `Info` on every run, two SMTP passwords and a live estate configuration workbook published inside a package, an OCR API key and about fifty document passwords. Each document records the detail under **Security findings**, without reproducing any value. Rotating them is independent of any Tracto work and should not wait for it.
