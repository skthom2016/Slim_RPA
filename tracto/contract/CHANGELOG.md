# Changelog

## [1.0.1] — 2026-10-23

### Stage 1 Complete

- **C1 APPROVED**: vars map at envelope top-level; script-scoped typed variables via outputBinding
- **C3 APPROVED**: FAILED/TIMED_OUT/UNKNOWN state taxonomy with 5 stable unknownReason codes; attempt fencing
- **C4 APPROVED**: Namespace-pattern capability advertisement; capabilityDigest = SHA-256 integrity hint only (not auth proof, ST1-09-AC03); interactiveSessionId = session identity; interactive session exclusivity
- **Q9 APPROVED**: DefaultMaxIterations = 100 normative for control.repeatBounded

### Deliverables

- JSON Schema files for 10 contract artifacts under schemas/v1/
- Tracto.Contract.Models — all DTOs, enums, protocol messages
- Tracto.Contract.Validation — envelope, policy, schema, worker registration, dispatch eligibility, pull-lease, service validation
- Tracto.Contract.Execution — state machines, variable space, retry policy, step lifecycle, evidence recorder
- Tracto.Contract.Orchestrator — IOrchestratorServer, IOrchestratorClient, InteractiveLeaseEnforcer
- Tracto.Mock.Orchestrator — in-process mock with lease, heartbeat, cancellation, reconcile, attempt fencing
- Tracto.Mock.BusinessService — all 5 ServiceOutcome values
- Tracto.Conformance.Fixtures — CF-01..CF-10 + 4 extension fixtures
- Tracto.Conformance.Runner — ConformanceRunner, ConformanceReport, all CF scenarios
- Tracto.Reference.Adapter — minimal round-trip adapter proving full contract lifecycle

### Test Coverage

165+ tests passing across execution, validation, and orchestrator projects.
All ST1-01 through ST1-15 acceptance criteria have automated evidence or explicit human-review records.
