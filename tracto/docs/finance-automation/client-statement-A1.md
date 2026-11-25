# Process A1: Approved staff claims reach the accounting system without re-keying

Created: 2026-11-25 | Last updated: 2026-11-25

## Feasibility

**This process is fully possible with our automation platform**, on one assumption about data:

> The input data (approved claims, contacts, the account coding) can be read, and the output (contacts, draft bills, the list of claims needing attention) can be written, **either through the systems themselves (their API or database) or as files in an agreed folder**.

Every automated stage in the process description can be delivered on that basis. The [Points to confirm](#points-to-confirm) below decide which of these routes is used for each system and how your business rules are applied. They do not change whether the process can be done.

## What changes for the finance team

| Stage | Today | After automation |
|---|---|---|
| 1 Approve claims | The finance officer verifies and approves claims in the claims platform | **Unchanged.** The finance officer approves claims exactly as today |
| 2 Collect and check | Approved claims are collected and checked by hand | **Automated.** Approved claims are picked up and each one is checked for completeness |
| 3 Match contact | The claimant is looked up in the accounting system and added if missing | **Automated.** The claimant's contact is found, or created if it does not exist |
| 4 Create draft bill | Each claim is typed into the accounting system, line by line | **Automated.** A draft bill is created with one coded line per expense item |
| 5 Review and approve | The finance officer reviews and approves the bills | **Unchanged.** The finance officer reviews and approves the draft bills |

In addition, **any claim that cannot be processed is flagged with the reason**, so nothing is skipped silently.

## What the finance team gets

- No manual re-keying of approved claims into the accounting system.
- Draft bills ready for review, with every expense item on its own line and coded to the right account.
- A clear list of the claims that need attention, each with the reason (for example, an expense category without an account code).
- No claim is ever billed twice, even if the automation runs again.
- Approval stays with the finance officer at both ends: claims before, draft bills after.

## Example

After the finance officer approves two claims, the next automation run produces:

> 2 claims processed: 1 draft bill created (claim 5530, three coded lines, total 193.40). 1 claim needs attention: claim 5531, item 2, the category "Taxi (overseas)" has no account code.

Once the missing account code is added, the next run creates the second bill. The officer then reviews and approves the draft bills in the accounting system.

## Points to confirm

The answers decide which data route is used for each system (API, database or files in an agreed folder) and how your rules are applied. They do not change the feasibility above.

**Systems and access**

1. Which claims platform and which accounting system are used (product name, cloud or on-premises)?
2. For each system, which data route is available: an API, read and write access to its database, or files (the claims platform exporting approved claims to a folder, the accounting system importing bills and contacts from a folder)? Can a dedicated account or access be provided for the automation?
3. If files are used: in what format, and which folder do the exports arrive in and the imports go to?

**Claims**

4. Which status in the claims platform means "approved and ready to transfer"? Can the platform record that a claim has been transferred?
5. What makes a claim complete: required fields, receipts, amount limits, age of expenses? Is there a written list of these rules?
6. Does the claims platform already offer a direct link to the accounting system? If so, is there a reason it is not used today?

**Contacts and bills**

7. How should a claimant be matched to a contact in the accounting system: employee number, email address or name? What should happen when two contacts match?
8. How is each expense item coded: account code, tax rate, tracking categories? Is there a mapping from expense category to account code today?
9. Should each claim become its own bill, or should a claimant's claims be combined into one bill per run? What should appear as the bill's reference and due date?

**Running the process**

10. How often should approved claims be picked up (for example, twice each working day)?
11. How many claims and expense items are typical per day?
12. How should the finance team be notified of each run's result and of claims needing attention (email, Teams)?
