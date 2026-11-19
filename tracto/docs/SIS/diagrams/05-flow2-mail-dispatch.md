# Page 5: Flow 2 · Mail dispatch (`maildispatch-main`)

**Purpose.** Customers mail their payment advices to each entity's mailbox: as PDFs, photos, spreadsheets, ZIP files, or as the text of the mail. This flow opens every mailbox, works out which customer sent each mail, files every attachment into a dated folder by entity and file type, and creates one work item per file so flows 3 and 4 can process it. When a file cannot be used, it writes back to the sender.

**Reference:** replaces the UiPath process `EmailPADispatcher`.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, mail dispatch start point | The whole loop below | To build |
| Go mail API | Fetching unread mail, saving attachments and `.eml` files, replying to senders | To build (the largest new piece) |
| Go file API | Creating the dated folders, unzipping archives, moving and renaming files | To build |
| Go work-item API | One work item per filed file | To build |
| Sheet API | Reading the entity table and the customer list; appending to the Email Tracker | Reading exists; appending rows is to build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | Every 15 minutes as proposed; the International mailbox at 05:45 Australian time. Or Run now. | Orchestrator |
| **Desktop runtime runs maildispatch-main** (purple) | One call to the Go API to start the mail dispatch; the job ends at once. | Desktop runtime |
| **Read the entity table** | For each entity: its mailbox, which file types it accepts, and its recipients. | Go API, through the **Sheet API** (`MasterConfig.xlsx`) |
| **For each entity** (dark blue) | Start of the outer loop. | Go API |
| **Fetch unread mail not yet processed** | Gets the new mail of this entity's mailbox, with body text and the list of attachments. A mail already handled in an earlier run is never fetched again, even if someone marked it unread. | Go API, through the **Go mail API** (Microsoft Graph for Microsoft 365, IMAP for `mail.sisindia.com`) |
| **Create {date}\{entity}\PDF_JPEG, XLS, MailBody** | Makes today's folders for the entity, if they are not there yet. | Go API, through the **Go file API** |
| **Read the customer list** | The list of customers with the keywords that identify them in a mail. | Go API, through the **Sheet API** (`Customers List.xlsx`) |
| **Pre-download** | Mails whose first attachment name or subject contains `PA_{entity}` are spreadsheet advices in the agreed layout. All their attachments are saved straight to `{date}\{entity}\`, where flow 3 looks for them. | Go API, through the **Go mail API** |
| **For each mail** (dark blue) | Start of the inner loop. | Go API |
| **Identify the customer** | The mail body is searched for each customer's keywords. The first customer that matches is the sender; if none matches, the customer is "Others". The keyword that matched is recorded. | Go API (in memory) |
| **Has attachments?** (diamond) | Decides how the mail is handled. | Go API |
| → no: **Save the mail as .eml in MailBody; one work item** | A mail without attachment may hold the advice in its text. The whole mail is saved as a file and a work item is created for it. | Go API, through the **Go mail API** and **Go work-item API** |
| → yes: **Save attachments; route by type** | Each attachment is saved and put in the right folder by its file type: PDF and photos to `PDF_JPEG`; spreadsheets to `XLS` (not for DTSS, which does not accept them); ZIP files are unzipped and each file inside is routed the same way. | Go API, through the **Go mail API** (save) and **Go file API** (unzip, move) |
| **File handled?** (diamond) | Did the file end up in a folder? | Go API |
| → no: **Reply to the sender only** (red) | The archive would not open, the file was missing after saving, or the file type is not processed. A reply goes to the sender only (not to everyone in copy), asking for the file again. The file's outcome is Failed. | Go API, through the **Go mail API** |
| → yes: **Rename; one work item per file** | The file gets the standard name `{customer}_{dd MMM yyyy hh mm ss}_{n}{ext}`, and a work item is added: file, entity, customer, sender, date, path, status Pending. | Go API, through the **Go file API** (rename) and **Go work-item API** (add) |
| **Record the outcome for the Email Tracker** | Every file and every mail, handled or failed, gets a line for the tracker. | Go API |
| **More mails?** (diamond) | Yes: back to "For each mail" (inner line on the left). No: continue. | Go API |
| **Append the Email Tracker; record the entity's outcome and mail count** | The lines are added to the bottom of the `Email Tracker` sheet, and the entity's result is stored. | Go API, through the **Sheet API** (`LogTracker.xlsx`) |
| **More entities?** (diamond) | Yes: back to "For each entity" (outer line on the far left). No: continue. | Go API |
| **Go API records the batch result** (grey) | The run ends. The work items now wait for flows 3 and 4. | Go API |

## The note on the page

- Only mail, files and spreadsheets: no screen is used.
- A ZIP file is deleted only after every file inside it has an outcome.
- A mail is recorded as processed only after its work items exist, so a rerun never repeats or loses a mail.
- Mailbox passwords never appear in a log.
- **Open:** which mailboxes exist and whether each is Microsoft 365 or the in-house server, and whether the Microsoft 365 administrator grants the mail permissions; which spreadsheets flow 3 should read (the `XLS` folder or the `PA_` copies); whether work items live in a SQLite table (the working default) or a sheet.
