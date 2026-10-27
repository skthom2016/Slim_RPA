# 13 — Sprint Plan

**Summary**: Six two-week sprints mapping WP-1 through WP-11 to outcome-focused sprint goals, with story allocation, parallel tracks, key deliverables, and exit criteria for each sprint.

**Sources**: `Tracto_Stage1_Common_Execution_Contract_User_Stories_v1_0_1.md`, `07-development-plan.md`, `08-traceability-matrix.md`

**Last updated**: 2026-10-22

---

Each sprint has a single outcome-focused goal. No sprint ends at an arbitrary code milestone. Parallel tracks within a sprint are explicitly noted. Fixture JSON authoring is a background track that runs from Sprint 1 onward (schemas exist from WP-1).

**Estimated duration**: 6 sprints for a team of 3–4 engineers. A single engineer on the critical path requires approximately 6–7 sprints.

---

## Sprint 1 — Foundation

**Goal**: The contract has a home — versioned JSON schemas, typed C# models, and a green CI pipeline are all committed. C1, C3, and C4 decisions are formally submitted to the Common Contract owner for approval.

**Work packages**: WP-1, WP-2

**Stories**: ST1-01 (schema), ST1-02 (schema), ST1-15 (partial)

**Pre-conditions before Sprint 1 starts**:
- Repository location confirmed; internal NuGet feed configured
- CI pipeline target confirmed (GitHub Actions or Azure DevOps)
- Q1 (.NET 10 LTS), Q2 (first schema major = v1, no placeholder previous major) resolved
- Library compatibility verified: `JsonSchema.Net`, OpenTelemetry .NET SDK, `System.Security.Cryptography.Pkcs` on .NET 10

### Key Deliverables

- JSON Schema files for all 10 contract artifacts under `schemas/v1/`
- `schema-version-catalogue.json` — declares v1 as the current major; no previous-major entry
- `Tracto.Contract.Models` NuGet — all DTOs, enums, and source-generated serializers
- `Tracto.Contract.Testing` — `FakeTimeProvider`, fluent builders, fixture loader
- `Directory.Build.props` — `net10.0`, nullable enable, warnings as errors
- CI pipeline skeleton — build, schema validation, and empty test run all green
- C1 / C3 / C4 recommended decisions formally submitted for contract-owner approval

### Sprint Exit Criteria

- [ ] All JSON Schema files validate using `JsonSchema.Net` on `net10.0`
- [ ] Model round-trip tests pass: every DTO serialises and deserialises with no data loss
- [ ] CI passes on an empty solution — build, test, and schema-lint steps all green
- [ ] C1 / C3 / C4 decision log submitted; approval gate status recorded in [[10-decision-log]]
- [ ] Library compatibility confirmed for all three provisional packages

---

## Sprint 2 — Contract Perimeter

**Goal**: No unverified artifact can reach execution. The envelope validator, trust verifier, step validator, and variable resolver together form a complete security perimeter around every automation envelope.

**Work packages**: WP-3

**Stories**: ST1-01, ST1-02, ST1-03

**Prerequisite**: Sprint 1 complete; WP-2 models stable.

### Key Deliverables

- `Tracto.Contract.Validation` — `EnvelopeValidator`, `StepValidator`, `RefResolver`, `PolicyEnforcer`
- `IArtifactTrustVerifier` reference implementation — SHA-256 digest + CMS signer verification
- C1 variable rules enforced in `RefResolver`: uninitialized-read, forward-read, type-mismatch at validation time
- Runtime-check for branch-conditional uninitialized reads (C1 branch case)
- Closed `automationId` format validation; mixed-runtime rejection; output classification check
- Full negative test suite: one test per required field removed, per wrong type, per invalid form

### Sprint Exit Criteria

- [ ] ST1-01-AC01 through AC08 all pass
- [ ] ST1-02-AC01 through AC09 all pass — all C1 variable rules verified
- [ ] ST1-03-AC01 through AC07 all pass — target map validation and trust boundary
- [ ] All negative schema tests green: tampered artifact rejected, unknown policyRef denied, plaintext secret in field rejected
- [ ] N/N-1 baseline: artifact with unsupported schema major rejected with republish diagnostic (ST1-01-AC05)

---

## Sprint 3 — Execution Semantics

**Goal**: All conditional execution paths, control flow guards, and run/step outcome taxonomy are contractually defined and unit-tested. Every legal state transition is verified; every illegal one is rejected without mutating state.

**Work packages**: WP-4a, WP-4b, WP-5 (three parallel tracks)

**Stories**: ST1-04, ST1-05, ST1-08

**Prerequisite**: Sprint 2 complete; C3 approved before WP-5 work starts.

**Parallel tracks**: WP-4a (condition grammar), WP-4b (control flow), and WP-5 (state machine) can be staffed concurrently after Sprint 2. WP-4a and WP-4b have no dependency on each other. WP-5 requires C3 decision approval; implementation must not begin before that approval is received.

### Key Deliverables

- `ConditionGrammarParser` — allowlist-based; domain arithmetic, C#/JS, recursive expressions rejected (WP-4a)
- `ControlFlowValidator` — forward-only branch graph; literal `maxIterations` only; backward refs rejected (WP-4b)
- `RunStateMachine` + `StepOutcomeNormalizer` — full C3 resolution: FAILED / TIMED_OUT / UNKNOWN distinctions (WP-5)
- `ResultEnvelopeBuilder` — all ST1-08-AC08 mandatory fields; `durationMs` via `FakeTimeProvider`
- `needsAttention=true` on every UNKNOWN result; `unknownReason` populated with stable code strings
- SKIPPED only from unselected branch arm; false precondition → FAILED, never SKIPPED

### Sprint Exit Criteria

- [ ] ST1-04-AC01 through AC07 pass (AC06 has a parallel human publishing-review record)
- [ ] ST1-05-AC01 through AC09 pass — all branch and iteration edge cases covered
- [ ] ST1-08-AC01 through AC08 pass — all legal transitions verified; all illegal transitions rejected; C3 UNKNOWN/TIMED_OUT distinction confirmed
- [ ] No test uses wall-clock time — `FakeTimeProvider` controls all timeouts
- [ ] COMPLETED rejected as a run state; domain expression in condition rejected for all runtimeTypes
- [ ] maxIterations cap confirmed by contract-owner before WP-4b implementation (Q9)

---

## Sprint 4 — Integration Contracts

**Goal**: The service integration boundary and the security and telemetry perimeters are complete. Service calls, async handoffs, secret redaction, and telemetry evidence can all be contract-tested end to end through the mock business service.

**Work packages**: WP-6, WP-7

**Stories**: ST1-06, ST1-07, ST1-12, ST1-13

**Prerequisite**: WP-4a and WP-5 both complete before WP-6 can start. Q6 output classification vocabulary approved before WP-7 telemetry work begins.

### Key Deliverables

- `ServiceInvokeValidator` — registered serviceRef only; raw URL, SQL, unapproved endpoints denied (WP-6)
- `Tracto.Mock.BusinessService` — all 5 serviceOutcomes; async callback with configurable delay and idempotency key (WP-6)
- Async handoff and static-continuation schema; callback signature validation; continuation-from-FAILED/UNKNOWN/HANDED_OFF/CANCELLED rejected (WP-6)
- `SecretRedactor`, `EvidenceManifestBuilder`, `ITelemetryEmitter` — zero plaintext secrets in any serialised output (WP-7)
- Security fixture matrix: DPAPI CurrentUser on Linux → capability validation fails; service response containing code → rejected (WP-7)
- Q6 output classification vocabulary confirmed and wired into telemetry redaction rules (WP-7)

### Sprint Exit Criteria

- [ ] ST1-06-AC01 through AC09 pass — all 5 serviceOutcome values tested; SUCCESS branch confirmed (REF-02)
- [ ] ST1-07-AC01 through AC08 pass — continuation deduplication, callback nonce validation, no continuation from terminal states
- [ ] ST1-12-AC01 through AC09 pass — all security fixture matrix cases verified through reference adapter
- [ ] ST1-13-AC01 through AC08 pass — no secret, auth header, or sensitive payload in any telemetry event
- [ ] Q6 output classification vocabulary explicitly approved and documented

---

## Sprint 5 — Worker Protocol

**Goal**: A worker can register, pull jobs, hold a lease, heartbeat, and be safely reconciled after expiry — the complete worker lifecycle is mock-verified. Conformance fixture JSON files are authored in parallel and validated against the contract schemas.

**Work packages**: WP-8, WP-9 (fixtures track)

**Stories**: ST1-09, ST1-10, ST1-11, ST1-14 (fixtures)

**Prerequisite**: WP-5 and WP-7 both complete; C4 approved before WP-8 starts.

Note: Fixture JSON authoring for WP-9 may begin as early as Sprint 1 (schemas exist). The fixture authoring track closes in this sprint; the conformance runner is built in Sprint 6.

### Key Deliverables

- `Tracto.Contract.Orchestrator` — `IOrchestratorClient`, `IOrchestratorServer`, `CapabilityDigestCalculator`, `InteractiveLeaseEnforcer` (WP-8)
- `Tracto.Mock.Orchestrator` — in-process stub with fault injection, lease expiry simulation, reconcile logic, stale-attempt fencing (WP-8)
- C4 capability advertisement: runtimeType / namespace mismatch rejected; capability digest is integrity hint only, not authentication proof (WP-8)
- Interactive session exclusivity: two workers with the same `interactiveSessionId` → one active lease only (WP-8)
- Conformance fixture JSON files CF-01 through CF-10 authored and validated against schemas (WP-9 fixture track)
- Extension fixtures: `cf-ext-success-branch`, `cf-ext-continuation-negative`, `cf-ext-response-schema-mismatch`, `cf-ext-empty-vars-read`

### Sprint Exit Criteria

- [ ] ST1-09-AC01 through AC08 pass — registration, capability validation, C4 digest, session exclusivity
- [ ] ST1-10-AC01 through AC08 pass — pull semantics, lease acknowledge, heartbeat monotonic sequence, stale result fencing
- [ ] ST1-11-AC01 through AC08 pass — lease expiry → reconcile → proven non-commit = safe requeue; uncertain = UNKNOWN
- [ ] All CF-01 through CF-10 + extension fixture JSON files validate against contract schemas in CI

---

## Sprint 6 — Conformance and Release

**Goal**: Stage 1 is done. The reference adapter satisfies every conformance fixture end to end, packages are versioned and released to the NuGet feed, and Runtime teams have everything they need to begin their own independent certification.

**Work packages**: WP-9 (runner), WP-10, WP-11

**Stories**: ST1-14, ST1-15

**Prerequisite**: All WP-1 through WP-8 contracts complete; fixture JSON complete (Sprint 5).

### Key Deliverables

- `Tracto.Conformance.Runner` — CI-executable; per-fixture pass/fail JSON report; `FakeTimeProvider`-controlled; deterministic (WP-9)
- `Tracto.Reference.Adapter` — minimal .NET adapter proving the full contract round-trip: registration → pull → execute → report (WP-10)
- Reference adapter conformance report: all CF-01 through CF-10 + extensions pass; report includes contract version, adapter version, AC IDs (WP-10)
- Versioned NuGet packages: all `Tracto.Contract.*` and `Tracto.Conformance.*` packages tagged and published (WP-11)
- Breaking-change detection CI step; `conformance-manifest.json` with all named ownership fields populated (WP-11)
- Traceability report: every AC in TRACTO-ST1-US-001 linked to its test or explicit human-review record (WP-11)

### Sprint Exit Criteria — Stage 1 Complete When All Pass

- [ ] ST1-14-AC01 through AC09 pass — conformance runner is deterministic and repeatable
- [ ] ST1-15-AC01 through AC08 pass — governance, ownership fields, changelog, compatibility matrix all present
- [ ] Reference adapter passes every CF-01 through CF-10 + extension; reference adapter conformance report exists
- [ ] All 15 stories accepted; all 124 ACs have automated evidence or an explicit human-review record
- [ ] Contract CI is fully green: build + all test projects + schema lint + breaking-change detection
- [ ] Named ownership fields (Lead Architect, host-supervisor owner, fixture maintainer) populated in `conformance-manifest.json`
- [ ] Conformance runner and fixtures published and available for Runtime teams to run independently

**Not required for Stage 1 exit**: Desktop UIA, Playwright runtime, Terminal command engine, production Orchestrator, real vault, Windows session provisioning, Designer, AI Recorder, or per-Runtime team conformance reports. Each Runtime team's certification is that team's own release gate.

---

## Contract-Owner Approval Gates

The following approvals must be obtained before the named sprint work begins. Implementation must not start before the gate is cleared.

| Approval | Required before | Sprint |
|---|---|---|
| C1 (variable declarations) — recommended decision | ST1-01, ST1-02 implementation (WP-3) | Sprint 2 |
| C3 (FAILED / TIMED_OUT / UNKNOWN) — recommended decision | ST1-08 implementation (WP-5) | Sprint 3 |
| Q9: `maxIterations` default cap confirmed | WP-4b implementation | Sprint 3 |
| Q6: Output classification vocabulary approved | WP-7 telemetry implementation | Sprint 4 |
| C4 (capability advertisement) — recommended decision | ST1-09 implementation (WP-8) | Sprint 5 |

---

## Related pages

- [[07-development-plan]]
- [[08-traceability-matrix]]
- [[09-testing-strategy]]
- [[10-decision-log]]
- [[12-exit-criteria]]
