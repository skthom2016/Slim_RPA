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

**Getting the data**

1. How can the automation get the approved claims with their expense items: through an API, by reading a database, or from export files placed in a folder? If files: in what format, and which folder?
2. How can it get the existing contacts, to look up each claimant: through an API, a database, or a file in a folder?
3. How can it get the account coding (expense category to account code, tax rate, tracking categories)? Is there such a mapping today, for example in a spreadsheet?

**Giving the data back**

4. How should new contacts and draft bills be delivered to the accounting system: through an API, by writing to a database, or as import files placed in a folder? If files: in what format, and which folder?
5. Can the automation record that a claim has been transferred, so it is never picked up twice? If so, how: API, database, or a file?
6. How should the result of each run and the claims needing attention be delivered: email, Teams, or a file in a folder?
7. Can a dedicated account, or access to the API, database or folders above, be provided for the automation?

**Claims**

8. Which status of a claim means "approved and ready to transfer"?
9. What makes a claim complete: required fields, receipts, amount limits, age of expenses? Is there a written list of these rules?
10. Is there already a direct link between the claims platform and the accounting system? If so, is there a reason it is not used today?

**Contacts and bills**

11. How should a claimant be matched to a contact: employee number, email address or name? What should happen when two contacts match?
12. Should each claim become its own bill, or should a claimant's claims be combined into one bill per run? What should appear as the bill's reference and due date?

**Running the process**

13. How often should approved claims be picked up (for example, twice each working day)?
14. How many claims and expense items are typical per day?
