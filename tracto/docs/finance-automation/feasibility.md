# Finance automation on Tracto: requirement, conversion and feasibility

Created: 2026-11-25 | Last updated: 2026-11-25

This document explains the requirement in *Finance automation user journeys* (`finance-automation-user-journeys.pptx`), shows how every step of it is built on Tracto, lists everything that has to be added to Tracto for it, and states what cannot be done even with additions.

Contents:

1. [What the project is](#1-what-the-project-is)
2. [Verdict at a glance](#2-verdict-at-a-glance)
3. [How it runs on Tracto](#3-how-it-runs-on-tracto)
4. [Process A1: approved claims to draft bills](#4-process-a1-approved-claims-to-draft-bills)
5. [Process A2: one bank payment file per run](#5-process-a2-one-bank-payment-file-per-run)
6. [Process B: online donations into the donation system](#6-process-b-online-donations-into-the-donation-system)
7. [How each system can be reached](#7-how-each-system-can-be-reached)
8. [Everything to build](#8-everything-to-build)
9. [What cannot be done, even with additions](#9-what-cannot-be-done-even-with-additions)
10. [Risks](#10-risks)
11. [Open questions](#11-open-questions)
12. [Order of work](#12-order-of-work)

---

## 1. What the project is

### The organisation and its finance team

The deck describes the finance team of an organisation that pays staff expense claims, pays suppliers by bank transfer, and receives **donations through online giving portals**, which it records in a **donation management system**. That combination is typical of a charity or non-profit. The deck does not name the organisation or any of its systems.

### The project in one paragraph

The finance team does three routine jobs by hand: copying approved expense claims into the accounting system, keying payments into the bank's website one by one, and downloading and re-entering online donations. The project automates the copying between systems with Tracto. The finance officer still approves claims and bills, decides what to pay and uploads the payment file to the bank; Tracto does the work in between.

### The three processes in two lines each

The deck has three slides, one process on each.

| Slide | Process | Flow |
|---|---|---|
| 1 | **A1** Staff claims to draft bills | The officer approves staff expense claims in the claims platform. The automation picks them up, checks them, finds or creates the person in the accounting system and creates a draft bill with one coded line per expense; the officer then approves the bills |
| 2 | **A2** Bills to one bank payment file | The officer chooses which approved bills to pay and the payment date. The automation reads those bills and each payee's bank details, builds one file in the bank's format, saves it and tells finance; the officer checks it and uploads it to the bank |
| 3 | **B** Online donations to the donation system | The automation signs in to each giving portal (the officer types a passcode where one is asked for), downloads the donation reports, merges them and loads them into the donation system, and at month end corrects dates to settlement dates. It reports any mismatch between the portals and the system; the officer reviews that report |

### How the three processes relate

```
A1  claim approved -> draft bill created -> officer approves the bill
                                                     |
                                                     v
                                    bill waits for payment in the accounting system
                                                     |
A2                                  officer selects it for a payment run -> bank file -> officer uploads to the bank

B   giving portals -> donation reports -> donation system -> reconciliation      (separate; money coming in)
```

- **A1 and A2 are connected through the accounting system's data.** The bills A1 creates, once approved, are among the bills the officer chooses from in A2. A2 also pays bills finance entered by hand, so it does not depend on A1.
- **They are still two separate runs.** An officer step always sits between them (approving the bills, then choosing what to pay), and A2 runs once per payment cycle while A1 runs daily. One never starts the other. In Tracto they share the accounting connector.
- **B is independent.** It works on other systems (the giving portals and the donation system), and money flows the other way. It shares only the common parts in Tracto: the Go service, notifications and the shared library.
- **For the build:** A1 and A2 belong together; B is a separate track that can run in parallel or later. A2 can be tested on its own with bills keyed by hand.

### The systems involved

The team works across five kinds of system:

| System (as named in the deck) | What the team does in it today |
|---|---|
| **Claims platform** | Staff submit expense claims; a finance officer checks and approves them |
| **Accounting system** | Holds contacts (people and suppliers), bills to be paid, and the payment records. Approved claims are typed in again here as bills |
| **Bank portal** | The bank's website, where payment files are uploaded and payments authorised |
| **Online giving portals** | Websites through which donors give. Each one offers a report of the donations it received |
| **Donation management system** | Records each donation against its donor; the books for donations are kept here |

### The problem each process solves

The deck's own words for what each process removes are in quotes.

| Process | Manual work today | After automation |
|---|---|---|
| **A1** Approved staff claims reach the accounting system | Each approved claim is typed into the accounting system again as a bill, line by line. "No manual re-keying of claims into the accounting system" | Approved claims become draft bills automatically, one coded line per expense item. The officer only reviews and approves the drafts |
| **A2** One bank payment file per payment run | Each payment is keyed into the bank portal line by line. "No line-by-line keying of payments into the bank portal" | The officer selects the bills to pay; one file in the bank's format is produced; the officer uploads it |
| **B** Online donations reach the donation system | Reports are downloaded from each portal by hand, re-entered, and at month end each donation's date is corrected to the date the money was settled, one at a time. "No manual downloads, re-entry or one-by-one date edits" | Reports are collected and merged automatically, loaded into the donation system, dates are corrected in bulk, and a reconciliation report shows any mismatch |

### Two lanes: the officer's tasks and the automation in between

Every slide has two lanes. The **finance officer** keeps a task at the start and a task at the end, as today. The **automation** does everything between them. Tracto's job is the middle lane, and the two hand-overs: knowing that the officer's first task is done, and telling the officer that the automated work is ready for the last task.

Stage numbers are the deck's.

| Process | Officer, before | Automation, in between | Officer, after | Hand-over in: how the automation starts | Hand-over out: how the officer knows |
|---|---|---|---|---|---|
| **A1** | Stage 1: verifies and approves claims in the claims platform | Stage 2: picks up approved claims and checks each is complete. Stage 3: finds or creates the claimant's contact. Stage 4: creates a draft bill with one coded line per item. Across 2 to 4: flags any claim it could not process, with the reason | Stage 5: reviews and approves the draft bills in the accounting system | A schedule (for example twice a day) picks up whatever has been approved since the last run; the officer does nothing extra | The draft bills are waiting in the accounting system, and a message lists what was created and which claims need attention |
| **A2** | Stage 1: selects the bills to pay this cycle and sets the payment date | Stage 2: reads the selection. Stage 3: reads each payee's bank details. Stage 4: builds one bank-format file. Stage 5: saves the file and notifies finance | Stage 6: checks the file and uploads it to the bank portal | The officer presses **Run** in the Tracto console after selecting (with the payment date), so the file is built for exactly that selection | The file is in the shared library and a message gives its name, line count and total, and any payee left out with the reason |
| **B** | Stage 1, only where a portal needs it: provides a one-time passcode | Stage 1: signs in to each portal and downloads the reports. Stage 2: merges them into one format. Stage 3: loads the donations. Stage 4 (month end): corrects dates to settlement dates. Stage 5: compares totals and reports mismatches | Stage 5: reviews the reconciliation report and resolves the exceptions | A schedule starts the collection, and a month-end schedule the date correction. A passcode is asked for in the console while the run waits | The reconciliation report is in the shared library and a message gives the matched count and the differences |

Other boundaries set by the deck:

- **The automation never signs in to the bank.** The officer uploads the file (A2 stage 6).
- Every stage carries "Subject to technical validation": how each system is reached (API, file or screen) is not decided yet. Section 7 covers each case.

### Terms

| Term | Meaning here |
|---|---|
| Claim | A staff member's request to be repaid for expenses; one claim has one or more expense items |
| Draft bill | A bill created in the accounting system but not yet approved; after approval it waits for payment |
| Contact | The accounting system's record of a person or supplier who is paid |
| Coded line | A bill line carrying the account code (and tax rate, tracking category) the expense is booked to |
| Payment run | One payment cycle: the bills chosen to be paid on one date |
| Bank payment file | A file in the bank's own format listing payments; uploaded to the bank portal and authorised there |
| Settlement date | The date a portal actually paid the donated money to the organisation, as opposed to the date the donor gave |
| Reconciliation | Checking that the portals' totals and the donation system's totals agree, and listing what does not |

---

## 2. Verdict at a glance

| Process | Verdict | Main condition |
|---|---|---|
| **A1** | **Possible.** Every step can be built; none is impossible | The claims platform and the accounting system offer an API or import/export files. With web pages only, still possible, slower and more fragile |
| **A2** | **Possible.** Every step can be built; none is impossible | The bank's file specification; how the selection and the payee bank details can be read |
| **B** | **Possible, with two hard limits**: a portal that blocks automated browsers cannot be collected from, and a passcode sent to a person's phone still needs that person each time | Each portal's sign-in and export; how the donation system accepts and updates data |

**Not possible, even with additions** (section 9): getting past a CAPTCHA or bot detection; receiving a phone passcode without the person who holds the phone; signing in to the bank portal; changing a donation date where the donation system itself forbids it.

**Before building, check two things that could remove work** (q24, q25): some accounting systems already produce bank payment files from a batch of bills, and some claims platforms already have a direct integration with the accounting system. If either is available and acceptable, the matching part of A1 or A2 becomes configuration instead of a build.

Verdict key used in the step tables:

| Verdict | Meaning |
|---|---|
| **Today** | Works with Tracto as it is now, configuration only |
| **With addition** | Needs an addition from section 8. Ordinary engineering, no research risk |
| **Conditional** | Possible only if the system offers a particular way in (API, file, screen). The condition is stated |
| **Person** | Stays a person's step by design. Not a gap |
| **Not possible** | Cannot be done even with additions |

---

## 3. How it runs on Tracto

### The pattern

Every process follows the pattern Tracto already uses for batches:

1. A **schedule** (or a person pressing Run in the console) starts a **main job**: one `http.post` step that calls a Go endpoint and ends at once.
2. The **Go service** does the work: reads the source, loops over the items, calls the other systems, and, where a step must happen in a web page, queues a **child job** for the browser runtime and waits for it. Only Go waits; a job never waits for another job.
3. Each item gets a status (`Done` or `Failed`) and a reason, kept in PostgreSQL. A rerun processes only the unfinished items. Nothing is created twice: each item is keyed by its source id.
4. Results go where finance works: the accounting system, the donation system, a report workbook in the shared library, and a notification.
5. Every step appears in the run's log in the console with its result, through the step logging Tracto already has (Info, Warning, Error).

### Components

```
                 schedules / Run button
                          |
                    Tracto Orchestrator --------------------- console (runs, logs, "waiting for input")
                     |            |
         desktop worker      browser worker(s)
         (one-step mains)    (portal downloads; screens of systems that have no API)
                     |            ^
                     v            | child jobs
             finance service (NEW, Go) -------------------- PostgreSQL, schema "finance" (NEW)
             |      |      |      |      |
   accounting  claims  donation  Microsoft   bank file builder,
   connector   conn.   loader    Graph       normaliser, reconciliation
       |         |        |      (library,
   accounting  claims  donation   mail)
   system      platform system
                          ^
             sheet service (EXISTS): reads and writes the configuration and report workbooks
```

- **Finance service** (new): a Go service in the same shape as the existing sheet service: its own port (for example 5095), its own PostgreSQL schema (`finance`), settings from a `.env` file, the same Orchestrator client and the same encrypted token store. It holds all three processes so connectors are shared. It could instead be added as packages of the sheet service; a separate service keeps the finance credentials apart.
- **Sheet service** (exists): reads the configuration workbook and writes the report workbooks, on SharePoint or a network share, without Excel.
- **Browser runtime** (exists, two actions added): used only where a system has no API or file route, and for the giving portals.
- **Desktop runtime** (exists): runs the one-step main jobs.

### Configuration workbook

One workbook, `finance-config.xlsx`, kept in the finance team's SharePoint library, edited by finance, read by the finance service through the sheet service. No code change is needed to change a rule.

| Sheet | Columns | Used by |
|---|---|---|
| `Settings` | key, value: library folder, notification recipients, time zone, payment-run defaults | All |
| `ExpenseCoding` | expense category, account code, tax rate, tracking category 1 and 2 | A1 |
| `ClaimRules` | rule, value: required fields, receipt required (yes/no), maximum amount per item, allowed age of an expense | A1 |
| `Payees` | payee id, account name, bank code, account number (only if bank details are not kept in the accounting system; access restricted) | A2 |
| `BankFile` | the bank's format settings: file type, originator details, batch reference rule | A2 |
| `Portals` | portal name, report page, browser profile name, how it signs in (`profile`, `password`, `passcode-mail`, `passcode-person`), mapping sheet name | B |
| `Map-<portal>` | source column, common field, transformation (date format, currency) | B |
| `Reconciliation` | grain (per day / per payout), tolerance, fee handling | B |

---

## 4. Process A1: approved claims to draft bills

**Starts** when claims are approved in the claims platform. **Ends** when the draft bills are approved in the accounting system.

### Steps and verdicts

| Stage (deck) | Lane | Task (from the deck) | How in Tracto | Verdict |
|---|---|---|---|---|
| 1 Approve claims | Officer | Verifies and approves claims in the claims platform, as today | Unchanged | **Person** |
| 2 Collect and check | Automation | Picks up approved claims and checks each one is complete | Claims connector reads approved, not yet transferred claims with their expense items; the claim rules check each one | **With addition**; **Conditional** on how claims leave the platform (section 7) |
| 3 Match contact | Automation | Finds the claimant's contact; creates one if it is missing | Accounting connector searches by the agreed key; creates the contact if none matches; stops the claim if two match | **With addition**; **Conditional** on an agreed matching key (q6) |
| 4 Create draft bill | Automation | Creates a draft bill with one coded line per expense item | Accounting connector creates the bill in draft, all lines in one call, codes from `ExpenseCoding`; the claim id is the bill reference | **With addition**; with web pages only, **Conditional** (a varying number of lines per bill, see section 7) |
| 2 to 4 | Automation | Flags any claim it could not process, with the reason | Status and reason per claim; the exceptions list in the notification and in the console | **With addition** (the per-item status already exists for sheet rows) |
| 5 Review and approve | Officer | Reviews and approves the draft bills | Unchanged, in the accounting system | **Person** |

### Worked example

The names, amounts and codes below are made up to show the flow; the real ones come from the claims platform and the coding table.

**Stage 1, officer.** On Monday the finance officer approves two claims in the claims platform:

```
Claim 5530  Priya Nair (employee 1042)    3 items
  1  Taxi to client meeting    18 Nov    42.50   category: Local travel      receipt attached
  2  Lunch with volunteers     19 Nov    86.00   category: Meals             receipt attached
  3  Printer cartridges        20 Nov    64.90   category: Office supplies   receipt attached

Claim 5531  Tom Reed (employee 1077)      2 items
  1  Train fare                21 Nov    23.40   category: Local travel      receipt attached
  2  Taxi (overseas)           22 Nov    55.00   category: Taxi (overseas)   receipt attached
```

**Stages 2 to 4, automation** (the 15:00 run picks both up):

| Stage | Claim 5530 | Claim 5531 |
|---|---|---|
| 2 Check | Complete: every item has a receipt, a known category and a positive amount | Item 2: category "Taxi (overseas)" is not in the coding table. **Stopped here** |
| 3 Contact | Employee 1042 found in the accounting system: "Priya Nair" | Not reached |
| 4 Draft bill | Draft bill created, reference `CLAIM-5530`, three coded lines (below) | Not created |
| Flag | | Recorded as `Failed`: "item 2: category 'Taxi (overseas)' has no account code" |

The draft bill for claim 5530, coded from `ExpenseCoding`:

```
Draft bill  CLAIM-5530   Contact: Priya Nair   Total 193.40
  Taxi to client meeting   42.50   account 6410 Local travel      tax: GST on expenses
  Lunch with volunteers    86.00   account 6420 Meals             tax: GST on expenses
  Printer cartridges       64.90   account 6110 Office supplies   tax: GST on expenses
```

The officer receives: "2 claims: 1 draft bill created (CLAIM-5530, 193.40). 1 needs attention: claim 5531, item 2 has no account code."

**Stage 5, officer.** The officer approves draft bill CLAIM-5530 in the accounting system. Finance adds "Taxi (overseas)" to the coding table; the next run picks claim 5531 up again (it is not yet in `claims_done`) and creates its bill. Claim 5530 is never billed twice: its reference `CLAIM-5530` is already recorded.

### Conversion in detail

**Tracto flow:** `claims-main` (desktop, one step), on a schedule, for example every working day at 09:00 and 15:00.

```json
{ "stepNumber": 1, "action": "http.post", "description": "Transfer approved claims to the accounting system",
  "parameters": { "url": "http://127.0.0.1:5095/claims/run/start", "body": "{}", "storeAs": "run" } }
```

**Endpoint:** `POST /claims/run/start` with an optional `{ "claimIds": [...], "dryRun": true }`. It replies at once:

```json
{ "runId": "cl-20261126-0900-1a2b3c", "state": "Running", "total": 12,
  "message": "12 approved claim(s) to transfer" }
```

`total` is 0 with the message "nothing to do: no approved claims waiting" when there is nothing new, so the main job's log says why nothing happened.

**What Go does, per run:**

1. Read the claim rules and expense coding from `finance-config.xlsx` (sheet service).
2. Ask the claims connector for claims in the approved state that are not in `finance.claims_done`.
3. For each claim, one at a time:
   1. **Check**: required fields present, every item has a category found in `ExpenseCoding`, amounts positive and within the limit, a receipt where required. A failed check marks the claim `Failed` with the reason (for example "item 3: category 'Taxi (overseas)' has no account code") and moves on.
   2. **Contact**: search the accounting system by the matching key. None: create it. One: use it. Two or more: `Failed`, "two contacts match employee 1042".
   3. **Draft bill**: one bill per claim (or per claimant per run, q8); reference = claim id; due date from `Settings`; one line per item with description, amount, account code, tax rate, tracking.
   4. **Record**: `finance.claims_done` (claim id, bill id, contact id, time); if the claims platform allows it, mark the claim transferred there.
4. Save the run summary (claims done, claims failed with reasons) as a small workbook in the library and send the notification: "12 claims: 11 draft bills created, 1 needs attention: claim 5531, item 3 has no account code."

**Stored per claim** (`finance.claim_items`): run id, claim id, claimant, total, state, contact id, bill id, reason, updated.

**Failure and rerun:**

- A claim that fails does not stop the others.
- Running again processes only claims not yet in `claims_done`; a claim fixed by finance is picked up on the next run.
- If the accounting system is unreachable, the run stops after three consecutive failures and says so, as the sheet batches already do.
- A bill reference equal to the claim id, checked before creating, makes a double bill impossible even if a run is interrupted between creating the bill and recording it.

**In the console:** the main run shows "12 approved claim(s) to transfer"; the run's log shows each claim's outcome; `GET /claims/run/{runId}` shows the per-claim table; `POST /claims/run/{runId}/cancel` stops after the current claim.

**Nothing in A1 is impossible.**

---

## 5. Process A2: one bank payment file per run

**Starts** when bills are selected for the payment cycle. **Ends** when the file is uploaded and sent for authorisation.

### Steps and verdicts

| Stage (deck) | Lane | Task (from the deck) | How in Tracto | Verdict |
|---|---|---|---|---|
| 1 Select bills | Officer | Selects the bills to pay this cycle and sets the payment date | Unchanged, in the accounting system; or in a payment-run workbook if the system cannot record a selection. Then presses **Run** in the Tracto console | **Person** |
| 2 Read selection | Automation | Identifies the selected bills, whatever their source | Accounting connector reads bills awaiting payment that carry the selection; bills from claims and bills keyed by hand are read the same way | **With addition**; **Conditional** on the selection being readable (q9). Workbook selection: **Today** |
| 3 Get payee details | Automation | Reads each payee's bank details from the maintained record | From the accounting system's contacts, or from the `Payees` sheet | **Today** (sheet) or **With addition** (accounting system) |
| 4 Build file | Automation | Builds one bank-format file: one line per payee, totals matched | Bank file builder | **With addition**; needs the bank's specification |
| 5 Save and notify | Automation | Saves the file to the shared library and notifies finance | Library upload and notification | **With addition** |
| 6 Check and upload | Officer | Checks the file and uploads it to the bank portal | Unchanged | **Person** |

### Worked example

Made-up bills and payees; the file layout is only an illustration, since the real one is set by the bank (q11).

**Stage 1, officer.** On Thursday the officer selects five approved bills for payment on 28 Nov and presses **Run**:

```
Bill         Payee           Amount   Source
CLAIM-5530   Priya Nair      193.40   staff claim (from A1)
CLAIM-5562   Priya Nair       41.00   staff claim (from A1)
INV-8812     Office Hub      320.00   supplier bill keyed by finance
INV-8840     Office Hub      180.50   supplier bill keyed by finance
INV-2207     City Catering   600.00   supplier bill keyed by finance
```

**Stages 2 to 5, automation:**

| Stage | What happens |
|---|---|
| 2 Read selection | The five bills with payment date 28 Nov are read, whichever way they were entered |
| 3 Payee details | Priya Nair and Office Hub have complete bank details. City Catering has no account number on record: **left out** with the reason |
| 4 Build file | Bills of the same payee become one line: Priya Nair 193.40 + 41.00 = 234.40; Office Hub 320.00 + 180.50 = 500.50. Control: 2 lines, total 734.90. The file is written, read back and checked against the same count and total |
| 5 Save and notify | Saved as `Payments/2026-11-28/PAY_20261128_1.csv`; finance is told |

The file (illustrative layout):

```
H,ORG-ACCOUNT,20261128,PAYRUN-20261128-1
D,Priya Nair,BANK-CODE,ACCOUNT-NO,234.40,CLAIM-5530 CLAIM-5562
D,Office Hub,BANK-CODE,ACCOUNT-NO,500.50,INV-8812 INV-8840
T,2,734.90
```

The message to finance: "Payment file PAY_20261128_1: 2 payments, total 734.90, from 4 bills. 1 bill not included: INV-2207 City Catering, 600.00, no account number on record." It carries no account numbers.

**Stage 6, officer.** The officer checks the totals, uploads the file to the bank portal and authorises it. Once City Catering's bank details are added, INV-2207 goes into the next run; the four bills already in this file are refused by any later run, so none is paid twice.

### Conversion in detail

**Tracto flow:** `payrun-main` (desktop, one step). Started by the officer from the console with **Run** once the selection is made (a payment run is the officer's decision, so a schedule is optional), with the payment date as a parameter:

```json
{ "params": { "paymentDate": "2026-11-28" },
  "steps": [ { "stepNumber": 1, "action": "http.post", "description": "Build the bank payment file",
               "parameters": { "url": "http://127.0.0.1:5095/payrun/start",
                               "body": "{\"paymentDate\":\"${param.paymentDate}\"}", "storeAs": "run" } } ] }
```

**Endpoint:** `POST /payrun/start` with `{ "paymentDate": "...", "selection": "accounting" | "workbook", "workbook": "<path>" }`. Reply:

```json
{ "runId": "pr-20261128-1", "state": "Completed", "bills": 37, "payees": 21,
  "total": "48210.55", "file": "Payments/2026-11-28/PAY_20261128_1.csv",
  "message": "21 payment line(s), total 48,210.55, from 37 bill(s)" }
```

A2 has no slow external step per item, so the run normally completes inside the call.

**What Go does:**

1. Read the selection: bills with the chosen payment date (or in the chosen batch/status/tag) from the accounting connector, or the rows of the payment-run workbook.
2. Read the bank details for every payee: accounting contacts, or the `Payees` sheet.
3. **Check before writing anything:**
   - every bill is awaiting payment and not already in an earlier file (`finance.pay_bills`);
   - every payee has complete bank details in a valid shape for the bank (account number length, bank code);
   - amounts are positive and in the bank's currency.
   A payee that fails is left out of the file and listed with the reason; the rest of the file is still produced, unless `Settings` says to stop the whole run (q26).
4. **Group** bills by payee (and by payment date if q12 says so) into one line with the summed amount and a reference built by the `BankFile` rule (for example the bill numbers, cut to the length the bank allows).
5. **Write** the file in the bank's format (CSV, fixed-width or ISO 20022 `pain.001` XML), with header and trailer as the specification requires, and the control count and control total.
6. **Re-read** the written file and check its count and total against step 4, so the file the officer uploads is the one that was checked.
7. **Save** to `Payments/<payment date>/` in the library, and the run summary (payees, amounts, excluded payees with reasons) beside it.
8. **Notify** finance: file name and link, line count, total, excluded payees. Account numbers are never put in the notification or the logs.
9. **Record** which bills went into which file (`finance.pay_bills`), so a second run for the same date does not pay a bill twice.

**Stored:** `finance.pay_runs` (run id, payment date, bills, payees, total, file path, file SHA-256, state, message) and `finance.pay_bills` (run id, bill id, payee, amount).

**Nothing in A2 is impossible.**

---

## 6. Process B: online donations into the donation system

**Starts** with the portal collection run. **Ends** when the reconciliation report has been reviewed.

### Steps and verdicts

| Stage (deck) | Lane | Task (from the deck) | How in Tracto | Verdict |
|---|---|---|---|---|
| 1 Collect from portals | Officer | Provides a one-time passcode where a portal requires it | Depends on the portal: see "Passcodes" below | From **Today** to **Person**; never fully removable for passcodes sent to a phone |
| 1 Collect from portals | Automation | Signs in to each giving portal and downloads the donation reports | One browser child per portal: sign in, open the report, set the dates, download. A portal with an API or emailed reports is read by Go instead | **With addition** (connect the existing download handler to flow scripts, section 8.4). **Not possible** for a portal that blocks automated browsers (CAPTCHA, bot detection) |
| 2 Consolidate | Automation | Merges data from all portals into one common format | Normaliser with one column mapping per portal | **With addition** |
| 3 Load donations | Automation | Loads the donations into the donation management system | Donation loader: API, bulk import file, or screen entry per donation | **With addition**; **Conditional** on how the system accepts data |
| 4 Month-end correction | Automation | Corrects donation dates to settlement dates in bulk | Settlement dates from the portals' payout data; bulk update through API or import, else per donation on screen | **With addition**; **Conditional** on settlement dates being available and the date being changeable |
| 5 Reconcile | Automation | Compares portal totals with system totals and reports mismatches | Reconciliation report | **With addition** |
| 5 Reconcile | Officer | Reviews the reconciliation report and resolves exceptions | Unchanged | **Person** |

### Worked example

Two portals, three donations, two payouts; made-up names and amounts.

**Stage 1, Collect.** Each portal's flow downloads two files in its signed-in session: the donation report and the payout report.

```
Portal A, donations:  txn A-901  Asha   50.00  28 Nov
                      txn A-902  Ben   100.00  29 Nov
Portal A, payouts:    payout PA-77  paid 3 Dec   contains A-901, A-902   gross 150.00  fee 4.50  net 145.50

Portal B, donations:  txn B-17   Chen   20.00  02 Dec
Portal B, payouts:    payout PB-5   paid 10 Dec  contains B-17           gross  20.00  fee 0.60  net  19.40
```

If the portal's kept session has ended, what happens depends on what is built. **Today:** the portal's flow fails at its first wait with "session expired", the other portals continue, and the officer signs the profile in again and reruns. **With the `ask` addition** (section 8.4, with `onlyIfVisible`): the run asks the officer for the passcode in the console and waits, only when the passcode prompt is actually shown.

**Stage 2, Consolidate.** The portals' files use different columns and date formats; they become one table:

```
portal  txn    donor  amount  donated  payout  settled
A       A-901  Asha    50.00  28 Nov   PA-77   3 Dec
A       A-902  Ben    100.00  29 Nov   PA-77   3 Dec
B       B-17   Chen    20.00  02 Dec   PB-5    10 Dec
```

**Stage 3, Load.** The three donations are created in the donation management system against their donors, dated with the day the donor gave (28 Nov, 29 Nov, 2 Dec). Each carries its portal transaction id, so a second run does not load it again.

**Stage 4, Month-end correction.** Each donation's date is changed to the day its payout was settled:

| Donation | Date after stage 3 | Date after stage 4 | Why |
|---|---|---|---|
| Asha, A-901 | 28 Nov | 3 Dec | In payout PA-77, paid 3 Dec |
| Ben, A-902 | 29 Nov | 3 Dec | In payout PA-77, paid 3 Dec |
| Chen, B-17 | 2 Dec | 10 Dec | In payout PB-5, paid 10 Dec |

Asha's and Ben's donations move from November to December, because that is when the money reached the organisation. Today this is done by hand, one donation at a time.

**Stage 5, Reconcile.** Per portal and payout, what the portal reports is compared with what the donation system now holds:

```
Payout  Portal total  System total  Result
PA-77      150.00        150.00     matched
PB-5        20.00         20.00     matched
```

Had A-902 failed to load, the report would show "PA-77: portal 150.00, system 50.00, missing A-902 (Ben, 100.00)", and the officer would resolve it.

### Where the settlement date comes from

The deck says only "corrects donation dates to settlement dates"; it does not say where that date is taken from (q19). The possible sources:

| Source | What it gives | How Tracto gets it |
|---|---|---|
| **The portal's payout report** (assumed here) | Each payout's date and the donations it contains, so every donation gets its own settlement date | Downloaded in the same signed-in session as the donation report: one more download in each portal's flow |
| **The bank statement** | The date each portal's lump sum arrived, as one amount per payout (for example 145.50 on 3 Dec). It does not say which donations that amount contains, so on its own it cannot date individual donations | See "The bank statement" below |
| **The accounting system's bank feed** | The same lump sums as the bank statement, received automatically by the accounting system | Through the accounting connector |

The portal's payout report is the only source that links a payment to its donations, so it is the natural one. The bank statement could only confirm the date once the payout report has said which donations a payment contains.

### What the reconciliation compares

As written in the deck, stage 5 compares **portal totals with donation system totals**: it checks that every donation reported by the portals was loaded into the donation system, once and with the right amount. Both sides come from data the process already has. **It does not use the bank statement.**

### The bank statement

The bank statement is not part of process B as described. A **bank reconciliation** (checking that each portal's payout actually arrived in the bank: payout PA-77, 145.50 on 3 Dec, against the bank's deposit of 145.50 on 3 Dec) would be an addition to the scope, to confirm with finance (q28). If it is wanted, the statement cannot be fetched by signing in to the bank, since the automation never signs in there. It would come from:

- the accounting system's bank feed, through its API;
- a statement file the bank sends automatically (by email or file transfer), read by the finance service;
- a statement the officer downloads and places in a folder.

Matching is then by payout amount (net of fees) and date, with the payout reference where the bank shows it.

### Conversion in detail

**Tracto flows:**

| Flow | Runtime | When | What it does |
|---|---|---|---|
| `donations-collect-main` | desktop, one step | Daily (or weekly) schedule | `POST /donations/collect/start` |
| `portal-<name>-download` | browser, one per portal, published under the `latest` tag | Queued by Go, one portal at a time | Signs in, opens the report, sets the date range, downloads the file |
| `donations-monthend-main` | desktop, one step | Month-end schedule | `POST /donations/month-end/start` |
| `donations-reconcile-main` | desktop, one step | After each collection, and at month end | `POST /donations/reconcile/start` |
| `donation-enter-one`, `donation-redate-one` | browser | Only if the donation system has no API and no import | Enters or corrects one donation on screen; queued per donation by Go |

**A portal child** (`portal-<name>-download`) is a straight line of browser steps, for example:

```
navigate   https://<portal>/reports/donations
wait       report page loaded (fails clearly if the sign-in page shows instead: "session expired")
fill       from date = ${param.from}, to date = ${param.to}
click      Export / Download CSV
download   save to ${param.folder}, store the saved path            <- built, to be connected (8.4)
```

It runs in the portal's signed-in browser profile (named in the `Portals` sheet), so no password is in the script. Where a portal must sign in every time, the steps before `navigate` fill the user name and `${secret:PORTAL_X_PASSWORD}` from the worker.

**Passcodes.** A straight-line script cannot decide "if a passcode page appears"; each portal's script is written for how that portal always behaves:

| Portal behaviour | Script | Person needed | Verdict |
|---|---|---|---|
| Remembers the device after one sign-in | No sign-in steps. The officer signs the profile in once from the Designer (**Sign in**); again only when the portal ends the session | Only when the session expires | **Today** |
| Sends the passcode by email to a mailbox the finance service can read | Sign-in steps, then `http.get` to `/otp/wait?portal=<name>`: Go reads the passcode from the mailbox and returns it; then `fill` | No | **With addition** (mail reading, passcode endpoint) |
| Sends the passcode to a person's phone at every sign-in | Sign-in steps, then `ask` ("Enter the passcode for <portal>", 5-minute limit): the run shows in the console as waiting for input; the officer types the passcode; the run continues | Yes, every sign-in (seconds) | **With addition** (ask action and console form); removing the person: **Not possible** |

**What Go does in `/donations/collect/start`** (`{ "from": "...", "to": "...", "portals": [...] }`, defaults from `Settings`):

1. For each portal in `Portals` (or the ones requested): queue `portal-<name>-download` with the dates and a run folder `Donations/<run>/raw/`, wait for it, record the file, its row count and SHA-256 in `finance.donation_files`. A failed portal is marked with the reason; the others continue.
2. **Normalise** each file with its `Map-<portal>` sheet into the common format: portal, transaction id, donor name, donor email, amount, currency, fee, net, donated at, payout id, settled at (when the report has it).
3. **De-duplicate** on portal + transaction id against `finance.donations`; keep only new donations.
4. Write the merged file `Donations/<run>/merged.xlsx` (sheet service) for finance to see.
5. **Load** the new donations into the donation system through the loader (section 7): API calls, or one import file, or one browser child per donation. Store the donation system's id per donation in `finance.donations`.
6. Run the reconciliation for the period (below) and notify finance.

**`/donations/month-end/start`** (`{ "month": "2026-11" }`):

1. For every donation of the month without a settlement date, find it in the portals' payout data (collected in step 1 or by a payout-report child); record `settled at`.
2. Update the donation date in the donation system to the settlement date: bulk through the API or a bulk-update import, else one browser child per donation through the row loop.
3. List donations that could not be updated (no settlement found, system refused) with the reason.

**`/donations/reconcile/start`** (`{ "period": "2026-11" }`): per portal and per payout (or per day, `Reconciliation` sheet), compare the portal's total with the donation system's total for the same donations; classify each as matched, different (with amount), missing in the system, missing in the portal; allow the configured tolerance and fee handling; write `Donations/Reconciliation/<period>.xlsx` and notify finance with the counts.

**Stored:** `finance.donation_runs`, `finance.donation_files`, `finance.donations` (portal, transaction id, amounts, dates, system id, run id), `finance.recon_results`.

---

## 7. How each system can be reached

The deck leaves this open ("subject to technical validation"). For each system, the three possible ways in, from best to worst, and what Tracto builds for each.

| System and need | API | File (export or import) | Web pages only |
|---|---|---|---|
| **Claims platform**: read approved claims, mark transferred | Claims connector over the API. Robust | Go reads a scheduled export from a folder or mailbox; marking "transferred" may not be possible, so `claims_done` alone prevents doubles | A browser child reads the approved-claims list with `getTexts` and each claim's items with `getText`. Works; breaks when the pages change |
| **Accounting system**: contacts, draft bills, bills awaiting payment, payee bank details | Accounting connector. All A1 and A2 needs in a few calls. Robust | Bills by import file (if the system imports bills with lines); selection and payees from workbooks | Contact search and creation by browser child. **A bill with a varying number of lines** cannot be entered by one straight-line script: either one child per line after creating the bill header (fragile), or a fixed maximum number of lines per script. Selection and bank details from workbooks instead |
| **Bank portal** | Not used | The file is the output; the officer uploads it | Not used (section 9) |
| **Giving portals**: donation and payout reports | Go reads the API. Best | Go reads reports delivered by email (mail reading) or to a folder | A browser child per portal with the `download` action. **Not possible** where the portal blocks automated browsers |
| **Donation system**: create donations, update dates, read totals | Loader and updates over the API. Robust | One import file for new donations, one bulk-update file for dates, uploaded by Go or by the browser `upload` action | One browser child per donation through the row loop, for both loading and date correction. Works; slow for large volumes |

Every row is buildable. Only the browser-only cases depend on pages staying the same.

---

## 8. Everything to build

### 8.1 New Go service: finance service

| Endpoint | Purpose | Request | Reply |
|---|---|---|---|
| `GET /health` | Health check | | `{"status":"ok"}` |
| `POST /claims/run/start` | A1 run | `{claimIds?, dryRun?}` | `{runId, state, total, message}` |
| `GET /claims/run/{id}` | A1 progress, claim by claim | | run and items |
| `POST /claims/run/{id}/cancel` | Stop after the current claim | | |
| `POST /payrun/start` | A2 run | `{paymentDate, selection, workbook?}` | `{runId, state, bills, payees, total, file, message}` |
| `GET /payrun/{id}` | A2 result, payee by payee (account numbers masked) | | run and lines |
| `POST /donations/collect/start` | B collection, merge, load, reconcile | `{from?, to?, portals?}` | `{runId, state, portals, message}` |
| `POST /donations/month-end/start` | B date correction | `{month}` | `{runId, state, total, message}` |
| `POST /donations/reconcile/start` | B reconciliation | `{period}` | `{runId, state, matched, differences, message}` |
| `GET /donations/run/{id}`, `POST /donations/run/{id}/cancel` | B progress, stop | | |
| `GET /otp/wait?portal=<name>&timeoutSeconds=` | Returns the next passcode mailed for that portal (only for portals that mail passcodes) | | `{code}` |
| `login <system>` (command) | One-time sign-in of the service to an API that needs a person's consent (OAuth2), like `sheet-service login` | | |

Every start endpoint follows the rules of `/process/start`: refuse before starting if something is missing, reply at once, one item at a time, a status and reason per item, rerun handles only unfinished items, a `message` that says what the run will do or why it does nothing.

### 8.2 Connectors (inside the finance service)

| Connector | Operations | Notes |
|---|---|---|
| Accounting system | find contact, create contact, create draft bill with lines, list bills awaiting payment with their selection, read contact bank details, read bill by reference | OAuth2 or API key; tokens encrypted at rest like the Microsoft sign-in |
| Claims platform | list approved claims with items and receipts, mark transferred | Or a file reader, if the platform only exports |
| Donation system | create donation, find by reference, update date, totals for a period | Or an import-file writer, if the system only imports |
| Giving portals (where they have an API) | donations for a period, payouts with settlement dates | Optional, per portal |
| Microsoft Graph | upload a file to a SharePoint folder, send an email (`Mail.Send`) | Uses the existing sign-in; needs the `Files.ReadWrite.All` (exists) and `Mail.Send` permissions |
| Mailbox (IMAP or Graph) | read new messages for a portal's passcode or report | Only if a portal mails passcodes or reports; shared with the SIS plan |

### 8.3 New logic (inside the finance service)

| Piece | Does |
|---|---|
| Claim checks and coding | Applies `ClaimRules`; maps categories with `ExpenseCoding` |
| Bank file builder | Groups by payee, validates bank details, writes the bank's format with header, trailer, control count and total, re-reads and verifies |
| Portal normaliser | Reads CSV and Excel exports; maps columns with `Map-<portal>`; parses dates, amounts, currencies; de-duplicates |
| Reconciliation | Compares portal and system totals at the configured grain; classifies differences |
| Item loop over API records | The `/process/start` loop generalised from sheet rows to any item key (claim id, donation id) |
| Run summaries and notifications | A summary workbook per run in the library; one message per run |

### 8.4 Browser runtime actions

| Action | State today | Parameters | Outputs | Used by |
|---|---|---|---|---|
| `download` | **Built, not connected.** The browser runtime library has `FileDownloadHandler` (`browser.file.download`, with tests): it clicks the element, waits for the file with Playwright, saves it into an approved folder, checks the expected file name, type and size, and removes a partial file when the run is cancelled. Flow scripts cannot reach it yet: the step translator has no `download` name and the run engine creates the executor without the handler | target to click, `folder`, optional `fileName`, `timeoutMs` | `path`, `fileName`, `size` | Portal children |
| `upload` | **Built, not connected.** `FileUploadHandler` (`browser.file.upload`) puts a file into a page's file field; same gap as `download` | target file field, `path` | | Donation-system import through its web page, if needed |
| `ask` | **New.** Nothing in Tracto pauses a run for a person today: the `HandedOff` run state ends a run and hands it on, it does not pause and resume one | `prompt`, `secret` (true: never logged), `timeoutMs`; optional `onlyIfVisible` (a target: ask only when it is on the page, otherwise skip, so the script stays a straight line) and `fill` (a target to type the answer into) | `value`, `asked` (true or false) | Portals that send passcodes to a phone, every time or only when a session has ended |

Connecting `download` and `upload` means: add both names to the step translator, pass the two handlers and the approved-folder setting to the executor in the run engine, document them in the browser runtime README and the authoring skill, and run one real flow that downloads a file.

### 8.5 Orchestrator and console

| Change | Why |
|---|---|
| **Waiting for input**: a run can ask a question; the console lists open questions with a field to answer; the answer goes to the waiting browser step, is never logged, and is deleted once delivered or expired | The `ask` action |
| Nothing else | Schedules, child jobs, the `latest` tag, run logs and cancel already cover the three processes |

### 8.6 Database (schema `finance`)

`claim_items`, `claims_done`, `pay_runs`, `pay_bills`, `donation_runs`, `donation_files`, `donations`, `recon_results`, `api_tokens` (encrypted). Applied by the finance service's own migrations, like the other services.

### 8.7 Tracto flows to publish

| Flow | Runtime | Steps |
|---|---|---|
| `claims-main` | desktop | 1 (`http.post`) |
| `payrun-main` | desktop | 1 |
| `donations-collect-main`, `donations-monthend-main`, `donations-reconcile-main` | desktop | 1 each |
| `portal-<name>-download` | browser | One per portal, about 5 to 12 steps each |
| Only for systems with web pages only: `claims-read-list`, `contact-find-or-create`, `bill-create-one`, `donation-enter-one`, `donation-redate-one` | browser | Per system; written after the pages are seen |

### 8.8 Configuration and setup

- `finance-config.xlsx` as in section 3.
- A service account (or API app) for each system with an API; the finance service signs in once.
- One signed-in browser profile per portal on the browser worker.
- The shared library folders: `Claims/`, `Payments/`, `Donations/`.
- The finance service added to `start-demo.sh` and the console tabs.

### 8.9 Size of each piece

Rough, before the open questions are answered. S: up to 3 days; M: 1 to 2 weeks; L: more than 2 weeks.

| Piece | Size |
|---|---|
| Finance service skeleton (endpoints, schema, Orchestrator client, configuration reading) | M |
| Accounting connector | M |
| Claims connector | S to M |
| Claim checks and coding | S |
| Bank file builder | M (mostly matching the bank's specification and testing with the bank) |
| Graph upload and mail send | S |
| Connect the browser `download` and `upload` actions (the handlers exist) | S (1 to 2 days) |
| `ask` action and console "waiting for input" | M |
| Portal normaliser | S per portal |
| Portal children | S per portal |
| Donation loader | S (API or import) to M (web pages) |
| Month-end date correction | S to M |
| Reconciliation | S to M |
| Mailbox reading and `/otp/wait` | S (shared with SIS) |

---

## 9. What cannot be done, even with additions

| # | What | Why | What to do instead |
|---|---|---|---|
| 1 | **Getting past a CAPTCHA, "I am not a robot" check or bot detection on a portal** | These checks exist to stop automated sign-in. Tracto will not be built to defeat them, and doing so would normally break the portal's terms of use | Ask the portal for an API, scheduled export or emailed reports. Otherwise that portal's file stays a manual download, and Tracto takes it from a folder from stage 2 (Consolidate) on |
| 2 | **A passcode sent to a person's phone (SMS or authenticator app), without that person** | Only the holder of the phone receives it; that is the passcode's purpose | Prefer portals that remember the device or mail the passcode. Otherwise the run asks in the console (`ask`), a few seconds per sign-in |
| 3 | **Signing in to the bank portal and uploading or authorising payments** | Excluded by the process design; bank portals use personal tokens and treat automated access as a breach of their terms | The officer uploads and authorises; Tracto provides the file and its totals |
| 4 | **Changing a donation's date where the donation system forbids it** (after a receipt is issued, or in a closed period) | A rule of the donation system, not a technical limit | Agree the rule with finance: correct only unreceipted donations, or record the settlement date in another field |

The officer's lane stays with a person by design: approving claims and draft bills (A1 stages 1 and 5), selecting the bills and uploading the file (A2 stages 1 and 6), passcodes and the reconciliation review (B stages 1 and 5). These are not limits of Tracto.

---

## 10. Risks

| Risk | Where | Handling |
|---|---|---|
| A web page changes and a browser step no longer finds an element | B stage 1; any system reached through its web pages | The step fails with its reason in the run log; the item is `Failed`; the child is fixed, published with a new version and the `latest` tag moved; the main is unchanged |
| A portal session expires | B stage 1 | The child fails with "session expired" instead of guessing; the officer signs the profile in again from the Designer |
| The same claim, bill or donation processed twice | A1, A2, B | Every item is keyed by its source id and checked before anything is created; reruns handle only unfinished items |
| A bill paid twice through two payment files | A2 | `pay_bills` records every bill already put in a file; a second file refuses it |
| Bank details or donor details exposed | A2, B | Logs and notifications carry counts, totals and ids only; files go only to the agreed library; the `Payees` sheet, if used, has restricted access |
| An API's call limit is reached | A1, A2, B | The connector spaces its calls and retries with back-off |
| A wrong account code books an expense to the wrong account | A1 | Codes come only from `ExpenseCoding`; an unknown category fails the claim instead of guessing; bills stay drafts until a person approves them |

---

## 11. Open questions

Each answer decides how a step is built; several turn a **Conditional** into a firm verdict.

**Systems and access**

- **q1.** Which products are the claims platform, the accounting system and the donation management system (name, cloud or on-premises, version)?
- **q2.** Does each offer an API? How does a program sign in (API key, OAuth2 app)? Can a service account be issued?
- **q3.** If a system has no API: does it export (claims) or import (bills, donations, date updates) files, and in what format?

**A1 (claims)**

- **q4.** Which claim state means "approved and ready to transfer", and can the platform record that a claim was transferred?
- **q5.** What makes a claim complete (fields, receipts, limits)? Is there a written list?
- **q6.** How is a claimant matched to an accounting contact (employee number, email, name)? What should happen when two match?
- **q7.** How is each expense item coded: account code, tax rate, tracking categories? Is there a mapping today?
- **q8.** One bill per claim, or one bill per claimant per run? What goes in the bill's reference and due date?

**A2 (payment file)**

- **q9.** How does the officer mark the bills for a cycle (payment date, batch, status, tag)? Can it be read through the API?
- **q10.** Where are payee bank details maintained, and who updates them?
- **q11.** Which bank, and which file format does its portal accept? Is there a specification and a sample file the bank accepted?
- **q12.** Bills of the same payee: always one line, or one line per payment date?
- **q13.** Where is the shared library, and how should finance be notified (email, Teams)?
- **q14.** After the bank pays, should Tracto mark the bills as paid in the accounting system?

**B (donations)**

- **q15.** Which giving portals? For each: how it signs in, whether it remembers the device, whether it shows a CAPTCHA, whether it has an API, scheduled export or emailed reports.
- **q16.** For each portal: the report used today, its format and columns, and a sample file.
- **q17.** Do the portals' terms of use allow automated sign-in and download?
- **q18.** How does the donation system accept donations (API, import file, screen), and how is a donor matched (email, donor id)?
- **q19.** Where does the settlement date come from (a payout report per portal?), and can a donation's date be changed after a receipt is issued?
- **q20.** What does reconciliation compare (per portal per day, per payout, per donation), and what difference is acceptable (fees, rounding)?

**All processes**

- **q21.** When should each process run (days, times, time zone, month-end date)?
- **q22.** Typical volumes: claims and expense lines per day, bills and payees per run, donations per month, number of portals.
- **q23.** Which machine or server will run the Tracto workers and the finance service, and can it reach each system and the library?
- **q24.** Can the accounting system already produce a bank payment file from a batch of bills? If so, is its format accepted by the bank?
- **q25.** Does the claims platform already offer a direct integration with the accounting system? If so, why is it not used today?
- **q26.** If one payee in a payment run has bad bank details: leave that payee out and produce the rest, or stop the whole file?
- **q27.** Where does finance take the settlement date from today: the portal's payout report, the bank statement, or somewhere else?
- **q28.** Besides comparing the portals with the donation system, should Tracto also check that each portal's payout arrived in the bank (a bank reconciliation)? If so, how does the bank statement reach finance today?

---

## 12. Order of work

1. **Answer q1, q2, q11, q15, q24 and q25 first.** They decide how much of A1 and A2 is a build, and which portals can be automated at all.
2. **A2 payment file.** It can start before any API is confirmed: selection and payee details from workbooks (sheet service, today), plus the finance service skeleton, the bank file builder, library upload and notification. Testable end to end with the bank's sample file.
3. **A1 claims to bills**, once the APIs are confirmed: accounting connector, claims connector, claim checks and coding. The accounting connector then replaces A2's workbooks.
4. **B donations, portal by portal**: first a portal with an export or emailed reports; then connect the `download` action and write the browser children for the others; normaliser, loader, month-end correction and reconciliation; the `ask` action only if a portal sends passcodes to a phone at every sign-in.
