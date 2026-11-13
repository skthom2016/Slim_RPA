# SIS flow 4: Payment.Advice.Extraction in Tracto

How the UiPath process `Payment Advice Extraction` 1.0.57 is rebuilt on Tracto. This is the flow that reads the PDFs. Tracto itself has no document extraction, but it does not need one: an existing in-house service already takes a document and a prompt, sends it to an LLM and returns the extracted content. This flow calls that service.

## In brief

**What it is for.** Most payment advices arrive as PDFs, and the figures inside them — which invoices were paid, for how much, with what deductions — have to be read out before anything can be matched or posted. This is the flow that reads them.

**What it does.** It takes one PDF at a time from what flow 2 filed, unlocks it if it is password-protected, extracts the header fields and the invoice lines, corrects them with rules written for each customer, and writes them into the same standard workbook flow 3 produces, with separate sheets for errors and credit notes, where the existing collection accounting service loads it. In the UiPath version a person also confirms the extracted figures on screen for every document that matched a known customer; Tracto does not carry that step over.

**What it becomes in Tracto.** The same shape as the others — a schedule, a one-step main job, a Go process — with one difference: the reading of the document is done by the existing in-house extraction service, which splits the file into the documents inside it, sends each one to an LLM with a prompt, and returns the fields — with a colour per field saying how far two readings agreed, once that second reading is switched on. Tracto owns everything around that call and does not own the extraction itself. That service has its own document: [extraction-service.md](extraction-service.md).

**Two things set this flow apart.** The figures it produces go into a finance system, so a wrong number is not a failed run but a wrong payment — which makes the validation of what comes back part of the flow rather than a refinement. And because the on-screen confirmation is not carried over, Tracto's own checks are the only filter between the extraction and the collection accounting service.

Open questions are at the end of this document.

## What the UiPath process does

It takes one payment-advice PDF at a time, reads the figures out of it with a trained machine-learning model, corrects them with per-customer rules, and writes them into the same standard table flow 3 produces.

**Where the work comes from.** The published entry point is `All queue.xaml`, a driver that loops the configured entities, writes the entity name into cell `B2` of a sheet in `MasterConfig.xlsx`, and invokes `Main.xaml` once per entity with a twenty-second gap. `Main.xaml` reads that cell back to know which entity it is working on, and drains the queue `<entity>_PDF_JPEG_Queue`. `Process.xaml` takes five fields off each queue item — `FilePath`, `EntityName`, `PAFileName`, `From`, `TimeDate` — which are exactly the fields flow 2 writes when it files a PDF. So flow 2 produces and flow 4 consumes.

The entity context travelling through one overwritten cell in a shared workbook is the same single-cell handoff flow 1 uses for its fetch bot, and it has the same consequence: two runs against the same configuration file would read each other's entity.

**Per document (`DU Extraction\DU Extract.xaml`), then once per entity:**

1. **Unlock the PDF.** `RemovePDFPassword.xaml` tries each of about fifty passwords in a hard-coded list until one opens the file, treating "not encrypted with a user password" as success.
2. **Digitize.** `Digitize Document` runs OCR over the PDF and produces a document object model and its plain text.
3. **Classify.** `Classify Document Scope` with a keyword classifier decides the document is a payment advice.
4. **Extract.** `Data Extraction Scope` runs a `Machine Learning Extractor` against the UiPath ML Skill **`PaymentAdvicesIndia`**, using the taxonomy in `DocumentProcessing	axonomy.json`, document type `PaymenAdvice`:

   - **Ten header fields:** `advice_number`, `advice_date`, `value_date`, `customer_name`, `beneficiaryName`, `beneficiary_account_number`, `beneficiary_bank_name`, `beneficiary_IFSC_code`, `UTR_number`, `total_credited_amount`.
   - **One `Table` of invoice lines, ten columns:** `invoice_number`, `invoice_date`, `invoice_amount`, `GST_tax`, `retention`, `other_deduction`, `net_amount`, `GST_TDS`, `wct_tds`, `GSTtax_amount`.
   - **One marker,** `OnelineTwoline`, telling the post-processing whether an invoice occupies one line or two.

   Note what is **not** there: the invoice table has no plain `TDS` column, although the output schema has one and flow 3 fills it from the spreadsheet advices. Either TDS is never read off a PDF, or it arrives through one of the GST columns. That has to be settled when the prompt is written — see the open questions.
5. **Show it to a person.** When the document matched a known keyword and a credited amount came out, `Present Validation Station` opens the document with the extracted values for a human to confirm or correct. This is in the production path, not an option: the project is marked `requiresUserInteraction`, so the robot needs an interactive session. Documents with no keyword match or no credited amount skip the screen and are exported straight through.
6. **Export** the results into a dataset with a `Simple Fields` sheet and a `Table` sheet.
7. **Post-process**, once the entity's queue is empty. `Payment advices postprocessing.xaml` (180 KB) branches on what the document's text contains — `"ITC LIMITED"`, `"ITC LIMITED ABD NORMAL"`, `"Lasalle Property Consult"`, `"RBL Bank Limited"`, a Hindustan Unilever marker, `"ROOTS"`, `"HSBC Advising"` — and calls a workflow written for that customer: `ITC.xaml`, `ITC Limited.xaml`, `JLL.xaml`, `RBL Bank Ltd.xaml`, `HindustanUnilever.xaml`. It also handles invoices split across two lines, converts amounts in parentheses to negatives, and carries a special rule for one entity's HSBC advices, where an empty value date is filled from the advice date.
8. **Shape the final workbook.** `Remove Duplicate Invoices.xaml` drops duplicate invoice rows, sends rows with no invoice number or no amount to an `Errors` sheet and rows with negative amounts to a `Credit Note` sheet, and writes the rest to `Output`.
9. **Clean and hand off.** The workbook is copied to a handoff folder, the amount columns are stripped of stray characters and percentage tokens, negatives are normalised, one customer's European decimal format is corrected, and the result is left as a single normalised sheet for the downstream consumer.

Two things about this are worth noting. **Everything from step 7 onwards runs in the state machine's `End Process` state**, after the last queue item — outside the per-transaction error handling and retries, so a failure there loses the whole entity's output for the run with no retry.

And **the notifications do not go out.** `Notifications\Missing Information.xaml` builds three HTML mails — extraction failed, mandatory information missing, credit notes found — but its only invocation is commented out. In this version a failed document is visible only in the exception folder and the daily log workbook, so nobody is told.

There is also a `Train Extractors Scope` with a `Machine Learning Extractor Trainer`, the feedback loop that sends corrected documents back to retrain the model.

## Where extraction comes from

**Three of the four SIS flows move to Tracto by writing Go and configuration. This one adds one call to a service that already exists.**

The extraction itself is not built here and is not a Tracto capability. The in-house document-extraction service takes a document and a prompt, passes both to an LLM (GPT or Claude), and returns the extracted content. The taxonomy the UiPath ML Skill `PaymentAdvicesIndia` was trained on — ten header fields, a ten-column invoice table and a one-line/two-line marker — becomes the schema in the prompt, and the service answers with the same fields and the same invoice-line table.

That removes the whole class of work the UiPath version carries: no OCR step, no classifier, no taxonomy to maintain, no ML Skill to host, no labelled training set and no retraining loop. It also removes most of the reason the per-customer workflows exist, because a format the prompt has not seen before is handled by the same call rather than by a new workflow.

What Tracto owns is everything around that call: the queue of documents, the per-document status, the post-processing rules, the output workbook, the exceptions record, the notification mails, the scheduling and the audit trail. The extraction is one step in the middle of it.

## The shape in Tracto

```
Flow 2 enqueues one item per PDF
   │
   ▼
Schedule ─▶ Main job (one http.post) ──POST /padvice/start──▶ Go service
                ends in seconds                                 │
                                                                │ claim the next pending item, one at a time:
                                                                │   unlock the PDF           (new: pdf support)
                                                                │   call the extraction service with the
                                                                │     payment-advice prompt   (exists, outside Tracto)
                                                                │     ◀── fields + invoice lines as JSON
                                                                │   apply the customer rules (Go, from a rules table)
                                                                │   check required fields    (Go)
                                                                │     failed → exceptions record, collected for the mail
                                                                │   write the advice workbook  (exists: sheet engine)
                                                                │   file the PDF onward      (new: fs API)
                                                                ▼   complete the item          (new: queue)
                                                           send the three summary mails
```

The per-document work is slow — each one goes to a model, sometimes twice — but the volume is modest, about 45 documents on a working day, so taking them one at a time finishes well inside the day. What the queue buys this flow is not parallelism. It is that each document has its own status and attempt count, so `ANALYZER_BUSY` from the extraction service holds an item and brings it back rather than failing it, nothing is lost if the service restarts part-way through, and an advice that fails validation is recorded with its reason.

The workbook it produces is the hand-off to the existing collection accounting service, described in [collection-accounting-handoff.md](collection-accounting-handoff.md): same file name, same folders per entity and day, the advice rows on the first sheet.

## Calling the extraction service

The service's contract is recorded in [extraction-service.md](extraction-service.md); what matters here is what flow 4 does with it.

```
POST /api/extract    multipart: prompt_id, pdf, file_path
   → { "extractor": { "summary": {...}, "detail": [...], "meta": {...} },
       "reviewer": {...}, "overall_color": "green|amber|red|unreviewed",
       "usage": {...} }
```

`summary` is the advice header, `detail` is one row per invoice line, and — when the review step is on — **every one of those rows carries its own review block with a colour per field**. A bundle that held genuinely separate advices comes back in a different shape, with an `invoices` array, so the caller has to handle both. The prompt carries the schema: the ten header fields, the ten invoice-line columns and the one-line/two-line marker listed above.

The call is one step in the Go loop. What belongs around it, on the Tracto side rather than the service's:

- **The prompt id per document type**, held as data rather than written into Go.
- **Unlocking first.** The service refuses an encrypted PDF with `ENCRYPTED_PDF`, so the password step runs before the file is sent.
- **An arithmetic check the models are never asked for:** do the invoice lines sum to the total credited amount, where the advice gives both. One line of Go, and it catches a class of error no amount of model agreement will.
- **The mapping** of the reply onto the twenty-four-column schema, and the status the row takes from the colours.
- **The raw reply kept** against the document, so a figure that turns out wrong can be traced to what the model actually returned.
- **Back-pressure treated as back-pressure.** `ANALYZER_BUSY` (503) and `ANALYZER_TIMEOUT` (504) mean the splitter is saturated, not that the document is bad. The item is held and retried, not failed — which is the clearest argument for documents being batch items with their own status rather than an in-process loop.

**What the colours give us, when they are there.** The reply can carry a colour per field: green for two readings agreeing confidently, amber for agreeing with one unsure, red for disagreeing or a mandatory field empty — and **on red no value is returned at all**. That maps onto the exceptions rule below: red holds the advice back, green passes, amber is the decision to make. Whether the colours arrive is the extraction service's own configuration rather than something this flow sets, so **Tracto has to work when everything comes back `unreviewed` too**, falling back on its own validation. Confirm which to expect with the service owner.

**What is still true about it being an LLM.** The output is not deterministic, so the same document can give slightly different answers on two runs. Agreement between two readings narrows that but does not remove it, and nothing in the service checks that the figures add up. The validation above and the exceptions rule below are what make it safe, and the first run should still be scored side by side against documents the business has already accepted through UiPath.

## Advices that fail validation

The UiPath version runs `Present Validation Station` in the production path: a person confirms or corrects what the model read, for every document that matched a known keyword and produced a credited amount. The three "Missing Information" mails that were meant as a fallback are commented out, so that screen is the only check in place today.

**Tracto does not carry it over.** There is no review screen in this migration, and the existing web pages over the collection accounting database are neither touched nor fed by Tracto. What replaces it is a rule and a record:

```
extraction returns ─▶ Go validates ─▶ passes every check? ── yes ─▶ advice written to the workbook, item Done
                                          │
                                          no
                                          ▼
                       not written to the workbook; exceptions record with the reason,
                       the raw reply and a pointer to the PDF; reported in the mails
                                          │
                                          ▼
                       corrected (document re-sent, rule fixed) and rerun
```

- **The exceptions record** holds one row per advice that failed, with the reason (a red field, a missing required field, lines that do not add up), the raw reply and the path of the PDF. The three notification mails summarise it for the SIS operations team.
- **Checks Tracto runs itself,** whether or not the extraction service reports colours: dates and amounts parse, required fields are present, and invoice lines sum to the total credited amount where the advice states one.
- **What this changes.** A person no longer looks at the figures before they reach the collection accounting service. The checks above are the only filter, so they need to be strict, and the side-by-side run against advices already accepted through UiPath is what shows whether they are strict enough before cutover.

## What has to be built

### 1. The call to the extraction service

`POST /extract/document` as above: the prompt store, the call, the validation of the reply, and the raw reply kept against the document. The service itself already exists and is not part of this work.

### 2. PDF handling (new)

- **Unlocking.** About fifty candidate passwords are tried in order, and they exist in three places at once: a text file on the robot, a hard-coded array in `RemovePDFPassword.xaml`, and a second, longer copy of the same array in `DU Extract.xaml`. In Tracto the passwords come from configuration, never from a workflow file, and the list should be keyed by customer rather than tried exhaustively, so an unlock failure is a real signal.
- **Text**, for the customer-detection step, and page images if the service wants them rather than the file.

A Go PDF library covers all three; none of this needs a worker.

### 3. Customer rules as data, not workflows (flow-specific, high value)

The post-processing dispatches on `text.Contains("ITC LIMITED")` and similar, into a workflow per customer. The same shape as flow 3's per-entity duplication, and the same answer: a rules table.

| Customer | MatchText | Rule | Applies to |
|---|---|---|---|
| ITC Limited | `ITC LIMITED` | `parenthesesAsNegative` | invoice_amount, net_amount |
| JLL | `Lasalle Property Consult` | `twoLineInvoice` | Table |
| RBL | `RBL Bank Limited` | … | … |

A new customer becomes a row. Whatever genuinely cannot be expressed as a named rule stays in Go, named after the customer, and the aim is for that list to shrink rather than grow.

### 4. The pieces shared with the other flows

`/fs/*`, `/mail/send` with an HTML body, the append write, and documents as items with a status. All three earlier flows need these, so by the time this one is built they exist.

### 5. The advice workbook and the exceptions record

The workbook in exactly the layout the collection accounting loader reads (see [collection-accounting-handoff.md](collection-accounting-handoff.md)), the exceptions record and the three notification mails. Straightforward once extraction returns structured data.

## Security findings in this package

**The OCR service API key is written in clear text** in `DU Extract.xaml`, as the default value of a variable, and ships inside the published `.nupkg`. Anyone who can read the package can call that endpoint with it. It should be rotated regardless of what happens with Tracto.

**A list of about fifty PDF passwords is hard-coded** in the same file and again in `RemovePDFPassword.xaml`, in addition to the text file those workflows also read. The values have the shape of account or customer codes used by sending banks as document passwords. They belong in one place, in configuration, with access limited to the service that needs them.

**The SMTP account and password for the notification mails are hard-coded** in `Notifications\Missing Information.xaml` — the same `@dtss.in` mailbox whose password flow 3 also carries. Rotating it covers both packages.

**The package also carries run artefacts**: a `debug.log` and an `Exceptions_Screenshots` folder of screenshots captured from failing production runs. Screenshots of a payment-advice run contain customer names, bank details and amounts, and should not be inside a published package.

All four packages have a finding of this kind: flow 1 keeps an Azure AD client secret in a shared workbook, flow 2 writes the mailbox password into the run log on every run, and flow 3 carries two SMTP passwords and ships a live copy of the estate configuration workbook. The pattern is worth addressing as its own piece of work, separate from the migration.

## What improves, and what to watch

**Improves**

- **Per-customer workflows become rows.** Five workflows and a 180 KB dispatcher collapse into a rules table and one routine.
- **No model to own.** No OCR, no taxonomy, no ML Skill, no labelled training set and no retraining: the prompt is the model, and a new customer format needs no new workflow.
- **The extractor stays replaceable.** Behind one interface the engine can change without the flow changing.
- **Every document has a status.** `Done`, `Failed` or in the exceptions record, with the reason on the row and a run id to look up. The UiPath version's answer to a failure is a mail with an HTML table.
- **No credentials in the workflow.** They move to configuration and to the worker's environment.

**Watch**

- **Accuracy is the whole product here.** Everything else in this document is plumbing; if the figures are wrong the flow is worthless. Nothing should go to production without a side-by-side run against documents the business has already accepted.
- **Corrections feed the prompt, not a model.** The UiPath version sends corrected documents back to retrain the extractor. There is nothing to retrain here, so a correction made in review should end up in the prompt's examples or in the rules table, otherwise the same mistake returns every month.
- **The prompt is now production code.** It decides what the figures are, so it belongs under version control with a record of which version read which document, and a change to it should be scored the same way the first switchover is.
- **`OnelineTwoline` is a model output driving control flow.** The model decides whether an invoice spans two lines, and post-processing branches on it. That is a classification decision inside an extraction, and it deserves checking whether a rule over the extracted table would be steadier.
- **Amounts in parentheses mean negative.** Handled per customer today. It should be a single rule applied everywhere, because a missed negative is a wrong payment.
- **Duplicate invoice removal is silent.** Rows with no invoice number and rows with negative amounts do get their own sheets, but the duplicates themselves are dropped with no record of which or why. In Tracto the dropped rows belong on the exception sheet with the row they duplicated.
- **The consolidation has no retry.** Everything after the last document runs outside the transaction loop, so one failure there costs the entity's whole day while every individual document succeeded. In Tracto that work is part of the same batch, with its own status, and a rerun resumes it.
- **Nobody is being told today.** The three notification mails are switched off, so whoever is watching this flow is reading an exception folder. Turning the equivalent on in Tracto will surface a backlog that has been invisible, and that is worth expecting rather than treating as a new problem.
- **The documents are financial records.** They leave the machine for the extraction service and from there for the model. Where they go, what is retained and for how long, and whether that is acceptable to the business, is worth settling in writing once rather than per flow.

## Build order

1. The shared pieces, already needed by flows 1 to 3.
2. PDF unlocking, with the passwords in configuration.
3. `POST /extract/document` over the existing extraction service, with the payment-advice prompt and the validation of its reply.
4. The exceptions record and the validation rules, before anything runs unattended.
5. A scored side-by-side run against documents already accepted through UiPath.
6. The customer rules table, replacing the per-customer workflows one at a time, each checked against real documents.

Flows 1, 2 and 3 should be built first. They share most of the plumbing, and by the time this flow is reached the shared pieces exist and there is working experience of what Tracto's Go-side processes feel like to run.

## Open questions

These need answers before the Tracto flow can be built. The first two are the ones that shape it.

### The ones that shape the flow

1. **The payment-advice prompt.** The service runs against utility invoices today, so a prompt that returns payment-advice fields has to be written against the taxonomy above — ten header fields, ten invoice-line columns — tested against real advices, and given an id. **Settle the TDS question while writing it:** the UiPath taxonomy has no `TDS` column in the invoice table although the output schema has one, so either the model is asked for it directly or we need to know what fills it today. The other things to confirm with the service owner are listed in [extraction-service.md](extraction-service.md).
2. **Is dropping the on-screen confirmation accepted, and who works the exceptions?** UiPath has a person confirm the figures on screen. Tracto has no such step, so the checks and the exceptions record are the only filter, and the notification mails go to whoever handles them. Confirm that this is acceptable, and who that is.

### For the process owner

3. **What does amber mean, and how far does a red reach?** Green passes and red holds — the service refuses to resolve a disagreement, so somebody has to. Amber (the models agree but one was unsure) is the real decision. And when one invoice line of a twenty-line advice is red, does the whole advice wait or only that row? On top of the colours Tracto can check that dates and amounts parse and that the lines sum to the total credited amount; whether that is enough, and what to do when an advice gives no total to check against, is a business judgement.
4. **Which fields are required?** An empty required field fails the advice, so the list decides how much goes to the exceptions record. It has to come from the process owner.
5. **The notifications are off today.** The three mails — extraction failed, mandatory information missing, credit notes found — are commented out, so nobody is being told. Turning the equivalent on will surface a backlog that has been invisible. That is worth expecting, and worth deciding who receives it.
6. **The PDF passwords.** About fifty are tried in order against every locked document. They look like customer account codes, so they are presumably known per customer — in which case keying them by customer makes an unlock failure meaningful instead of just the end of a list. We need the real list, and where it is allowed to live.
7. **Per-customer rules: how many are real?** Five customers have their own workflow today. The aim is a rules table where a new customer is a row. To know how far that goes we need a handful of real documents per customer, especially for the two-line invoice layouts and anything where amounts appear in parentheses.
8. **Where may these documents go?** They are customer financial records and they will leave the machine for the extraction service and from there to a model. Where they go, what is retained, and for how long, is worth settling in writing once for the whole estate rather than per flow.
9. **Duplicate rows.** They are dropped today with no record of which or why. The intention is to put them on the exception sheet with the row they duplicated — which, as with flow 3, means somebody will start seeing something that has been invisible.
10. **One HSBC rule and one customer's decimal format** are special cases in the current code. Both are easy to carry over, but it is worth knowing whether they are still needed or are leftovers from a one-off.

### Not in Tracto yet

The extraction itself is not a gap — the service exists and answers in a usable shape. What has to be built is the call and the validation of its reply, PDF unlocking and text, the advice workbook and exceptions record, the customer rules table, and the shared pieces the other three flows need anyway. Full detail under "What has to be built".
