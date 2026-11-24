# Page 1: Overview

The whole picture on one page: five separate flows, the folders and files they pass between them, and the collection accounting program at the end. Read the page from left to right: mail comes in on the left, payments are posted in Oracle Fusion on the right. Flow 1 sits on its own at the bottom, because it does not take part in the advice chain.

The words used here are explained in [README.md](README.md).

## Why five separate flows

Each flow does a different job, runs at a different time and can fail on its own. Keeping them separate means a problem in one (for example a mailbox that cannot be reached) does not stop the others, and each can be rerun alone. They never call each other: one flow leaves files and work items behind, and the next one picks them up.

Every flow is started the same way: a schedule (or a person pressing Run now) starts a small main job, the main job makes one call to the Go API, and the Go API does the work. Page 3 shows that in detail.

Indian and International entities go through the same flows 1 to 4. What differs per entity is held as data: its mailbox, its recipients, its spreadsheet format, its output columns and its time zone. Flow 5 exists only for the International entity.

## What starts each step

| Step | Today | On this page |
|---|---|---|
| Mail download | UiPath trigger: Indian 10:00, International 05:45 Australian time | Flow 2, on two schedules |
| Extraction | Indian: a SPOC runs it by hand from UiPath Assistant. International: UiPath trigger at 05:49 | Flows 3 and 4: Indian by Run now, International flow 4 on a schedule |
| Bank check | UiPath trigger: Indian 11:00 and 11:30, International 07:00 Australian time | Flow 1, on schedules at the same times |
| International Bot 1 | UiPath trigger at 07:55, through `run_SXP_bot.bat` | Flow 5, on a schedule |
| International Bot 3, Indian run | A person, from the web page | Pink boxes, outside Tracto |

The time zone of the Indian times and the days of the week are still to be confirmed. The full table, with what is still open, is in [the SIS README](../README.md#the-daily-chain-what-starts-each-step).

## The boxes, left to right

| Box | What it is |
|---|---|
| **Entity mailboxes** | The mailboxes customers send their payment advices to, one per entity. Some are Microsoft 365, some are on the in-house server `mail.sisindia.com`. |
| **Flow 2 · Mail dispatch** | Reads every entity mailbox, saves each attachment into a dated folder by entity and file type, and creates one work item per file. When a file cannot be used, it replies to the sender. Started by its main job `maildispatch-main`, Indian at 10:00, International at 05:45 Australian time. Detail: [05-flow2-mail-dispatch.md](05-flow2-mail-dispatch.md). |
| Line "unread mail" | Flow 2 reads the new mail from the mailboxes. |
| Line "replies to the sender" | Flow 2 answers a sender whose file could not be used (broken archive, unsupported file type). |
| **Dated folders per entity** | What flow 2 leaves behind: for each day and entity, a `PDF_JPEG` folder, an `XLS` folder, a `MailBody` folder and the `PA_` spreadsheet files, plus a work item for each file. |
| **Flow 3 · Spreadsheet advices** | Picks up the spreadsheet advices, checks each one against that entity's agreed layout, cleans the values and writes the day's hand-off workbook. DTSS sends no spreadsheets, so it is skipped. Main job `excelextract-main`. Detail: [06-flow3-spreadsheet-advices.md](06-flow3-spreadsheet-advices.md). |
| **Flow 4 · PDF and image advices** | Picks up the PDF and photo advices one work item at a time, sends each to the Extraction API to be read, checks the result and writes the day's hand-off workbook. For the International entity it runs at 05:49 Australian time. Main job `padvice-main`. Detail: [07-flow4-pdf-advices.md](07-flow4-pdf-advices.md). |
| **Extraction API** | The existing extraction service. Flow 4 sends it one document and gets back the payment fields and invoice lines, read by an LLM. |
| **Hand-off workbooks** | The Excel files flows 3 and 4 write: one per entity per day, in fixed folders under `ExtractedPaymentAdvices\Excel\...` and `ExtractedPaymentAdvices\PDF_JPEG\...`. Each is written once, so a rerun cannot add the same rows twice. These files are the only thing collection accounting reads from Tracto. |
| **Indian entities** (pink) | For the Indian entities, a person starts the collection accounting run by clicking **Start Process** on the existing web page `ca_with_pa`. This stays exactly as it is today and is outside Tracto. |
| **Flow 5 · International run (SXP)** | For the International entity, Tracto starts the collection accounting run itself at 07:55 Australian time, after flow 4 has written that day's workbook. It then reads two result numbers and mails the outcome. Main job `carun-main`. Detail: [08-flow5-international-run.md](08-flow5-international-run.md). |
| **Result mail** | The one mail flow 5 sends: the reports attached when it worked, or a message saying the Oracle side was down, no bank statement came, or the run failed. |
| **Collection accounting, Indian copy** | The existing Python program `bot_run.py` with its MySQL database `collection_accounting`. It loads the hand-off workbooks, downloads data from Oracle Fusion, creates receipts, matches payments and posts them. Not changed. |
| **Collection accounting, International Bot 1** | The International copy of the same program, started through the batch file `run_SXP_bot.bat`, with its own database `ca_with_pa_sxp`. Not changed. |
| Lines "loaded by bot_run.py" | When collection accounting runs, it reads the day's hand-off workbooks. This is why the extraction flows must finish before it starts. |
| **Bot 3** (pink) | A second International copy of the Python code that matches, posts and writes the reconciliation reports. It uses the same database as Bot 1. A person starts it from the same web page, through `run-bot-sxp.php`. This stays as it is, outside Tracto. |
| **Oracle Fusion** | The finance system. Collection accounting downloads the bank statement and open invoices from it and posts receipts to it, through the middleware Magic xpi. |

## Flow 1, at the bottom

| Box | What it is |
|---|---|
| **Flow 1 · Bank statement availability** | Indian at 11:00 and 11:30, International at 07:00 Australian time, it checks that today's bank statement folder exists for each entity, Indian and International. Main job `bankcheck-main`. Detail: [04-flow1-bank-check.md](04-flow1-bank-check.md). |
| **Bank statement folders** | One folder per day and entity under `Statements1\BankStatements\`. If today's folder is there, that entity's statements have arrived. |
| **Fetch bots** | The existing Python downloads: `run_Bank_bot.bat` for the Indian entities, `run_Bank_bot_{entity}.bat` for each International entity. Flow 1 starts the entity's fetch bot when its folder is missing; it downloads the statements into the folder. |
| **MT940 status mails** | Flow 1's two mails: the list of entities whose statements are available, and a table of the bank accounts still missing. |

## The note and the legend

The note on the page repeats the rule every flow follows. The legend lists the colours; they are explained in [README.md](README.md).
