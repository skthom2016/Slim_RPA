# Microsoft SharePoint / OneDrive setup

The service reads and writes `.xlsx` workbooks in SharePoint and OneDrive through the Excel API of Microsoft Graph. Only the rows asked for are read and only the cells sent are written; the workbook is never downloaded or uploaded as a whole.

## How authentication works

### Why a sign-in is needed

The Excel API of Microsoft Graph works on behalf of a **signed-in user**. It does not accept an application secret on its own. The service therefore uses a dedicated service account, signs in as that account once, and keeps the sign-in alive without further human involvement.

### The two parts

| Part | Who | When | What happens |
|---|---|---|---|
| **One-time sign-in** | A person | Once, before the first flow runs | The person runs `sheet-service login`, enters a short code in a browser, signs in as the service account and approves the permissions. The service saves the result. |
| **Automatic renewal** | The service | Every time a flow calls it | The service uses the saved sign-in, and renews it by itself when it is close to expiring. No person and no browser are involved. |

The sign-in is **not** repeated for each flow run, and it cannot happen during a run: a flow may run at night or on a schedule with nobody present, and the sign-in needs a person to enter a code and complete multi-factor authentication.

### What is saved

After the one-time sign-in the service stores two tokens in a SQLite file (`SHEET_SERVICE_DB`):

- **Access token.** Proves the sign-in to Microsoft Graph. It is valid for about one hour.
- **Refresh token.** Allows the service to obtain a new access token without a person. Each time it is used, Microsoft returns a new refresh token, and the service saves the new one in place of the old one. The default lifetime of a refresh token is 90 days.

### What happens when a flow runs

1. The flow sends a request to the service (`/batch`, `/update` or `/apply`) with the SharePoint file link. The flow never handles tokens.
2. The service reads the saved access token. If it has more than 5 minutes left, it is used as it is.
3. Otherwise the service sends the refresh token to Microsoft, receives a new access token and a new refresh token, and saves both.
4. The service calls the Excel API for the requested rows or cells and returns the result to the flow.

This continues unattended for as long as Microsoft accepts the saved refresh token.

### When the sign-in has to be repeated

A person must run `sheet-service login` again when Microsoft no longer accepts the saved refresh token. The usual causes are:

- the service account's password is changed or reset,
- an administrator or the user revokes the account's sessions,
- a Conditional Access or sign-in frequency policy in the organisation requires a new sign-in,
- the refresh token expires after a long period without use (default lifetime 90 days).

When this happens, requests fail with HTTP `401` and the message:

```
microsoft sign-in is missing or expired; run: sheet-service login
```

The failing flow step shows this message in its result. Once the sign-in is repeated, flows work again without any other change. Nothing can repeat the sign-in automatically, because the person signing in is the proof of identity.

### Security of the saved sign-in

- The refresh token acts like a password for the service account's files. Anyone who has the database file can use it.
- In the current version the tokens are stored as plain text in the SQLite file. Keep the file on the server that runs the service, outside any shared or version-controlled folder (`*.db` is ignored by git), and restrict access to it with operating-system permissions.
- Use a dedicated account that has access only to the files the flows need. The permission `Files.ReadWrite.All` is limited to what that account can open.
- To remove a saved sign-in, delete the database file (or revoke the account's sessions in Microsoft 365) and run `sheet-service login` again.

## 1. Prepare a service account

Use a dedicated Microsoft 365 account (not a personal one) that can open and edit the workbook in SharePoint. Give it Edit permission on the file, or on the document library that holds it. If the organisation enforces multi-factor authentication on this account, the person signing in completes it during the one-time sign-in.

## 2. Register the application

1. Open the Microsoft Entra admin center (<https://entra.microsoft.com>) and go to **Identity → Applications → App registrations**.
2. Click **New registration**.
3. Enter a name (for example `tracto-sheet-service`).
4. Under **Supported account types** choose **Accounts in this organizational directory only**.
5. Leave **Redirect URI** empty and click **Register**.
6. On the **Overview** page copy:
   - **Application (client) ID**
   - **Directory (tenant) ID**

## 3. Allow public client flows

The sign-in uses a device code, which belongs to the public client flows.

1. In the app, open **Authentication**.
2. Find **Allow public client flows** (under *Advanced settings*) and set it to **Yes**.
3. Click **Save**.

## 4. Add the permission

1. Open **API permissions → Add a permission → Microsoft Graph → Delegated permissions**.
2. Search for **Files.ReadWrite.All** and tick it. (`offline_access`, needed for the refresh token, is requested at sign-in.)
3. Click **Add permissions**.
4. If **Grant admin consent for <your organisation>** is available to you, click it. Otherwise ask an administrator to grant consent. Without consent the sign-in stops with a message that approval is required.

Delegated permissions let the service act only within what the signed-in service account can already access.

## 5. Configure the service

Put the two IDs in `sheet-service/.env`:

```
MS_TENANT_ID=<Directory (tenant) ID>
MS_CLIENT_ID=<Application (client) ID>
SHEET_SERVICE_DB=C:\path\to\sheet-service.db
```

`SHEET_SERVICE_DB` is the SQLite file that holds the saved sign-in. Leave it out to use `sheet-service.db` in the folder the service is started from. If the service and the `login` command are started from different folders, set it explicitly so both use the same file.

`MS_SCOPES` is optional and defaults to `Files.ReadWrite.All offline_access`.

## 6. Sign in once

Run the command on the machine where the service runs, from the `sheet-service` folder:

```powershell
cd <path>\sheet-service
.\sheet-service.exe login
```

What happens:

1. The command prints a short code and a link, for example:

   ```
   To sign in, use a web browser to open the page https://microsoft.com/devicelogin
   and enter the code ABCD1234 to authenticate.
   ```

2. Open the link in any browser, on any device, and enter the code.
3. Sign in as the service account, complete multi-factor authentication if asked, and accept the permissions.
4. The command finishes with:

   ```
   Signed in. Saved as "default" in <database path>
   ```

The code is valid for about 15 minutes. If it expires, run the command again.

To keep more than one sign-in, for example for different accounts, give each a name:

```powershell
.\sheet-service.exe login -name finance
```

A request picks one with `"connection": "finance"` inside `source`. Without `connection`, the sign-in called `default` is used.

## 7. Start the service

```powershell
cd <path>\sheet-service
.\sheet-service.exe
```

The service listens on port `5090` (change it with `SHEET_SERVICE_PORT`). It logs `settings loaded from ...` when it reads `.env`. Check it with:

```powershell
Invoke-RestMethod http://localhost:5090/health
```

## 8. Use it

Pass a link to the workbook as `path` and the worksheet tab name as `sheet`. Use the link from **Share → Copy link** in SharePoint, or the file's address. Row 1 holds the column headers.

```json
{
  "source": {
    "path": "https://<tenant>.sharepoint.com/sites/<site>/Shared%20Documents/items.xlsx",
    "sheet": "Items"
  },
  "startRow": 2,
  "endRow": 21
}
```

Example call:

```powershell
$body = @{
  source   = @{ path = "https://<tenant>.sharepoint.com/sites/<site>/Shared%20Documents/items.xlsx"; sheet = "Items" }
  startRow = 2
  endRow   = 21
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri http://localhost:5090/batch -ContentType application/json -Body $body
```

The same `source` works with `POST /batch`, `POST /update` and `POST /apply`.

Behaviour to know:

- Only `.xlsx` workbooks are supported.
- Values written are interpreted as if typed in Excel: `"1/1/2016"` becomes a date and `"100"` a number.
- Neighbouring rows are written in one call; cells that are not changing are sent as `null`, which Excel ignores.
- Throttled requests (`429`, `503`, `504`) are retried after the delay Microsoft asks for.

## Troubleshooting

| Message | Cause and fix |
|---|---|
| `microsoft settings missing: set MS_TENANT_ID and MS_CLIENT_ID` | `.env` has no IDs, or the service was started from a place where it cannot find `.env`. |
| Sign-in rejects the request, or reports that the app is not a public client | **Allow public client flows** is not set to Yes (step 3). |
| Sign-in says approval or admin consent is required | Grant admin consent for `Files.ReadWrite.All` (step 4). |
| `device code request failed` | The client ID or tenant ID is wrong. Use the tenant ID or domain, not `common`. |
| `sign-in code expired before sign-in finished; run login again` | The code was not entered within about 15 minutes. Run `sheet-service login` again. |
| `microsoft sign-in is missing or expired; run: sheet-service login` | No sign-in is saved, or Microsoft no longer accepts it. Run `sheet-service login`. If the service and the command use different folders, set `SHEET_SERVICE_DB` explicitly. |
| `graph api 403` or `404` on the file | The service account cannot open the file, or the link is wrong. |
| `graph api 400` mentioning the workbook or an unsupported file | The file is not an `.xlsx` workbook. |
