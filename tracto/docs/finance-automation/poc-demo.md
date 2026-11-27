# Finance automation proof of concept: demo guide (A1 and A2)

Created: 2026-11-27 | Last updated: 2026-11-27

This proof of concept shows two processes running on NeeliRPA with a dummy client application:

- **A1:** approved staff claims become draft bills (a PDF and a bill record each), with no re-keying.
- **A2:** the bills the finance officer approves become one bank payment file per payment date.

Every hand-over between steps is a file in a folder, so each step can be shown on screen.

## What is where

Everything lives under `tracto/finance-poc/`:

| Folder | Contents |
|---|---|
| `mock-app/` | The dummy client application (React, Vite): claims, bills, payments. Port 5173 |
| `finance-service/` | The NeeliRPA finance service (Go). Port 5095 |
| `scripts/` | The two main jobs (`finance-a1-claims-to-bills_1.0.0.json`, `finance-a2-payment-file_1.0.0.json`) and `publish.ps1` |
| `seed/` | Seed data: employees, expense categories with account codes, payees with bank details, the four claims the demo starts with |
| `poc-data/` | The hand-over folders, emptied and re-seeded by **Reset demo data** |

The hand-over folders under `poc-data/`:

| Folder | What is in it | Written by |
|---|---|---|
| `claims/submitted/` | Claims submitted by employees, waiting for the officer | App |
| `claims/approved/` | One JSON per claim the officer approved | App (officer approves) |
| `claims/processed/` | Claims whose draft bill has been created | A1 |
| `bills/draft/` | One PDF and one JSON per draft bill | A1 |
| `bills/approved/` | Bill JSON approved by the officer, with the payment date | App (officer approves) |
| `bills/paid/` | Bill JSON that went into a payment file | A2 |
| `payments/` | `PAY_<date>_<n>.csv` bank payment files and a `.summary.json` per file | A2 |
| `exceptions/` | Claims (`CLM-….json`) and payees (`PAYEE-….json`) that could not be processed, with the reason | A1, A2 |

The seed data includes two deliberate exceptions:

- Tom Reed's claim CLM-0002 has an item in the category **Taxi (overseas)**, which has no account code. A1 stops it.
- Sara Lim has **no account number** on record. A2 leaves her out of the payment file.

## Starting everything

From the repository root in Git Bash:

```bash
./start-demo.sh
```

Choose **1) Keep everything**. Windows Terminal opens one tab per service. The last two tabs are new:

- **8 Finance service** runs `go run .` in `tracto/finance-poc/finance-service`.
- **9 Finance app** runs `npm run dev` in `tracto/finance-poc/mock-app`. On first start it runs `npm install`.

To start only those two in an existing demo window: `./start-demo.sh finance-service finance-app`.

**Publish the two main jobs.** Do this once, and again after a "Start fresh", which clears the published scripts and schedules:

```powershell
powershell -ExecutionPolicy Bypass -File tracto\finance-poc\scripts\publish.ps1
```

It publishes both scripts, sets them as **latest**, and creates two A1 schedules, 08:00 and 15:00 (Asia/Kolkata), both **disabled**. Running it again changes nothing.

**Check before the audience arrives:**

| Check | Where |
|---|---|
| Orchestrator console opens | http://localhost:4000 |
| Workers tab shows the desktop runtime **AVAILABLE** | Console, Workers |
| Artifacts tab lists both finance scripts, marked **latest** | Console, Artifacts |
| Finance app opens on the sign-in page | http://localhost:5173 |
| Finance service answers | http://127.0.0.1:5095/health |

## Reset step

1. In the finance app, sign in as **Finance Officer**.
2. Click **Reset demo data**, then **Yes, reset**.

All hand-over folders are emptied and the four seed claims are back in `claims/submitted/`. Nothing outside `poc-data/` is touched. Reset before every demo.

Keep a File Explorer window open on `tracto\finance-poc\poc-data` to show the folders as they fill.

## Demo story

### Part 1: the employee submits a claim

1. Sign out. Sign in as **Employee**, **Priya Nair**.
2. **My claims** shows CLM-0001, already submitted.
3. In **New expense claim**, add one item and click **Submit claim**:
   - Category **Meals**
   - Amount **41.00**
   - Description **Volunteer briefing snacks**
4. The message reads "Claim CLM-0005 submitted". The claim appears with the status **Submitted**.
5. Point out that the category list includes **Taxi (overseas)**, which has no account code in the accounting system. The demo comes back to it.

### Part 2: the finance officer approves claims

1. Sign out. Sign in as **Finance Officer**.
2. **Claims** shows five claims under **Waiting for approval**. Click ▸ on a claim to show its items.
3. Click **Approve** on each of the five claims. They move to **All claims** with the status **Approved**.
4. **Folder:** `claims/approved/` now holds five JSON files, one per claim. This is the hand-over to NeeliRPA.

### Part 3: A1 runs in NeeliRPA

1. In the Orchestrator console, open **Schedules**. Two schedules for `finance-a1-claims-to-bills`, at 08:00 and 15:00, are disabled. In production A1 runs at those times by itself. For the demo it is started by hand.
2. Open **Artifacts**. On **Finance A1: Approved Claims to Draft Bills**, click **▶ Run**, then **Submit Job**.
3. Open **Jobs**. The run reaches **SUCCEEDED** within a few seconds.
4. Click **Logs** on the run. Below the runtime's own lines you see one line per claim from `finance-service`:
   - Info: `CLM-0001 (Priya Nair): draft bill BILL-CLM-0001 created, total 193.40.`
   - **Warning:** `CLM-0002: not processed, moved to exceptions: item 2: category 'Taxi (overseas)' has no account code`
   - Info lines for CLM-0003, CLM-0004 and CLM-0005
   - The summary: `5 approved claim(s): 4 draft bill(s) created (...). 1 need(s) attention: CLM-0002, ...`

   The same summary is the job's output `a1.message` (`GET /jobs/{runId}`).
5. **Folders:**
   - `claims/approved/` is empty.
   - `claims/processed/` holds four claims.
   - `bills/draft/` holds four PDFs and four JSON files.
   - `exceptions/CLM-0002.json` holds Tom's claim and the reason.
6. Back in the app, **Claims** shows **Bill created** with the bill number for four claims, and **Needs attention** with the reason for CLM-0002.
7. Optional: click **▶ Run** on A1 again. The run succeeds and its log says `Nothing to do: no approved claims waiting in claims/approved.` A claim is never billed twice.

### Part 4: the finance officer reviews and approves the draft bills

1. In the app, open **Bills**. The **Draft** tab lists four bills.
2. Click **Open PDF** on BILL-CLM-0001. The draft bill opens in a new browser tab: payee, claim reference, one coded line per expense item with its account code, and the total.
3. Click ▸ on a bill to show the coded lines in the page.
4. For each of the four bills, pick the payment date **28 Nov 2026** and click **Approve**.
5. **Folder:** `bills/approved/` holds four bill JSON files, each with its `paymentDate`. The PDFs stay in `bills/draft/`.

### Part 5: A2 runs in NeeliRPA

1. In the console, open **Artifacts**. On **Finance A2: Approved Bills to Bank Payment File**, click **▶ Run**, then **Submit Job**.
   - With no parameter, A2 takes every approved bill.
   - Submitted through the API with `params.paymentDate` (for example `2026-11-28`), it takes only the bills for that date.
2. The run reaches **SUCCEEDED**. Click **Logs** on it. The log shows:
   - **Warning:** `Sara Lim (E1090): left out of the payment file for 2026-11-28: no account number on record. 1 bill(s), 268.20, stay in bills/approved.`
   - Info: `Priya Nair (E1042): 234.40 from 2 bill(s) (BILL-CLM-0001, BILL-CLM-0005) in PAY_20261128_1.csv.` Two bills for the same payee become one payment.
   - Info: `Tom Reed (E1077): 50.10 from 1 bill(s) (BILL-CLM-0004) in PAY_20261128_1.csv.`
   - The summary: `Payment file PAY_20261128_1 for 2026-11-28: 2 payment(s), total 284.50, from 3 bill(s). 1 payee(s) left out: ...`

   Account numbers never appear in the log or in the summary.
3. **Folders:**
   - `payments/` holds `PAY_20261128_1.csv` and `PAY_20261128_1.summary.json`.
   - `bills/paid/` holds the three paid bills, each marked with its payment file.
   - `bills/approved/` still holds Sara's bill.
   - `exceptions/PAYEE-E1090-20261128.json` records why she was left out.
4. In the app, open **Payments** and click **Open** on `PAY_20261128_1.csv`. The page shows the file and the summary:

   ```
   H,DEMO-ORG-0001,20261128,PAY_20261128_1
   D,Priya Nair,7171,0123456789,234.40,CLM-0001 CLM-0005
   D,Tom Reed,7339,5566778899,50.10,CLM-0004
   T,2,284.50
   ```

   - `H` is the header: organisation account, payment date, file reference.
   - Each `D` line is one payee: name, bank code, account number, amount, claim references.
   - `T` is the control line: number of payments and total.

   NeeliRPA writes the file, reads it back, and checks it against this count and total before saving.
5. Click **Download**. This is the file the officer checks and uploads to the bank. NeeliRPA never signs in to the bank.
6. **Bills**, **Paid** tab: the three bills with their payment date and payment file.
7. Optional: click **▶ Run** on A2 again. No new file is written. The log says Sara is still left out, and nothing already paid is paid again.

## Exceptions at a glance

| Exception | Raised by | What happens | Where it shows |
|---|---|---|---|
| Expense category without an account code | A1 | The claim moves to `exceptions/` with the reason. No bill is created | Run log Warning; app **Claims** "Needs attention"; `exceptions/CLM-0002.json` |
| Claim with missing fields, an unknown employee, or a zero amount | A1 | Same as above. Every problem in the claim is listed at once | Same as above |
| Claim already billed | A1 | No second bill. The claim moves to `claims/processed/` | Run log Warning |
| Payee without an account number or bank code | A2 | The payee is left out. Their bills stay in `bills/approved/` for the next run | Run log Warning; payment summary "Left out"; `exceptions/PAYEE-….json` |

## If something goes wrong

| Symptom | Cause and fix |
|---|---|
| A run fails and its log says the connection to 127.0.0.1:5095 was refused | The finance service is not running. Start tab **8 Finance service** |
| A run stays QUEUED | No desktop runtime is available. Check the Workers tab and start tab **5 Desktop runtime** |
| Run logs have no finance-service lines | The finance service could not reach the Orchestrator. Its tab shows why. The lines are still in that tab |
| The app shows no claims after a fresh start | Click **Reset demo data** |
| Artifacts tab has no finance scripts | Run `scripts\publish.ps1` (see "Starting everything") |
