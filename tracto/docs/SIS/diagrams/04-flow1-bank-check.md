# Page 4: Flow 1 · Bank statement availability (`bankcheck-main`)

**Purpose.** Every working morning, before the bank statement processing starts, someone needs to know whether today's bank statements have arrived for every entity. This flow checks each entity, tries to download what is missing, and mails the result: which entities are fine, and which bank accounts still have no statement. It covers the Indian entities and the International ones (MSS-Security and Southern-Cross-Protection) in one run.

**Reference:** replaces the UiPath processes `BankAvailCheck` (Indian) and `InternationalBankAvailCheck` (International).

## Indian and International entities

Both groups go through the same boxes. What differs is held on each entity's row of the entity table:

| Setting | Indian entities | International entities |
|---|---|---|
| Statement folder root | `C:\Automation\Data\Input\Statements1\BankStatements` | `C:\xampp\htdocs\Statements1\BankStatements` |
| Bank workbook (accounts per entity, cell `EntitySelected!A2`) | `BankMaster.xlsx` | `BankMasterInternational.xlsx` |
| Fetch bot | `run_Bank_bot.bat`, one for all entities | `run_Bank_bot_{entity}.bat`, one per entity |
| Retry when still missing | None: tried once | Every five minutes, up to a limit still to be agreed |
| Recipients | Each entity's own | Each entity's own (`_receiverID`, `_bank_issue_receiverID`, `_receiverID_CC`) |

A fetch bot is an existing Python program, a copy of the collection accounting code that only downloads; the Indian one runs `C:\xampp\htdocs\prod\collectionaccountingBankCheck\bot_run.py`. It reads the entity from cell `A2` of the bank workbook, asks Oracle Fusion which accounts have a statement, writes that answer to a check workbook, and downloads the statement into the entity's folder only if every account has one. It always ends with exit code 0, even after an error, so the folder check after it is the real result.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, bank check start point | The whole loop below, including holding an entity for a retry | To build |
| Sheet API | Reading `MasterConfig.xlsx`, `BankMaster.xlsx` and `BankMasterInternational.xlsx`, writing cell `A2`, reading the check workbook | Yes |
| Go file API | Checking whether a statement folder exists | To build |
| Go program runner | Starting the existing fetch bots and waiting for them | To build |
| Go mail API | Sending the mails | To build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | Indian entities at 11:00 and 11:30, International at 07:00 Australian time, as in UiPath today. A person can also press Run now. | Orchestrator |
| **Desktop runtime runs bankcheck-main** (purple) | The main job makes one call to the Go API to start the bank check. The Go API answers "accepted" at once and the job ends. | Desktop runtime |
| **Read the settings and the entity table** | The Go API reads the list of entities (Indian and International rows), and for each one its statement folder root, bank workbook, fetch bot, recipients and retry setting. Today's date is taken in the schedule's time zone, so the folder name is right even if the machine clock is in another zone. | Go API, through the **Sheet API** (lavender box on the left: `MasterConfig.xlsx`, `BankMaster.xlsx`, `BankMasterInternational.xlsx`) |
| **For each entity** (dark blue) | The start of the loop. Each entity gets its own result, so a problem with one does not stop the rest. | Go API |
| **Today's statement folder for the entity exists?** (diamond) | Is there a folder `{root}\{today}\{entity}`? If yes, that entity's statements have arrived. | Go API, through the **Go file API** |
| → yes: **Mark the entity available** | The entity goes on the "available" list. | Go API |
| → no: **Write the entity into its bank workbook, EntitySelected!A2** | The fetch bot reads the entity name from this cell, so the Go API writes it there first. | Go API, through the **Sheet API** |
| **Run the entity's fetch bot and wait** | The Go API starts the entity's fetch bot (Indian `run_Bank_bot.bat`, International `run_Bank_bot_{entity}.bat`) and waits, with a time limit. It keeps the exit code and the output, so a failure can be explained. | Go API, through the **Go program runner** |
| **Folder exists now?** (diamond) | After the download, check the folder again. If yes, the entity is marked available (the line goes left to "Mark the entity available"). | Go API, through the **Go file API** |
| → no: **Read the check workbook and list accounts with AVAILABLE = N** (red) | The fetch bot leaves a check workbook, `{entity}_check\bank_statements_check.xlsx`, saying account by account whether a statement came. The Go API compares it with the entity's list of accounts in its bank workbook and notes each account still missing. If the check workbook itself is missing, this try of this entity is marked failed. | Go API, through the **Sheet API** |
| **Tries left for this entity?** (diamond) | Only entities with a retry setting (the International ones, every five minutes) get another try, up to their limit. | Go API |
| → yes: **Hold the entity; try again after the interval** | The entity waits; the other entities go on meanwhile. When the interval is over, the line on the right goes back up to "Write the entity into its bank workbook" and the fetch runs again. | Go API |
| → no: **unavailable** | The line goes left to "More entities?": the entity's missing accounts stay on the list for the mail. | Go API |
| **More entities?** (diamond) | Yes: back up to "For each entity" (line on the far left). No: continue. | Go API |
| **Build the HTML table of unavailable accounts** | All missing accounts of all entities, duplicates removed, as a table for the mail. | Go API |
| **Send the availability mail** (orange) | The list of entities whose statements are available goes to the availability recipients. | Go API, through the **Go mail API** (Microsoft Graph) |
| **Any unavailable accounts?** (diamond) | If none, the flow ends. | Go API |
| → yes: **Send the unavailable-accounts mail** (orange) | The table of missing accounts goes to each entity's own recipients, once per run, after the last try. Today the Indian lists all go to the DTSS recipients, and the International check mails a sentence without the accounts every five minutes; both are corrected. | Go API, through the **Go mail API** |
| **Go API records the batch result** (grey) | The result of each entity is stored and shown in the console. Flow 5 can read whether Southern-Cross-Protection's statement arrived; today the International check signals it with an item in the UiPath queue `Southern-Cross-Protection_BankAvail_Success_Queue`. | Go API |

## The note on the page

- The desktop runtime only starts the flow. Everything else is done by the Go API; no screen is used.
- One failing entity does not stop the others.
- Each entity row says what differs: statement root, bank workbook, fetch bot, recipients and retry.
- The fetch bots are console programs that always end with exit code 0, so the folder check after them is the result.
- All three fetch bots, Indian included, read their date window from `Bot_run_Configurations.xlsx`, a calendar with a different rule from the one flow 5 uses.
- **Open:** which calendar rule is right, and whether the note in row 1 of the supplied calendar is in the live file. For the International entities: how many tries, what reads the success queue today, and whether MSS-Security is to be checked at all.
