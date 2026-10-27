# 04 — Implementation Architecture and Module Responsibilities

## Architecture Principle

Stage 1 is implemented **within** the frozen v1.2A architecture. The structure below maps directly to that architecture without adding components, crossing responsibility boundaries, or creating shared state between independently deployed workers.

## Package Map

All packages are .NET 10 NuGet packages produced from a single repository. Dependency direction is strict: lower packages know nothing of higher packages.

```
Tracto.Contract.Schemas          ← JSON Schema files + schema-version catalogue
       ↑
Tracto.Contract.Models           ← C# DTOs, enums, typed models (no logic)
       ↑
Tracto.Contract.Validation       ← Validators, parsers, trust-verification interfaces
       ↑
Tracto.Contract.Execution        ← State machine, step engine contracts, ref resolver
       ↑
Tracto.Contract.Orchestrator     ← Pull/lease/heartbeat/cancel/reconcile message contracts
       ↑
Tracto.Contract.Telemetry        ← Telemetry event schema, redaction contracts
       ↑
Tracto.Contract.Testing          ← Test helpers, fake builders, controllable clock
       ↑
Tracto.Mock.Orchestrator         ← Mock Orchestrator (test infrastructure)
Tracto.Mock.BusinessService      ← Fake custom-service fixture (test infrastructure)
       ↑
Tracto.Conformance.Fixtures      ← CF-01–CF-10 + extensions (conformance tool)
Tracto.Conformance.Runner        ← CI fixture runner, per-runtime report (conformance tool)
       ↑
Tracto.Reference.Adapter         ← Reference .NET adapter proving the shared interface
```

## Module Responsibilities

### Tracto.Contract.Schemas (Production deliverable)

**Responsibility:** Canonical JSON Schema files for every contract artifact. These are the authoritative schemas all teams reference.

**Contents:**
- `automation-envelope.schema.json` (v1.x — the current and only major at Stage 1)
- `step-envelope.schema.json`
- `target-map.schema.json`
- `technical-condition.schema.json`
- `service-invoke.schema.json`
- `async-callback.schema.json`
- `run-result.schema.json`
- `worker-registration.schema.json`
- `pull-lease-protocol.schema.json`
- `telemetry-event.schema.json`
- `schema-version-catalogue.json` (declares the current major; a previous-major entry is added only when a second major is actually published)

**Public interfaces:** JSON files consumed by validators and test harnesses. Also exposes a .NET `EmbeddedSchemas` resource accessor.

**Dependencies:** None.

**Owning role:** Lead Architect / Common Contract owner.

**Stories covered:** ST1-01, ST1-02, ST1-03, ST1-04, ST1-05, ST1-06, ST1-07, ST1-08, ST1-09, ST1-10, ST1-11, ST1-12, ST1-13, ST1-15.

**Delivery classification:** Production deliverable.

---

### Tracto.Contract.Models (Production deliverable)

**Responsibility:** C# record types, enums, and typed DTO definitions corresponding to every contract schema. No logic; pure data types.

**Contents:**
- Envelope types: `AutomationEnvelope`, `StepEnvelope`, `TargetMap`, `VarDeclaration` (C1 resolution)
- Step types: `TechnicalCondition`, `ControlFlow`, `ServiceInvoke`, `ValueCompose`, `OutputBinding`
- Enums: `RunState`, `StepState`, `ErrorCategory`, `ServiceOutcome`, `BusinessOutcome`, `RuntimeType`, `RetryClass`, `HostClass`
- Worker protocol types: `WorkerRegistration`, `AdvertisedCapabilities`, `PullJobRequest`, `LeaseGranted`, `LeaseAccepted`, `Heartbeat`, `FinalResult`, `CancelRequest`, `ReconcileRequest`
- Telemetry types: `TelemetryEvent` and subtypes
- Callback types: `BusinessCallback`

**Public interfaces:** All types are `public record` or `public enum`; serialization via `System.Text.Json` with source generation.

**Dependencies:** None (pure models).

**Owning role:** Common Contract owner.

**Stories covered:** All 15 stories use these types.

**Delivery classification:** Production deliverable.

---

### Tracto.Contract.Validation (Production deliverable)

**Responsibility:** All schema validation, trust verification interfaces, artifact digest checking, condition grammar parsing, and policy enforcement logic.

**Contents:**
- `EnvelopeValidator` — validates automation envelopes against JSON Schema; checks `runtimeType`, `policyRefs`, `automationId` format, output classifications
- `ArtifactTrustVerifier` — interface `IArtifactTrustVerifier`; reference implementation using `System.Security.Cryptography.Pkcs` for signer verification and SHA-256 digest
- `StepValidator` — validates step envelopes; resolves typed references; enforces C1 variable rules
- `RefResolver` — resolves `{"ref":"inputs.x"}`, `{"ref":"vars.y"}`, `{"ref":"steps.s1.outputs.z"}`, `{"ref":"secrets.k"}`; enforces forward-reference prohibition
- `ConditionGrammarParser` — allowlist-based parser for technical conditions; rejects domain expressions
- `ControlFlowValidator` — validates `control.branch` and `control.repeatBounded`; enforces forward-only graph, literal `maxIterations`
- `ServiceInvokeValidator` — validates `service.invoke` against registry; enforces `serviceRef`/`operationRef` allowlist
- `PolicyEnforcer` — checks `policyRefs` against known policy registry

**Public interfaces:** `IEnvelopeValidator`, `IArtifactTrustVerifier`, `IConditionParser`, `IRefResolver`, `IServiceRegistry`

**Error behavior:** All validation failures return a structured `ValidationResult` containing `errorCategory`, field path and stable detail code. No exceptions for contract-level validation failures; exceptions only for internal defects.

**Dependencies:** `Tracto.Contract.Schemas`, `Tracto.Contract.Models`

**Owning role:** Common Contract owner.

**Stories covered:** ST1-01, ST1-02, ST1-03, ST1-04, ST1-05, ST1-06, ST1-07, ST1-12.

**Delivery classification:** Production deliverable.

---

### Tracto.Contract.Execution (Production deliverable)

**Responsibility:** State machine definitions, step-lifecycle contracts, result normalization, and the execution interface that Runtime implementations must satisfy.

**Contents:**
- `RunStateMachine` — validates legal transitions (C3 resolution); table from Contract §7
- `StepOutcomeNormalizer` — maps raw step completion data to canonical `StepState` and `errorCategory`
- `IStepExecutor` interface — the contract each Runtime action must satisfy (input, output, timeout, cancellation token)
- `AttemptJournal` — interface for recording attempt checkpoints used by reconciliation (ST1-10, ST1-11)
- `ResultEnvelopeBuilder` — assembles the final `RunResult` including all mandatory fields from ST1-08-AC08
- `UnknownReasonCodes` — static class of stable `unknownReason` string constants

**Public interfaces:** `IStepExecutor`, `IAttemptJournal`, `RunStateMachine`, `ResultEnvelopeBuilder`

**Error behavior:** Illegal transition returns `TransitionResult.Rejected`; no exception. Unknown/uncertain outcomes set `needsAttention=true` and `unknownReason` per C3.

**Dependencies:** `Tracto.Contract.Models`, `Tracto.Contract.Validation`

**Owning role:** Common Contract owner (shared implementation); Runtime teams implement `IStepExecutor`

**Stories covered:** ST1-05, ST1-08, ST1-10, ST1-11.

**Delivery classification:** Production deliverable.

---

### Tracto.Contract.Orchestrator (Production deliverable)

**Responsibility:** Worker-to-Orchestrator protocol message contracts and client/server message exchange interfaces.

**Contents:**
- `IOrchestratorClient` — `Register`, `PullJob`, `AcknowledgeLease`, `SendHeartbeat`, `ReportStatus`, `ReportFinalResult`, `Reconcile`
- `IOrchestratorServer` — the interface the mock Orchestrator implements; mirrors the above
- `CapabilityDigestCalculator` — computes and verifies the C4 capability digest
- `CompatibilityMatrix` — evaluates N/N-1 protocol compatibility
- `InteractiveLeaseEnforcer` — per-session foreground-input lease logic

**Public interfaces:** `IOrchestratorClient`, `IOrchestratorServer`

**Dependencies:** `Tracto.Contract.Models`, `Tracto.Contract.Validation`

**Owning role:** Common Contract owner; Orchestrator team implements production server

**Stories covered:** ST1-09, ST1-10, ST1-11.

**Delivery classification:** Production deliverable (interfaces); mock in `Tracto.Mock.Orchestrator`

---

### Tracto.Contract.Telemetry (Production deliverable)

**Responsibility:** Telemetry event schema, field rules, redaction contracts and evidence reference conventions.

**Contents:**
- `TelemetryEmitter` — interface `ITelemetryEmitter`; reference implementation using `Microsoft.Extensions.Logging`
- `SecretRedactor` — strips plaintext from telemetry payloads; replaces with `[REDACTED:<classification>]`
- `EvidenceManifestBuilder` — assembles `evidenceRef` fields with run/attempt/step attribution, sensitivity classification
- `EventSequenceValidator` — deduplication logic for out-of-order event delivery (ST1-13-AC06)

**Public interfaces:** `ITelemetryEmitter`, `IEvidenceManifestBuilder`

**Error behavior:** Telemetry failures are logged locally and do not cause the run to fail.

**Dependencies:** `Tracto.Contract.Models`

**Owning role:** Common Contract owner.

**Stories covered:** ST1-13.

**Delivery classification:** Production deliverable.

---

### Tracto.Contract.Testing (Test infrastructure)

**Responsibility:** Shared test helpers, fake builders, controllable `TimeProvider`, and deterministic fixture utilities.

**Contents:**
- `FakeTimeProvider` — implements `System.TimeProvider` with manual advance
- `ArtifactBuilder` / `EnvelopeBuilder` — fluent builders for test artifacts
- `FixtureValidator` — asserts that a given result matches an expected contract state
- `SchemaFixtureLoader` — loads JSON fixture files from embedded resources

**Public interfaces:** All public for test projects; not shipped in production packages.

**Dependencies:** `Tracto.Contract.Models`, `Tracto.Contract.Validation`

**Owning role:** Common Contract owner.

**Stories covered:** ST1-14 (used by all conformance tests).

**Delivery classification:** Test infrastructure.

---

### Tracto.Mock.Orchestrator (Conformance tool — test double)

**Responsibility:** A lightweight .NET in-process stub implementing `IOrchestratorServer`. Not a fourth product.

**Contents:**
- In-memory run/attempt/lease state store
- Message handlers: register, pull, lease, heartbeat, status, cancel, reconcile
- Lease expiry simulator (injectable via `FakeTimeProvider`)
- Fault-injection control (latency, duplicate delivery, restart simulation)
- Conformance report generator

**Constraints:**
- Must not persist to disk in production use
- Must not implement production scheduling, persistence or cluster-level dispatch
- Must be clearly marked as test infrastructure in all packaging

**Dependencies:** `Tracto.Contract.Orchestrator`, `Tracto.Contract.Testing`

**Owning role:** Conformance-fixture maintainer.

**Stories covered:** ST1-09, ST1-10, ST1-11, ST1-14.

**Delivery classification:** Conformance tool (test double).

---

### Tracto.Mock.BusinessService (Conformance tool — test double)

**Responsibility:** A fake custom-service endpoint supporting all five `serviceOutcome` values, optional response-schema mismatch and deterministic callback events.

**Contents:**
- Five outcome handlers: `SUCCESS`, `BUSINESS_REJECTION`, `TECHNICAL_FAILURE`, `TRANSIENT_FAILURE`, `UNKNOWN_OUTCOME`
- Response-schema-mismatch fixture
- Async callback event generator (with configurable delay, signature, nonce, timestamp)
- Idempotency key recorder

**Constraints:** Customer-owned adapters in production; this is Stage 1 test infrastructure only.

**Dependencies:** `Tracto.Contract.Models`

**Owning role:** Conformance-fixture maintainer.

**Stories covered:** ST1-06, ST1-07, ST1-14.

**Delivery classification:** Conformance tool (test double).

---

### Tracto.Conformance.Fixtures (Conformance tool)

**Responsibility:** JSON fixture files for CF-01 through CF-10 plus all extended scenarios. Machine-readable inputs for the conformance runner.

**Contents:**
- `cf-01-happy-path.json` through `cf-10-domain-expression.json`
- Extension fixtures: `cf-ext-success-branch.json`, `cf-ext-continuation-from-failed.json`, `cf-ext-response-schema-mismatch.json`, `cf-ext-empty-vars-read.json`, etc.
- Fixture manifest (`conformance-manifest.json`) with contract version, fixture version and story IDs

**Dependencies:** Schema files from `Tracto.Contract.Schemas`.

**Owning role:** Conformance-fixture maintainer.

**Stories covered:** ST1-14 (all fixtures), ST1-15 (fixture manifest).

**Delivery classification:** Conformance tool.

---

### Tracto.Conformance.Runner (Conformance tool)

**Responsibility:** CI-executable runner that applies conformance fixtures against a Runtime adapter and produces a machine-readable per-runtime conformance report.

**Contents:**
- `ConformanceTestHost` — sets up mock Orchestrator, mock business service and the Runtime adapter under test
- `FixtureExecutor` — drives each CF scenario and asserts expected outcomes
- `ConformanceReporter` — emits JSON report: fixture ID, runtime type, adapter version, contract version, pass/fail, detail

**Constraints:** Each Runtime team runs this independently; no cross-runtime dependency.

**Dependencies:** All `Tracto.Mock.*`, `Tracto.Conformance.Fixtures`, `Tracto.Contract.*`

**Owning role:** QA automation engineer.

**Stories covered:** ST1-14, ST1-15.

**Delivery classification:** Conformance tool.

---

### Tracto.Reference.Adapter (Reference implementation)

**Responsibility:** A minimal .NET adapter that implements `IOrchestratorClient` and `IStepExecutor`, demonstrating that the shared interface can be parsed, registered and reported against the mock. Does not implement real Desktop/Browser/Terminal actions.

**Constraints:**
- Must not quietly become the production implementation of a later-stage Runtime.
- Must be clearly labelled as a reference implementation in code, packaging and documentation.
- Action execution is simulated: all actions return configurable outcomes without touching real UI/CLI.

**Public interfaces:** Implements `IOrchestratorClient` and `IStepExecutor` from `Tracto.Contract.Execution` and `Tracto.Contract.Orchestrator`.

**Dependencies:** All `Tracto.Contract.*`

**Owning role:** Common Contract owner / Lead Architect.

**Stories covered:** ST1-01 through ST1-13 (proves the full contract can be satisfied).

**Delivery classification:** Reference implementation.

## Shared Behavior vs. Shared Library

The architecture mandates **shared contract behavior**; it does not mandate that all Runtimes link the same shared library. Each independently delivered Runtime must produce identical behavior for all common contract requirements, but may use its own internal .NET libraries to do so (ST1-15-AC08). The shared NuGet packages provide schemas, DTOs, interfaces and reference implementations. A Runtime team that reimplements a validator from the schema specification rather than linking the shared validator is conformant, provided it passes the same conformance fixtures.

## Shared vs. Runtime-Specific Code

| Code | Shared | Runtime-Specific |
|---|---|---|
| JSON Schema files | ✓ | ✗ |
| DTO / enum models | ✓ | ✗ |
| Envelope validator, ref resolver, condition parser | ✓ | ✗ |
| `IStepExecutor` interface | ✓ | Implementation per runtime |
| `IOrchestratorClient` interface + message shapes | ✓ | Implementation per runtime (transport) |
| Run/step state machine | ✓ | ✗ |
| Telemetry schema + redaction | ✓ | ✗ |
| UIA element interaction | ✗ | Desktop only |
| Playwright locators / navigation | ✗ | Browser only |
| CMD/PS/Bash execution | ✗ | Terminal only |
| DPAPI secret resolution | ✗ | Desktop/Windows Runtime |
| Interactive session provisioning | ✗ | Desktop/headed Browser |

## Independent Implementability

Each Runtime team receives:
1. The versioned NuGet packages from `Tracto.Contract.*`
2. The `Tracto.Mock.Orchestrator` and `Tracto.Mock.BusinessService` packages
3. The `Tracto.Conformance.Runner` CI tool
4. The `Tracto.Conformance.Fixtures` JSON fixture pack

They do **not** depend on any other Runtime's code. CI for each Runtime team produces an independent conformance report. Browser/Terminal releases do not require Desktop to be built or tested first.

## Prohibited Dependencies

The following dependency directions are prohibited:
- Any shared contract package importing Runtime-specific code
- `Tracto.Mock.Orchestrator` being deployed as a production Orchestrator
- `Tracto.Reference.Adapter` shipping as production Desktop/Browser/Terminal code
- Runtime packages depending on each other's packages
- Orchestrator or shared packages depending on Windows-only APIs (DPAPI, UIA)
