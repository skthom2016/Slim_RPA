# What to build for UiPath migration

The recommendation behind the five options in `UIPATH-MIGRATION-OPTIONS.md`. That document
lays out the mechanisms; this one says which to pursue and why the choice matters less than
it looks.

---

## The conversion mechanism is nearly irrelevant

Once Claude authors the script, options 1, 3 and 4 collapse into the same thing with
different amounts of ceremony. Choosing between them is choosing between variants of the
cheap half of the problem.

What caps the migration rate is **what the Tracto runtime can express**. No converter,
however good, moves that ceiling by one point.

---

## The best thing to build first is not a tool

**Migrate five real UiPath flows by hand, with no tooling, and record every activity
encountered.**

Nobody currently knows what these UiPath flows are made of. Every coverage estimate in these
documents — including the ranking of which backend service matters most — is a guess about a
distribution that has never been measured. Five hand migrations replace the guess with the
real histogram: how many activities are Excel work, how many are desktop control clicks, how
many flows contain an `If` that cannot be dissolved.

The record is not throwaway:

- It **is** the migration skill — each flow contributes recipe rows (the UiPath activity
  shape, the Tracto steps it became, why).
- It **is** the gap-priority list, ordered by what actually bit, not by what was predicted.

### Why measure rather than estimate

The browser skill's templates were written from the runtime source, looked correct, and were
wrong in three ways that only running them exposed:

- `getTexts` published a `count` field that no documentation mentioned.
- An invented Outlook selector (`[role='option'] [title]`) matched 47 elements and returned
  empty text.
- `browser-then-desktop` failed, then passed unchanged — Notepad happened to be open with
  unsaved files.

Reading found none of these. Running found all of them. A migration tool built on an
estimate of flow composition fails the same way, only more expensively.

---

## Then build, in this order

A prediction of what the five flows will show, to be checked against the evidence rather
than trusted ahead of it. Row numbers refer to Table 2 of
`UIPATH-TRACTO-CAPABILITY-MAPPING.md`.

1. **Native Excel and file operations as services** (#13, #15). Likely the most common
   activity family in enterprise RPA, and it removes the most common loop at the same time:
   iteration moves into Go, where it is free.
2. **Desktop UI Automation selectors** (#23). Converts the worst category — UiPath desktop
   control clicks — from "rewrite by hand as a keyboard route" to "translate directly".
   Standard .NET APIs, already proven inside this codebase.
3. **`if` in the script schema** (#1). The expensive one: it touches all three runtimes, the
   validator and the Designer.
4. **The migration skill**, grown continuously from steps 1-3 rather than written up front.

---

## On the 90% target

The number behaves differently depending on what is being counted.

**90% of steps** is realistic once #13, #15 and #23 exist. Most steps in most flows are
ordinary open / type / read / save work.

**90% of flows** is not, without #1. A loop over rows dissolves into a service call. A
*decision* does not — "if the amount is over 10,000, route to approval" is branching inside
the flow, and no backend endpoint removes it unless the entire flow moves into the service.
Under the current straight-line runtime, a single `If` anywhere in a flow makes that whole
flow unmigratable.

So: high step coverage is cheap; high flow coverage costs the control-flow work. The five
hand migrations determine which one is actually being bought.

---

## The work that will dominate the project

**A failed run currently does not record why it failed.** Diagnosing means reading the
console on the machine that ran it. That is tolerable for one script and it is the entire
cost structure of a migration project, where dozens of scripts each fail several times
before they are right.

Writing the JSON takes minutes. The loop of *run → it died at step 7 → why? → walk to the
machine → the window title differed → fix → rerun* is the project. Persist a failure reason
and evidence per step and that loop closes without a person leaving their desk.

This is small work, it is not migration-specific, and it likely buys more migrated flows per
day than anything else listed here. Scoped in `next-task.md`.

---

## If only one thing gets built

The five hand migrations. If two: that, plus failure reasons.
