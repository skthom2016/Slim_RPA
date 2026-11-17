# Epic E: Document extraction process (PDF and images)

Scope: reading one payment-advice document (PDF with a text layer, scanned PDF, jpg or jpeg) with an LLM through the existing in-house extraction service, and returning the field set of the entity's profile with a confidence per field. Flow 4 (F1-F4) calls this process once per document in its Go loop; output quality (I1-I6) and platform capabilities (A1-A6) are written separately and referred to by ID.
Ownership: the extraction service (`POST /api/extract`, Go, repo `apexcos-poc/backend`, branch `big-file-split-pdf`) belongs to the module team, who make the additions; Tracto's Go side calls it over HTTP, holds the configuration around it and decides each document's outcome. Each task names its owner in Notes as "Module team" or "Tracto Go".

| ID | Title |
|---|---|
| E1 | Intake and file handling |
| E2 | Document splitting |
| E3 | LLM extraction |
| E4 | Cross-check |
| E5 | Result assembly |
| E6 | Operation and resilience |
| E7 | Accuracy evidence |
| E8 | Entity profiles for the field set |
| E9 | Character recognition for scans and images |

Evidence paths below are relative to `H:\Neelitech\apexcos-poc\backend` on branch `big-file-split-pdf` (read only). Sample counts come from [payment-advice-samples.md](../payment-advice-samples.md); no value from a sample document is reproduced here.

---

## E1 — Intake and file handling
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | none |
| Capability today | Partly. PDF-only entry check exists (`internal/features/extraction/handler.go` `extract`, `pdfvalidate.go` `looksLikePDF`, codes `INVALID_INPUT`, `UPLOAD_READ_FAILED`, `EMPTY_PDF`, `INVALID_PDF`). Rejection of unreadable, oversized and encrypted PDFs exists as 422 `UNREADABLE_PDF`, `PDF_TOO_LARGE`, `ENCRYPTED_PDF` (`pdf-split/splitter/errors.py`, `internal/pdfsplitter/errors.go` `DescribeError`). To build: image acceptance, any password handling (no `password` field anywhere in the handler, the splitter or the config), image rendering for the LLM (both clients send only `application/pdf`: `internal/anthropic/client.go` `ExtractText`, `internal/openai/client.go` data URL). |

**User story.** As the SIS operations team, I want every PDF and image advice accepted, unlocked with the right customer password and either read or rejected with a clear code, so that no document silently disappears and no password is exposed.

**Context.** UiPath unlocks each PDF with `RemovePDFPassword.xaml`, trying about fifty hard-coded passwords from three places in turn, and treats a file without a user password as success ([SIS-04, step 1](../SIS-04-payment-advice-extraction.md)). JPG and JPEG take the same path as a PDF. In Tracto the service accepts the file and an optional password, Tracto holds the password list in configuration keyed by customer, and a locked file that no password opens ends with the outcome `locked`. In the International sample 20 of 803 PDFs on the six distinct days are locked (2.5%), none of the 112 DTSS PDFs ([extraction-module-updates.md](../extraction-module-updates.md)).

**Acceptance criteria.**
1. Given a file whose first bytes are `%PDF-`, when it is posted to `/api/extract`, it is accepted as a PDF. Given a file whose first bytes are `FF D8 FF`, it is accepted as an image whatever its extension; a `.jpg` that starts with `%PDF-` is handled as a PDF.
2. Given a file that starts with neither signature (an xlsx, a zip, text), the reply is 400 with code `UNSUPPORTED_TYPE` and no LLM call is made. A zero-byte file returns 400 `EMPTY_PDF` as today. `INVALID_PDF` is no longer returned for a non-PDF.
3. Given a PDF protected by a user password and no `password` field in the request, the reply is 422 `ENCRYPTED_PDF` and no LLM call is made.
4. Given a locked PDF and a `password` field holding the right password, the file is unlocked in memory before splitting and the reply has the same shape and content as for the unlocked file. Given a wrong password the reply is 422 `PDF_PASSWORD_INCORRECT`, a code distinct from `ENCRYPTED_PDF`, and no LLM call is made.
5. Given a PDF with an owner password only (restrictions but opens without a password), it is extracted without any password, as UiPath does. (Today `pdf-split/splitter/extraction/reader.py` lines 136-139 reject any file with `is_encrypted` set.)
6. Given Tracto holds a candidate list for the customer, an entity default list and a shared list, it sends one candidate per request in that order, stops at the first success and records only the number of attempts, never the value.
7. Given no candidate opens the file, or no list is configured, the document's outcome is `locked` with reason "no configured password opened the file (N candidates tried)" or "no password configured"; the item is not retried automatically and the file name appears in the day's `Errors` sheet and mail (R7 in the samples document).
8. The password never appears in any reply body or header, the Tracto result and exceptions records, the Tracto run log, the module log, or the splitter's command line. Test: use a canary password, then search every one of those sinks for it.
9. The password list is not in the repository: a repository-wide search for the canary and for the config file name finds nothing; the list is read from a secret store, an environment value or a file outside the repository that only the service account can read.
10. Given a PDF with more pages than `MAX_PDF_PAGES` (default 5000) or a file above the configured `MAX_UPLOAD_BYTES`, the reply is 422 `PDF_TOO_LARGE`; a corrupt PDF returns 422 `UNREADABLE_PDF`; neither is retried.
11. Given a jpg or jpeg with an EXIF rotation, it reaches the LLM upright, converted to a one-page PDF so the splitting, OCR and model calls are unchanged (decision: one path for everything, as in [extraction-api-exists-and-additions.md](../extraction-api-exists-and-additions.md)); the reply has the same shape as a PDF's.
12. Given the 20 locked International samples and the configured list, all 20 open and extract; a locked file whose password is deliberately removed from the list ends as `locked`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E1.1 | Accept PDF and image files (JPEG, JPG and other common formats) with type and signature checks | Module team: add the `FF D8 FF` check and `UNSUPPORTED_TYPE` beside `looksLikePDF`. Other formats (png, tiff) stay rejected; UiPath handles only jpg and jpeg |
| E1.2 | Unlock PDFs with per-customer passwords | Module team: optional multipart `password` field; decrypt in memory in the handler (pdfcpu is already a dependency, `pdfpages.go`) before `countPages` and before the Python splitter, so the password never reaches the subprocess command line (`internal/pdfsplitter/analyzer.go` builds `exec.CommandContext` args); owner-password-only files open without a password; unlocked bytes are not kept. If the module team cannot deliver, Tracto Go unlocks before sending |
| E1.3 | Reject unreadable, oversized or locked files with clear codes | Module team: add `PDF_PASSWORD_INCORRECT`; add `MAX_UPLOAD_BYTES`; make a user-locked PDF return `ENCRYPTED_PDF`. By reading, `ExtractFile` calls `countPages` then `repairPDF` (qpdf) before the splitter, so a locked file likely ends as a plain 502 from `split into documents: ...` and never reaches the 422 mapping; confirm with a test |
| E1.4 | Render pages and images for the LLM | Module team: PDF pages already go as a `document` block (exists); add jpg/jpeg to one-page PDF conversion with EXIF rotation applied. Vision limits are in E9 |
| E1.5 | Tracto Go: pre-send type check and mapping of intake codes to outcomes | Tracto Go: `UNSUPPORTED_TYPE`, `EMPTY_PDF`, `UNREADABLE_PDF`, `PDF_TOO_LARGE`, `ENCRYPTED_PDF`, `PDF_PASSWORD_INCORRECT` to the item outcomes `unsupported`, `empty`, `unreadable`, `too_large`, `locked` |
| E1.6 | Tracto Go: password configuration and candidate order | Tracto Go: keys `pdfPasswords.byCustomer`, `pdfPasswords.byEntity`, `pdfPasswords.shared` (names only here); stable order; attempt counter; missing config is a clear `locked` reason, not a crash |
| E1.7 | Tracto Go and Module team: no-leak tests for the password | Canary test across reply, records, both logs and process arguments (criterion 8) |

**Test notes.** Needs the 20 locked International PDFs and the real candidate list in the test environment's secret store (never in the repository); one owner-password-only PDF, one corrupt PDF, one over-limit file, one jpg with rotation. Checks: codes and HTTP statuses per criterion, no LLM call on rejected files (provider request count is zero), canary absent from every sink.

**Open items.**
- The real password list and its owner: which entries belong to which customer. All 856 International documents are filed under the generic prefix `Others` (samples section 2), so a customer key is not known at unlock time for that entity; the order for them is entity default then shared.
- Whether the International list equals the Indian one (samples section 12, item 5).
- Whether the module team ships the `password` field or Tracto unlocks first; the documents give both ([extraction-service.md](../extraction-service.md) says Go unlocks, [extraction-module-updates.md](../extraction-module-updates.md) says the module does).
- Retention of the stored upload when the service runs in callback mode (the unlocked bytes would sit in `STORAGE_DIR` until delivery).
- The size limit to configure, set from the LLM provider's own request limit.

---

## E2 — Document splitting
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E1 |
| Capability today | Partly. The splitter exists and runs on every upload: `internal/features/extraction/documentsplitter.go` `PDFFileSplitter.Split`, `extractfile.go` `ExtractFile`, Python package `pdf-split/splitter` (boundary detection, same-page regions, `starts_mid_page`). It was measured on a utility-invoice batch (147 of 147, `pdf-split/README.md`), not on advices. The page range of each document is not in the `/api/extract` reply: `ExtractFile` only logs it (the range is returned by `/api/test-extract`, `start_page`/`end_page`). |

**User story.** As the flow 4 loop, I want a file that bundles several advices separated into one result per advice, each with its page range, so that no advice is merged into another or lost.

**Context.** UiPath treats each queued file as one document; there is no splitting. The extraction service splits first by design (generic, no LLM) and extracts each document on its own ([extraction-service.md](../extraction-service.md)). The samples are mostly one page (839 of 894 International PDFs; DTSS 111 of 112), and bundling is not measured in them, so the splitter's behaviour on small advice bundles is unproven. The splitter needs at least `ANCHOR_MIN_FREQ` (default 5) repetitions of an opener structure; fewer and the file is returned whole with a warning.

**Acceptance criteria.**
1. Given a one-page advice, the reply is a single result and `meta.source_pages` is `{start:1,end:1}`.
2. Given a file that bundles two advices on separate pages, the reply has `multi_invoice: true` and two entries, each with its own `source_pages`.
3. Given a bundle of fewer than five advices, the splitter still separates them (after the threshold is lowered or an advice-specific opener rule is added), or the reply flags `meta.split_warning` and Tracto records the file as `needs_split_check` instead of treating it as one advice.
4. Given a second advice that starts part-way down a page that holds the end of the first, each result holds only its own content and `starts_mid_page` is true for the second.
5. Given an advice that runs over two pages, it stays one result.
6. Given the splitter fails, the reply keeps the existing statuses: 422 for data problems, 503 `ANALYZER_BUSY`, 504 `ANALYZER_TIMEOUT`, 502 `ANALYZER_FAILED` or `SPLIT_FILES_MISSING`.
7. Given a bundle where one document fails extraction, the other documents are still returned and the reply lists the failed one in `failed_documents` with `source_pages` and a code (today `ExtractFile` logs and skips it, so a missing advice is invisible).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E2.1 | Detect the separate advices inside a bundled file | Module team: exists; the task is running it on advice bundles and tuning `ANCHOR_MIN_FREQ`, `ACCEPT_THRESHOLD` |
| E2.2 | Boundaries, including a document that starts part-way down a page | Module team: exists (same-page region split, `starts_mid_page`); verify on advice layouts |
| E2.3 | Split output per document with page ranges | Module team: add `meta.source_pages`, `meta.doc_id`, `meta.boundary_confidence` to each result in the `/api/extract` reply |
| E2.4 | Splitting tests with bundled samples | Module team and Tracto Go: needs bundled advices (see open items) |
| E2.5 | Small-bundle behaviour and failed-document list | Module team: criterion 3 and 7 (`failed_documents`); Tracto Go reads both |

**Test notes.** Needs at least three real or synthetic bundles (two advices; five or more; one advice starting mid-page) and one two-page advice. Checks: result count, each result's pages, no content missing or duplicated across results.

**Open items.**
- No bundled advice is identified in the samples; the number and size of bundles must come from the mailbox or the process owner.
- Whether an advice that lists many invoices over several pages could be mistaken for a bundle (25 International PDFs have two pages, 6 more have three to five).

---

## E3 — LLM extraction
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E1, E2 |
| Capability today | Partly. Provider clients exist (`internal/anthropic/client.go`, `internal/openai/client.go`, selected by `LLM_PROVIDER`, keys `ANTHROPIC_API_KEY` and `OPENAI_API_KEY` from the environment, `internal/config/config.go`). Prompt store exists as `prompt_id` resolved by `dataverse_prompt_fetcher.go` (only an `isactive` flag, no version). JSON parsing with fence stripping and one retry exists (`service.go` `extractWithRetry`, `maxExtractAttempts = 2`; `jsonextract.go`). Token usage exists (`internal/llm/usage.go`, `usage` block and `X-LLM-*` headers). Not present: any payment-advice prompt (shipped prompts are utility invoices: `master_prompt.txt`, `electricity_master_prompt*.txt`), a prompt version in the reply, advice-specific tests. |

**User story.** As the flow 4 loop, I want each document read by an LLM with a versioned payment-advice prompt and returned as structured JSON with token usage, so that the figures can be validated and traced to the prompt that produced them.

**Context.** UiPath reads advices with the ML skill `PaymentAdvicesIndia` against a fixed taxonomy: ten header fields, a ten-column invoice table, and a `One line/Two line` marker ([SIS-04, step 4](../SIS-04-payment-advice-extraction.md)). Tracto replaces the trained model with a prompt on the service. The invoice table in the package's own `taxonomy.json` has `TDS` and no `GST_tax`, which is also what the DTSS per-document workbooks show; [SIS-04](../SIS-04-payment-advice-extraction.md) and [extraction-api-exists-and-additions.md](../extraction-api-exists-and-additions.md) list `GST_tax` and no `TDS`. The prompt follows the package taxonomy. This story covers the Indian field set and the mechanics; the profile mechanism and the International field set are E8.

**Acceptance criteria.**
1. Given `LLM_PROVIDER=claude` or `openai` and its key in the environment, a call returns a result from that provider; a missing key stops the service at start with a message naming the variable, not the value. No key appears in any reply or log.
2. Given a prompt id for the Indian profile, the reply `extractor.summary` has the ten header fields `advice_number, advice_date, value_date, customer_name, beneficiaryName, beneficiary_account_number, beneficiary_bank_name, beneficiary_IFSC_code, UTR_number, total_credited_amount`, and `extractor.detail` rows have `invoice_number, invoice_date, invoice_amount, TDS, retention, other_deduction, net_amount, GST_TDS, wct_tds, GSTtax_amount`, plus the one-line or two-line marker.
3. Given the prompt is edited, a new version is recorded and the reply carries `meta.prompt_id` and `meta.prompt_version`; two documents read under different versions show different versions.
4. Given the prompt id does not exist or the prompt source is down, the reply is 502 with code `PROMPT_FETCH_FAILED` (today a plain 502 with no code); it is retried by Tracto, not recorded as a bad document.
5. Given the model answers with non-JSON or JSON inside a code fence or after a preamble, the reply is repaired where it parses, retried once otherwise, and ends as 502 `INVALID_MODEL_OUTPUT` if still invalid; the raw text is available for the exceptions record.
6. Given any document, the reply carries token usage (input, output, total, provider, model) for that document, summed over its attempts.
7. Given the sample advices, the extraction tests assert field presence and shape (not values copied into the repository): ten header keys, ten detail keys, dates and amounts as returned strings.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E3.1 | LLM provider client: configuration and key handling | Module team: exists; the task is choosing provider and model for advices, and key handling check |
| E3.2 | Prompt store: versioned prompts per document type | Module team: version and `prompt_version` in the reply; Tracto Go keeps the prompt id per profile as data (E8.8) |
| E3.3 | Field schema: ten header fields, ten-column invoice table, one-line or two-line marker | Module team with Tracto: the Indian-profile prompt; decide whether the marker stays a model output (SIS-04 flags it as a model decision driving control flow) |
| E3.4 | Structured reply parsing, with repair of invalid output | Module team: partly exists; add the `INVALID_MODEL_OUTPUT` and `PROMPT_FETCH_FAILED` codes |
| E3.5 | Token usage tracking per document | Module team: exists; Tracto Go stores it per work item |
| E3.6 | Extraction tests on sample advices | Tracto Go: DTSS bank advice (93 documents, one fixed layout) and the 19 customer-produced layouts |

**Test notes.** Needs a service instance with a provider key and the advice prompt loaded, DTSS and International samples. Checks: key set, version recorded, reply shape; the provider's request count equals attempts plus reviews.

**Open items.**
- Who writes the advice prompt, where it lives and how a change is scored (E7).
- Whether `GST_tax` or `TDS` is the intended seventh column, and what fills `TDS` today on DTSS advices (the package taxonomy and the workbooks agree on `TDS`; the docs do not).
- Which model and provider, and the cost per document at about 150 documents a working day.
- The service runs against utility invoices only today; nothing about it has been tested on advices.

---

## E4 — Cross-check
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E3 |
| Capability today | Partly. A second reading exists (`internal/review/double_extraction.go`, `cached_judge.go`), switched by the environment variable `REVIEW_MODE` = `off` (default), `double` or `judge` (`internal/config/config.go`). Red returns no value (`internal/review/resolve.go`). Gaps: the compared fields are fixed to four utility fields (`comparisonFields`: `Service StartDate`, `Service EndDate`, `Meter Number`, `TotalConsumption`); the reviewer is always OpenAI and needs `OPENAI_API_KEY`; `BuildResponse` (`response.go`) returns only the raw extractor, the raw reviewer, `overall_color` and `usage`, so the per-field and per-row colours described in `API.md` and in [extraction-service.md](../extraction-service.md) are computed but not in the reply. |

**User story.** As the process owner, I want a second reading of each document that I can switch on by configuration, so that I can raise accuracy where the cost is accepted and run without it where it is not.

**Context.** UiPath has a person confirm each figure at the Validation Station; Tracto has no review screen and no second LLM unless it is configured. The switch is a configuration value: off means every field arrives `unreviewed` and Tracto relies on its own checks (arithmetic, parsing, required fields). When on, a colour per field and per row says whether two readings agreed ([SIS-04, advices that fail validation](../SIS-04-payment-advice-extraction.md)).

**Acceptance criteria.**
1. Given `REVIEW_MODE=off`, no second provider call is made, the reply has no `reviewer`, and every field reports `unreviewed`; Tracto processes the document using its own checks.
2. Given `REVIEW_MODE=double` or `judge`, each document costs one extra call and the usage block shows the reviewer's tokens separately.
3. Given a profile's configured comparison list, only those fields are compared; for the Indian profile the list includes `total_credited_amount`, `UTR_number`, `invoice_number`, `invoice_amount`, `net_amount` (final list set by the process owner).
4. Given the two readings disagree on a compared field, that field's `final_value` is null and its colour is red; Tracto never receives a guessed winner.
5. Given a required field of the profile is empty, its colour is red.
6. Given the reply, each summary field and each detail row has a `color` of `green`, `amber`, `red` or `unreviewed`, and `overall_color` is the worst of them.
7. Given the reviewer call fails, the document is returned with every field `unreviewed`, not as an error (today's behaviour in `service.go` `runReview`).
8. Changing the mode needs a deployment configuration change only; no code change. Tracto must not assume colours are present.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E4.1 | Second reading of the same document | Module team: exists |
| E4.2 | Field comparison with a colour per field and row | Module team: exists inside `review.Resolve`; the task is putting the colours in the reply (criterion 6) |
| E4.3 | No value returned on disagreement | Module team: exists |
| E4.4 | Cross-check mode switch in configuration, with tests | Module team: exists as `REVIEW_MODE`; add tests, and a `/readyz` line showing the mode |
| E4.5 | Per-profile comparison list and required-field list | Module team: replace the fixed `comparisonFields` with a list per profile (E8.1) |
| E4.6 | Tracto Go: treat `unreviewed` as normal and read the colours when present | Tracto Go: decision table in F-stories; this task is only the reader |

**Test notes.** One run per mode over ten advices including two with a deliberately blurred amount; second provider key present only for the on modes. Checks: calls per document, colours, `final_value` null on red.

**Open items.**
- The meaning of amber and how far a red reaches (one invoice line or the whole advice) is a process-owner decision (SIS-04 open question 3).
- The reviewer is tied to OpenAI; whether that is acceptable for a service that would otherwise use one provider.
- The comparison and required field lists, per profile.

---

## E5 — Result assembly
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E2, E3 |
| Capability today | Partly. Merge and grouping exist (`internal/features/extraction/grouping.go`, `mergebatches.go` `mergeBatchResults`, `extractfile.go` `mergeDocResults`, `shapeDocResults`), and the multi-document reply exists (`response.go` `BuildMultiInvoiceResponse`). Both are written for utility invoices: the group key is `summary."Client Name"` plus `"Invoice Number"` (`grouping.go` `groupKey`) and detail rows merge on a meter number (`mergebatches.go` `rowPrimaryID`). The raw model reply is returned in `extractor` and `reviewer`. |

**User story.** As the flow 4 loop, I want the results of one advice merged into one result and the results of different advices kept apart, with the raw reply kept, so that each advice becomes exactly its own rows.

**Context.** A document can be read in overlapping page batches (`PAGE_THRESHOLD` 5, `PAGE_OVERLAP` 1) and a file can hold several advices. An advice is identified by its advice number or UTR together with the payer; a line by its invoice number within the advice ([extraction-api-exists-and-additions.md](../extraction-api-exists-and-additions.md)). In Tracto, one file may give several results and each is a separate step outcome in the loop (F-stories).

**Acceptance criteria.**
1. Given the pieces of one advice that ran over two read batches, the merged result has each invoice line once; a line seen in the overlap page is not doubled.
2. Given two advices from the same payer with different advice numbers in one file, the reply has two entries and neither is merged.
3. Given the identity cannot be formed (no advice number, no UTR, no payer name), each piece stays its own entry; nothing is merged on a guess.
4. Given the International profile, which has no advice number, the identity is the payer plus the payment date plus the total credited amount; two pieces with the same triple merge.
5. Given a multi-result reply, each entry carries its own `extractor`, `reviewer`, `overall_color`, `usage` and `source_pages` (E2.3).
6. Tracto stores, per result, the raw reply as received (before any mapping), the prompt version and the usage; a result can be re-read later without calling the service.
7. Given identical invoice lines inside one document (same invoice number and amounts), they are collapsed and the collapse is reported with the line it duplicated (supports R5 in the samples).

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E5.1 | Merge results that belong to the same advice | Module team: exists for invoices; becomes the advice rule (E5.5) |
| E5.2 | Keep separate advices separate, including the multi-document result | Module team: shape exists; Tracto Go reads both shapes |
| E5.3 | Keep the raw LLM reply per document | Module team: raw is returned; Tracto Go persists it per result |
| E5.4 | Assembly tests | Module team and Tracto Go |
| E5.5 | Advice identity key and invoice-line key | Module team: replace the `Client Name` and `Invoice Number` key and the meter-number row key with a per-profile key (E8.1) |

**Test notes.** Needs synthetic split pieces of one advice, two advices from one payer, an advice with no number. Checks: entries count, lines per entry.

**Open items.**
- Whether the International identity (payer, date, total) is strong enough; the Indian one uses advice number or UTR.
- The merge also drops near-duplicate rows (`foldNearDuplicateOrphans`); whether that rule suits advices is to be checked on real data.

---

## E6 — Operation and resilience
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E3 |
| Capability today | Partly. Synchronous mode and a callback mode exist (`handler.go` `submitAsync`, `worker.go`, `callback.go`; needs `STORAGE_DIR`, `CALLBACK_URL`, Dataverse settings; reply 202 with `request_id`). Back-pressure codes exist (503 `ANALYZER_BUSY`, 504 `ANALYZER_TIMEOUT` from the splitter, `SPLITTER_MAX_CONCURRENT`, `SPLITTER_TIMEOUT`). Model and prompt failures are a plain 502 with no `code` (`handler.go`). `/healthz` returns a fixed `{"status":"ok"}` (`internal/server/router.go`); no readiness check. No authentication on `/api/extract`, CORS `*`. |

**User story.** As the SIS operations team, I want the call to be synchronous with clear, retryable failure codes and a health check, so that a busy or failing service holds a document rather than marks it bad.

**Context.** UiPath has no service to fail; a failed document goes to an exception folder. Tracto treats 503 and 504 as retry, not failure, with the attempt count on the work item (A5) and a limit ([extraction-service.md](../extraction-service.md)). Tracto uses synchronous mode: one document is one HTTP call in the loop, so callback mode and a receiving endpoint are not built. The service takes about 150 documents a working day in the samples (the largest International day has 248), against 45 assumed earlier.

**Acceptance criteria.**
1. Given a normal document, the call blocks and returns 200 with the result; the Tracto HTTP timeout is above the service's own (the Anthropic client waits up to 300 s) so Tracto never abandons a call the service is still making.
2. Given 503 `ANALYZER_BUSY` or 504 `ANALYZER_TIMEOUT`, the item is held and retried with backoff; each retry increments the attempt count; after the configured limit (default 5) the item ends as `failed` with reason `service_busy` or `service_timeout`.
3. Given a model rate limit or provider outage, the reply is 502 with code `MODEL_UNAVAILABLE` (retry); given invalid model output, 502 `INVALID_MODEL_OUTPUT` (retry once more, then fail); given a missing prompt, 502 `PROMPT_FETCH_FAILED` (retry). Today none of these has a code.
4. Given 4xx codes from E1 (`UNSUPPORTED_TYPE`, `ENCRYPTED_PDF`, `PDF_PASSWORD_INCORRECT`, `UNREADABLE_PDF`, `PDF_TOO_LARGE`), the item is not retried.
5. Given the service restarts mid-run, the item that was in flight is retried; no document is lost or processed twice (the work item's claim and attempt count from A5).
6. Given `GET /healthz` the service answers liveness; given `GET /readyz` it answers 200 only when the splitter is configured, the prompt source answers and a provider key is set, and the body names the review mode; otherwise 503 with the failing part.
7. Given a call without the configured API key header, the reply is 401; Tracto sends the key from configuration and never logs it; CORS is restricted to the configured origins.
8. Given a bundle in which one document failed, the reply includes `failed_documents` (E2.5) and Tracto records the file as partly processed, not complete.
9. Tracto keeps no more calls in flight than the service's `SPLITTER_MAX_CONCURRENT`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E6.1 | Synchronous call and optional callback mode | Module team: both exist; Tracto Go: synchronous only, timeout setting |
| E6.2 | Retry on capacity or timeout, with an attempt count | Tracto Go: backoff and limit using the A5 attempt count; Module team: nothing |
| E6.3 | Error codes and structured failure reasons | Module team: add `MODEL_UNAVAILABLE`, `INVALID_MODEL_OUTPUT`, `PROMPT_FETCH_FAILED` in `{error, code}` form |
| E6.4 | Health and readiness endpoint | Module team: add `/readyz` beside `/healthz` |
| E6.5 | API key on `/api/extract` and restricted CORS | Module team |
| E6.6 | Tracto Go: key configuration, client timeout, in-flight cap | Tracto Go |
| E6.7 | Tracto Go: partly-processed file handling from `failed_documents` | Tracto Go; the field itself is E2.5 |

**Test notes.** Needs a service instance that can be made busy (set `SPLITTER_MAX_CONCURRENT=1` and send two calls), a stopped provider key, a bad prompt id. Checks: status and code per case, attempt counts, restart mid-call.

**Open items.**
- The service's documents-at-once limit and time per document are unmeasured; the daily total of 150 to 250 documents decides whether one at a time finishes in a day.
- Where the service runs and who may reach it; retention of uploads and outputs; whether documents may leave the machine for the model (SIS-04 open question 8).
- Whether the module team accepts the new codes and the API key.

---

## E7 — Accuracy evidence
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E3, E5, E8 |
| Capability today | To build. `internal/features/extraction/manualverify_test.go` and `check_results.py`, `check_master_results.py` in the module repository are utility-invoice checks; nothing scores advices. |

**User story.** As the process owner, I want the extraction scored per field and per customer against advices the business has already accepted, so that I can decide whether to go live and see the effect of each prompt change.

**Context.** UiPath output is the business's accepted result, not a clean truth: its day workbooks swap day and month on ambiguous dates, write auto-accepted lines twice and drop leading zeros ([known-faults-in-current-output.md](../known-faults-in-current-output.md), F1, F2, F6). The 248 International workbooks that went through confirmation carry a confidence per field and the corrections a person made; they are the validated set. The prompt is production code, so every change is scored the same way as the first switchover ([SIS-04, watch](../SIS-04-payment-advice-extraction.md)).

**Acceptance criteria.**
1. Given a folder of source documents and a folder of accepted workbooks, the harness runs the service on each document and writes a score per field (header and table) and per customer; counts and percentages only, no value is written to the report.
2. Given the accepted workbook has a known fault (swapped dates, doubled lines, numeric account numbers), the harness normalises it before comparing: dates day-first to true dates, amounts to numbers rounded to two decimals, identifiers as text, doubled lines collapsed.
3. Given a field is empty on the document and empty in the result, it scores as correct; a value where the document has none scores as a guess and is counted separately.
4. Given the 10 non-advices and the 9 images and scans of the International sample, they are scored for the outcomes `not_a_payment_advice`, `illegible` and the read result respectively (E8, E9).
5. Given two prompt versions, the report compares them side by side with the version recorded in each reply.
6. Given confidence per field and the validated corrections, the report gives the confidence value below which the share of wrong fields exceeds a target (set by the process owner), as the basis of R10.
7. The run is repeatable: the same inputs, the same prompt version and the same model give a report that differs only in the usage figures and sampling noise; the repeat difference is reported.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E7.1 | Scoring harness against accepted advices | Tracto Go: a command that calls the service, so it also exercises E1-E6 |
| E7.2 | Per-field and per-customer accuracy report | Tracto Go |
| E7.3 | First prompt tuning iteration on the samples | Module team edits the prompt; Tracto Go supplies the report |
| E7.4 | Ground-truth normalisation for the accepted workbooks | Tracto Go: criterion 2 |
| E7.5 | Confidence calibration against the validated corrections | Tracto Go: criterion 6 |
| E7.6 | Negative set: non-advices, locked, scans, images | Tracto Go: criterion 4 |

**Test notes.** Needs the DTSS sample (112 documents with workbooks) and the International sample (856 documents, 248 validated), outside the repository, run from a path that does not contain code. Checks: report totals equal the file counts; no customer value in the report.

**Open items.**
- Which workbooks count as accepted, and the target accuracy per field.
- Whether the validated set holds the corrections themselves or only the final values.
- Cost of a full run at the chosen model.

---

## E8 — Entity profiles for the field set
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E3 |
| Capability today | To build. The service has one prompt per `prompt_id` and one fixed review field list; there is no profile, no field list per profile and no `document_type`. A confidence per field exists only when the prompt asks the model for a `confidence` block (`internal/review/flatten.go` `ParseFieldCandidates` reads `confidence.summary` and `confidence.detail[i]`, 0 to 100); no confidence per table exists. |

**User story.** As the flow 4 loop, I want to name the entity's profile in the request and get that profile's fields back with a confidence for each field and for the table, with absent fields empty and non-advices identified, so that both entity layouts are read correctly and nothing is guessed.

**Context.** The two samples need two field sets, not one schema. The Indian profile (DTSS) has ten header fields and ten invoice columns including `TDS`, `GST_TDS`, `wct_tds`, `retention` and `GSTtax_amount`. The International profile has the header `UTR_number, value_date, beneficiaryName, beneficiary_account_number, beneficiary_bank_name, beneficiary_bank_bsb_code, customer_name, total_credited_amount` and the invoice columns `invoice_number, invoice_date, invoice_amount, other_deduction, net_amount, merchant_fees`: a BSB code not an IFSC code, no advice number or advice date, no Indian tax columns ([payment-advice-samples.md](../payment-advice-samples.md) section 4). 10 of 803 International files were other documents (the entity's own statements, a service notice, a subcontractor's statement, an alarm docket, a supplier's invoice), and a reader that always returns fields would turn them into rows. The weakest fields are the payer's name, the net amount where columns compete and the whole table (section 4.1).

**Acceptance criteria.**
1. Given the request field `profile=india`, the reply summary and detail keys are exactly the Indian set; given `profile=international`, exactly the International set; given an unknown profile the reply is 422 `UNKNOWN_PROFILE`, with no LLM call. The reply echoes `meta.profile`.
2. Given the entity table in Tracto, the entity DTSS maps to `india` and the International entity to `international`; the mapping and the prompt id per profile are data, not code.
3. Given any reply, every summary field and every detail row field has a confidence from 0 to 100 and the table has `table_confidence`; a field the model did not score is reported with no confidence, never 0.
4. Given a field that is not on the document (for example `UTR_number` on an Australian advice, `beneficiary_IFSC_code` on a bank advice that prints none), it is returned empty (null) and its confidence is empty; it is never filled from another field or guessed. Tracto treats null and an empty string the same.
5. Given a document that is not a payment advice, the reply is 200 with `document_type: "not_payment_advice"`, an empty summary, an empty detail and a one-line `not_advice_reason` of a category (statement, notice, supplier invoice, other); Tracto records the outcome `not_a_payment_advice` and lists the file in the day's `Errors` sheet and mail. A payment advice returns `document_type: "payment_advice"`.
6. Given the 10 non-advices in the International sample, all are returned as `not_payment_advice`; given the 793 advices, no more than the agreed small share is wrongly rejected (target set with the process owner and scored in E7).
7. Given an advice that lists a document number next to an invoice number (SAP-style: "your document", "document", "payment document"), `invoice_number` is the invoice the entity issued; the other numbers are not placed in `invoice_number`. For the Indian profile an advice-level reference goes to `advice_number`; for the International profile it is not placed in `UTR_number` (an EFT or payment number is not a bank UTR, as 0 of 2,973 International rows have one).
8. Given an invoice number written inside a description ("Invoice 1051221" shape), `invoice_number` holds the number only.
9. Given a deduction column printed as `merchant_fees` or as a discount, the value is returned in `merchant_fees` (International) or `other_deduction`, not dropped (fault F12).
10. Given a table with one amount column only (the large majority), that amount goes to `net_amount` and `invoice_amount` stays empty; given invoice total, amount paid and still-owing columns, `invoice_amount` is the total, `net_amount` the paid amount.
11. Given the profile's field list changes, no Go change is needed in Tracto.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E8.1 | Profile registry in the module | Module team: profile name to prompt id, field list, required fields, identity key (E5.5), comparison list (E4.5) |
| E8.2 | International profile prompt and field schema | Module team; the Indian profile prompt is E3.3 |
| E8.3 | `profile` request field, `meta.profile` echo, `UNKNOWN_PROFILE` | Module team |
| E8.4 | Confidence per field and per table in the reply | Module team: expose the prompt's confidence block in a stable place and add `table_confidence` |
| E8.5 | Empty-not-guessed rule and its tests | Module team: prompt rule plus a post-check that blanks any field the model filled with a placeholder |
| E8.6 | "Not a payment advice" answer | Module team: `document_type` and `not_advice_reason` |
| E8.7 | Document number versus invoice number rules | Module team: prompt rules and tests for the SAP-style layout and the description-embedded number |
| E8.8 | Tracto Go: entity to profile mapping and reply handling | Tracto Go: profile and prompt id per entity from the entity table, `document_type` outcomes, null versus empty |
| E8.9 | Tests on both samples | Tracto Go and Module team: counts of present, empty and guessed fields per profile |

**Test notes.** Needs DTSS and International samples, the 10 non-advices, advices with a document number beside the invoice number, advices with no UTR and no bank details. Checks: key sets per profile, null where absent, `document_type` per file, confidence present.

**Open items.**
- Whether an EFT or payment number may fill `UTR_number` for International advices; the loader matches by UTR first, so a wrong value could cause a false match (samples section 9.2). Default: leave empty.
- Whether the International hand-off needs the eight columns the loader requires and so whether the International profile should also return `advice_number`, `advice_date` and an IFSC-equivalent (samples section 12, item 2; I-stories).
- The wrong-rejection rate tolerated for the not-an-advice answer.
- Whether the International entity is in scope of the four flows (samples section 12, item 1); if not, only the Indian profile is built and E8.2 and E8.9 reduce to the Indian half.

---

## E9 — Character recognition for scans and images
| Field | Value |
|---|---|
| Epic | E Document extraction process (PDF and images) |
| Type | Story |
| Depends on | E1, E3 |
| Capability today | Partly. The splitter OCRs text-less pages with Tesseract only to find document boundaries (`pdf-split/splitter/extraction/ocr.py`, `ENABLE_OCR` `auto`/`on`/`off`, `OCR_DPI` 200, `OCR_MIN_CHARS` 32; without Tesseract the page is an empty-content page with a warning). The text that OCR finds is not sent to the model; the model receives the PDF itself as a `document` block (`internal/anthropic/client.go`) or a `data:application/pdf` file (`internal/openai/client.go`), so a scan is read by the provider's own PDF vision. Images are not accepted at all (E1.1). No reply field says what kind of input was read. |

**User story.** As the SIS operations team, I want scanned PDFs and jpg or jpeg images read, and a clear outcome when one cannot be read, so that a scanned advice becomes rows or a listed exception and never a silent gap.

**Context.** The International sample has 5 of 894 PDFs without a text layer (3 were read by the current process, 2 were not) and 4 images on the six distinct days (7 jpg or jpeg in all); none was reported (finding F9, [known-faults-in-current-output.md](../known-faults-in-current-output.md)). UiPath digitises everything with its own OCR; Tracto has no OCR step because an LLM reads the page image. Images are converted to a one-page PDF at intake (E1.4), so they follow the scanned-PDF path. The module team's list of updates names image extraction as missing ([extraction-module-updates.md](../extraction-module-updates.md)).

**Acceptance criteria.**
1. Given a scanned PDF with one advice, the reply holds the profile's fields with their confidences, read from the page image, with `meta.input_kind: "scanned_pdf"`; a text PDF has `"text_pdf"`, an image `"image"`.
2. Given the LLM provider in use, both configured providers are shown to read an image-only PDF page and a jpg on the sample set before the provider is chosen; a provider or model that cannot is not selectable (configuration check at start-up).
3. Given Tesseract is not installed on the module host, the service starts, boundaries on scanned bundles may be missed, the reply carries `meta.split_warning: "ocr_unavailable"`, and Tracto records the file as `needs_split_check` if the page count is above one.
4. Given an image or scan that is too large (pixels or bytes above the configured limits) or too small (below `MIN_IMAGE_PIXELS`), the reply is 422 `IMAGE_TOO_LARGE` or `IMAGE_TOO_SMALL`; given a file with a jpg signature that cannot be decoded, 422 `UNREADABLE_IMAGE`.
5. Given a scan or image from which no payment-advice field can be read (all header fields and all table rows empty with confidence below the configured floor, and `input_kind` not `text_pdf`), the Tracto outcome is `illegible`: the file is not posted, is listed in the day's `Errors` sheet and the mail with its file name and the reason, and the sender is not contacted by this process.
6. Given a scan or image that is an advice read at confidence under the review floor (R10), it is written to the review folder and listed, not posted.
7. Given the 4 images and 5 scans in the sample, each ends in exactly one of: rows with confidences, `illegible`, or `not_a_payment_advice`; none ends with no outcome.
8. Failure reasons carry the file name and code only, no page content.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| E9.1 | Confirm and configure the scanned-PDF path for both providers | Module team: criterion 2; document the provider and model that read image-only pages |
| E9.2 | Tesseract on the module host and the `ocr_unavailable` warning | Module team: install and set `ENABLE_OCR`; warning in the reply |
| E9.3 | Image limits and image failure codes | Module team: `IMAGE_TOO_LARGE`, `IMAGE_TOO_SMALL`, `UNREADABLE_IMAGE` |
| E9.4 | `meta.input_kind` in the reply | Module team |
| E9.5 | Tracto Go: `illegible` outcome and low-confidence routing | Tracto Go: criteria 5 and 6, uses the review floor from the I-stories |
| E9.6 | Tests on the images and scans of the samples | Tracto Go and Module team: the nine files, a rotated image, a very small image, a corrupt jpg |

**Test notes.** Needs the 4 images and 5 text-less PDFs of the International sample, one rotated image, one very small image, one corrupt jpg, a module host with and without Tesseract. Checks: one outcome per file, `input_kind`, codes, no page text in a failure reason.

**Open items.**
- Whether png and tiff must be accepted (UiPath handles only jpg and jpeg).
- The confidence floor below which a scan is `illegible`, set from E7.
- Whether the 2 scans the current process did not read are readable by the chosen provider; unknown until tested.
- Cost per scanned page for the chosen provider.

---

## Cross-epic dependencies

- A3 file operations: Tracto reads the filed document bytes before each call (E1, E9).
- A5 work items: the attempt count and claim behind E6.2, and the per-result raw reply (E5.3) stored against the item.
- F1-F4 (flow 4): the loop calls `POST /api/extract` once per document, reads both reply shapes, applies customer rules, arithmetic checks and required fields, writes the workbook and the exceptions record; the outcomes defined here (`locked`, `unsupported`, `empty`, `unreadable`, `too_large`, `not_a_payment_advice`, `illegible`, `needs_split_check`, `service_busy`, `service_timeout`) are the inputs to F2's `Errors` sheet and mail.
- I1-I6 (output quality): consume `confidence` per field and per table (E8.4) for the review floor, empty fields (E8.5) for dates and amounts parsing, and `meta.source_pages` for one-line-per-advice writing.
- Secrets: the password list (E1.6) and the service API key (E6.6) need a Tracto secret store or environment source; it is not in A1-A6.
- Module team: all tasks marked "Module team" are delivered before F's first end-to-end run; the earliest are E1.2, E1.3, E3.2, E6.5 and E8.1 to E8.4.
