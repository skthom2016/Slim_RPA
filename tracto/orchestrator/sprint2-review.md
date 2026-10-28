# Sprint 2 Review — Identity

**Date:** 2026-10-24
**Sprint Goal:** Workers register; immutable artifacts published.

---

## Sprint Goal: ACHIEVED

Both sprint stories are committed, build clean, and all tests pass.

---

## Build

| Metric | Result |
|---|---|
| Errors | 0 |
| Warnings | 227 (pre-existing CS1591 XML-comment warnings, unrelated to sprint stories) |
| Build time | ~3 s |

---

## Test Counts

| Suite | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| Unit | 213 | 0 | 0 | 213 |
| Integration | 20 | 0 | 0 | 20 |
| **Grand total** | **233** | **0** | **0** | **233** |

---

## Story AC Coverage

### OC-01 — Worker Registration and Capability Advertisement (commit `ae983c6`)

| Acceptance Criterion | Status | Evidence |
|---|---|---|
| POST /workers/register upserts worker row (create + re-register idempotent 200) | PASS | `RegisterWorkerCommandHandler.HandleAsync` branches on existing vs new; integration test `Register_SameWorkerTwice_Returns200BothTimes` |
| Missing workerId or runtimeType → 400 validation error | PASS | Handler returns `Error.Validation`; integration test `Register_MissingRuntimeType_Returns400ProblemDetails` |
| capabilityDigest mismatch → warning logged, registration succeeds (not rejected) | PASS | Handler logs warning and continues; integration test `Register_DigestMismatch_Returns200` |
| RuntimeType immutable after first registration | PASS | `UpdateRegistration` explicitly does not update `RuntimeType` |
| Re-registration restores Offline worker to Available without touching active leases | PASS | `WorkerRecord.UpdateRegistration` sets `IsOnline=true`, transitions `Offline→Available` |
| DELETE /workers/{id}/offline → 200; unknown worker → 404 | PASS | `MarkWorkerOfflineCommand`; integration tests `MarkOffline_UnknownWorker_Returns404`, `MarkOffline_RegisteredWorker_Returns200` |
| No plaintext secrets stored or returned | PASS | No credential fields; capabilityDigest is an integrity hint, not a secret |
| Writes atomic (EF Core + journal in same txn) | PASS | `UnitOfWork.BeginAsync/CommitAsync` wraps every write; 213 unit + 20 integration tests all green |

### OC-11 — Script and Artifact Registry (commit `1cc17a8`)

| Acceptance Criterion | Status | Evidence |
|---|---|---|
| PUT /artifacts/{id}/versions/{version} publishes artifact; returns 201 with contentHash | PASS | `PublishArtifactHandler` + `ArtifactEndpoints.PublishAsync`; `Publish_ValidCommand_ReturnsSuccessWithContentHash` |
| contentHash is lower-case SHA-256 hex of payload bytes | PASS | `Publish_ContentHash_IsSha256HexOfPayload` asserts 64-char hex regex |
| Overwrite existing non-deleted version → 409 VERSION_EXISTS | PASS | `Publish_VersionAlreadyExists_ReturnsConflict` |
| SchemaVersion ≠ "1" → 422 UNSUPPORTED_SCHEMA with diagnostic | PASS | `Publish_UnsupportedSchemaVersion_ReturnsUnsupportedSchemaError`, `Publish_SchemaVersionEmpty_ReturnsUnsupportedSchemaError` |
| Empty (non-null) signature → 422 INVALID_SIGNATURE | PASS | `Publish_EmptySignatureProvided_ReturnsInvalidSignatureError` |
| Non-empty signature stub accepted; null signature accepted | PASS | `Publish_NonEmptySignatureProvided_Succeeds`, `Publish_NullSignature_Succeeds` |
| SelectorContent stored verbatim (opaque passthrough) | PASS | `Publish_WithSelectorContent_StoredInRecord`, `Get_WithSelectorContent_ReturnedPayloadUnchanged` |
| GET /artifacts/{id}/versions/{version} returns payload + ETag = ContentHash | PASS | `Get_ExistingArtifact_ReturnsPayloadAndContentHash`; `ArtifactEndpoints.GetAsync` sets `ETag` header |
| GET soft-deleted or missing artifact → 404 | PASS | `Get_DeletedArtifact_ReturnsNotFound`, `Get_NonExistentArtifact_ReturnsNotFound` |
| DELETE blocked (409 ARTIFACT_IN_USE) while LEASED/RUNNING run references artifact (invariant 7) | PASS | `Delete_ArtifactInUseByActiveLease_ReturnsConflict`; `IArtifactRepository.IsReferencedByActiveRunAsync` |
| DELETE unknown / already-deleted artifact → 404 | PASS | `Delete_AlreadyDeletedRecord_ReturnsNotFound`, `Delete_NonExistentArtifact_ReturnsNotFound` |
| Versions immutable once published (no content-update path exposed) | PASS | No PATCH/PUT-overwrite route; `ArtifactRecord` exposes only `SoftDelete` mutation |
| StoredAt taken from IClock (FakeClock governs in tests) | PASS | `Publish_StoredAtTimestamp_MatchesFakeClock` |
| Zero EF/ASP refs in Domain + Application layers | PASS | Domain and Application .csproj have no EF/ASP package references |

---

## Gaps / Notes

None. All acceptance criteria are covered, build is error-free, and the full test suite is green.

The 227 CS1591 warnings are pre-existing across the test assemblies (XML doc comments not required for test classes). They do not block any story and were present before Sprint 2 started.

---

## Decision

**SPRINT 2 CLOSED** — sprint goal achieved, 233/233 tests green, 0 build errors.
