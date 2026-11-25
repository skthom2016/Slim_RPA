# Process B: Online donations reach the donation system without manual collection

Created: 2026-11-25 | Last updated: 2026-11-25

## Feasibility

**This process is possible with our automation platform**, on the same assumption about data as processes A1 and A2:

> The donation management system can be read and written **either through the system itself (its API or database) or as files in an agreed folder** (an import file for new donations and for date corrections), and the outputs (the consolidated file, the reconciliation report) are written to an agreed folder or library.

On that basis, merging the reports, loading the donations, correcting the dates and reconciling can all be delivered. The open question is **the sign-in to the giving portals**, which must be validated first:

1. **How each giving portal signs in, and how long it stays signed in.** If a portal needs a person at every run, that portal cannot be collected unattended. See [Validation 1](#validation-1-signing-in-to-the-giving-portals).
2. **Where the settlement dates come from.** The month-end date correction needs, for every donation, the date its money was settled; the source has to be identified. See [Validation 2](#validation-2-where-the-settlement-dates-come-from).

If both are confirmed without issue, the whole process is fully possible.

## What changes for the finance team

| Stage | Today | After automation |
|---|---|---|
| 1 Collect from portals | Donation reports are downloaded from each portal by hand | **Automated.** The automation opens each giving portal and downloads the donation reports. The finance officer provides a one-time passcode only where a portal requires one (see below) |
| 2 Consolidate | Reports in different layouts are combined by hand | **Automated.** The data from all portals is merged into one common format |
| 3 Load donations | Donations are re-entered in the donation management system | **Automated.** The donations are loaded into the donation management system |
| 4 Month-end correction | Each donation's date is changed to its settlement date, one at a time | **Automated.** At month end, donation dates are corrected to settlement dates in bulk |
| 5 Reconcile | Totals are checked by hand | **Automated.** Portal totals are compared with the donation system's totals and every mismatch is reported. **The finance officer** reviews the report and resolves the exceptions |

## Validation 1: signing in to the giving portals

Each portal is used through a browser session. The finance officer signs in to each portal **once**, on the machine that runs the automation, entering the password and any one-time passcode. The automation then keeps that signed-in session and reuses it on every run, so no password or passcode is needed while the session lasts. When a portal finally ends the session, the run reports it and the officer signs in again.

Whether this works without a person at each run depends on each portal's behaviour:

| What to check for each portal | Why it matters | Effect if the answer is unfavourable |
|---|---|---|
| **How does it sign in?** Username and password, Google or Microsoft account, or a link sent by email | Decides how the sign-in is set up | None on its own; each method can be handled |
| **Does it show a CAPTCHA** ("I am not a robot") or another automated-browser check, at sign-in or later? | Our platform does not bypass these checks, and will not | A check shown only at sign-in is handled when the officer signs in. A check shown during normal use blocks that portal from being collected automatically |
| **Does it send a one-time passcode? Where to:** an email inbox, or a person's phone (SMS or authenticator app)? | A passcode sent to an email inbox can be read by the automation. A passcode sent to a phone needs the person who holds it | If a passcode to a phone is required **at every sign-in** and the session is not kept, a person is needed at every run |
| **Does it offer "remember this device" or "keep me signed in"?** | Lets the kept session survive between runs | Without it, the session may end between runs |
| **How long does a session last** (an hour, a day, weeks)? Does using it daily keep it alive? | Decides how often the officer has to sign in again | A session that ends within hours means signing in before every run, which defeats unattended running |
| **Does it ask for a passcode again before exporting** data, even when signed in? | A second passcode at export needs a person every time | That portal needs a person at every run |
| **Can the donation report, and the payout report, be downloaded directly?** Or is the export emailed later? In what format: CSV, Excel, PDF? | Decides how the reports are collected | An emailed export or a PDF report is still possible, by a different route |
| **Does the portal offer an API, or scheduled reports by email?** | Either avoids signing in through a browser altogether | None: this is the preferred route where available |
| **Do the portal's terms of use allow automated sign-in and download?** | Must be respected | If not allowed, that portal's reports are taken from an API, scheduled email, or a manual download |

**The outcome for each portal is one of three:**

- **Fully automatic:** no person at each run. The officer signs in again only when the portal ends the session, for example every few weeks.
- **Automatic with a passcode prompt:** the run pauses and asks the finance officer for the passcode, which takes seconds. The rest continues on its own.
- **Not collectable automatically:** the portal blocks automated browsers during use, or requires a person at every export. For that portal, its report is obtained through an API, a scheduled email, or a manual download. Every later stage, from consolidation to reconciliation, still runs automatically.

A simple test settles this before any build. Sign in to each portal once and keep the session, then check after 1, 3 and 7 days whether it is still signed in and the reports can still be downloaded.

## Validation 2: where the settlement dates come from

The month-end correction replaces each donation's date (when the donor gave) with its **settlement date** (when the portal paid that money to your bank account). The process description does not say where the settlement date is taken from. This must be confirmed first.

**Example.** Two donations given on 28 and 29 November are paid out by the portal together on 3 December. After the correction, both carry 3 December, the date the money reached the organisation.

The possible sources:

| Source | What it shows | Can it provide each donation's settlement date? |
|---|---|---|
| **The portal's payout (settlement) report**, if the portal has one | Each payout's date and the donations it contains | **Yes.** It links each donation to its payout. This is the expected source |
| **The bank statement** | The date each portal's payment arrived, as one total per payout (for example 145.50 on 3 December) | **Not on its own.** It does not say which donations a payment contains. It can confirm a date once the payout report has said which donations it covers |
| **The accounting system's bank feed** | The same per-payout totals as the bank statement | **Not on its own**, for the same reason |

Please also note that the reconciliation in stage 5, as described, compares **the portals' totals with the donation system's totals**. It does not use the bank statement. Checking that each payout actually arrived in the bank (a bank reconciliation) would be an addition, see question 18.

## What the finance team gets

- No manual downloads, re-entry or one-by-one date edits.
- All portals' donations in one consolidated file, loaded into the donation system once, never twice.
- Donation dates matching the settlement dates at month end.
- A reconciliation report showing what matches and every mismatch, for the officer to resolve.

## Points to confirm

**To validate first** (these decide whether the process runs unattended)

1. How many giving portals are there? For each one: how does it sign in, does it show a CAPTCHA, does it send a one-time passcode and to where, does it remember the device, and how long does a session stay signed in? (The checklist under Validation 1.)
2. For each portal: does it ask for a passcode again before exporting data?
3. **Where is the settlement date taken from today:** the portal's payout report, the bank statement, or somewhere else? Does each portal provide a payout or settlement report listing the donations in each payout?
4. Do the portals' terms of use allow automated sign-in and download?

**Getting the data from the portals**

5. For each portal: which report is used today, in what format (CSV, Excel, PDF), and can a sample be provided?
6. Can any portal provide its data without a sign-in in a browser: through an API, or as scheduled reports sent by email or to a folder?

**Giving the data to the donation management system**

7. How should new donations be delivered to the donation management system: through an API, by writing to a database, or as an import file placed in a folder? If files: in what format, and which folder?
8. How should the date corrections be delivered: the same way, or differently?
9. How can the automation read back what the system holds, for the reconciliation: API, database, or an export to a folder?
10. Can a dedicated account, or access to the API, database or folders above, be provided for the automation?

**Donations and dates**

11. How is a donor matched: by email address, donor ID or name? What should happen with a new donor?
12. Can a donation's date be changed after it has been recorded? Does that change if a tax receipt has already been issued or the month has been closed?
13. Should the settlement date replace the donation date, or be recorded in a separate field?

**Reconciliation**

14. What should be compared: totals per portal per day, per payout, or per donation?
15. How should portal fees be treated, and what difference is acceptable (for example rounding)?
16. Besides comparing the portals with the donation system, should the automation also check that each payout arrived in the bank? If so, how does the bank statement reach finance today?

**Running the process and delivering the results**

17. How often should the portals be collected (daily, weekly), and when is month end processed?
18. How many donations per month are typical?
19. Where should the consolidated file and the reconciliation report be saved, and how should finance be notified (email, Teams)?
