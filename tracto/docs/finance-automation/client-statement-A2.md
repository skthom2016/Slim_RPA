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

**The bank file**

1. Which bank is used, and which file format does its portal accept for bulk payments? Can the bank's file specification and a sample file it has accepted be provided?
2. Can the accounting system already produce a bank payment file from a batch of bills? If so, is that file accepted by the bank?

**The selection**

3. How does the finance officer mark the bills for a payment run in the accounting system: a payment date, a batch, a status or a tag?
4. Should bills for the same payee always be combined into one payment, or only when they share the same payment date?

**Payee bank details**

5. Where are payee bank details maintained: in the accounting system's contacts or in a separate record? Who keeps them up to date?
6. If one payee in a run has missing or invalid bank details: should that payee be left out and the rest of the file produced, or should the whole file be stopped?

**Systems and access**

7. Which accounting system is used (product name, cloud or on-premises), and which data route is available: an API, read access to its database, or an export of the selected bills and payee details to a folder? Can a dedicated account or access be provided for the automation?

**Saving and notifying**

8. Where should the payment files be saved (SharePoint site and folder, or network folder), and who may open them?
9. How should finance be notified (email, Teams), and who should receive the notification?
10. After the bank has made the payments, should the automation mark the bills as paid in the accounting system, or does finance do that?

**Running the process**

11. How often are payment runs made, and how many bills and payees are typical per run?
