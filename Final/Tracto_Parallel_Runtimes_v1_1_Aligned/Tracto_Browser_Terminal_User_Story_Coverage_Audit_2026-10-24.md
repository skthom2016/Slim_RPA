# Tracto — Browser & Terminal User Story Coverage Audit

**Date:** 24 October 2026  
**Baseline:** Browser/Terminal Functional Specifications v1.1 (updated Markdown), frozen Architecture v1.2A, Common Execution Contract v1.0.1, and uploaded Browser/Terminal User Stories v1.0.  
**Verdict:** Identifier coverage complete; behavioral acceptance coverage and cross-document consistency are not yet 100%.

## Automated results

| Check | Browser | Terminal |
|---|---:|---:|
| User stories | 21 | 23 |
| Requirement IDs represented | 36/36 | 35/35 |
| Acceptance test IDs represented | 28/28 | 28/28 |
| Requirement-to-primary-test mapping differences in individual story headers | 9 | 8 |

Counting identifiers does not establish that acceptance criteria test each required behavior. The nine and eight mapping differences are not equivalent to missing tests: every fixture identifier appears elsewhere in each backlog.

## Priority corrections before asserting complete functionality

| Priority | Target story/source | Confirmed issue | Required correction |
|---|---|---|---|
| P1 | Browser BRW-US-003, BRW-008 | BRW-008 is network/proxy/TLS support, yet US-003 cites it for session isolation and no story explicitly tests TLS certificate rejection or enterprise proxy handling. | Remove BRW-008 from US-003; add specific positive and negative proxy/TLS criteria to US-005 or US-015. |
| P1 | Browser BRW-US-005 / WEB-001 | Navigate and redirects tested; reload, back and forward history actions required by WEB-001 not explicitly accepted. | Add replayable reload/back/forward checks under the same story. |
| P1 | Browser BRW-US-011 / WEB-003 | Drag is required in WEB-003, but not explicitly included in story criteria. | Add supported Playwright drag behavior and bounded failure verification. |
| P1 | Browser BRW-US-004 / BRE-004 | AC03 says pre-effect crash is FAILED; AC04 says any browser crash terminates UNKNOWN, contradicting both AC03 and conditional safe restart in source spec. | Make terminal classification depend on proven action state; allow only explicitly approved safe restart, never replay uncertain effectful actions. |
| P1 | Terminal TER-US-013 / STR-007 / §9 | User stories mandate `outputRedaction` while functional spec declares normative `secretHandle`; security policy is inconsistent. | Select a single schema/policy field approved by contract owner; update spec and story together; redaction cannot be disabled. |
| P1 | Terminal command catalogue §9 and TER-US-014/015 | `terminal.process.start`, `terminal.process.await`, `terminal.stdin.write`, `terminal.stdout.read`, `terminal.stderr.read`, `terminal.process.cancel`, `terminal.file.exists`, `terminal.env.inspect` are normative actions but their invocation/outputs/failure tests are only implicit or missing. | Add concise action-level acceptance cases to the matching existing stories; do not create micro-stories. |
| P1 | Browser+Terminal Appendix C of specs | Appendix C uses non-CEC `errorCategory` members ACTION_TIMEOUT, TRANSIENT_FAILURE, INTERNAL_ERROR, UNKNOWN, BUSINESS_REJECTION, SECURITY_VIOLATION despite claiming exact CEC taxonomy. | Align functional specs with the 11 implemented CEC categories, preserve `browser.*` / `terminal.*` as detailCode. |
| P1 | Browser BRE-007, Terminal §9, Browser BRW-SUP-001 | Existing specs contain unresolved architecture-review contradictions: continuing after UNKNOWN, exception for shell API bypass, universal Supervisor registration. Stories sometimes correctly narrow them. | Approve corrected normative source specs before certifying user-story alignment. |
| P2 | Browser BRW-US-010 / DOM-009 | DOM inspection covers attributes, displayed text and form values; criterion currently refers only generically to one field value. | Add explicit read/displayed-text, approved-attribute and form-value evidence with opaque pass-through. |
| P2 | Browser BRW-US-012/014 / WEB-009 | Sign-in and MFA are covered, but expired session/relogin detection is not explicit. | Add expiration detection, no silent credential bypass, and authorized recovery/attention. |
| P2 | Browser BRW-US-015 AC05 | Cites `TER-007`, a Terminal-only requirement, for Browser reconciliation. | Replace with Browser BRE-007/CEC-004 and BRW-US-019 cross-reference as appropriate. |
| P2 | Both service-outcome stories | `BUSINESS_REJECTION` is stated to imply technical SUCCEEDED unconditionally. | Qualify success on completion of the configured handling UI/CLI step; independent technical failure remains FAILED. |
| P2 | Both async service-handoff stories | Wording can imply the continuation run is created immediately at acknowledged HANDED_OFF. | Explicitly require an eligible authenticated deduplicated callback or other approved continuation trigger before creating Run B. |
| P2 | Terminal TER-US-004 / CMD-002 | Argument bound, URL/path policy and maximum parameter length are not separately verified. | Add invalid size/path/URL and injection boundary cases without adding an action. |
| P2 | Terminal TER-US-011 / STR-001, STR-003 | Aggregate output caps tested, but per-chunk 64KB maximum and CRLF/LF normalization are not explicit. | Add per-chunk and newline-normalization cases. |
| P2 | Terminal TER-US-014 / §9 | Technical `terminal.file.exists` and allowlisted `terminal.env.inspect` behaviors are not explicitly tested. | Add technical-only positive/negative checks and secret-redaction cases. |
| P2 | Terminal TER-US-018 | A failed Orchestrator heartbeat is said to never stop the command, without lease/deadline bounds. | Preserve safe continuation within approved lease/fencing/deadline constraints; avoid unbounded execution. |
| P2 | Browser BRW-US-020 and Terminal TER-US-023 | Real Orchestrator tests are framed only for connected mode. | Require actual mode-appropriate real-Orchestrator integration for each deployment mode being claimed; no dependency on other Runtimes. |
| P2 | Browser BRW-SUP-001 and Terminal ENV-006 | Some architect proposals (new Supervisor protocol, exact WSL distro/network topology) appear as normative without confirmed architecture approval. | Mark as approval-dependent; do not silently introduce new shared protocols or deployment constraints. |

## Additional source-accuracy observations

* Browser BRW-US-018 says **Terminal** run states: an editorial cross-engine label error.
* Browser BRW-US-020 says browser binaries "build under .NET"; Playwright .NET application builds, browser binaries are pinned and packaged.
* Terminal TER-US-003 rejects any `../` even if canonicalized path remains within an authorized root; this is stricter than ENV-003 and needs an explicit policy decision.
* Terminal TER-US-021 uses unconditional technical `SUCCEEDED` for rejection: distinguish service business outcome from completion of the technical run.
* Terminal TER-US-023 should not assume a WSL feature is a mandatory release gate if that deployment capability is not advertised, unless the baseline specifically requires it.

## Mapping matrix — documentary traceability (not semantic certification)

### Browser

| Functional requirement | Story(ies) that cite it | Missing primary test refs from corresponding story headers |
|---|---|---|
| `BRW-001` | `BRW-US-001`, `BRW-US-021` | — |
| `BRW-002` | `BRW-US-002` | — |
| `BRW-003` | `BRW-US-003` | — |
| `BRW-004` | `BRW-US-004`, `BRW-US-016` | — |
| `BRW-005` | `BRW-US-005`, `BRW-US-017` | — |
| `BRW-006` | `BRW-US-002`, `BRW-US-006` | — |
| `BRW-007` | `BRW-US-006` | — |
| `BRW-008` | `BRW-US-003` | `BRW-AT-08`, `BRW-AT-28` |
| `DOM-001` | `BRW-US-007` | — |
| `DOM-002` | `BRW-US-007` | — |
| `DOM-003` | `BRW-US-008` | — |
| `DOM-004` | `BRW-US-008` | — |
| `DOM-005` | `BRW-US-009` | — |
| `DOM-006` | `BRW-US-009` | — |
| `DOM-007` | `BRW-US-009` | `BRW-AT-01`, `BRW-AT-05` |
| `DOM-008` | `BRW-US-010` | `BRW-AT-20` |
| `DOM-009` | `BRW-US-010` | — |
| `WEB-001` | `BRW-US-005` | `BRW-AT-01`, `BRW-AT-02` |
| `WEB-002` | `BRW-US-011` | — |
| `WEB-003` | `BRW-US-011` | `BRW-AT-05` |
| `WEB-004` | `BRW-US-012` | — |
| `WEB-005` | `BRW-US-012` | — |
| `WEB-006` | `BRW-US-013` | — |
| `WEB-007` | `BRW-US-013` | — |
| `WEB-008` | `BRW-US-014` | — |
| `WEB-009` | `BRW-US-014` | `BRW-AT-09` |
| `WEB-010` | `BRW-US-007` | `BRW-AT-04` |
| `BRE-001` | `BRW-US-009` | — |
| `BRE-002` | `BRW-US-005` | — |
| `BRE-003` | `BRW-US-015` | — |
| `BRE-004` | `BRW-US-004` | — |
| `BRE-005` | `BRW-US-009` | — |
| `BRE-006` | `BRW-US-014` | — |
| `BRE-007` | `BRW-US-015` | `BRW-AT-11`, `BRW-AT-20` |
| `BRE-008` | `BRW-US-015`, `BRW-US-016` | — |
| `BRW-SUP-001` | `BRW-US-021` | `BRW-AT-22` |

### Terminal

| Functional requirement | Story(ies) that cite it | Missing primary test refs from corresponding story headers |
|---|---|---|
| `ENV-001` | `TER-US-001` | — |
| `ENV-002` | `TER-US-002` | — |
| `ENV-003` | `TER-US-003` | `TER-AT-16` |
| `ENV-004` | `TER-US-003`, `TER-US-013` | — |
| `ENV-005` | `TER-US-003`, `TER-US-004` | — |
| `ENV-006` | `TER-US-002`, `TER-US-008` | `TER-AT-09` |
| `ENV-007` | `TER-US-005` | — |
| `ENV-008` | `TER-US-005` | — |
| `CMD-001` | `TER-US-006` | `TER-AT-01` |
| `CMD-002` | `TER-US-004` | — |
| `CMD-003` | `TER-US-004` | — |
| `CMD-004` | `TER-US-007` | — |
| `CMD-005` | `TER-US-007` | — |
| `CMD-006` | `TER-US-008` | — |
| `CMD-007` | `TER-US-009` | — |
| `CMD-008` | `TER-US-010` | — |
| `CMD-009` | `TER-US-006` | — |
| `CMD-010` | `TER-US-010` | `TER-AT-11`, `TER-AT-21` |
| `STR-001` | `TER-US-011` | — |
| `STR-002` | `TER-US-011` | `TER-AT-03` |
| `STR-003` | `TER-US-011` | — |
| `STR-004` | `TER-US-012` | — |
| `STR-005` | `TER-US-012` | — |
| `STR-006` | `TER-US-009`, `TER-US-014`, `TER-US-021` | — |
| `STR-007` | `TER-US-013` | — |
| `STR-008` | `TER-US-014` | `TER-AT-04`, `TER-AT-07` |
| `TER-001` | `TER-US-015` | `TER-AT-05` |
| `TER-002` | `TER-US-015` | — |
| `TER-003` | `TER-US-015` | `TER-AT-20` |
| `TER-004` | `TER-US-017` | — |
| `TER-005` | `TER-US-016` | — |
| `TER-006` | `TER-US-016` | — |
| `TER-007` | `TER-US-018` | — |
| `TER-008` | `TER-US-019` | — |
| `TER-009` | `TER-US-019` | — |

## Approval recommendation

**Not approved as 100% behaviorally covered.** Keep 21 Browser and 23 Terminal stories, add targeted acceptance criteria to existing stories, and synchronize any contradicted normative spec paragraphs. Re-run this matrix and an independent criterion-by-criterion review; then use actual tests and conformance evidence for implementation certification. No frozen architecture or Stage 1 change is justified by this audit.
