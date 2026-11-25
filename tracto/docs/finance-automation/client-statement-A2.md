# Process A2: One bank payment file covers every bill selected for the run

Created: 2026-11-25 | Last updated: 2026-11-25

## Feasibility

**This process is fully possible with our automation platform**, on one assumption about data:

> The input data (the selected bills and the payees' bank details) can be read, **either through the accounting system itself (its API or database) or as files in an agreed folder**, and the output (the bank payment file and the run summary) is written to an agreed folder or library.

Every automated stage in the process description can be delivered on that basis. The [Points to confirm](#points-to-confirm) below, chiefly the bank's file format, decide which route is used and the exact layout of the file. They do not change whether the process can be done.

The automation never signs in to the bank. The finance officer keeps full control of what is paid and of the upload to the bank.

## What changes for the finance team

| Stage | Today | After automation |
|---|---|---|
| 1 Select bills | The finance officer selects the bills to pay this cycle and sets the payment date | **Unchanged.** The finance officer selects the bills and the payment date, then starts the run |
| 2 Read selection | | **Automated.** The selected bills are identified, whatever their source: bills that came from staff claims and bills entered by finance |
| 3 Get payee details | Each payee's bank details are looked up | **Automated.** Each payee's bank details are read from the maintained record |
| 4 Build file | Each payment is keyed into the bank portal, line by line | **Automated.** One file is produced in the bank's format, with one line per payee and the totals checked |
| 5 Save and notify | | **Automated.** The file is saved to the shared library and finance is notified |
| 6 Check and upload | | **Unchanged.** The finance officer checks the file and uploads it to the bank portal for authorisation |

## What the finance team gets

- No line-by-line keying of payments into the bank portal.
- One file per payment run, in the bank's own format, with bills for the same payee combined into one payment.
- The line count and the total are checked before the file is saved, and stated in the notification, so the officer can compare them with the bank portal before authorising.
- Any payee whose bank details are missing or invalid is listed with the reason, instead of being sent to the bank.
- No bill is ever included in two payment files.
- Bank account numbers do not appear in notifications.

## Example

The finance officer selects five bills for payment on 28 November and starts the run. The result:

> Payment file PAY_20261128_1 saved to the shared library: 2 payments, total 734.90, from 4 bills. 1 bill not included: INV-2207 City Catering, 600.00, no account number on record.

Priya Nair's two claim bills (193.40 and 41.00) are paid as one line of 234.40, and Office Hub's two supplier bills (320.00 and 180.50) as one line of 500.50. The officer checks the totals, uploads the file to the bank portal and authorises it. City Catering's bill is paid in a later run once its bank details are added.

## Points to confirm

The answers decide which data route is used (API, database or files in an agreed folder) and the exact layout of the bank's file. They do not change the feasibility above.

**Getting the data**

1. How can the automation get the bills selected for a payment run: through an API, by reading a database, or from an export file placed in a folder? If files: in what format, and which folder?
2. How does the finance officer mark the bills for a run (a payment date, a batch, a status or a tag), so the automation can tell which ones were selected?
3. How can the automation get each payee's bank details: from the same source as the bills (API, database or export), or from a separate record such as a spreadsheet in a folder? Who keeps them up to date?

**Giving the data back**

4. Which file format does the bank accept for bulk payments? Can the bank's file specification and a sample file it has accepted be provided?
5. Where should the payment files be saved (a SharePoint library or a network folder), and who may open them?
6. How should finance be notified of each run (email, Teams), and who should receive it?
7. After the bank has made the payments, should the automation mark the bills as paid? If so, how: API, database, or an import file?
8. Can a dedicated account, or access to the API, database or folders above, be provided for the automation?

**The payment file**

9. Should bills for the same payee always be combined into one payment, or only when they share the same payment date?
10. If one payee in a run has missing or invalid bank details: should that payee be left out and the rest of the file produced, or should the whole file be stopped?
11. Can the accounting system already produce a bank payment file from a batch of bills? If so, is that file accepted by the bank?

**Running the process**

12. How often are payment runs made, and how many bills and payees are typical per run?
