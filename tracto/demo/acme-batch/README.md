# ACME invoice batch

Looks up every invoice in an Excel file on the ACME site and writes the details back into the same row.

| File | What it is |
|---|---|
| `acme-batch-invoices.xlsx` | The invoice numbers (column `Id`). The bot fills `InvoiceItem`, `Total`, `Date`, `Status` and `Message`. |
| `acme-main-start-batch_1.0.0.json` | The **main** job. One desktop step that asks sheet-service to start the batch, then ends. |
| `acme-child-fetch-invoice_1.0.0.json` | The **child** job. Takes one `invoiceId`, searches it on ACME, returns Item, Total and Date. It never touches Excel. |

## How it runs

```
Main job  --POST /process/start-->  sheet-service (5090)
(ends in seconds)                      for each row whose Status is not Done, one at a time:
                                         queue the child job with that row's Id
                                         wait for it
                                         write Item, Total, Date and Status into the row
```

The main job ends as soon as the batch has started, so it holds no worker. "Main job succeeded" means "batch started". To see how far the batch has got:

```
GET http://localhost:5090/process/<batchId>
```

The `batchId` is in the main job's outputs (`batch.batchId`).

## Before the first run

1. Run sheet-service (port 5090) on a build that has `/process/start`.
2. Publish both scripts. Publish the **child first**: starting a batch is refused if the child is not published.
3. Sign the browser profile in once: `dotnet run -- --login default`.
4. Start a desktop worker and a browser worker.
5. Keep `acme-batch-invoices.xlsx` closed in Excel while the batch runs.

## Reading the result

- `Status` is `Done` or `Failed` for each row. `Message` says why a row failed.
- Run the main job again to retry: only rows that are not `Done` are processed.
- A row that fails does not stop the others.
- To use a different workbook, give the main job a different `excelPath` when you submit it.
