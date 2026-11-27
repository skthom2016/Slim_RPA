# ACME invoice batch with sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

Looks up every invoice in an Excel file on the ACME site and writes the details back into the same row. Each run starts an empty browser and signs in to ACME with a stored credential, so no signed-in browser profile is needed.

| File | What it is |
|---|---|
| `acme-login-invoices.xlsx` | The invoice numbers (column `Id`). The bot fills `InvoiceItem`, `Total`, `Date`, `Status` and `Message`. |
| `acme-login-main-start-batch_1.0.0.json` | The **main** job. One desktop step that asks sheet-service to start the batch, then ends. |
| `acme-login-child-fetch-invoice_1.0.0.json` | The **child** job. Opens `https://acme-test.uipath.com/login` in an empty browser, signs in, then opens `https://acme-test.uipath.com/invoices/search`, searches one `invoiceId` and returns Item, Total and Date. |

## The stored credential

The child reads one credential, `acme-login`, from the Orchestrator's credential store:

| Name | Kind | Group |
|---|---|---|
| `acme-login` | Credential: username (the ACME e-mail) and password | `acme` |

The script holds only the references `${credential:acme-login.username}` and `${credential:acme-login.password}`. The values are never written in a script, a log or a result. Credential and group names are lowercase, so the group `ACME` is stored as `acme`.

The `acme` group must allow the child script `acme-login-child-fetch-invoice`.

## Before the first run

1. Start the Orchestrator with `CREDENTIAL_MASTER_KEY` set.
2. In the Orchestrator UI, open **Credentials** and create the group `acme` with access for `acme-login-child-fetch-invoice`. Add the credential `acme-login` to it.
3. Run sheet-service (port 5090) on a build that has `/process/start`.
4. Publish the **child first**, then set its `latest` tag to it. Then publish the main job.
5. Start a desktop worker and a browser worker.
6. Keep `acme-login-invoices.xlsx` closed in Excel while the batch runs.

## Reading the result

- `Status` is `Done` or `Failed` for each row. `Message` says why a row failed.
- A wrong or expired password fails every row at the search-box step.
- Run the main job again to retry: only rows that are not `Done` are processed.
