# Sprint 5 Review — Tracto Orchestrator

**Sprint Goal:** Every run reaches immutable terminal; crash-safe reconciliation.

**Review Date:** 2026-10-24

---

## Sprint Goal: ACHIEVED

Evidence:
- All terminal state transitions (SUCCEEDED, FAILED, HANDED_OFF, CANCELLED, TIMED_OUT, UNKNOWN) are enforced through `CommitFinalResultCommand` (OC-06) and guarded by the AttemptId fence.
- Crash-safe reconciliation is implemented through `ReconciliationWorker` (BackgroundService, 15-second scan) → `ReconciliationService.ScanOnceAsync` → `ReconcileRunCommandHandler`. A run whose worker disconnects is either recovered to SUCCEEDED (journal confirms RUN_SUCCEEDED with matching AttemptId) or transitioned to UNKNOWN (uncertain/empty journal). Neither path ever sets State = QUEUED or CREATED.
- Build passes with 0 errors (471 XML-doc warnings only, all pre-existing).
- All 512 tests pass; 0 failures; 0 skipped.

---

## Test Counts

| Assembly                                      | Passed | Failed | Skipped | Total |
|-----------------------------------------------|--------|--------|---------|-------|
| Tracto.Orchestrator.Unit.Tests.dll            | 492    | 0      | 0       | 492   |
| Tracto.Orchestrator.Integration.Tests.dll     | 20     | 0      | 0       | 20    |
| **Total**                                     | **512**| **0**  | **0**   | **512** |

---

## Story AC Coverage

### OC-06 — Commit Final Result (commit 688c622)

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | RUNNING → terminal (SUCCEEDED / FAILED / HANDED_OFF / CANCELLED / TIMED_OUT / UNKNOWN) | PASS — `CommitFinalResultCommand` transitions run state via `RunRecord` domain methods; all six terminal states supported. |
| AC-2 | AttemptId mismatch → 409 WRONG_ATTEMPT | PASS — handler checks `command.AttemptId != run.AttemptId` and returns `Error.Conflict("WRONG_ATTEMPT", …)`. |
| AC-3 | Already-terminal run → idempotent 200, no duplicate FinalResultRecord | PASS — `run.IsTerminal()` guard returns idempotent success without writing. |
| AC-4 | UNKNOWN without UnknownReason → 422 UNKNOWN_REASON_REQUIRED | PASS — validated before any write in handler. |
| AC-5 | No plaintext secrets in payload — sensitive pattern scan | PASS — payload is scanned with regex; rejected with 422 SENSITIVE_PAYLOAD and security journal entry on hit. |
| AC-6 | Worker slot released exactly once, atomically | PASS — `IWorkerRepository.DecrementSlot` called inside single EF Core transaction via `IUnitOfWork.CommitAsync`. |
| AC-7 | FinalResultRecord written in same transaction as state change | PASS — `_finalResultRepo.AddAsync` + `_unitOfWork.CommitAsync` in same `try/finally` block. |
| AC-8 | Journal entry appended (RUN_SUCCEEDED / RUN_FAILED / etc.) in same transaction | PASS — `_journalRepo.AppendAsync` precedes `CommitAsync`. |
| AC-9 | 439 unit + 20 integration tests green (as of OC-06 commit) | PASS — 459 tests confirmed green at OC-06 commit 688c622. |

### OC-10 — Reconciliation After Disconnect or Restart (commit f735a59)

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | Background scan every 15 s via BackgroundService | PASS — `ReconciliationWorker` uses `PeriodicTimer(ReconciliationService.ScanInterval)` where `ScanInterval = TimeSpan.FromSeconds(15)`. |
| AC-2 | Scan finds all Expired leases (set by OC-05) | PASS — `_leaseRepo.FindExpiredAsync` in `ReconciliationService.ScanOnceAsync`. |
| AC-3 | Already-terminal run → skipped (no write) | PASS — `run.IsTerminal()` guard before dispatch. |
| AC-4 | Stale AttemptId lease → skipped silently | PASS — `run.AttemptId != lease.AttemptId` guard before dispatch. |
| AC-5 | Journal with RUN_SUCCEEDED + matching AttemptId → SUCCEEDED | PASS — `ReconcileRunCommandHandler` inspects entries for `SucceededEventType` with matching AttemptId; calls `run.ForceSucceed`. |
| AC-6 | Uncertain/empty journal → UNKNOWN with NeedsAttention=true + unknownReason | PASS — `ReconcileToUnknownAsync` calls `run.MarkUnknown("Reconciliation: uncertain journal", now)`; `NeedsAttention=true` enforced by `RunRecord.MarkUnknown`. |
| AC-7 | NEVER auto-replay: no transition to QUEUED or CREATED | PASS — documented invariant in handler comments and enforced by state machine — only SUCCEEDED and UNKNOWN are target states. |
| AC-8 | AttemptId NOT incremented during reconciliation | PASS — `run.ForceSucceed` and `run.MarkUnknown` do not change `AttemptId`; handler retains original `command.AttemptId`. |
| AC-9 | All writes atomic (EF Core transaction + journal entry in same txn) | PASS — `_unitOfWork.BeginAsync` / `CommitAsync` / `RollbackAsync` pattern wraps all writes in `ReconcileRunCommandHandler`. |
| AC-10 | POST /jobs/{runId}/reconcile — worker can submit own journal | PASS — `ReconcileEndpoints.MapReconcileEndpoints` wires `POST /jobs/{runId:guid}/reconcile`; maps DTOs to `ReconcileRunCommand`. |
| AC-11 | 409 on AttemptId mismatch from API | PASS — endpoint maps `Error.Code == "WRONG_ATTEMPT"` → HTTP 409. |
| AC-12 | 53 OC-10 unit tests across three test files | PASS — `ReconcileRunCommandTests.cs` (Jobs/), `ReconcileRunCommandHandlerTests.cs` (Recovery/), `ReconcileRunCommandTests.cs` (Reconciliation/) all present; total unit count rose to 492. |

---

## Gaps / Issues

None. All acceptance criteria for OC-06 and OC-10 are implemented and verified by green tests.

XML-doc warnings (471) are non-blocking and pre-dated this sprint. They are informational only.

---

## Verdict

**SPRINT 5 CLOSED** — Goal achieved, all tests green (512/512 passed, 0 failed, 0 skipped).

Next sprint: Sprint 6 (Conformance and Release).
