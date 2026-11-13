# The extraction service

The document-extraction service that flow 4 calls instead of UiPath Document Understanding. It exists and runs for PDFs, but PDF password management and image extraction are not in it yet, and the module team has to add them (see [extraction-module-updates.md](extraction-module-updates.md)). This document records only what a caller needs: how to call it, what comes back, how it fails, and what Tracto does around it.

Service: `H:\Neelitech\apexcos-poc\backend`, a Go service listening on `8080` by default. Its own reference is `backend/API.md`, with `backend/openapi.yaml` beside it; those are authoritative if anything here drifts.

## In brief

Give it a PDF and the id of a prompt. It returns the fields as JSON. It stores nothing — what comes back is the caller's to keep.

That last point shapes the design: **the service extracts, Tracto owns everything else.** The queue of documents, the statuses, the retries, the output workbook, the exceptions record, the audit trail, the schedule.

## The call

```
POST /api/extract          multipart/form-data
```

| Field | Required | Meaning |
|---|---|---|
| `prompt_id` | yes | Which master prompt to use |
| `pdf` | yes | The file. Must start with `%PDF-` |
| `file_path` | no | The real path of the source file; stamped into the result in place of the model's `"FilePath": "UNKNOWN"` |
| `date_format` | no | Output date format |

`GET /healthz` is there for a health check. The service's other endpoints are not needed by this flow.

## What comes back

One of two shapes, and **a caller has to handle both** — which one arrives depends on the file, not on anything the caller asked for.

**A single result**, for a file holding one document:

```json
{
  "extractor": {
    "summary": { "...": "the header fields" },
    "detail":  [ { "...": "one row per line item" } ],
    "meta":    { "FileName": "...", "detail_row_count": 1 }
  },
  "reviewer": { "...": "present only when the service's review step is enabled" },
  "overall_color": "green",
  "usage": { "...": "token counts" }
}
```

**A multi-document result**, when the upload held several separate documents: `"multi_invoice": true` and an `invoices` array, each entry carrying its own `extractor`, `reviewer`, `overall_color` and `usage`, with the top-level single-result keys absent.

That one matters for SIS: a single mailed PDF can hold several advices, and the service separates them without being asked. Flow 4 has to be ready for one file to produce several results.

**The colours.** When the service's review step is enabled, every field — and every summary and detail row — carries a review block with a colour:

| Colour | Means |
|---|---|
| **green** | Two readings agreed and both were confident |
| **amber** | They agreed, but one was not confident |
| **red** | They disagreed, or a mandatory field came back empty |
| **unreviewed** | The review step is not enabled, or it had nothing to say about that field |

**On red no value is returned** — the service does not pick a winner between two readings. `overall_color` is the whole-file rollup, worst wins.

Whether review is enabled is the service's own configuration, not something the caller sets. So **Tracto must cope with `unreviewed` arriving for everything** and must not assume the colours will be there.

## How it fails

| Status | Meaning |
|---|---|
| 400 | `prompt_id` or `pdf` missing, unreadable upload, empty file, or no `%PDF-` header |
| 422 | The file was rejected: `UNREADABLE_PDF`, **`ENCRYPTED_PDF`**, `PDF_TOO_LARGE`, `INVALID_INPUT` |
| 502 | The model call failed, the prompt could not be fetched, or the output was not valid JSON |
| 503 | The service is at capacity (`ANALYZER_BUSY`) |
| 504 | It timed out (`ANALYZER_TIMEOUT`) |

Two of these shape the Tracto side directly.

**`ENCRYPTED_PDF` means unlocking stays ours.** The service will not open a password-protected PDF, and most bank advices are password-protected, so flow 4's password step runs in Go before the file is sent.

**`ANALYZER_BUSY` and `ANALYZER_TIMEOUT` are back-pressure, not failure.** A 503 means try again shortly; the document is fine. The item is held and retried rather than marked failed.

## Synchronous or callback

Both exist, and which is in use is the service's deployment setting rather than a choice the caller makes:

- **Synchronous:** the call blocks and returns `200` with the result.
- **Callback:** the call returns **`202` with a `request_id`** immediately, and the result is POSTed later to a configured callback URL, with the same payload plus `request_id` and `status`.

Tracto has to know which one it is talking to: the second needs an endpoint to receive the result and an item that stays open until it arrives.

## What Tracto does around the call

- **Hold the prompt id per document type** as data rather than in code.
- **Unlock the PDF first**, because of `ENCRYPTED_PDF`.
- **Check the arithmetic**, which nothing in the service does: do the invoice lines sum to the total credited amount, where the advice gives both. One line of Go, and it catches a class of error no amount of model agreement will.
- **Map the reply onto the twenty-four-column schema**, and decide the item's status from the colours when they are there and from its own validation when they are not.
- **Keep the raw reply** against the document, so a figure that turns out wrong can be traced to what came back.
- **Treat 503 and 504 as retry**, not failure.
- **Expect one file to become several results**, and keep them separate.

## What to confirm with the service owner

1. **The payment-advice prompt.** The service runs against utility invoices today, so a prompt that returns payment-advice fields has to exist and be given an id. It needs the taxonomy flow 4 records — ten header fields, a ten-column invoice table, a one-line/two-line marker — and testing against real advices. Who writes it, where it lives, and how a change to it is versioned are all open.
2. **Is the review step enabled?** It decides whether the colours arrive or everything comes back `unreviewed`, and therefore how much of flow 4's decision about which advices go to the exceptions record rests on the service and how much on Tracto's own checks.
3. **Synchronous or callback**, as above.
4. **How many documents at once?** The service has a capacity limit and says `ANALYZER_BUSY` when it is reached. The number sets how many Tracto may have in flight.
5. **Where it runs and who may reach it.** It listens on `8080` with no authentication on the extract endpoint and permissive CORS — fine on a developer machine, a decision to make before it is called from elsewhere.
6. **Where the documents go, and what is retained.** They are customer financial records; in callback mode the service holds the file until delivery succeeds, and the model sees the content either way.
