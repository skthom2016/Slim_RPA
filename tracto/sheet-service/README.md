# sheet-service

Stateless Go service that reads and writes spreadsheet rows in bounded batches. Sources sit behind one interface (`internal/sheet.Source`), so the engine and API work the same for every source.

Run: `go run .` (port `5090`). Test: `go test ./...`.

## Settings

All settings, including the port and every credential, are read from environment variables. At start the service also loads a `.env` file, from the folder of the executable and then from the working directory (`SHEET_SERVICE_ENV_FILE` names another file). A real environment variable overrides the same name in the file. Copy `.env.example` to `.env` and fill in what you use; `.env` is git-ignored.

| Variable | Use |
|---|---|
| `SHEET_SERVICE_PORT` | listening port (default `5090`) |
| `GOOGLE_APPLICATION_CREDENTIALS` / `GOOGLE_ACCESS_TOKEN` | Google Sheets |
| `MS_TENANT_ID`, `MS_CLIENT_ID`, `MS_CLIENT_SECRET` / `MS_ACCESS_TOKEN` | OneDrive and SharePoint |

## Sources

A request names the file with `path` only; the source type is worked out from it (or set `type` to override). Credentials are never sent in a request; they come from environment variables.

| Path | Type | Credentials (environment) |
|---|---|---|
| `C:\data\book.xlsx`, `\\server\share\book.xlsx` | `xlsx` | none |
| `https://docs.google.com/spreadsheets/d/<id>/...` or a bare spreadsheet ID | `gsheet` | `GOOGLE_APPLICATION_CREDENTIALS` (service-account key file) or `GOOGLE_ACCESS_TOKEN` |
| `https://<tenant>.sharepoint.com/...`, `https://onedrive.live.com/...`, `https://1drv.ms/...` | `graph` | `MS_TENANT_ID`, `MS_CLIENT_ID`, `MS_CLIENT_SECRET` (app-only login) or `MS_ACCESS_TOKEN` |

Every request also gives `sheet` (the worksheet name) and optionally `headerRow` (default 1).

Setup notes:
- Google: share the spreadsheet with the service account's e-mail address.
- Microsoft: the app registration needs the application permission `Files.ReadWrite.All` (or `Sites.ReadWrite.All`) with admin consent.

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

`GET /health` returns `{"status":"ok"}`.

## Behaviour to know

- Row numbers are valid for the read that produced them; nothing is kept between runs.
- `endOfData` means the requested range ran past the last non-empty row. It assumes the data has no blank gap as long as the rest of the range.
- `xlsx`: reading streams the file, so memory depends on the batch size. Writing rewrites the whole file, and the file must not be open in Excel.
- `gsheet`: each call asks the API for only the rows needed. Google allows 60 reads and 60 writes per minute per user (300 per project), and a batch counts as one request.
- `graph` (OneDrive and SharePoint): the Excel range API of Microsoft Graph does not accept app-only logins, so the service downloads a copy of the workbook, reads and edits the copy, and uploads it back. The copy is kept in the system temp folder (`sheet-service`), so reading a large sheet in batches downloads the file once. A write is refused with a conflict when the cloud version differs from the one that was read, and the next read downloads the new version. Uploads are limited to 250 MB, and replacing a file that has a sensitivity label is not possible with an app-only login.
- Adding a source means implementing `ReadRows` and `Write` and registering its type in `sheet.Open`.
