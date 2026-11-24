# Page 7: Flow 4 · PDF and image advices (`padvice-main`)

**Purpose.** Most payment advices arrive as PDFs or photos. This flow takes each one that flow 2 filed, has it read by the Extraction API (an LLM), applies the customer-specific rules, checks the result, and writes the day's hand-off workbook. An advice that fails a check is not written; it is recorded and reported so a person can deal with it.

**Reference:** replaces the UiPath process `Payment.Advice.Extraction`.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, PDF advice start point | The whole loop below, including the rules and checks | To build |
| Go work-item API | Taking the next pending document, holding it for a retry, marking it done or failed | To build |
| Extraction API | Reading each document | Exists; unlocking password-protected PDFs and reading images are with the extraction module team |
| Entity profiles, customer rules, keyword list | Which columns each entity's workbook has (Indian 24, International 15); special handling per customer; keywords that identify a customer | To build (sheets) |
| Sheet API | Reading the profiles and rules; writing the day workbook | Reading exists; creating a workbook and appending rows are to build |
| Go file API | Moving the PDF onward; copying a held document to `NeedValidation` | To build |
| Go mail API | The summary mails | To build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | For the International entity at 05:49 Australian time. For the Indian entities a SPOC starts it by hand today, so in Tracto the SPOC presses Run now. | Orchestrator |
| **Desktop runtime runs padvice-main** (purple) | One call to the Go API; the job ends at once. | Desktop runtime |
| **Load the entity profiles, customer rules and keyword list** | The layout each entity's workbook needs, the rules for particular customers, and the keyword list used to recognise customer names. | Go API, through the **Sheet API** |
| **Claim the next pending PDF / JPEG work item** (dark blue) | Start of the loop: take the next document flow 2 filed, one at a time. "Claim" means no other run can take the same document. | Go API, through the **Go work-item API** (box on the right) |
| **Password-protected?** (diamond) | Some PDFs are locked with a password. | Go API |
| → yes: **Unlock with the password list** | The PDF is opened with the customer's password. If it cannot be opened, the document becomes an exception and is not sent for reading. The line comes back to the next box. | Go API |
| **Read the document** | The document is sent to the extraction service, which returns the header (payer, date, total, bank reference) and the invoice lines, with a colour per field: green when two readings agree, amber when they agree but one is unsure, red when they disagree. | Go API, through the **Extraction API** (box on the left, line both ways) |
| **Busy or timed out?** (diamond) | The extraction service may be overloaded. | Go API |
| → yes: **Hold the item and retry later** (red) | The document is not failed: it is put back as pending and tried again later, and the attempt is counted. The line goes to "Record the item outcome". | Go API, through the **Go work-item API** |
| → no: **Apply the customer rules; match the customer name** | Customer-specific corrections (for example a European number format), and the customer name looked up by keyword. | Go API (in memory) |
| **Normalise dates, amounts, IDs; derive the net amount** | Every date, amount and reference is put into one standard form, and the net amount is worked out. | Go API (in memory) |
| **Passes the checks?** (diamond) | Are the required fields present, is no field red, and do the invoice lines add up to the total? | Go API |
| → no: **Not written to the workbook: exception record** (red) | The advice is held back. Its reason, the raw reply and the PDF's location are recorded, and the PDF is copied to a `NeedValidation` folder for a person. | Go API, through the **Go file API** |
| → yes: **Keep the advice rows; file the PDF to ExtractedPDF** | The rows are kept for the day workbook and the PDF is moved to the done folder. | Go API, through the **Go file API** |
| **Record the item outcome: Done, Held or Exception** | The work item gets its final status. All three paths (done, held, exception) meet here. | Go API, through the **Go work-item API** |
| **More pending items?** (diamond) | Yes: back to "Claim the next pending item". No: continue. | Go API |
| **Write each entity's day workbook once** | All accepted advices of the day are written into `PDF_JPEG\ML_ExtractedFiles\{date}\{entity}\Extracted payment advices.xlsx`. Credit notes are kept off the first sheet, which is the one collection accounting reads. Written once, so a rerun cannot add rows twice. | Go API, through the **Sheet API** |
| **Summary mails** (orange) | To the operations team: documents that could not be read, advices missing information, credit notes found, and the list of documents in `NeedValidation`. | Go API, through the **Go mail API** |
| **Go API records the batch result** (grey) | The run ends. | Go API |

## The note on the page

- Each document is one step of the Go API loop. There is no separate job per document.
- Special handling for particular customers is rows in the rules table, not separate code.
- There is no review screen: an advice that fails a check is held back and reported, never written to the workbook with doubtful figures.
- A busy or slow extraction service holds the document for later; it is not failed.
- **Open:** where the PDF password list is and who owns it, and what to do with the 20 locked International PDFs; the confidence level below which a document goes for review; how a reviewed document is released.
