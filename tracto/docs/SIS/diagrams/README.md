# SIS flows on Tracto: diagram guide

`sis-flows-on-tracto.drawio` holds eight pages. Each page has its own guide here, which explains every box on the page in plain words. Open the diagram in draw.io (File → Open from → Device), pick the page from the tabs at the bottom, and read the matching guide next to it.

| Diagram page | Guide | What it answers |
|---|---|---|
| 1 Overview | [01-overview.md](01-overview.md) | What the five flows are and how the work passes from one to the next |
| 2 Components and calls | [02-components-and-calls.md](02-components-and-calls.md) | Which pieces of software exist, and which one calls which |
| 3 How a flow runs | [03-how-a-flow-runs.md](03-how-a-flow-runs.md) | What happens, step by step, from the moment a flow is started until someone checks the result |
| 4 Flow 1 Bank check | [04-flow1-bank-check.md](04-flow1-bank-check.md) | Checking every morning that each company's bank statements have arrived |
| 5 Flow 2 Mail dispatch | [05-flow2-mail-dispatch.md](05-flow2-mail-dispatch.md) | Reading the mailboxes and filing every payment advice |
| 6 Flow 3 Spreadsheets | [06-flow3-spreadsheet-advices.md](06-flow3-spreadsheet-advices.md) | Turning spreadsheet payment advices into the standard workbook |
| 7 Flow 4 PDF advices | [07-flow4-pdf-advices.md](07-flow4-pdf-advices.md) | Reading PDF and image payment advices into the standard workbook |
| 8 Flow 5 International run | [08-flow5-international-run.md](08-flow5-international-run.md) | Starting the International collection accounting run and mailing its outcome |

## The words used in the diagram

| Word | Meaning |
|---|---|
| **Payment advice** | A document a customer sends to say what it has paid: the amount, the date, the bank reference and which invoices the payment covers. It arrives by mail as a PDF, a photo, a spreadsheet or the text of the mail. |
| **Entity** | One company of the SIS group (DTSS, SIS, Southern Cross Protection, ...). Each entity has its own mailbox, folders and recipients. "Indian entities" and "International entities" are the two groups; Southern Cross Protection (SXP) is the only International one today. |
| **Collection accounting** | The existing Python program and MySQL database that load the payment advices, download the bank statement and the open invoices from Oracle Fusion, create receipts, match payments to invoices and post them. Tracto does not change it; it only feeds it files and, for the International entity, starts it. |
| **Hand-off workbook** | The Excel file `Extracted payment advices.xlsx` (one per entity per day) that the flows write and collection accounting reads. It is the only connection between the two. |
| **Tracto** | The automation platform these flows run on. Its pieces are listed on page 2 and in [02-components-and-calls.md](02-components-and-calls.md). |
| **Orchestrator** | The Tracto service that holds the schedules, starts jobs at the right time and keeps a record of every run. |
| **Orchestrator UI** | The web console of the Orchestrator, where a person sees schedules, runs and their results, and can press **Run now**. |
| **Desktop runtime** | The Tracto program on a Windows machine that runs jobs. In these flows it only ever does one thing: make one web call to start a flow. |
| **Main job** | The small published job that starts a flow, for example `bankcheck-main`. It has a single step: call the Go API. There is one main job per flow. |
| **Go API** | The new program, written in Go, that does the actual work of every flow: it loops over the companies, files or documents and calls the other APIs. Each flow has its own start point in it. |
| **Sheet API** | The existing Tracto service (sheet-service) that reads and writes Excel workbooks. |
| **Go file API** | New: checks, lists, creates, copies, moves, deletes and unzips files and folders, only inside folders it is allowed to touch. |
| **Go mail API** | New: reads unread mail and saves attachments, sends mail and replies to a sender, through Microsoft 365 (Graph) or the in-house mail server (IMAP / SMTP). |
| **Go program runner** | New: starts an existing program or batch file (such as the Python bank statement download), waits for it with a time limit and keeps its exit code and output. |
| **Go work-item API** | New: a list of work items, one per filed document, each with a status. One flow adds items, another picks them up. |
| **Extraction API** | The existing extraction service that reads one PDF or image with an LLM and returns the fields and invoice lines. |
| **Work item** | One entry in that list: "this file, for this entity and customer, is waiting to be processed", with a status (Pending, Running, Done, Failed, Held) and an attempt count. |
| **Batch** | One run of a flow. A batch has one item per entity, file or document, and each item has its own status, so one failure does not stop the others. |
| **Run now** | The button in the Orchestrator UI that starts a flow straight away instead of waiting for its schedule. |

## Colours and shapes

| Shape | Meaning |
|---|---|
| Green rounded box | The trigger: a schedule or Run now, or something the Orchestrator does |
| Purple box | The main job, run by the desktop runtime |
| Blue box | A step done by the Go API |
| Darker blue box, bold | The start of a loop ("for each entity", "for each file") |
| Lavender box on the left of a flow page | The API that step calls, and the file or system it touches. The dashed line between them says what is done (read, write, send, claim, run) |
| Yellow diamond | A decision. The lines leaving it are labelled yes or no |
| Orange box | A mail is sent |
| Red box | A failure or rejection path |
| Page-shaped box | A folder or a workbook |
| Cylinder | A program with its database |
| Grey square box | A system outside Tracto |
| Pink box | Something a person does |
| Dashed red box | Not known yet: an open question |
| Note (yellow, folded corner) | Points and open questions for that page |
| Grey line under a page title | Which UiPath process the flow replaces, for reference only |

On every flow page, the main path runs straight down the middle. Failure branches go out to the right and come back on their own lines. Loop-back lines ("yes, more entities") run up the far left. The italic purple line at the bottom of a box says which part of Tracto does that step.
