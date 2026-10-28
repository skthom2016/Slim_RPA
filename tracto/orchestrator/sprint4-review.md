# Sprint 4 Review — Lease Lifecycle

**Sprint Goal**: Execution slots fenced, heartbeated, cancellable.

**Review date**: 2026-10-24

---

## Sprint Goal: ACHIEVED

All three stories (OC-04, OC-05, OC-09) delivered and verified. Execution slots are now
fenced by AttemptId, kept alive by heartbeat with automatic expiry detection, and
cancellable from any non-terminal state. Domain invariants 1–7 are honoured throughout.

---

## Build

| Metric | Result |
|--------|--------|
| Errors | 0 |
| Warnings | 384 (all pre-existing CS1591 XML-doc warnings — no new warnings introduced) |
| Framework | net10.0 / C# 14 |

Command: `dotnet build -c Debug`

---

## Test Results

| Suite | Passed | Failed | Skipped | Total |
|-------|--------|--------|---------|-------|
| Unit | 395 | 0 | 0 | 395 |
| Integration | 20 | 0 | 0 | 20 |
| **Grand total** | **415** | **0** | **0** | **415** |

Cumulative growth by story:

| Story | After-story unit count | Integration | Total |
|-------|------------------------|-------------|-------|
| OC-04 | 318 | 20 | 338 |
| OC-05 | 362 (+44) | 20 | 382 |
| OC-09 | 395 (+33) | 20 | 415 |

---

## Story AC Coverage

### OC-04 — Lease Acceptance and AttemptId Fencing
Commit: `f9f8b1d`

| Acceptance Criterion | Status | Evidence |
|----------------------|--------|----------|
| Worker echoes AttemptId; mismatch → 409 WRONG_ATTEMPT | PASS | `AcceptLeaseCommandHandler` step 3: `AttemptId != command.AttemptId` → `Error.General("WRONG_ATTEMPT")` |
| WorkerId mismatch → 409 WRONG_WORKER | PASS | Step 2: ordinal WorkerId check before AttemptId check |
| Background-expired lease (State=Expired in DB) → 409 LEASE_EXPIRED | PASS | Step 1 fallback to `FindByRunIdAsync`; WrokerId+AttemptId checks still applied before surfacing LEASE_EXPIRED |
| Lease alive but ExpiresAt < now: no RUN_RUNNING journal → UNKNOWN (needsAttention, unknownReason set) | PASS | Step 4: `HasEntriesOfTypeAsync("RUN_RUNNING")` false → `run.MarkUnknown(...)` |
| Lease alive but ExpiresAt < now: RUN_RUNNING confirmed → Requeue | PASS | Step 4: `HasEntriesOfTypeAsync("RUN_RUNNING")` true → `run.Requeue(now)` |
| Idempotent: same WorkerId + AttemptId, lease Accepted → 200 (no second transition) | PASS | Step 5 (before Step 4): `lease.State == LeaseState.Accepted` → returns existing payload |
| Happy path: LEASED→RUNNING; journal entry RUN_RUNNING; atomic commit | PASS | Step 6: `run.Start(...)`, `AppendAsync("RUN_RUNNING")`, `CommitAsync` inside try/finally |
| No plaintext secrets in payload | PASS | `LeasePayload.Create(...)` calls `AssertNoResolvedSecrets()` at construction; `BuildPayload` passes `run.SecretRefs` (opaque refs) |
| All writes atomic (invariant 5) | PASS | Single `BeginAsync`/`CommitAsync` wraps all writes; `RollbackAsync` in finally if not committed |

### OC-05 — Heartbeat Processing and Lease Expiry
Commit: `ca1918f`

| Acceptance Criterion | Status | Evidence |
|----------------------|--------|----------|
| WorkerId fencing before AttemptId (security ordering) | PASS | `SendHeartbeatCommandHandler` step 2a checks WorkerId first |
| AttemptId mismatch → 409 WRONG_ATTEMPT | PASS | Step 2b |
| Stale SeqNo (≤ LastSeqNo) → STALE_SEQ_NO; LastHeartbeatAt NOT updated | PASS | Step 3 early return before any write; `command.SeqNo <= lease.LastSeqNo` |
| Heartbeat on Offered (not yet Accepted) lease → WRONG_STATE | PASS | Step 4: `lease.State == LeaseState.Offered` → `Error.General("WRONG_STATE")` |
| Successful heartbeat updates LastHeartbeatAt + LastSeqNo atomically | PASS | Step 5: `lease.RecordHeartbeat(now, command.SeqNo)` inside `BeginAsync`/`CommitAsync` |
| Response includes `cancellationRequested` flag | PASS | Step 6: `new HeartbeatResult(lease.CancellationRequested)` |
| LeaseExpiryMonitorService: ExpiryWindow=60s, ScanInterval=10s | PASS | `static readonly TimeSpan ExpiryWindow = TimeSpan.FromSeconds(60)` and `ScanInterval = TimeSpan.FromSeconds(10)` |
| Per-lease atomic commit; one lease failure does not abort the batch | PASS | `foreach` loop: each lease has its own `BeginAsync`/`CommitAsync`/`catch`/`finally` |
| Already-expired lease (background expiry) → 409 LEASE_EXPIRED | PASS | Step 1 fallback: `FindByRunIdAsync` non-null with Expired state |
| No lease exists → 404 NotFound | PASS | Step 1 fallback: `FindByRunIdAsync` null |

### OC-09 — Cancellation
Commit: `3bfaa3c`

| Acceptance Criterion | Status | Evidence |
|----------------------|--------|----------|
| QUEUED run → immediate CANCELLED; RUN_CANCELLED journal; active lease force-expired | PASS | `CancelRunCommandHandler` else-branch: `run.Cancel(now)`, `AppendAsync("RUN_CANCELLED")`, `activeLease.ForceExpire()` |
| LEASED run → immediate CANCELLED; offer/accepted lease force-expired via ForceExpire | PASS | Same else-branch; `ForceExpire()` handles both Offered and Accepted without throwing |
| RUNNING run → sets `CancellationRequested = true`; RUN_CANCEL_REQUESTED journal; run stays RUNNING | PASS | `lease.RequestCancellation()`, `AppendAsync("RUN_CANCEL_REQUESTED")`; no `run.Cancel()` called |
| CANCELLED run → idempotent 200 (no write) | PASS | Early return: `run.State == RunState.Cancelled` → `Result.Ok(command.RunId)` before transaction |
| Terminal states (SUCCEEDED, FAILED, TIMED_OUT, HANDED_OFF, UNKNOWN) → 409 TERMINAL_STATE | PASS | `run.IsTerminal()` → `Error.General("TERMINAL_STATE")` |
| RequestedBy required; null/blank → 400 Validation | PASS | Guard: `string.IsNullOrWhiteSpace(command.RequestedBy)` → `Error.Validation(...)` |
| All writes atomic (invariant 5) | PASS | Single `BeginAsync`/`CommitAsync`/`finally RollbackAsync` |
| Journal payload properly JSON-serialized (no injection) | PASS | `JsonSerializer.Serialize(new { requestedBy })` — comment in code explains why (control chars, surrogates) |

---

## Domain Invariant Compliance

| Invariant | Description | Sprint 4 compliance |
|-----------|-------------|---------------------|
| 1 | Run state machine | All transitions verified: QUEUED→LEASED→RUNNING (OC-04); RUNNING stays on cancel request (OC-09); QUEUED/LEASED→CANCELLED (OC-09) |
| 2 | UNKNOWN = needsAttention + unknownReason; never auto-replayed | OC-04 expired-no-evidence path: `MarkUnknown("Late accept; start uncertain")` — no auto-replay |
| 3 | AttemptId fences every stage; stale/foreign → 409; duplicate idempotent 200 | OC-04 and OC-05 both enforce in all branches including pre-expired fallback |
| 4 | No plaintext secrets stored or returned | `LeasePayload.Create` + `AssertNoResolvedSecrets()` enforced at domain boundary |
| 5 | All writes atomic (journal in same txn) | Every handler uses BeginAsync/CommitAsync/RollbackAsync; journal appended inside same txn |
| 6 | interactiveSessionId foreground lease | Not modified this sprint; prior invariant intact |
| 7 | Artifact versions immutable; deletion blocked while lease references | Not modified this sprint; prior invariant intact |

---

## Gaps / Technical Debt

None blocking Sprint 5. Minor items logged for awareness:

1. **384 CS1591 warnings** — all pre-existing XML-doc warnings on test classes. No new warnings added this sprint. Low priority; a `<NoWarn>CS1591</NoWarn>` in test project csproj would clean the build output.
2. **RUNNING→CANCELLED flow relies on worker acknowledgement** — this is correct by design (spec: worker discovers via heartbeat and calls completion endpoint). No gap, but Sprint 5 completion endpoint (OC-06 / OC-07) must wire the final RUNNING→CANCELLED transition.
3. **LeaseExpiryMonitorService does not update RunRecord state** — it marks the lease Expired but leaves the RunRecord in RUNNING. The AcceptLease handler covers the UNKNOWN/Requeue logic for late accepters. The RUNNING→TIMED_OUT or RUNNING→UNKNOWN path for a heartbeat-lost run that was never explicitly cancelled should be confirmed in Sprint 5 scope.

---

## Verdict

**SPRINT 4 CLOSED** — goal achieved, all tests green.

- Sprint Goal "Execution slots fenced, heartbeated, cancellable": **YES**
- Build errors: **0**
- Test failures: **0** (415/415 passed)
- Story AC gaps: **none**
- Regressions: **none** (integration suite count unchanged at 20)
