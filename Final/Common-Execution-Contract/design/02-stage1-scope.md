# 02 — Stage 1 Scope and Exclusions

## Stage 1 Objective (from TRACTO-ST1-US-001 §1)

Stage 1 establishes the **contract that independently delivered .NET Desktop, Browser and Terminal Runtime applications must implement**. It produces contract schemas, a reference validator, normalized messages and results, a mock Orchestrator, and executable conformance fixtures. It does **not** implement a production Orchestrator, a complete Runtime engine, a Designer, an application-specific selector library, or business-service logic.

## Component Classification

Every proposed component must be one of:
1. **Stage 1 production deliverable** — ships as a versioned contract artifact used by all three Runtime teams
2. **Stage 1 reference implementation** — a .NET reference adapter proving the contract is implementable; clearly distinct from production Runtime code
3. **Stage 1 test double or conformance tool** — mock, fake or fixture infrastructure; explicitly not a product component
4. **Deferred to a subsequent track** — identified and excluded from Stage 1

| Component | Classification | Rationale |
|---|---|---|
| Automation envelope schema (JSON Schema) | Production deliverable | Core contract; CEC-001, ST1-01 |
| Step envelope schema and typed-reference model | Production deliverable | Core contract; ST1-02 |
| Target-map schema and reference validator | Production deliverable | Core contract; ST1-03 |
| Technical-condition grammar and allowlist parser | Production deliverable | Core contract; ST1-04 |
| Control-flow schema (branch, repeatBounded) | Production deliverable | Core contract; ST1-05 |
| `service.invoke` schema and service registry mock | Production deliverable | Core contract; ST1-06 |
| Async handoff and callback schemas | Production deliverable | Core contract; ST1-07 |
| Run/step state machine enums and transition table | Production deliverable | Core contract; ST1-08 |
| Error category taxonomy and serialization | Production deliverable | Core contract; ST1-08 |
| Worker registration and capability message schemas | Production deliverable | Core contract; ST1-09 |
| Pull/lease/heartbeat/result/reconcile message schemas | Production deliverable | Core contract; ST1-10 |
| Cancel request and reconcile message schemas | Production deliverable | Core contract; ST1-11 |
| `secretRef` semantics, redaction rules | Production deliverable | Core contract; ST1-12 |
| Telemetry event schema and field definitions | Production deliverable | Core contract; ST1-13 |
| Versioned contract package (NuGet) | Production deliverable | ST1-15 |
| Reference .NET validator (schema + trust check) | Reference implementation | Proves contract implementability; ST1-01, ST1-04 |
| Reference .NET type-checker and ref resolver | Reference implementation | Proves ST1-02, ST1-03 implementability |
| Reference condition grammar parser | Reference implementation | Proves ST1-04 implementability |
| Reference state-machine enforcer | Reference implementation | Proves ST1-08 implementability |
| Mock Orchestrator (register, pull, lease, heartbeat, cancel, reconcile) | Conformance tool (test double) | ST1-14; must not become fourth product |
| Fake custom-service fixture (5 outcomes + schema mismatch) | Conformance tool | ST1-14; customer-owned in production |
| Conformance fixture pack (CF-01–CF-10 + extensions) | Conformance tool | ST1-14 |
| Security policy test matrix and fixtures | Conformance tool | ST1-12, ST1-14 |
| Telemetry deduplication and export fixtures | Conformance tool | ST1-13 |
| Windows UIA Desktop action library | Deferred — Desktop Runtime track | Not a Stage 1 dependency |
| Playwright Browser action library | Deferred — Browser Runtime track | Not a Stage 1 dependency |
| Terminal CMD/PS/Bash action library | Deferred — Terminal Runtime track | Not a Stage 1 dependency |
| Production Orchestrator (scheduling store, dispatch engine) | Deferred — Orchestrator track | Mock covers Stage 1 needs |
| Designer and AI Recorder | Deferred — Designer track | Not needed for contract conformance |
| Windows DPAPI secret provider (production) | Deferred — Runtime track | Mock interface covers Stage 1; PoC-gated anyway |
| Windows interactive session creation/provisioning | Deferred — PoC-gated | POC-04, POC-10 |
| Connected-mode vault integration | Deferred | POC-10 |
| Production telemetry export (OpenTelemetry collector) | Deferred | Contract defines fields; implementation is Runtime/Orchestrator track |

## Explicitly Excluded from Stage 1

- Real Windows UIA, Playwright DOM or shell-command execution
- Production Windows interactive session provisioning
- Production Orchestrator scheduling, persistence or cluster scaling
- Designer publishing UI or AI authoring integration
- Business-service container deployment or secrets management
- Windows DPAPI production integration (interface is specified; implementation deferred)
- Connected-mode enterprise vault integration
- Commercial monitoring/observability stack
- Any fourth Tracto product component

## Functional Story Boundaries

The 15 user stories (ST1-01 through ST1-15) in the backlog remain as authored. No micro-story splits or mega-story merges are introduced here. Implementation subtasks may be created beneath stories but do not replace them as acceptance units.
