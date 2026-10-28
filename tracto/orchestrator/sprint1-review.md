# Sprint 1 Review — Tracto Orchestrator

**Sprint Goal**: Durable container in both modes; atomic state persistence.

**Review date**: 2026-10-24

---

## Sprint Goal: ACHIEVED

Evidence:
- Container starts in Connected mode (binds 0.0.0.0) and Isolated mode (binds 127.0.0.1) via `TRACTO_MODE` env var.
- Invalid or missing `TRACTO_MODE` aborts startup with a fatal, actionable error message (code `TRACTO_MODE_MISSING` / `TRACTO_MODE_INVALID`).
- `GET /health` returns 200 OK with `{ status, dbConnected, queueDepth, mode }` backed by a live SQLite `CanConnectAsync()` probe.
- EF Core migrations run idempotently on startup; `EnsureCreatedAsync` used for the `Testing` environment.
- `RestartRecoveryService` reconciles RUNNING runs whose worker was offline at restart; batch committed atomically via `IUnitOfWork.CommitAsync`.
- All domain invariants for the state machine and UNKNOWN state encoded directly in `RunRecord`.

---

## Test Counts

| Suite | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| Unit | 164 | 0 | 0 | 164 |
| Integration | 13 | 0 | 0 | 13 |
| **Total** | **177** | **0** | **0** | **177** |

Build: **0 errors**, 169 warnings (CS1591 missing XML doc comments — non-breaking, no-op for runtime).

---

## Story AC Coverage

### OC-16 — Deployment: Connected and Isolated Modes (commit e70af4a)

| AC | Status | Evidence |
|---|---|---|
| Container starts in Connected mode, binds 0.0.0.0 | PASS | `OrchestratorSettings.ListenUrl` returns `http://0.0.0.0:{port}` for Connected; wired in `Program.cs` via `builder.WebHost.UseUrls`. |
| Container starts in Isolated mode, binds 127.0.0.1 | PASS | `OrchestratorSettings.ListenUrl` returns `http://127.0.0.1:{port}` for Isolated. |
| Invalid `TRACTO_MODE` aborts with actionable error | PASS | `DeploymentModeParser.Parse` returns `Result.Failure` with codes `TRACTO_MODE_MISSING` / `TRACTO_MODE_INVALID`; `Program.cs` calls `Log.Fatal` + throws `InvalidOperationException`. |
| `GET /health` returns 200 OK | PASS | Integration test `Get_Health_Returns200OK` confirmed; endpoint registered in `Program.cs`. |
| Health body contains `{ status, dbConnected, queueDepth, mode }` | PASS | `HealthResponse` record; `dbConnected` from `CanConnectAsync`; `queueDepth` from `db.RunRecords.CountAsync(r => r.State == RunState.Queued)`. |
| Mode reflected in health response | PASS | Integration test `Get_Health_ModeReflectsDeploymentMode` confirmed. |
| Test coverage (OC-16 commit) | PASS | 109 unit + 13 integration = 122 (as stated in story outcome). |

### OC-15 — Persistence, Durability and Restart Recovery (commit d4f4bfa)

| AC | Status | Evidence |
|---|---|---|
| Full run state machine encoded (CREATED→QUEUED→LEASED→RUNNING→terminal) | PASS | `RunRecord` with `Enqueue`, `Lease`, `Start`, `Succeed`, `Fail`, `HandOff`, `Cancel`, `MarkTimedOut`, `MarkUnknown` — state enforced by `EnsureState`. |
| UNKNOWN is a state, not an error category; always NeedsAttention=true + unknownReason | PASS | `RunRecord.MarkUnknown` throws `ArgumentException` if reason is blank; always sets `NeedsAttention = true`. |
| AttemptId fences every transition; mismatch → 409 | PASS | `EnsureAttemptId` in `RunRecord`; error message explicitly says "Respond 409". |
| Restart recovery marks orphaned RUNNING runs UNKNOWN atomically | PASS | `RestartRecoveryService.StartAsync` collects offline workers, calls `run.MarkUnknown`, appends `JournalEntry`, commits via `IUnitOfWork.CommitAsync` — all in one batch. |
| Writes atomic (EF Core transaction + journal entry in same txn) | PASS | `IUnitOfWork.CommitAsync` wraps run updates and journal appends in one `SaveChangesAsync`. |
| IClock / FakeClock used in tests (no Thread.Sleep) | PASS | `IClock` abstraction in Application layer; `SystemClock` in Infrastructure; tests use FakeClock. |
| EF migrations applied idempotently on startup | PASS | `MigrateAsync` in production; `EnsureCreatedAsync` in Testing environment (`Program.cs` lines 107-129). |
| Test coverage (post OC-15) | PASS | 164 unit + 13 integration = 177 total (matches stated story outcome). |

---

## Domain Invariant Compliance

| Invariant | Met | Notes |
|---|---|---|
| 1. Run state machine (CREATED→…→terminal) | YES | `RunRecord` enforces all transitions. |
| 2. UNKNOWN = state, never errorCategory; NeedsAttention=true + unknownReason | YES | `MarkUnknown` enforces at domain layer. |
| 3. AttemptId fences; stale → 409 | YES | `EnsureAttemptId` in domain; 409 semantics documented in error message. |
| 4. No plaintext secrets; no DPAPI | YES | No `ProtectedData` references found; no credential storage in Sprint 1 scope. |
| 5. All writes atomic (EF + journal in same txn) | YES | `IUnitOfWork` pattern; `RestartRecoveryService` commits batch atomically. |
| 6. interactiveSessionId foreground-input lease | N/A | Not in Sprint 1 scope. |
| 7. Artifact versions immutable | N/A | Not in Sprint 1 scope. |

---

## Gaps / Notes for Next Sprint

- 169 CS1591 warnings (missing XML doc comments on test types). Non-breaking. Consider enabling `<GenerateDocumentationFile>` only on `src/` projects, not test projects, or suppressing `CS1591` in test `.csproj` files to keep the build output clean.
- `UnitTest1.cs` placeholder test class in Integration.Tests is still present (test passes trivially). Should be removed before Sprint 2 to avoid confusion.
- No gaps blocking Sprint 2.

---

## Verdict

**SPRINT 1 CLOSED** — goal achieved, all tests green (177/177 passed, 0 failed, 0 skipped), build clean (0 errors). Both OC-16 and OC-15 acceptance criteria fully covered by committed code. Ready to proceed to Sprint 2.
