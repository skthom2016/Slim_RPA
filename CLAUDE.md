# Finance automation on Tracto (NeeliRPA)

Created: 2026-11-27 | Last updated: 2026-11-27

This branch (`finance-automation`) is the finance automation client project. To the client the product is called **NeeliRPA**; internally it is Tracto.

## The three processes

- **A1:** approved staff claims become contacts and draft bills in the accounting system, without re-keying. The finance officer approves claims before and draft bills after.
- **A2:** the bills the officer selects become one bank payment file, saved to a shared folder. The officer checks it and uploads it to the bank. NeeliRPA never signs in to the bank.
- **B:** donation reports are collected from each online giving portal, merged, loaded into the donation management system, corrected to settlement dates at month end, and reconciled against the system's totals.

## Read first

1. `tracto/docs/finance-automation/feasibility.md`: the full analysis. Section 3 is how it runs on Tracto, sections 4 to 6 each process step by step with worked examples, section 8 everything to build with sizes, section 9 what cannot be done.
2. `client-statement-A1.md`, `-A2.md`, `-B.md` (same folder): what the client has been told for each process.
3. `NeeliRPA-Finance-Automation-open-Questions.docx` (same folder): the first-meeting questions sent to the client.
4. `tracto/CLAUDE.md`: how Tracto works (orchestrator, runtimes, script format, publishing, jobs).

## How it runs on Tracto

- A schedule or the Run button starts a **one-step main job** (`http.post`) that calls a new Go **finance service** (schema `finance`). The service does the work item by item, with a status and reason per item; a rerun handles only unfinished items.
- Where a step must happen in a web page (the giving portals, or a system with no API), the finance service queues a **browser child job** using a signed-in browser profile, and waits for it.
- The **sheet service** reads the configuration workbook and writes report workbooks to SharePoint or a network share.
- To build besides the finance service: connect the browser `download` and `upload` actions (handlers exist, not wired), and a new `ask` step with "waiting for input" in the console for portals that send a passcode to a phone. These two are shared features and belong on `general-dev`.
- Cannot be done: getting past a CAPTCHA, a phone passcode without its holder, signing in to the bank, changing a donation date the donation system forbids.

## Rules for this project

- Shared Tracto changes (anything every client needs) are made on `general-dev` in `H:\Neelitech\Slim_RPA`, merged into `general` after testing, then merged into `finance-automation`. Only finance-specific work is committed here.
- Client-facing documents use the name NeeliRPA, contain only what the client needs (no internal reasoning columns or commentary), and avoid the word "blocker".
- Project documents go in `tracto/docs/finance-automation/`, each with a `Created | Last updated` date line under its title.
