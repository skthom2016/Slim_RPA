# Tracto Browser & Terminal Runtime — User Stories Index

**Date:** 24 October 2026  
**Status:** Updated — coverage audit corrections applied (2026-10-24)  
**Source documents:**
- `Tracto_Browser_Runtime_Functional_Spec_v1_1_Aligned.md`
- `Tracto_Terminal_Runtime_Functional_Spec_v1_1_Aligned.md`
- `Tracto_Browser_Terminal_Alignment_Register.md`

---

## Overview

This index covers the full user story set for both independent Runtime tracks. The two tracks can be implemented in parallel — no story in the Browser track depends on the Terminal track or vice versa. Both tracks share the CEC v1.0.1 conformance fixtures (CF-01–CF-10) and can reuse the same mock Orchestrator fixture suite.

| Track | Story document | Story count | Requirement count | Acceptance test count |
|---|---|---|---|---|
| Browser Runtime | `Tracto_Browser_Runtime_User_Stories_v1_0.md` | 21 stories | 35 requirements | 28 tests (BRW-AT-01–28) |
| Terminal Runtime | `Tracto_Terminal_Runtime_User_Stories_v1_0.md` | 23 stories | 35 requirements | 28 tests (TER-AT-01–28) |
| **Total** | | **44 stories** | **70 requirements** | **56 tests** |

---

## Epic Map

### Browser Runtime Epics

| Epic | Stories | Theme |
|---|---|---|
| 1 — Browser Lifecycle and Environment | BRW-US-001 to BRW-US-006 | Binary provisioning, modes, context isolation, URL policy, metadata |
| 2 — Locator and DOM Operation | BRW-US-007 to BRW-US-010 | Semantic locators, frames, shadow DOM, dynamic waits, assertions |
| 3 — Browser Interactions and Page Features | BRW-US-011 to BRW-US-014 | Forms, tabs/popups/dialogs, file transfer, session state |
| 4 — Browser Failure Handling and Recovery | BRW-US-015, BRW-US-016 | Network failures, UNKNOWN outcome, cancellation |
| 5 — CEC Contract and Service Invocation | BRW-US-017 to BRW-US-020 | Artifact validation, five service outcomes, lease fencing, conformance |
| 6 — Offline Packaging and Isolated Deployment | BRW-US-021 | Bundled binaries, air-gapped execution |

### Terminal Runtime Epics

| Epic | Stories | Theme |
|---|---|---|
| 1 — Shell Environment and Process Identity | TER-US-001 to TER-US-005 | Shell validation, identity, working directory, argument binding, headless |
| 2 — Approved Command and Parameter Model | TER-US-006 to TER-US-010 | Allowlist, PowerShell policy, WSL isolation, no business logic, idempotency |
| 3 — Streams, Prompts, and Output Management | TER-US-011 to TER-US-014 | Bounded stdout/stderr, prompt matching, redaction, output bindings |
| 4 — Lifecycle, Deadlines, and Recovery | TER-US-015 to TER-US-019 | Deadlines, UNKNOWN classification, WSL failure, heartbeat, cleanup |
| 5 — CEC Contract and Service Invocation | TER-US-020 to TER-US-023 | Artifact validation, five service outcomes, lease fencing, conformance |

---

## Suggested Sprint Allocation

Both tracks can run in parallel from Sprint 1. Each sprint below targets one epic per track. Adjust based on team velocity.

| Sprint | Browser track | Terminal track | Shared / integration work |
|---|---|---|---|
| **Sprint 1** | Epic 1 — Lifecycle & Environment (BRW-US-001–006) | Epic 1 — Environment (TER-US-001–005) | Set up mock Orchestrator fixture (CF-01–CF-10); stub `service.invoke` fake |
| **Sprint 2** | Epic 2 — DOM & Locators (BRW-US-007–010) | Epic 2 — Command Policy (TER-US-006–010) | Implement signed artifact validation (shared between both tracks) |
| **Sprint 3** | Epic 3 — Interactions (BRW-US-011–014) | Epic 3 — Streams & Prompts (TER-US-011–014) | Secret redaction infrastructure (shared pattern) |
| **Sprint 4** | Epic 4 — Recovery (BRW-US-015–016) | Epic 4 — Lifecycle & Recovery (TER-US-015–019) | UNKNOWN state handling; run journal and reconciliation |
| **Sprint 5** | Epic 5 — CEC Contract (BRW-US-017–020) | Epic 5 — CEC Contract (TER-US-020–023) | All five service outcomes; CEC conformance suite run against mock |
| **Sprint 6** | Epic 6 — Offline deployment (BRW-US-021) | (complete) | Real Orchestrator integration gate; mode certification; security gate |

---

## Shared Constraints and Cross-Cutting Concerns

The following constraints apply to every story in both tracks. They do not need to be repeated in individual stories.

### CEC Contract (non-negotiable)

- Wire protocol: CEC v1.0.1 exactly. No Runtime may change shared enum values independently.
- Exact 11 `errorCategory` values: `VALIDATION`, `POLICY_DENIED`, `TARGET_NOT_FOUND`, `TARGET_AMBIGUOUS`, `TIMEOUT`, `SESSION_UNAVAILABLE`, `ENVIRONMENT`, `TRANSPORT`, `AUTHENTICATION`, `CANCELLED`, `INTERNAL`.
- Exact 5 `serviceOutcome` values: `SUCCESS`, `BUSINESS_REJECTION`, `TECHNICAL_FAILURE`, `TRANSIENT_FAILURE`, `UNKNOWN_OUTCOME`.
- Exact terminal run states: `SUCCEEDED`, `FAILED`, `CANCELLED`, `TIMED_OUT`, `UNKNOWN`, `HANDED_OFF`. `UNKNOWN` is terminal and stops the attempt.
- `UNKNOWN_OUTCOME` (service outcome) ≠ UNKNOWN (run state). A service returning `UNKNOWN_OUTCOME` terminates the attempt as UNKNOWN; no `actionCode` branch may continue before external reconciliation.
- `HANDED_OFF` is a run state after confirmed async acknowledgement — it is not a service outcome.

### Security (non-negotiable)

- No secret value may appear in logs, evidence, job messages, audit records, crash dumps, or Orchestrator payloads.
- Business-comparison predicates, dynamic scripts, and `page.evaluate` (Browser) are rejected at artifact validation before any side effect.
- Shell metacharacter injection and network-bypass shell commands (curl/wget/Invoke-WebRequest targeting business APIs) are rejected before process launch.
- Output redaction cannot be disabled by a script setting.

### Independence (non-negotiable)

- No Browser story may depend on a Terminal story being complete, and vice versa.
- Neither Runtime may depend on Desktop Runtime stories being complete.
- Each Runtime releases independently after passing its own integration and certification gates.

### Scope boundary (non-negotiable)

- No cross-engine scripts (mixed `browser.*` + `terminal.*` in one run).
- No runtime LLM reasoning, business rule engine, or direct business API/database integration.
- No fourth Tracto product component introduced by either Runtime specification.

---

## Release Gates (both tracks must satisfy independently)

| Gate | Browser | Terminal |
|---|---|---|
| Engineering build | Builds under approved .NET baseline, isolated from other runtimes | Builds under approved .NET baseline, isolated from other runtimes |
| Contract | CF-01–CF-10 pass against mock; all 5 outcomes, cancellation, fencing, UNKNOWN, signed artifact | CF-01–CF-10 pass against mock; all 5 outcomes, cancellation, fencing, UNKNOWN, signed artifact |
| Security | No secrets in any evidence or payload; business predicates and API bypass rejected | No secrets in any evidence or payload; business predicates and shell bypass rejected |
| Mode certification | Headless and headed only where advertised; headed claims exclusive input | Headless only; no interactive session claim; WSL Bash (if advertised) in certified separate customer distro — this gate applies only when WSL Bash is in the capability advertisement |
| Real integration | Against actual compatible Orchestrator before production release | Against actual compatible Orchestrator before production release |
| Deployment PoC | Architecture POC-01–POC-10 preserved; fixture tests do not certify host boot or session provisioning | Architecture POC-01–POC-10 preserved; fixture tests do not certify host boot or WSL restoration |

---

## Traceability Quick Reference

### Browser: Requirement → Story

| Requirement | Story |
|---|---|
| BRW-001 | BRW-US-001 |
| BRW-002, BRW-006 | BRW-US-002, BRW-US-006 |
| BRW-003 | BRW-US-003 |
| BRW-004 | BRW-US-004 |
| BRW-005 | BRW-US-005 |
| BRW-007 | BRW-US-006 |
| BRW-008 | BRW-US-005 |
| BRW-SUP-001 | BRW-US-002, BRW-US-021 |
| DOM-001, DOM-002 | BRW-US-007 |
| DOM-003, DOM-004 | BRW-US-008 |
| DOM-005, DOM-006, DOM-007 | BRW-US-009 |
| DOM-008, DOM-009 | BRW-US-010 |
| WEB-001 | BRW-US-005 |
| WEB-002, WEB-003 | BRW-US-011 |
| WEB-004, WEB-005 | BRW-US-012 |
| WEB-006, WEB-007 | BRW-US-013 |
| WEB-008, WEB-009 | BRW-US-014 |
| WEB-010 | BRW-US-007 |
| BRE-001, BRE-005 | BRW-US-009 |
| BRE-002 | BRW-US-005 |
| BRE-003, BRE-007, BRE-008 | BRW-US-015 |
| BRE-004 | BRW-US-004 |
| BRE-006 | BRW-US-014 |
| Appendix A (CEC-001, 002, 012) | BRW-US-017 |
| Appendix A (CEC-004, HOST-002, 003) | BRW-US-019 |
| Appendix A (CEC-005, 013, 016) | BRW-US-018 |
| Appendix A (CEC-007, 010) | BRW-US-020 |

### Terminal: Requirement → Story

| Requirement | Story |
|---|---|
| ENV-001 | TER-US-001 |
| ENV-002, ENV-006 | TER-US-002 |
| ENV-003, ENV-004, ENV-005 | TER-US-003, TER-US-004 |
| ENV-007, ENV-008 | TER-US-005 |
| CMD-001, CMD-009 | TER-US-006 |
| CMD-002, CMD-003 | TER-US-004 |
| CMD-004, CMD-005 | TER-US-007 |
| CMD-006 | TER-US-008 |
| CMD-007 | TER-US-009 |
| CMD-008, CMD-010 | TER-US-010 |
| STR-001, STR-002, STR-003 | TER-US-011 |
| STR-004, STR-005 | TER-US-012 |
| STR-006 | TER-US-009, TER-US-014 |
| STR-007 | TER-US-013 |
| STR-008 | TER-US-014 |
| TER-001, TER-002, TER-003 | TER-US-015 |
| TER-004 | TER-US-017 |
| TER-005, TER-006 | TER-US-016 |
| TER-007 | TER-US-018 |
| TER-008, TER-009 | TER-US-019 |
| Appendix A (CEC-001, 002, 012) | TER-US-020 |
| Appendix A (CEC-004, HOST-002, 003) | TER-US-022 |
| Appendix A (CEC-005, 013, 016) | TER-US-021 |
| Appendix A (CEC-007, 010) | TER-US-023 |
