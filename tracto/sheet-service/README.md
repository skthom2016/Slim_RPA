# sheet-service

Stateless Go service that reads and writes spreadsheet rows in bounded batches. Sources sit behind one interface (`internal/sheet.Source`), so the engine and API work the same for every source.

Run: `go run .` (port `5090`). Test: `go test ./...`.

## Settings

All settings, including the port and every credential, are read from environment variables. At start the service also loads a `.env` file, from the folder of the executable and then from the working directory (`SHEET_SERVICE_ENV_FILE` names another file). A real environment variable overrides the same name in the file. Copy `.env.example` to `.env` and fill in what you use; `.env` is git-ignored.

| Variable | Use |
|---|---|
| `SHEET_SERVICE_PORT` | listening port (default `5090`) |
| `GOOGLE_APPLICATION_CREDENTIALS` / `GOOGLE_ACCESS_TOKEN` | Google Sheets |
| `MS_TENANT_ID`, `MS_CLIENT_ID` | OneDrive and SharePoint: the Entra app registration |
| `MS_SCOPES` | optional; default `Files.ReadWrite.All offline_access` |
| `MS_ACCESS_TOKEN` | optional ready token, used instead of the saved sign-in |
| `SHEET_SERVICE_DB` | SQLite file holding the saved Microsoft sign-in (default `sheet-service.db` in the working directory) |

Setup guides: [Google Sheets](docs/google-sheets-setup.md) and [Microsoft SharePoint / OneDrive](docs/microsoft-sharepoint-setup.md).

## Microsoft sign-in

The Excel range API of Microsoft Graph, which reads only the rows asked for and writes only the cells sent, accepts signed-in users only. The service therefore signs in once as a service account and keeps that sign-in alive:

1. In the Entra app registration: add the delegated permission `Files.ReadWrite.All` (admin consent may be needed), and turn on *Allow public client flows*. The Application (client) ID and Directory (tenant) ID go in `MS_CLIENT_ID` and `MS_TENANT_ID`.
2. Run `sheet-service login` (optionally `-name <connection>`). It prints a code and a link; open the link on any device, enter the code and sign in as the service account. The access token and refresh token are saved in the SQLite file named by `SHEET_SERVICE_DB`. The tokens are stored as plain text.
3. From then on the service renews the access token (about an hour) from the refresh token by itself, and saves the new refresh token Microsoft returns each time. Nobody signs in again unless Microsoft stops accepting the refresh token (password change, revocation by an admin, a Conditional Access policy, or a long period of no use). Requests then fail with `401` and the message `microsoft sign-in is missing or expired; run: sheet-service login`.

A request can name a saved sign-in with `"connection": "<name>"` inside `source` (default `default`).

## Sources

A request names the file with `path` only; the source type is worked out from it (or set `type` to override). Credentials are never sent in a request; they come from environment variables.

| Path | Type | Credentials (environment) |
|---|---|---|
| `C:\data\book.xlsx`, `\\server\share\book.xlsx` | `xlsx` | none |
| `https://docs.google.com/spreadsheets/d/<id>/...` or a bare spreadsheet ID | `gsheet` | `GOOGLE_APPLICATION_CREDENTIALS` (service-account key file) or `GOOGLE_ACCESS_TOKEN` |
| `https://<tenant>.sharepoint.com/...`, `https://onedrive.live.com/...`, `https://1drv.ms/...` | `graph` | `MS_TENANT_ID`, `MS_CLIENT_ID` and a saved sign-in (`sheet-service login`), or `MS_ACCESS_TOKEN` |

Every request also gives `sheet` (the worksheet name) and optionally `headerRow` (default 1).

Setup notes:
- Google: share the spreadsheet with the service account's e-mail address.
- Microsoft: see [Microsoft sign-in](#microsoft-sign-in).

## API

`POST /batch` reads rows `startRow..endRow` (inclusive). `startRow` defaults to the row below the header, `endRow` to `startRow + 999`, and a batch is capped at 5000 rows.

```json
{
  "source": { "path": "C:\\data\\book.xlsx", "sheet": "Category_Request" },
  "startRow": 2, "endRow": 1001,
  "pending": { "column": "Approved_by_Finance", "doneValues": ["Approved", "Rejected"] }
}
```

`pending` is optional. When present, only rows whose `column` is empty or not in `doneValues` are returned. The response carries `rows` (each with its sheet `row` number), `scanned`, `count`, `endOfData` and `nextStartRow`. Repeat with `startRow = nextStartRow` until `endOfData` is true.

`POST /update` writes any number of rows, each with any number of columns, in one call. Rows are addressed by row number and columns by header name:

```json
{
  "source": { "path": "C:\\data\\book.xlsx", "sheet": "Category_Request" },
  "rows": [
    { "row": 32, "values": { "Approved_by_Finance": "Approved", "Bot_Status": "SUCCESS", "Bot_Comments": "done" } },
    { "row": 33, "values": { "Bot_Status": "FAILED" } }
  ]
}
```

The older single-cell form (`"updates": [ { "row": 32, "column": "Bot_Status", "value": "SUCCESS" } ]`) is also accepted, alone or together with `rows`. The response is `{"rows": 2, "cells": 4}`. If any row or column in the call is invalid, nothing is written. A `409` means the file changed after it was read (see below).

`POST /cell` reads one value: the cell in `column` (a header name) on sheet row `row`. The reply is flat, so a script reads it as `${name.value}`:

```json
{ "source": { "path": "C:/data/book.xlsx", "sheet": "Invoices" }, "row": 2, "column": "InvoiceNumber", "required": true }
```

The response is `{"row": 2, "column": "InvoiceNumber", "value": "349236", "found": true}`. An unknown column is a `400`. An empty cell returns `"value": ""` and `"found": false`, unless `required` is true, in which case it is a `422`, so a flow that cannot continue without the value stops instead of carrying on.

`GET /health` returns `{"status":"ok"}`.

## Behaviour to know

- Row numbers are valid for the read that produced them; nothing is kept between runs.
- `endOfData` means the requested range ran past the last non-empty row. It assumes the data has no blank gap as long as the rest of the range.
- `xlsx`: reading streams the file, so memory depends on the batch size. Writing rewrites the whole file, and the file must not be open in Excel.
- `gsheet`: each call asks the API for only the rows needed. Google allows 60 reads and 60 writes per minute per user (300 per project), and a batch counts as one request.
- `graph` (OneDrive and SharePoint): reads use the Excel range API for only the requested rows (a batch is cut at the last used row), and the header is read separately. Writes send the cells in a workbook session; neighbouring rows go in one rectangle and cells that are not changing are sent as `null`, which Graph ignores. The file is never downloaded or uploaded as a whole. Throttled calls (`429`, `503`, `504`) are retried after the delay Graph asks for. Only `.xlsx` workbooks work. Values written are interpreted as typed in Excel, so `"1/1/2016"` becomes a date and `"100"` a number.
- Adding a source means implementing `ReadRows` and `Write` and registering its type in `sheet.Open`.
