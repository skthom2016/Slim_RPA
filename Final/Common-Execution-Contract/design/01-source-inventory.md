# 01 — Source Document Inventory and Constraint Summary

## Documents Found

| # | Exact Filename | Internal Version | Document ID | Date | Status |
|---|---|---|---|---|---|
| 1 | `Tracto_Architecture_v1_2A_Independent_Runtime_Amendment.docx` | v1.2A | TRACTO-ARCH-001 | 21 Oct 2026 | CONDITIONAL FREEZE |
| 2 | `Tracto_Common_Execution_Orchestrator_Contract_v1_0_1.docx` | v1.0.1 | (unnamed contract) | 22 Oct 2026 | Functional baseline |
| 3 | `Tracto_Stage1_Common_Execution_Contract_User_Stories_v1_0_1.md` | v1.0.1 | TRACTO-ST1-US-001 | 22 Oct 2026 | Functional stories ready for refinement |

## Missing or Unreadable Content

All three documents were fully readable. The .docx decision register (AD-01–AD-14) and PoC table (POC-01–POC-10) extracted correctly row by row. CEC-HOST shared requirements (§11.1) and conformance fixture pack (§12) in the contract were fully extracted. No missing appendices.

## Scope and Authority of Each Document

**Architecture v1.2A** supersedes Tracto v1.1. It owns structural decisions: three-component boundary, .NET platform, deployment topology, identity model, PoC gates. CONDITIONAL FREEZE — product boundary and functional scope accepted; unattended session provisioning pending POC-01–POC-10. No architectural change authorized without explicit approval.

**Contract v1.0.1** defines the smallest shared rules for all three Runtime applications, the Designer and the Orchestrator. Governs schemas, condition grammar, run/step state machine, error taxonomy, worker protocol, security and telemetry. Does not define UIA selectors, Playwright locators, shell commands or shared implementation libraries. Functional baseline accepted as development baseline.

**User Stories v1.0.1 (TRACTO-ST1-US-001)** defines 15 user stories covering the shared contract and supporting infrastructure. Defines build order, stable acceptance-criterion IDs, story dependencies and exit gate. Explicitly excludes production Orchestrator, complete Runtime engines, Designer and AI Recorder from Stage 1.

Where the three documents disagree, resolution is: Architecture governs structural decisions; Contract governs shared execution behavior; User Stories define the implementation backlog. Discrepancies are recorded in `10-decision-log.md`.

## Constraints Relevant to Stage 1

### From Architecture v1.2A

| Constraint | Source | Stage 1 Impact |
|---|---|---|
| All Tracto components in modern .NET | §1, §5, AD-04 | C# / .NET 10 LTS for every Stage 1 deliverable |
| Exactly three logical components | §3.1, AD-01 | Mock Orchestrator is a test fixture, never a fourth product component |
| Three independently developed .NET worker applications | §5, v1.2A delta | Shared contracts must allow independent CI per Runtime team |
| Runtime-initiated outbound pull/lease/heartbeat | §6.2, AD-06 | No inbound worker listener; same protocol on localhost and network |
| One active job per worker in v1 | §5.6, AD-07 | Mock enforces one active lease per worker |
| One foreground-input owner per interactive session | §5.6, AD-07 | `interactiveSessionId` fencing required in registration |
| Orchestrator is a .NET Linux container | §6, AD-01 | Linux Orchestrator cannot access Windows DPAPI directly |
| DPAPI CurrentUser (isolated) / vault (connected) on Windows side only | §10.2, AD-14 | `secretRef` only; no plaintext in Orchestrator, WSL, logs |
| Closed technical-condition grammar; no relational/arithmetic expressions | §8, AD-08 | Allowlist parser; no expression evaluator |
| Current + previous major schema compatibility (N/N-1) | §8, AD-08 | Stage 1 begins with one schema major (v1). N/N-1 applies when an actual second major is published. Stage 1 tests verify that an artifact with an unsupported schema major is rejected with a republish diagnostic. |
| OpenTelemetry-style telemetry | §11.3, AD-09 | Standard field set; no commercial monitoring stack at Stage 1 |
| POC-01–POC-09 and POC-10 are conditional-freeze gates | §14 | NOT a Stage 1 dependency; Stage 1 proceeds independently |
| Mixed-engine scripts rejected | §8, AD-08, CEC-008 | Single `runtimeType` per script validated at envelope level |
| Designer uses AI only during authoring; execution is deterministic | §4.3, §8 | No LLM dependency in Stage 1 runtime contract |

### From Contract v1.0.1

| Constraint | Section | Relevant Story |
|---|---|---|
| Signed artifact validation before any side effect | §§2–3, CEC-001, CEC-HOST-001 | ST1-01 |
| No business expression interpreter; allowlist grammar | §5, CEC-002 | ST1-04 |
| `service.invoke` only to registered custom services | §6.1, CEC-013 | ST1-06 |
| UNKNOWN when irreversible commitment cannot be proved | §7, CEC-004 | ST1-08, ST1-11 |
| `needsAttention=true` on UNKNOWN outcomes | §7 | ST1-08 |
| Attempt fencing; lease expiry ≠ replay permission | §§7–8, CEC-004 | ST1-10, ST1-11 |
| `secretRef` only; Windows-side resolution | §10, CEC-006 | ST1-12 |
| Protocol N/N-1 compatibility | §8.1, CEC-010, CEC-015 | ST1-09, ST1-15 |
| SKIPPED only for unselected branch arm | §7, CEC-011 | ST1-05, ST1-08 |
| `COMPLETED` is not a valid run state | §7 | ST1-08 |
| Mock Orchestrator is a test fixture; must not become fourth product | §11 | ST1-14 |
| Business service SUCCESS outcome exists (five total outcomes) | §6.1 | ST1-06, ST1-14 |
| `outputs` must declare evidence/telemetry classification | §3 (field table) | ST1-01 |
| `vars` are script-scoped; written only through `outputBinding` | §4 (field table), ST1-02 | ST1-02 (C1) |
