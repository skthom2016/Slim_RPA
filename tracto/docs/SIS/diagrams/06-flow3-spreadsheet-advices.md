# Page 6: Flow 3 · Spreadsheet advices (`excelextract-main`)

**Purpose.** Some customers send their payment advice as a spreadsheet in an agreed layout. This flow checks each spreadsheet against the layout agreed for that entity, cleans the values, maps the columns onto the standard columns and writes the day's hand-off workbook that collection accounting reads.

**Reference:** replaces the UiPath process `India.Excel.Extraction`.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, spreadsheet start point | The whole loop below, including the cleaning and mapping | To build |
| Format profiles | One row per entity describing its expected sheet name, columns and how each maps to the standard column. Replaces one copied routine per entity | To build (a sheet) |
| Sheet API | Reading the profiles and each spreadsheet; writing the day workbook | Reading exists; reading sheet names, creating a workbook and appending rows are to build |
| Go file API | Listing today's files; copying each source to the archive folder | To build |
| Go mail API | The rejection mail and the report mail | To build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | After flow 2, or Run now. | Orchestrator |
| **Desktop runtime runs excelextract-main** (purple) | One call to the Go API; the job ends at once. | Desktop runtime |
| **Read the entity table and the format profiles** | Which entities send spreadsheets, and for each one the sheet name and columns to expect and how to map them. | Go API, through the **Sheet API** (`MasterConfig.xlsx`, format profiles) |
| **For each entity (DTSS skipped)** (dark blue) | Outer loop. DTSS does not accept spreadsheets. | Go API |
| **List today's spreadsheet advices** | Finds the spreadsheets flow 2 filed today for this entity. | Go API, through the **Go file API** (`Payment Advices\{date}\{entity}`) |
| **For each file (own status)** (dark blue) | Inner loop. Each file has its own result, so one bad file does not stop the rest. | Go API |
| **Read the sheet names and the header row** | Opens the spreadsheet and reads which sheets it has and the column headings. | Go API, through the **Sheet API** |
| **Sheet and columns match the profile?** (diamond) | Is the expected sheet there, with the expected columns? | Go API |
| → no: **Mail the format rejection to the sender** (red) | The sender is told the file is not in the agreed layout. The file is marked Failed with the reason. The line comes back to "More files?". | Go API, through the **Go mail API** |
| → yes: **Clean and map** | A cell that is only `-` becomes blank; an empty TDS or Other Deductions becomes 0; repeated spaces are removed. Dates, amounts and IDs are put into one standard form. Each column is mapped onto the standard column. | Go API (in memory) |
| **Keep the rows; copy the source to the archive; file Done** | The rows are kept for the day workbook. Rows that cannot be converted are listed, not dropped silently. The original file is copied to the archive folder. | Go API, through the **Go file API** (`Extracted Standard Excel Advices`) |
| **More files?** (diamond) | Yes: back to "For each file". No: continue. | Go API |
| **Write the day workbook once** | All the entity's rows of the day are written in one go into `Excel\Extractor_ExtractedFiles\{date}\{entity}\Extracted Payment Advices.xlsx`, with a timestamped copy. Written once, with a duplicate check, so a rerun cannot add the same rows twice. | Go API, through the **Sheet API** |
| **More entities?** (diamond) | Yes: back to "For each entity". No: continue. | Go API |
| **Report mail** (orange) | How many files were done, rejected, and which rows could not be converted. | Go API, through the **Go mail API** |
| **Go API records the batch result** (grey) | The run ends. | Go API |

## The note on the page

- One profile row per entity: adding an entity means adding a row, not new code.
- A workbook in the wrong layout fails on its own; the other files still run.
- The day workbook is written once per entity.
- **Open:** the spreadsheet layouts of the entities other than DTSS; whether to read the `XLS` folder or the `PA_` copies; which cleaning rules are wanted (only three are active today).
