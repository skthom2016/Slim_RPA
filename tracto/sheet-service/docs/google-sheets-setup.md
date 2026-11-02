# Google Sheets setup

The service reads and writes Google Sheets as a **service account**: a robot Google identity with its own e-mail address and key file. No person signs in while flows run. Setup is done once.

## 1. Create or pick a Google Cloud project

1. Open <https://console.cloud.google.com> and sign in with the Google account that will own the setup.
2. Use the project selector at the top to create a project (for example `tracto-rpa`) or choose an existing one.

## 2. Enable the Google Sheets API

1. Go to **APIs & Services → Library**.
2. Search for **Google Sheets API**, open it and click **Enable**.
3. Wait a minute or two before the first call.

Without this step every request fails with `403 ... Google Sheets API has not been used in project ... or it is disabled`.

## 3. Create the service account and its key

1. Go to **APIs & Services → Credentials → Create credentials → Service account**. If the wizard asks what data the application will access, choose **Application data** (the robot account), not **User data**.
2. Enter a name (for example `tractorpa`) and click **Create and continue**. No project roles are needed; click **Done**.
3. Open the new service account, go to the **Keys** tab and choose **Add key → Create new key → JSON**. A `.json` file downloads.
4. Move the file somewhere safe, outside the repository, for example `C:\Users\<you>\Documents\tractoGoogleKey\key.json`. Anyone with this file can act as the service account.
5. Open the file and note the value of `client_email`. It looks like `name@project-id.iam.gserviceaccount.com`.

## 4. Share each spreadsheet with the service account

1. Open the Google Sheet and click **Share**.
2. Paste the `client_email` address and set the role to **Editor**.
3. Clear *Notify people* if you like and click **Share**.

A sheet that has not been shared with this address cannot be read or written.

## 5. The file must be a native Google Sheet

The Sheets API does not work on an Excel file that was uploaded to Google Drive and opened in Sheets. Such a file shows `.xlsx` next to its name, and its link contains `rtpof=true`. Requests on it fail with `400 This operation is not supported for this document. The document must not be an Office file`.

To convert it:

1. Open the uploaded file in Google Sheets.
2. Choose **File → Save as Google Sheets**. A new copy opens in a new tab.
3. Share the **new copy** with the service account as Editor (sharing does not carry over).
4. Use the new copy's link.

## 6. Configure the service

Add the key path to `sheet-service/.env`:

```
GOOGLE_APPLICATION_CREDENTIALS=C:\Users\<you>\Documents\tractoGoogleKey\key.json
```

Restart the service. It logs `settings loaded from ...` when it reads the file.

## 7. Use it

Pass the spreadsheet link as `path` and the worksheet tab name as `sheet`. Row 1 holds the column headers.

```json
{
  "source": {
    "path": "https://docs.google.com/spreadsheets/d/<spreadsheet-id>/edit",
    "sheet": "Items"
  },
  "startRow": 2,
  "endRow": 21
}
```

The same `source` works with `POST /batch`, `POST /update` and `POST /apply`.

## Limits

Google allows 60 read requests and 60 write requests per minute per user per project. One `/update` or `/apply` call counts as one write request, however many rows and columns it carries.

## Troubleshooting

| Message | Cause and fix |
|---|---|
| `403 ... Google Sheets API has not been used in project ... or it is disabled` | Enable the API (step 2) and wait a few minutes. |
| `400 This operation is not supported for this document. The document must not be an Office file` | The file is an uploaded `.xlsx`. Convert it (step 5). |
| `403 The caller does not have permission` | The sheet is not shared with the service account as Editor (step 4). |
| `404 Requested entity was not found` | The link or spreadsheet ID is wrong. |
| `400 Unable to parse range` | The `sheet` value does not match a tab name exactly. |
| `google credentials missing: set GOOGLE_APPLICATION_CREDENTIALS or GOOGLE_ACCESS_TOKEN` | `.env` has no key path, or the service was started from a place where it cannot find `.env`. |
