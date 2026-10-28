# Sprint 3 Review — Intake & Dispatch

**Date:** 2026-10-24
**Reviewer:** Sprint-review workflow (automated)

---

## Sprint Goal

> Jobs validated, queued, offered exclusively.

**Achieved: YES**

Evidence:
- `CreateJobCommand` validates all guards (ContinuationScriptRef, SchemaVersion, artifact existence, deadline), deduplicates by EventId, and persists CREATED→QUEUED with two journal entries in a single atomic transaction (Domain Invariant 5).
- `PullJobCommand` scans QUEUED runs, enforces RuntimeType + capability-subset matching, blocks duplicate offers, enforces interactiveSessionId exclusivity (Domain Invariant 6), creates a `LeaseRecord` with a 30-second offer window, and emits a `LEASE_OFFERED` journal entry atomically.
- Build and tests are green with no regressions.

---

## Build

| Metric | Result |
|---|---|
| Errors | 0 |
| Warnings | 292 (all CS1591 XML-doc on test methods — pre-existing, not actionable) |
| NuGet resolution warnings | 4 (pre-existing, not actionable) |

---

## Test Counts

| Suite | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| Unit | 291 | 0 | 0 | 291 |
| Integration | 20 | 0 | 0 | 20 |
| **Total** | **311** | **0** | **0** | **311** |

---

## Story AC Coverage

### OC-02 — Job Intake: Manual, Scheduled and Event Triggers
**Commit:** 98e9378

| Acceptance Criterion | Status | Evidence |
|---|---|---|
| POST /jobs accepts TriggerType enum (MANUAL, SCHEDULED, EVENT) | PASS | `CreateJobCommand.TriggerType` field; `RunRecord` stores it |
| ContinuationScriptRef present → 422 error CONTINUATION_SCRIPT_REF_FORBIDDEN | PASS | Guard at line 76–79 of `CreateJobCommand.cs` |
| SchemaVersion ≠ "1" → 422 error UNSUPPORTED_SCHEMA | PASS | Guard at line 82–85 |
| Artifact not found or soft-deleted → 422 error ARTIFACT_NOT_FOUND | PASS | Guard at line 88–93 |
| Deadline ≤ now → 422 error DEADLINE_IN_PAST | PASS | Guard at line 98–101 |
| EventId dedup: duplicate within 5-minute window → idempotent 200 with existing RunId | PASS | Lines 104–116; `EventIdDedupWindow = TimeSpan.FromMinutes(5)` |
| Run persisted CREATED→QUEUED with two journal entries in one transaction | PASS | Lines 119–151; `BeginAsync`…`CommitAsync` wrapping both journal appends |
| 35 unit tests covering OC-02 paths | PASS | `tests/Tracto.Orchestrator.Unit.Tests/Runs/CreateJobCommandTests.cs` |

### OC-03 — Queue Management and Worker Capability Matching
**Commit:** d1ee5a4 (19 files changed)

| Acceptance Criterion | Status | Evidence |
|---|---|---|
| Worker must be registered and in AVAILABLE state | PASS | Lines 110–121 of `PullJobCommand.cs` |
| Worker must not already hold an active lease | PASS | Lines 124–131; `FindActiveForWorkerAsync` check |
| RuntimeType matching (case-insensitive) against artifact SelectorContent | PASS | `ArtifactRuntimeTypeMatches` helper; `StringComparison.OrdinalIgnoreCase` |
| RequiredCapabilities must be a subset of worker capability names | PASS | `RunCapabilitiesAreSatisfied` helper with HashSet lookup |
| Only one active offer per run (exclusive dispatch) | PASS | `HasActiveOfferForRunAsync` guard at line 159 |
| InteractiveSessionId: only one headed worker per sessionId | PASS | Domain Invariant 6 enforced via `HasActiveSessionLeaseAsync` at line 165 |
| Headless worker skips headed-only jobs | PASS | `RunRequiresInteractiveSession` check at line 171 |
| Offer lease window = 30 seconds | PASS | `LeaseOfferWindow = TimeSpan.FromSeconds(30)` at line 55 |
| LeaseRecord created with AttemptId, hash pinned from ArtifactRecord.ContentHash | PASS | Lines 189–202; `artifactHash: matchedHash!` |
| LEASE_OFFERED journal entry written atomically with lease | PASS | Lines 204–213 inside the same `BeginAsync`/`CommitAsync` |
| No journal entry or lease created when no matching run | PASS | Early returns at lines 185, 121, 130 before any write |
| NoPendingJob returned when no match | PASS | `MatchResult.NoPendingJob` returned on all no-match paths |
| Transaction rollback guaranteed on any non-commit exit | PASS | `finally { if (!committed) await _unitOfWork.RollbackAsync(); }` at lines 237–238 |

---

## Gaps to Fix Before Sprint 4

None. All acceptance criteria are satisfied, the build is clean, and all 311 tests pass.

The four NuGet resolution warnings and 292 XML-doc warnings are pre-existing and do not block the next sprint. They should be tracked as technical-debt items if XML-doc generation is a delivery requirement.

---

## Verdict

**SPRINT 3 CLOSED** — Goal achieved, tests green (311/311), no blocking gaps.
