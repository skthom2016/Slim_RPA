# 09 — Testing and Conformance Strategy

## Test Classification (from User Stories §2)

| Label | Type | When Used |
|---|---|---|
| U | Deterministic unit / schema test | Validator, parser, state machine, DTO serialization |
| C | Cross-process contract test | Mock Orchestrator ↔ Reference Adapter; service invoke ↔ fake service |
| M | Mock Orchestrator / service integration | End-to-end scenario through mock stack |
| S | Security / policy test | Denial, redaction, boundary enforcement |
| R | Compatibility / regression test | Schema N/N-1, breaking-change detection, cross-runtime isolation |

## General Principles

- **Controllable clocks everywhere:** Use `System.TimeProvider` (`FakeTimeProvider`) in all code that depends on time — lease deadlines, heartbeat timeouts, repeat budgets, telemetry timestamps. No test relies on real waiting.
- **Deterministic fixtures:** All conformance fixtures use seeded, fixed inputs and deterministic fault schedules (CF-09's "broken connection" is simulated by the mock, not by a real network failure).
- **No shared runtime state across tests:** Each test constructs its own mock Orchestrator and fake service instance with fresh in-memory state.
- **Stable AC identifiers:** Tests are annotated with the AC ID they verify (e.g., `[Trait("AC", "ST1-01-AC02")]`). This preserves traceability from requirement to test.
- **No automated test can claim to validate business intent:** ST1-04-AC06 and similar intent-based checks require a human publishing reviewer. Tests validate structural grammar enforcement only.

## Coverage by Area

### Schema and Model Tests (`Tracto.Contract.Schemas.Tests`, `Tracto.Contract.Models.Tests`)

- Every JSON Schema validates a set of known-good fixtures (positive cases).
- Every JSON Schema rejects known-bad fixtures — one per required field removed, one per wrong type, one per out-of-range value.
- Schema N/N-1: Stage 1 begins with one schema major (v1). N/N-1 compatibility testing applies when a second major is actually published. Until then, the test suite verifies that (a) a hypothetical "older major" artifact (e.g., a fixture with a fabricated `schemaVersion` value outside the supported range) is rejected with a republish diagnostic, and (b) the current major is accepted. Do not create a placeholder v2 schema solely to satisfy N/N-1 tests.
- DTO round-trip: every C# model serializes to JSON and back with `System.Text.Json`; no data loss; all enums preserve string values.
- `automationId` format: path-like, executable-like, URL-like, and path-traversal values all rejected.

### Validator and Parser Tests (`Tracto.Contract.Validation.Tests`)

- `EnvelopeValidator`: full positive case; each required field removed; each field wrong-typed; policyRef unknown; runtimeType mismatch; automationId invalid forms; output without classification.
- `ArtifactTrustVerifier`: valid signer; invalid digest; tampered content; untrusted signer; offline bundle with full field check.
- `RefResolver` / `StepValidator`: all four ref namespaces (`inputs.*`, `vars.*`, `steps.*.outputs.*`, `secrets.*`); forward ref; cyclic ref; type mismatch; uninitialized var (C1); sensitive var redaction.
- `ConditionGrammarParser`: each allowed predicate type per runtime; finite all/any/not; domain arithmetic rejected; C#/JS code rejected; depth limit exceeded.
- `ServiceInvokeValidator`: approved serviceRef; raw URL; unknown serviceRef; SQL connection; response schema mismatch; executable content in response.
- `ControlFlowValidator`: forward branch selection; backward ref rejected; maxIterations within/outside cap; dynamic maxIterations rejected.

### State Machine Tests (`Tracto.Contract.Execution.Tests`)

- All legal run-state transitions (from table §7 of Contract).
- All illegal transitions (e.g., SUCCEEDED→RUNNING, UNKNOWN→QUEUED): each rejected without mutating state.
- C3 scenarios: precondition timeout → FAILED/TIMEOUT; action deadline with confirmed non-commit → TIMED_OUT; action deadline with uncertain commit → UNKNOWN; postcondition fail with reversible effect → FAILED; postcondition fail with committed irreversible → UNKNOWN.
- `needsAttention=true` present on every UNKNOWN result; absent on FAILED/TIMED_OUT unless explicitly flagged.
- `COMPLETED` and `SUCCESS` rejected as invalid state values.
- SKIPPED only from unselected branch arm; false precondition → never SKIPPED.
- All 11 `errorCategory` values serialize/deserialize correctly.
- Result envelope mandatory fields (ST1-08-AC08): all present; `durationMs` uses `FakeTimeProvider`; `lastHeartbeatAt` nullable.

### Mock Orchestrator Integration Tests (`Tracto.Contract.Orchestrator.Tests`)

- Registration acceptance for Desktop/Browser/Terminal.
- Protocol incompatibility (older major) rejected.
- runtimeType/capability namespace mismatch rejected (C4).
- Capability digest integrity hint verified; not treated as authentication proof.
- Interactive session exclusivity: two headed workers same `interactiveSessionId` → one active lease.
- Headless worker coexistence while interactive busy.
- Outbound pull semantics: no job, one job, capacity enforcement.
- Heartbeat monotonic sequence; stale sequence rejected.
- Lease expiry → reconciliation flagged; no blind dispatch.
- Stale attempt fencing: old result does not overwrite newer authoritative state.
- Reconcile with journal: proven non-commit → safe requeue; uncertain → UNKNOWN.
- Cancellation delivery per heartbeat response; bounded cleanup acknowledgment.

### Security Tests (`Tracto.Contract.Validation.Tests` + `Tracto.Contract.Telemetry.Tests`)

- Plaintext secret in script field → rejected.
- `secretRef` opaque in all Orchestrator-facing serializations.
- Linux worker requesting `CurrentUser` DPAPI directly → capability/security validation fails (ST1-12-AC07).
- Telemetry events contain zero plaintext secrets, auth headers, cookies, or sensitive payloads.
- Evidence manifests contain only `evidenceRef`, classification and retention reference.
- Service response containing code/script IDs/endpoint URLs → rejected before binding.
- `curl`/`Invoke-WebRequest` to unregistered endpoint (terminal policy) → POLICY_DENIED.
- Browser navigation allowed; direct script-supplied HTTP integration denied.
- All three runtime adapters pass the same security fixture set independently (ST1-12-AC09).

### Telemetry Tests (`Tracto.Contract.Telemetry.Tests`)

- Mandatory event fields present in every worker event.
- `stepId`/`leaseId`/`errorCategory` in step/lease events.
- Cross-system correlation by `correlationId`/`businessJobId`.
- Out-of-order delivery deduplication; stale message cannot overwrite final status.
- Buffered export preserves correlation and redaction.
- UNKNOWN/FAILED/CANCELLED terminal → stable taxonomy + evidence pointers; no false success.

### Conformance Fixture Validation (`Tracto.Conformance.Fixtures`)

The fixture JSON files themselves are validated against contract schemas as part of CI. This ensures fixtures cannot drift from the schema they claim to test.

### Conformance Runner Integration (`Tracto.Conformance.Runner`)

- CF-01 through CF-10 scenarios (see traceability matrix for story mapping).
- Extension fixtures: `cf-ext-success-branch` (SUCCESS outcome in all/any/not), `cf-ext-continuation-negative` (no continuation from HANDED_OFF/FAILED/UNKNOWN/CANCELLED), `cf-ext-response-schema-mismatch`, `cf-ext-empty-vars-read`.
- Each Runtime team runs the conformance runner independently; results are per-runtime reports.
- Fixture seeding is deterministic; no wall-clock dependence (FakeTimeProvider).
- Conformance report: machine-readable JSON; fixture ID, runtime type, adapter version, contract version, pass/fail, AC ID, failure detail.

## Meaningful Negative and Boundary Scenarios

| Scenario | Story | Test Type |
|---|---|---|
| Invalid or undeclared variable reference | ST1-02 | U |
| Forward read of var (C1) | ST1-02 | U |
| Type mismatch on outputBinding to var (C1) | ST1-02 | U |
| Uninitialized var read with no defaultValue (C1) | ST1-02 | U |
| Unsupported action namespace in script | ST1-01, ST1-09 | U/C |
| Unsupported predicate type for runtime | ST1-04 | C |
| Malformed service response / schema mismatch | ST1-06 | C |
| SUCCESS serviceOutcome as valid branch (REF-02) | ST1-06, ST1-14 | C/M |
| False precondition until timeout → FAILED (not SKIPPED) | ST1-05, ST1-08 | U |
| False postcondition → FAILED or UNKNOWN if irrevocable | ST1-05, ST1-08 | U |
| Action timeout with confirmed non-commit → TIMED_OUT | ST1-08 | U |
| Action timeout with uncertain commit → UNKNOWN | ST1-08 | U |
| Stale attempt final result after fencing | ST1-10 | M |
| Lease expiry before execution → safe requeue | ST1-10 | M |
| Lease expiry during irreversible action → UNKNOWN | ST1-10, ST1-11 | M |
| Continuation deduplication (same sourceRunId + ruleId) | ST1-07 | M |
| No continuation from HANDED_OFF / FAILED / UNKNOWN / CANCELLED | ST1-07 | M |
| Duplicate callback eventId → zero new runs | ST1-07 | M |
| Secret in telemetry payload → redacted | ST1-12, ST1-13 | S |
| `authenticated:true` self-assertion in callback → rejected | ST1-07 | S |
| Domain expression in condition → rejected all runtimeTypes | ST1-04 | S |
| Mixed-engine steps in one script | ST1-01 | U |
| Prohibited direct business API in terminal command | ST1-12 | S |
| COMPLETED as run state → rejected | ST1-08 | U |
| maxIterations derived from business data → rejected | ST1-05 | U |
| onFailure triggering business compensation → rejected | ST1-02 | U |
| Script-supplied continuation script ref in callback → ignored | ST1-07 | S |

## Stage 1 Exit Criteria (Derived from TRACTO-ST1-US-001 §5)

Stage 1 is complete when:
1. All 15 stories are accepted (all ACs with automated evidence where applicable; human-review ACs explicitly recorded).
2. Canonical JSON schemas and examples are published in the NuGet package.
3. Run and step state transition tables are stable and tested.
4. Contract CI is green (build + all test projects + conformance fixture JSON schema validation).
5. Mock Orchestrator and fake business-service fixture packages are available.
6. Negative security tests pass via the reference adapter. Individual Runtime team security conformance is each Runtime track's own gate, not a Stage 1 prerequisite.
7. N/N-1 schema and protocol compatibility fixtures pass (for Stage 1: "older major" rejection confirmed; full two-major compatibility testing applies when a second major is actually published).
8. One reference .NET adapter proves the full shared interface round-trip against the mock.
9. Conformance runner and fixtures are available for Runtime teams to use independently. Per-Runtime certification is each Runtime team's own release gate.
10. Named ownership fields in ST1-15-AC05 are populated.
11. Traceability report links every AC to its test or human-review record.

**Not required for Stage 1 exit:** Windows UIA implementation, Playwright runtime, Terminal command engine, production Orchestrator, real vault, Windows session provisioning PoC completion, Designer or AI Recorder.
