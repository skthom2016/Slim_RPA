# Extraction API: what exists and what to add

What the existing extraction API does today, what it does not do, and what has to be added to cover every kind of attachment the UiPath flow handles.

Service: `apexcos-poc/backend`, a Go service with a Python document splitter, `POST /api/extract`. Read on the branch `big-file-split-pdf`, the newest line of work.

## What exists today

| Capability | What it does |
|---|---|
| Endpoint | `POST /api/extract` takes a multipart request with `prompt_id`, `pdf`, and optionally `file_path` and `date_format`. `GET /healthz` is a health check. |
| Input | **PDF only.** A file that does not start with `%PDF-` is rejected as `INVALID_PDF`. |
| Splitting | A Python splitter finds the separate documents inside one PDF and runs OCR (Tesseract) on scanned pages. Each document is extracted on its own. |
| LLM extraction | The prompt named by `prompt_id` is fetched and sent with the PDF to Claude (Anthropic) or GPT (OpenAI), as a PDF document. The reply is parsed into JSON (`summary`, `detail`, `meta`) with token usage. |
| Cross-check | Optional, off by default (`REVIEW_MODE`: `off`, `double` or `judge`). A second reading marks each field green, amber or red, and a red field returns no value. |
| Merging | Results with the same client name and invoice number merge into one. A file holding separate documents returns an `invoices` array. |
| Operation | Synchronous by default. An asynchronous mode returns a `request_id` and posts the result to a callback URL; it needs a storage folder, the callback URL and Dataverse settings. |
| Errors | 400 for a bad request, 422 for a rejected file (`UNREADABLE_PDF`, `ENCRYPTED_PDF`, `PDF_TOO_LARGE`), 502, 503 and 504 for model or splitter failures. |
| Access | No authentication on `/api/extract`, permissive CORS, port 8080. |

## What it does not do today

- **Password-protected PDFs.** They are rejected as `ENCRYPTED_PDF`. Nothing accepts a password or unlocks the file.
- **Images.** A JPEG or PNG is rejected at the entry check, and both LLM clients send documents only as `application/pdf`.
- **Any other file type.** Excel, Word, text and mail bodies are not accepted.
- **Payment advices.** The shipped prompts, the fields the cross-check compares and the merge rules are all written for utility invoices.

## What the UiPath flow needs, type by type

| Attachment | What UiPath does today | In Tracto | Change to the extraction API |
|---|---|---|---|
| PDF | Unlock, read with UiPath OCR and its trained extractor, apply per-customer rules | The API reads it with an LLM; Tracto applies the rules, validation and workbook | A payment-advice prompt and the adjustments below |
| Password-protected PDF | Tries about fifty candidate passwords until one opens it | Tracto holds the passwords | **Password handling** (below) |
| JPG, JPEG | Goes down the same path as a PDF | Same as a PDF | **Image input** (below) |
| XLS, XLSX (not DTSS) | Validated against the standard eleven-column layout and mapped by rules, with no machine learning; a file in another layout is rejected with a mail | Tracto's spreadsheet flow, by rules | None |
| ZIP | Extracted, then each inner file handled by its own type | Tracto unzips and routes each file | None |
| Any other type | The sender is told it is unsupported | Tracto replies the same way | None |
| Mail body with no attachment | Handled by a process outside the four | Out of scope | None |

**Excel does not go to the LLM.** The standard layout is handled by rules, which is exact and costs nothing per file. If a customer later sends a non-standard layout, a sheet could be converted to text and sent through the API's text path (already used by the cross-check), but nothing in the UiPath flow requires that.

## Password-protected PDFs

**Do not pass the password to the LLM. Unlock the file first and send the unlocked copy.**

- The splitter opens the file before any LLM is called and rejects an encrypted one, so the model would never be reached.
- The provider reads the PDF itself. A password written into the prompt does not unlock it, and it would send a secret to a third party.
- UiPath works through about fifty candidate passwords until one opens the file. Only an attempt to open the file can do that.

**What to add.** The API accepts an optional password with the request and uses it to unlock the file before splitting and before the LLM call.
- A locked file sent without a password keeps returning `ENCRYPTED_PDF`.
- A wrong password returns a different code, so the caller can try the next candidate. A failed attempt is cheap, because it fails before any LLM call.
- The password is never logged or stored, and the unlocked copy is not kept after the request.
- The splitter's PDF library supports opening a document with a password.

**Where the password list lives** is a decision. Tracto can hold it and send one password at a time, which keeps the passwords in one place, or the API can hold it and try them itself. If the API cannot take a password, Tracto can unlock the PDF before sending it.

## Images

LLMs can read images, but the API only accepts and sends PDFs. Two ways to add them:

1. **Convert the image to a PDF at intake (recommended).** Splitting, OCR and the LLM call stay exactly as they are, and there is one path for everything.
2. **Send the image to the LLM as an image input.** This changes both model clients.

Either way the type is detected from the file's content and not its name, other types are rejected with a clear code, and size and resolution limits apply. UiPath handles JPG and JPEG only; PNG or TIFF would be new.

## Other additions for payment advices

1. **A payment-advice prompt and field schema:** ten header fields (`advice_number`, `advice_date`, `value_date`, `customer_name`, `beneficiaryName`, `beneficiary_account_number`, `beneficiary_bank_name`, `beneficiary_IFSC_code`, `UTR_number`, `total_credited_amount`), an invoice table of ten columns (`invoice_number`, `invoice_date`, `invoice_amount`, `GST_tax`, `retention`, `other_deduction`, `net_amount`, `GST_TDS`, `wct_tds`, `GSTtax_amount`) and a one-line or two-line marker. The UiPath table has no `TDS` column although the collection accounting loader reads one, so decide whether the LLM should now return it.
2. **Cross-check fields that can be configured.** Today it compares only `Service StartDate`, `Service EndDate`, `Meter Number` and `TotalConsumption`. Advices need their own list, and a list of required fields whose absence forces a red.
3. **Identity and merging for advices.** An advice is identified by its advice number or UTR together with the customer; a line by its invoice number within the advice. A PDF bundling several advices returns one result per advice and never merges them.
4. **Utility-specific processing switched off.** The consumption-month calculation and the date reformatting do not apply to advices.
5. **Access and operation.** An API key for callers, a decision on synchronous or callback mode, capacity and timeout settings sized for about 45 documents on a working day, and a stated retention for stored uploads and outputs.

## What Tracto does itself

Excel advices, unzipping, replies for unsupported files, per-customer rules (negatives in parentheses, two-line invoices, credit notes), validation of every reply, the advice workbook, the exceptions record and the notification mails.

## Decisions needed

1. Where the PDF password list lives.
2. How images are added: converted to PDF, or sent as image inputs.
3. Synchronous mode or callback.
4. Who writes the payment-advice prompt, and how a change to it is versioned.
5. When the updates will be available, since the PDF and image advice flow depends on them.
