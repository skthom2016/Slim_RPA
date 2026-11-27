# ACME invoice batch with sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

Looks up every invoice in an Excel file on the ACME site and writes the details back into the same row. Each run starts an empty browser and signs in to ACME with a stored e-mail and password, so no signed-in browser profile is needed.

| File | What it is |
|---|---|
| `acme-login-invoices.xlsx` | The invoice numbers (column `Id`). The bot fills `InvoiceItem`, `Total`, `Date`, `Status` and `Message`. |
| `acme-login-main-start-batch_1.0.0.json` | The **main** job. One desktop step that asks sheet-service to start the batch, then ends. |
| `acme-login-child-fetch-invoice_1.0.0.json` | The **child** job. Opens `https://acme-test.uipath.com/login` in an empty browser, signs in, then opens `https://acme-test.uipath.com/invoices/search`, searches one `invoiceId` and returns Item, Total and Date. |

## The stored secret and variables

The child reads two environment variables, `GP100_ACME_USERNAME` and `GP100_ACME_PASSWORD`. They are kept in the Orchestrator, not in the script. Every group has a number, shown on the **Groups** page, and a variable you add as `ACME_USERNAME` is read by scripts as `GP<number>_ACME_USERNAME`. The scripts here use `GP100`: if your group has another number, change the two names in the child script to match it.

| Where | Name | Holds |
|---|---|---|
| **Secrets** page | `acme-password` | the ACME password (kind `text`, no expiry) |
| **Groups** page, group `acme with login` | `ACME_USERNAME` | a plain value: the ACME account e-mail |
| **Groups** page, group `acme with login` | `ACME_PASSWORD` | points at the secret `acme-password` |

The group `acme with login` lists the script `acme-login-child-fetch-invoice` under **Scripts allowed**. Read its number from the group page (the first group created gets 100).

The script holds only the references `${env:GP100_ACME_USERNAME}` and `${env:GP100_ACME_PASSWORD}`, where 100 is the group number. The password is masked as `●●●●` in logs, errors and results, and it is never written in a script, a sheet or this folder.

## Before the first run

1. Start the Orchestrator with `CREDENTIAL_MASTER_KEY` set.
2. **Secrets** page: add `acme-password` with the ACME password.
3. **Groups** page: create the group `acme with login`, add `acme-login-child-fetch-invoice` under **Scripts allowed**, then under **Environment variables** add `ACME_USERNAME` (value: the ACME account e-mail) and `ACME_PASSWORD` (secret: `acme-password`). The page shows the `GP100_` prefix (use the number on your group page) in front of each name box; type only the rest. Save.
4. Run sheet-service (port 5090) on a build that has `/process/start`.
5. Publish the **child first**, then set its `latest` tag to it. Then publish the main job.
6. Start a desktop worker and a browser worker.
7. Keep `acme-login-invoices.xlsx` closed in Excel while the batch runs.

## Reading the result

- `Status` is `Done` or `Failed` for each row. `Message` says why a row failed.
- A wrong or expired password fails every row at the search-box step.
- A row fails with a message naming the variable when the group does not allow the child or the variable is missing.
- Run the main job again to retry: only rows that are not `Done` are processed.
