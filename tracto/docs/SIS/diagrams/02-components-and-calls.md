# Page 2: Components and calls

The pieces of software involved, and which one calls which. The top row is what starts a flow. The wide blue box in the middle is the Go API, which does the work. The row of lavender boxes under it are the services the Go API calls, and the bottom row is what each of those services touches.

## Top row: starting a flow

| Box | What it is | Exists today? |
|---|---|---|
| **Operator** (pink) | The person responsible for the flows. Sets up the schedules once, can press Run now, and checks the results. | — |
| **Orchestrator UI** (port 4000) | The web console. Pages for Schedules, Jobs, Logs and, to be added, Work items. | Yes; the Work items page is to build |
| **Orchestrator** (port 5080, SQLite) | Keeps the schedules, fires them at the right time, queues the jobs, stores the published main jobs and keeps a record of every run. | Yes |
| **Desktop runtime** | The program on a Windows machine that runs jobs. It asks the Orchestrator for work, runs the main job and reports the result back. | Yes |
| Line "uses" | The operator works through the web console. |
| Line between UI and Orchestrator | The console shows what the Orchestrator holds and sends it the operator's actions (Run now, new schedule). |
| Line "hands out the job / gets the result" | The desktop runtime picks up the queued main job and, a few seconds later, tells the Orchestrator it succeeded or failed. |
| Line "one HTTP call: start the flow" | The main job's only step. The desktop runtime calls the Go API to start the flow, and that is all it does. It never opens a window or a browser in these flows. |

## Middle: the Go API

| Box | What it is | Exists today? |
|---|---|---|
| **Go API: the SIS flow service** | A program written in Go with one start point per flow: bank check, mail dispatch, spreadsheet advices, PDF advices and International run. When the main job calls it, it answers at once ("accepted", with a batch id, or "busy" if that flow is already running) so the main job can finish. Then it does the flow's work in the background: it loops over the entities, files or documents and keeps a status for each one. Every flow page shows what it does step by step. | To build |
| Lines "calls" | For each step, the Go API calls the service that can do that kind of work. |

## Services the Go API calls, and what they touch

| Service | What it does | Exists today? | What it touches |
|---|---|---|---|
| **Sheet API** (sheet-service, port 5090) | Reads sheets of an Excel workbook, writes single cells, and appends rows at the end of a sheet. | Reading and cell writes exist; appending rows and creating a new workbook are to build | **Workbooks**: `MasterConfig.xlsx`, `BankMaster.xlsx`, the customer list, the trackers and the hand-off workbooks |
| **Go file API** | Checks whether a file or folder exists, lists a folder, creates folders, copies, moves, deletes and unzips. It refuses any path outside the folders it is allowed to use, and refuses an archive that tries to write outside its target. | To build | **Folders**: `E:\Automation\...` and the bank statement folders |
| **Go mail API** | Fetches unread mail (with body and attachment list), saves attachments or the whole mail to a folder, sends a mail (HTML, CC, attachments) and replies to a sender only. Remembers which mails it has processed, so a rerun never handles a mail twice. | To build | **Mailboxes**: Microsoft Graph for Microsoft 365, IMAP and SMTP for `mail.sisindia.com` |
| **Go program runner** | Starts a program named in its configuration (never an arbitrary command), waits for it with a time limit, stops it if it runs too long, and returns its exit code and output. | To build | **Python programs**: `run_Bank_bot.bat` (bank statement download) and `run_SXP_bot.bat` (International collection accounting), which in turn use MySQL and Oracle Fusion |
| **Go work-item API** | Adds a work item, lets a flow claim the next pending one (so two runs never take the same item), marks it done or failed, and counts attempts so an item can be retried. | To build | **Work-item table** in SQLite |
| **Extraction API** (extraction service) | Reads one PDF or image and returns the header fields and invoice lines, with a colour per field showing how sure it is. | Exists; unlocking passwords and reading images are with the extraction module team | **LLM provider**: two readings of each document, compared |

The double-headed lines at the bottom show that each service both reads from and writes to what it touches.

## The note

- **Exists and to build** repeats the "Exists today?" column above.
- Which Go program will hold the Go API and the new services (inside the existing sheet-service or a new program) is still to be decided. The diagram does not depend on that choice.
- **The rule:** no job waits for another job; only the Go API waits (for a program to finish, for the extraction service to answer). The desktop runtime only starts things.
