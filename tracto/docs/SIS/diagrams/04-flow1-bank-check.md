# Page 4: Flow 1 · Bank statement availability (`bankcheck-main`)

**Purpose.** Every working morning, before the bank statement processing starts, someone needs to know whether today's bank statements have arrived for every entity. This flow checks each entity, tries to download what is missing, and mails the result: which entities are fine, and which bank accounts still have no statement.

**Reference:** replaces the UiPath process `BankAvailCheck`.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, bank check start point | The whole loop below | To build |
| Sheet API | Reading `MasterConfig.xlsx` and `BankMaster.xlsx`, writing cell `A2`, reading the check workbook | Yes |
| Go file API | Checking whether a statement folder exists | To build |
| Go program runner | Starting the existing download `run_Bank_bot.bat` and waiting for it | To build |
| Go mail API | Sending the two mails | To build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | The schedule starts the flow on weekday mornings, before statement processing. A person can also press Run now. | Orchestrator |
| **Desktop runtime runs bankcheck-main** (purple) | The main job makes one call to the Go API to start the bank check. The Go API answers "accepted" at once and the job ends. | Desktop runtime |
| **Read the settings and the entity table** | The Go API reads the list of entities, the folder where statements are kept, and the mail recipients. Today's date is taken in the schedule's time zone, so the folder name is right even if the machine clock is in another zone. | Go API, through the **Sheet API** (lavender box on the left: `MasterConfig.xlsx`, `BankMaster.xlsx`) |
| **For each entity** (dark blue) | The start of the loop. Each entity gets its own result, so a problem with one does not stop the rest. | Go API |
| **Today's statement folder for the entity exists?** (diamond) | Is there a folder `BankStatements\{today}\{entity}`? If yes, that entity's statements have arrived. | Go API, through the **Go file API** |
| → yes: **Mark the entity available** | The entity goes on the "available" list. | Go API |
| → no: **Write the entity into BankMaster.xlsx, EntitySelected!A2** | The existing download program reads the entity name from this cell, so the Go API writes it there first. | Go API, through the **Sheet API** |
| **Run run_Bank_bot.bat and wait** | The Go API starts the existing Python download for that entity and waits, with a time limit. It keeps the exit code and the output, so a failure can be explained. | Go API, through the **Go program runner** |
| **Folder exists now?** (diamond) | After the download, check the folder again. If yes, the entity is marked available (the line goes left to "Mark the entity available"). | Go API, through the **Go file API** |
| → no: **Read the check workbook and list accounts with AVAILABLE = N** (red) | The download leaves a check workbook saying, account by account, whether a statement came. The Go API compares it with the entity's list of accounts in `BankMaster.xlsx` and notes each account still missing. If the check workbook itself is missing, only this entity is marked failed. | Go API, through the **Sheet API** |
| **More entities?** (diamond) | Yes: back up to "For each entity" (line on the far left). No: continue. | Go API |
| **Build the HTML table of unavailable accounts** | All missing accounts of all entities, duplicates removed, as a table for the mail. | Go API |
| **Send the availability mail** (orange) | The list of entities whose statements are available goes to the availability recipients. | Go API, through the **Go mail API** (Microsoft Graph) |
| **Any unavailable accounts?** (diamond) | If none, the flow ends. | Go API |
| → yes: **Send the unavailable-accounts mail** (orange) | The table of missing accounts goes to each entity's own recipients (today all of them go to the DTSS list; that is corrected). | Go API, through the **Go mail API** |
| **Go API records the batch result** (grey) | The result of each entity is stored and shown in the console. | Go API |

## The note on the page

- The desktop runtime only starts the flow. Everything else is done by the Go API; no screen is used.
- One failing entity does not stop the others.
- The download is a console program, so the Go program runner can start it directly and stop it if it hangs.
- **Open:** the batch file `run_Bank_bot.bat` has not been supplied, and what reads cell `A2` is not known. Whether there is also an International bank check (the workbook `BankMasterInternational.xlsx` suggests so) is not known.
