# Tracto Terminal Runtime — Independent Sprint Delivery Plan

**Plan version:** 1.0 (proposed for architect / product owner approval)  
**Prepared:** 24 October 2026  
**Scope:** 23 existing `TER-US-*` stories; no story added or removed  
**Track independence:** No dependency on the other Runtime track completing

> **North-star sprint objective:** Deliver a secure, deterministic, independently certified Terminal Runtime that consumes the completed Stage 1 contract and conforms to frozen Tracto Architecture v1.2A without embedded business logic.

## Source and authority rules

**Frozen functional boundary**: Tracto Architecture v1.2A → completed Common Execution Contract v1.0.1 and actual Stage 1 NuGet/schema/protocol packages → approved engine-specific functional specification → approved user stories and criteria → this sprint plan. If the source specification and story disagree, obtain a narrow architect/contract-owner decision and update both before writing the dependent behavior. This plan schedules **existing** stories only; it authorizes no extra Runtime features.

**Source status warning:** The document newly attached as a "sprint plan" is an **alignment register**, not a backlog with dated sprints. Accordingly the sprint organization here is newly proposed from the story dependencies. The register describes fixes as implemented, but the separately available v1.1 specification and v1.0 story files may not yet be synchronized; Sprint 0 verifies the exact code-facing approved revisions rather than relying on register assertions. Any earlier audit items are targeted AC/spec clarifications to existing stories, not new stories.

**Planning assumptions, not promises:** Sprint 0 is provisionally 1 week; development sprints are provisionally 2 weeks. Dates, velocity and staffing require a real capacity check. A workable initial allocation is 2–3 implementers per track, one independent reviewer and independent QA coverage, plus shared architecture/security availability. Each sprint must be sized by measured capacity before commitment. Teams share the **Stage 1 contract and Orchestrator**, not their engine implementations, repos, or release gates.

**Story completion pipeline:** READY → DEVELOPING → independent CODE REVIEW → corrections/re-review → independent QA with executed evidence → INTEGRATED → DONE. Review findings must cite exact approved AC/contract/architecture behavior and code evidence; optional design suggestions cannot block. A story is DONE only with integrated commit, passing applicable positive/negative tests, documented security and regression evidence, and no blocking defects. Sprint review requires goal demonstration, all committed stories DONE (or explicit authorized scope revision), and evidence links. Do not quietly carry incomplete work forward.

**Shared checks run throughout, not just in the last sprint:** signed artifacts before side effects; exact CEC protocol and error enums; lease/attempt fencing and worker journal; five `serviceOutcome` values distinct from terminal run states; no plaintext secrets; no business comparisons; no direct third-party business API/database integration; `service.invoke` only to registered independent services; no cross-engine steps. CF-01–CF-10 fixture subset runs whenever relevant, then full suite at release. Deployment POC-01–POC-10 remain **unchanged architecture gates** applicable only to advertised modes, never substituted by BRW-AT or TER-AT fixtures.

**Release baseline:** .NET 10 LTS is the reviewed technology direction, subject to actual package compatibility with the completed Stage 1 implementation. Script schema initial major v1; do not simulate nonexistent previous major merely to claim compatibility. All protocol message names come from the compiled Stage 1 interface, not from illustrative plan text.

## Why this sequencing

- **Earliest failure mode.** Before command process launch: validate signed artifact, shell, commandRef, interpreter, executable integrity, typed arguments, effective identity, path and policy.
- **Argument security.** ProcessStartInfo.ArgumentList provides argument escaping, but when launching cmd.exe/PowerShell/Bash the shell still interprets script syntax; approved fixed templates and input checks remain necessary.
- **Real PowerShell policy.** PowerShell ConstrainedLanguage configuration by itself is not a universal security boundary. Honor deployer-enforced application control and version-certified restrictions.
- **Source alignment before coding.** Resolve `secretHandle` vs `outputRedaction`, Terminal §9 launch-only fields, canonical CEC enum values, and the cleared no-shell-business-integration rule.
- **WSL topology is not a new frozen component.** Confirm control-plane separation for advertised WSL mode; exact distro/container selection requires approved deployment profile, not a new fourth Tracto component.

### External technical validation used for planning (not new functionality)

- [ArgumentList .NET semantics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist): Use typed argument lists and fixed templates; shell syntax remains independently dangerous.
- [PowerShell security boundaries](https://learn.microsoft.com/en-us/powershell/scripting/security/security-features): Deployment application-control rules matter; do not rely on language mode alone to claim containment.
- [WSL explicit distro and user](https://learn.microsoft.com/en-us/windows/wsl/basic-commands): Distro and user can be selected explicitly, enabling reproducible identity checks when WSL support is advertised.

## Proposed sprint overview

| Sprint | Goal | Story count | Duration* | Exit demonstration |
|---|---|---:|---|---|
| **T0** | Baseline, shell feasibility and policy authority | 0 | 1 week (planning assumption) | Versioned approved baseline |
| **T1** | Secure worker and execution environment | 6 | 2 weeks | Unsigned/wrong-engine job rejected before process start |
| **T2** | Approved command execution and isolation | 5 | 2 weeks | Tampered/unapproved command rejected before launch |
| **T3** | Streams, secrets and output contracts | 5 | 2 weeks | No process deadlock from stderr flood |
| **T4** | Lifecycle, uncertainty and reconciliation | 4 | 2 weeks | Cancelled and timed-out process trees cleaned |
| **T5** | Service handoff and independent release | 3 | 2 weeks | All applicable CF-01–CF-10 and TER-AT-01–28 plus approved supplemental tests pass |

*Durations are planning assumptions; backlogs are not firm commitments until capacity calibration.*

## Detailed sprint backlogs and dependencies

### T0 — Baseline, shell feasibility and policy authority

**Sprint goal.** Resolve contract/spec/story discrepancies and prove an approved Windows CLI process can be admitted, launched and cleaned up without unreviewed shell execution.

**Parallel implementation lanes.** Two parallel lanes: (A) approve normative schema/redaction/WSL deployment decisions and inspect compiled Stage 1 APIs; (B) CMD/PowerShell/WSL feature probe, controlled test command fixture and CI security baseline.

**Preflight work (non-story work package; no new backlog story ID):**
1. Approve the exact synchronized spec/story/CEC baselines and record remaining decisions.
2. Check actual published Stage 1 packages, schema v1, protocol and conformance runner against repository code.
3. Build deterministic fixtures and a separate working branch/CI path for this Runtime.
4. Assign feature-code ownership, independent reviewer, QA, and per-story traceability records.
5. Perform a small real feasibility test on the primary engine and document any unproven deployment support.


**Acceptance-oriented sprint demonstration.** Versioned approved baseline; signed command fixtures; shell/identity support matrix; WSL separation option approved for advertised modes; actual process-launch and stdout smoke tests; list of safety checks before first shell side effect.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** Proceed to T1 only after this gate is passed.

### T1 — Secure worker and execution environment

**Sprint goal.** A standalone headless worker can register/pull exactly one trusted Terminal job and safely admit its requested shell, identity, paths and scoped environment.

**Parallel implementation lanes.** Lane A: TER-US-020+022 (signed artifact, lease/journal); lane B: TER-US-001+002 (shell identity/capability); lane C: TER-US-003+005 (working directory, environment, headless/remote boundary). Combine in no-command admission test.

| Approved story | Priority* | Prerequisites / ordering | Independent lane | Existing test IDs |
|---|---|---|---|---|
| **TER-US-020** — Signed Artifact Validation and Closed Behavior | P0 — safety/contract | CEC package/schema approved | Contract/Trust | TER-AT-21, TER-AT-26, TER-AT-28 |
| **TER-US-022** — Lease Fencing, Heartbeat, and Attempt Reconciliation | P0 — safety/contract | 020 | Worker protocol | TER-AT-14, TER-AT-28 |
| **TER-US-001** — Shell Availability and Version Validation | P1 — capability | Shell/OS support matrix | Shell admission | TER-AT-02, TER-AT-03 |
| **TER-US-002** — Execution Identity and Privilege Enforcement | P1 — capability | 001 + deployment identity approval | Identity | TER-AT-02, TER-AT-03, TER-AT-23 |
| **TER-US-003** — Working Directory Policy and Environment Variable Handling | P1 — capability | 002 | Environment | TER-AT-01, TER-AT-11, TER-AT-19 |
| **TER-US-005** — Headless Terminal Operation and Remote Context Rejection | P1 — capability | 020 + 022 | Host policy | TER-AT-12, TER-AT-15, TER-AT-28 |

**Acceptance-oriented sprint demonstration.** Unsigned/wrong-engine job rejected before process start; one worker slot; approved identity; path traversal/unknown remote blocked; no GUI lease; journal and heartbeats use compiled Stage 1 semantics.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** Proceed to T2 only after this gate is passed.

### T2 — Approved command execution and isolation

**Sprint goal.** Safely run only approved CMD, PowerShell and conditionally WSL Bash command definitions with typed inputs and integrity enforcement; no business-processing or API-bypass routes.

**Parallel implementation lanes.** Lane A: TER-US-006+004 (commandRef, signed integrity, typed argv/template); lane B: TER-US-007 (PowerShell and batch policy); lane C: TER-US-008 (WSL identity and isolation if advertised); security lane TER-US-009 tests bypass across all command paths.

| Approved story | Priority* | Prerequisites / ordering | Independent lane | Existing test IDs |
|---|---|---|---|---|
| **TER-US-004** — Process Creation with Typed Argument Binding | P1 — capability | 003 + 006 | Typed process | TER-AT-01, TER-AT-22 |
| **TER-US-006** — Command Allowlist Enforcement and Integrity Verification | P0 — safety/contract | 020 + 003 | Command registry | TER-AT-02, TER-AT-21 |
| **TER-US-007** — PowerShell Safe Execution Policy | P1 — capability | 006 + 004 | PowerShell | TER-AT-02 |
| **TER-US-008** — WSL Bash Script Invocation and Distro Isolation | P1 — capability | 001 + 002 + 006 | WSL | TER-AT-03, TER-AT-18, TER-AT-23 |
| **TER-US-009** — No Business Logic in Commands and Network Bypass Prevention | P0 — safety/contract | 006 + 004 | Security policy | TER-AT-13, TER-AT-16, TER-AT-17, TER-AT-20, TER-AT-26 |

**Acceptance-oriented sprint demonstration.** Tampered/unapproved command rejected before launch; injection strings remain data or rejected; fixed script refs only; no unapproved network/API/SQL bypass; WSL is control-plane-separated per approved certified topology.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** Proceed to T3 only after this gate is passed.

### T3 — Streams, secrets and output contracts

**Sprint goal.** Run commands with bounded stdin/stdout/stderr, safe prompts, technical outputs, audit, redaction and classified retry readiness.

**Parallel implementation lanes.** Lane A: TER-US-011 (stream caps/encoding) then TER-US-014 (output bindings, artifacts); lane B: TER-US-012 (bounded stdin/prompt); lane C: TER-US-013 (mandatory secret redaction) and TER-US-010 (idempotency/audit). QA runs flood/injection/token tests throughout.

| Approved story | Priority* | Prerequisites / ordering | Independent lane | Existing test IDs |
|---|---|---|---|---|
| **TER-US-010** — Idempotency Classification and Command Audit | P1 — capability | 006 + 004 | Audit/retry | TER-AT-08, TER-AT-14, TER-AT-27 |
| **TER-US-011** — Bounded stdout/stderr Capture and Deadlock Prevention | P1 — capability | 004 | Stream | TER-AT-04, TER-AT-06 |
| **TER-US-012** — Bounded Stdin and Allowlisted Prompt Matching | P1 — capability | 004 + 011 | Prompts | TER-AT-05 |
| **TER-US-013** — Output Redaction and Secret Protection | P0 — safety/contract | 003 + 011 | Redaction | TER-AT-11, TER-AT-19 |
| **TER-US-014** — Output Bindings and Approved Result Artifacts | P1 — capability | 011 + 013 | Bindings/files | TER-AT-10, TER-AT-24 |

**Acceptance-oriented sprint demonstration.** No process deadlock from stderr flood; configured per-chunk and total caps; output redacted before storage/telemetry; prompt allowlist respected; terminal.process.await/stdin/write/read/file.exists/env.inspect actions have explicit positive and negative assertions.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** Proceed to T4 only after this gate is passed.

### T4 — Lifecycle, uncertainty and reconciliation

**Sprint goal.** Time-bound all owned processes, clean up without affecting unrelated hosts, detect WSL or Orchestrator outage and classify irreversible uncertainty as UNKNOWN without replay.

**Parallel implementation lanes.** Lane A: TER-US-015 (deadlines/process trees); lane B: TER-US-016 (fencing/UNKNOWN); lane C: TER-US-017 (WSL loss) and TER-US-018 (long process heartbeats); integrate fault injection into same deterministic harness.

| Approved story | Priority* | Prerequisites / ordering | Independent lane | Existing test IDs |
|---|---|---|---|---|
| **TER-US-015** — Process Deadline Enforcement and Owned Process-Tree Cleanup | P1 — release | 004 + 011 + 012 | Lifecycle | TER-AT-07, TER-AT-27 |
| **TER-US-016** — Unknown Side Effect Classification and No Duplicate Replay | P0 — safety/contract | 010 + 015 | Outcome | TER-AT-08, TER-AT-27 |
| **TER-US-017** — WSL Failure Detection and Reconciliation | P1 — release | 008 + 016 (if WSL advertised) | WSL recovery | TER-AT-09, TER-AT-23 |
| **TER-US-018** — Heartbeat During Long-Running Commands and Control-Plane Restart | P1 — release | 022 + 015 | Heartbeat | TER-AT-14, TER-AT-28 |

**Acceptance-oriented sprint demonstration.** Cancelled and timed-out process trees cleaned; no orphan and no collateral kill; effectful outcome lost => UNKNOWN and operator attention; control-plane restart cannot replay; heartbeat fail remains subject to actual lease/fencing constraints.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** Proceed to T5 only after this gate is passed.

### T5 — Service handoff and independent release

**Sprint goal.** Demonstrate authorized service.invoke, five exact service outcomes, safe asynchronous handoff/continuation, and certify the Terminal worker independently in advertised deployment modes.

**Parallel implementation lanes.** Lane A: TER-US-019+021 (cleanup, service outcome/handoff); lane B: TER-US-023 (CF and mode harness, build packaging); integration/security QA runs in parallel after each lane is review approved, but release gate follows both.

| Approved story | Priority* | Prerequisites / ordering | Independent lane | Existing test IDs |
|---|---|---|---|---|
| **TER-US-019** — Filesystem Cleanup and Async Service Handoff | P1 — release | 014 + 015 + 022 | Async cleanup | TER-AT-07, TER-AT-10, TER-AT-19, TER-AT-24, TER-AT-25 |
| **TER-US-021** — All Five Service Outcomes and Run State Handling | P0 — safety/contract | 019 + 020 | Service | TER-AT-10, TER-AT-24, TER-AT-25 |
| **TER-US-023** — CEC Conformance Suite and Independent Release | P0 — safety/contract | All T1–T4 acceptance + 019 + 021 + 022 | Certification | TER-AT-28 |

**Acceptance-oriented sprint demonstration.** All applicable CF-01–CF-10 and TER-AT-01–28 plus approved supplemental tests pass; mock and real compatible Orchestrator; exact CEC states; statically configured continuation only after eligible callback; WSL mode only if advertised; release traceability complete.

**Sprint review gate:** The goal is demonstrated from an integrated build; every committed story meets its original and architect-approved refined acceptance criteria; reviewer and QA evidence is attached; all applicable fault/security/regression checks pass; remaining blockers are openly listed. If a blocked story prevents the goal, the sprint cannot be marked PASSED.

**Handoff:** After certification, publish this Runtime independently; do not wait for the other engine.

## Cross-sprint dependency and readiness checkpoints

| Checkpoint | Dependency that must be satisfied | Who verifies |
|---|---|---|
| Start T1 | Authoritative files and Stage 1 compiled contract are aligned, no conflicting normative fields | Contract owner + architect |
| Start T2 | Trust/lease admission and primary execution environment proven | Runtime lead + QA |
| Start T3 | Engine locator/command execution foundations tested with approved fixture | Runtime lead + security reviewer |
| Start T4 | Effectful actions and output handling are integration-tested | Independent QA + integration |
| Start T5 | All prior sprint stories integrated with mandatory negative tests | Release owner + contract owner |
| Final release | Applicable CEC fixtures and the entire engine-specific acceptance suite pass against real Orchestrator in advertised modes | Independent QA + security + architect |

## Approved-baseline reconciliation actions to close in Sprint 0

These items reflect the previously documented audit/review; no additional user-story IDs or independent product features are authorized. Only corrections supported by already-approved functionality may be incorporated.

1. Align Terminal Appendix C to exact 11 CEC error categories; not service outcomes or noncanonical INTERNAL_ERROR.
2. Use approved `outputRedaction`/security-policy wording consistently in Terminal spec and TER-US-013; minimum redaction cannot be disabled.
3. Make launch-only commandRef/shell/cwdRef fields apply to terminal.process.start, not every terminal.* action.
4. Remove any shell HTTP/API/SQL third-party business integration exception: programmatic business service calls only via service.invoke.
5. Choose/approve WSL control-plane separation deployment approach; separate distro/container is one candidate, not an unapproved architecture amendment.
6. Strengthen existing story acceptance: command arg limits/path/URL validation, stdout 64KB chunk and newline checks, read/await/cancel/file.exists/env.inspect action-level cases.
7. Scope heartbeat-loss continuation within lease/fencing/step deadlines; conditional business rejection SUCCEEDED only after declared technical handling; callback continuation is not immediate on HANDED_OFF.
8. Check path canonicalization policy: reject outside approved roots, but unconditional rejection of any `../` needs explicit approved stricter policy.

## Acceptance traceability and full-track exit checklist

- All **23** approved stories present exactly once in the plan and marked DONE with actual evidence.
- All **TER-AT-01 through TER-AT-28** cases mapped and executed where applicable, including negative/fault/security cases and any approved gap-closing tests.
- Every functional requirement is mapped to at least one implemented feature and evidence-backed criterion; ID references alone do not establish behavioral coverage.
- Common Contract CF-01–CF-10 suite passes using the actual compatible Stage 1 artifact, mock fixture, and real Orchestrator integration for supported modes.
- Every non-idempotent or uncertain side effect is fenced; UNKNOWN stops current attempt and is not retried blindly; HANDED_OFF and later continuation remain distinct.
- No secret leaks into scripts, logs, telemetry, traces, evidence, shell streams, Orchestrator messages, or offline packages.
- Mode support matrix is explicit: supported versions, OS/build, headless/headed or CMD/PS/WSL as applicable, identity, policies and conditional host PoCs.
- Independent review, QA, security approval, integration build, deployment notes, and release evidence are archived.
- No unapproved shared new protocol, no cross-engine implementation dependency, no fourth component, and no arbitrary business processing embedded in Tracto.

## Sprint execution record template

| Story | Planned sprint | Developer branch/commit | Reviewer approval | QA execution report | Integrated build | Status/blocker |
|---|---|---|---|---|---|---|
| TER-US-020 | T1 | — | — | — | — | READY |
| TER-US-022 | T1 | — | — | — | — | READY |
| TER-US-001 | T1 | — | — | — | — | READY |
| TER-US-002 | T1 | — | — | — | — | READY |
| TER-US-003 | T1 | — | — | — | — | READY |
| TER-US-005 | T1 | — | — | — | — | READY |
| TER-US-004 | T2 | — | — | — | — | READY |
| TER-US-006 | T2 | — | — | — | — | READY |
| TER-US-007 | T2 | — | — | — | — | READY |
| TER-US-008 | T2 | — | — | — | — | READY |
| TER-US-009 | T2 | — | — | — | — | READY |
| TER-US-010 | T3 | — | — | — | — | READY |
| TER-US-011 | T3 | — | — | — | — | READY |
| TER-US-012 | T3 | — | — | — | — | READY |
| TER-US-013 | T3 | — | — | — | — | READY |
| TER-US-014 | T3 | — | — | — | — | READY |
| TER-US-015 | T4 | — | — | — | — | READY |
| TER-US-016 | T4 | — | — | — | — | READY |
| TER-US-017 | T4 | — | — | — | — | READY |
| TER-US-018 | T4 | — | — | — | — | READY |
| TER-US-019 | T5 | — | — | — | — | READY |
| TER-US-021 | T5 | — | — | — | — | READY |
| TER-US-023 | T5 | — | — | — | — | READY |

## Primary source references

1. `Tracto_Terminal_Runtime_User_Stories_v1_0.md` — story IDs and exact story-level test references.
2. `Tracto_Terminal_Runtime_Functional_Spec_v1_1_Aligned.md` — engine action catalogue, requirements, security and acceptance tests (verify synchronized approved revision at T0).
3. `Tracto_Browser_Terminal_Alignment_Register(2).md` — latest architecture-alignment decisions and constraints (not a sprint schedule).
4. Tracto Architecture v1.2A Independent Runtime Amendment — frozen boundaries and conditional deployment PoCs.
5. Common Execution & Orchestrator Contract v1.0.1 and actually published Stage 1 packages — shared execution authority.
6. Coverage Audit 2026-10-24 — list of semantic-coverage refinements to verify, not a replacement source of scope.

> **Approval note:** This is a proposed delivery sequencing document. It does not certify corrected source specs, implementation readiness, tested acceptance, or unperformed deployment PoCs.
