# SIS on Tracto

Analysis of the SIS payment-advice automation and how each part of it is rebuilt on Tracto. One document per flow: what the process does today, what shape it takes in Tracto, what has to be built, and what still has to be answered before it can be.

No code here. Each document ends with an **Open questions** section — those are the points where building would mean guessing.

| Document | The flow | Shape in Tracto |
|---|---|---|
| [SIS-01](SIS-01-bank-avail-check.md) | **BankAvailCheck** — check each entity's bank statements have arrived, start a fetch bot for the ones missing, mail two reports | Go process, no script |
| [SIS-02](SIS-02-email-pa-dispatcher.md) | **EmailPADispatcher** — read the entity mailboxes, file every advice by customer and type, raise a work item per file, chase the sender when a file is unusable | Go process, no script |
| [SIS-03](SIS-03-india-excel-extraction.md) | **India.Excel.Extraction** — validate the spreadsheet advices against the agreed layout, clean them, map them onto the standard table | Go process, no script |
| [SIS-04](SIS-04-payment-advice-extraction.md) | **Payment.Advice.Extraction** — read the PDF advices, correct them per customer, produce the same standard table | Go process over the extraction service, no script |
| [collection-accounting-handoff.md](collection-accounting-handoff.md) | **The hand-off** — how extracted advices reach the existing Python collection accounting service | Reference: the workbook layout flows 3 and 4 write |
| [extraction-module-updates.md](extraction-module-updates.md) | **Extraction module updates** — what the existing module still needs: PDF password management and image extraction | Handed to the module team |
| [extraction-service.md](extraction-service.md) | **The extraction service** — the existing Go + Python service flow 4 calls instead of UiPath Document Understanding | Already exists; reference only |

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

**Two flows are mostly copy-paste today.** Flow 3 is one routine duplicated per entity in a single 500 KB file; flow 4 has five per-customer workflows behind a 180 KB dispatcher. In both the differences are data, so a profile sheet and a rules table replace them, and adding a customer becomes adding a row.

## Before any of this is built

Every one of the four packages carries credentials in clear text or writes them to a log — an Azure AD client secret in a shared workbook, a mailbox password logged at `Info` on every run, two SMTP passwords and a live estate configuration workbook published inside a package, an OCR API key and about fifty document passwords. Each document records the detail under **Security findings**, without reproducing any value. Rotating them is independent of any Tracto work and should not wait for it.
