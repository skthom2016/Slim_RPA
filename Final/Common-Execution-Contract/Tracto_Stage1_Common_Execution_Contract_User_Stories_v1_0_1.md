# Tracto Stage 1 — Common Execution & Orchestrator Contract
## Functional User Stories and Testable Acceptance Criteria

**Document ID:** TRACTO-ST1-US-001  
**Version:** 1.0.1 — reviewed acceptance-criteria patch  
**Architecture:** Tracto v1.2A — frozen; no architectural changes authorized  
**Basis:** Tracto Common Execution & Orchestrator Interface Contract v1.0.1 (22 October 2026)  
**Audience:** Lead architect, shared-contract team, three Runtime teams, Orchestrator team, QA and security reviewers  
**Status:** Functional stories ready for refinement; implementation and deployment PoCs remain unverified

---

## 1. Stage 1 objective and non-negotiable boundaries

Stage 1 establishes the **contract that independently delivered .NET Desktop, Browser and Terminal Runtime applications must implement**. It includes contract schemas, a reference validator, normalized messages and results, a mock Orchestrator and executable conformance fixtures. It does **not** implement a production Orchestrator, a complete Runtime engine, a Designer, an application-specific selector library or business-service logic.

- **Three-component product boundary is frozen:** Designer, Runtime family, Orchestrator. Go/Node.js/Python/.NET custom business services remain independent external systems.
- **One automation per runtime type:** `desktop`, `browser` or `terminal`. Cross-engine journeys comprise separate runs, never one mixed script.
- **Runtime integrations are constrained:** technical UI/CLI activity, Orchestrator protocol and `service.invoke` to *registered custom business-service operations only*. No direct third-party business API/database integration in scripts or command workarounds.
- **Technical control only:** no business calculations, arithmetic or relational domain comparisons, arbitrary expression evaluators, generated code or dynamic automation selection.
- **Deterministic execution:** a published script executes without LLM interpretation. Every action and recovery path is bounded and observable.
- **Correctness before features:** no blind replay when completion is uncertain; preserve attempt fencing, controlled retries, policy enforcement and secret isolation.
- **Scope of this backlog:** deliver shared specifications and tested contracts; runtime-specific selectors, browser actions, shell commands and Windows provisioning belong to later tracks.

## 2. User-story sizing and definition of done

Each story represents an implementable *capability*: one meaningful interface, validator/service boundary or test harness outcome, rather than one field or the entire platform. The team may implement internal subtasks, but must not split acceptance into separate standalone micro-stories unless scope changes are approved.

For a Stage 1 story to be accepted: (a) normative schema or contract rule is documented and versioned; (b) a working .NET testable contract/validator or fixture implementation exists where applicable; (c) positive, negative and boundary cases below pass; (d) error outputs are stable and sensitive data is redacted; (e) compatibility impact is reviewed; (f) at least one consumer-facing example is provided. Mock interactions are sufficient for Stage 1; do not claim real Windows, browser or shell behavior has been validated.

**Test labels:** `U` = deterministic unit/schema test; `C` = cross-process contract test; `M` = mock Orchestrator/service integration; `S` = security/policy test; `R` = compatibility/regression test. Every criterion has a persistent AC identifier suitable for linking to automated tests.

## 3. Story catalogue and recommended build order

| Sequence | Story ID | Capability | Primary role | Depends on |
|---|---|---|---|---|
| 1 | ST1-01 | Publish and validate trusted automation envelopes | Automation publisher | None |
| 2 | ST1-02 | Normalize step definitions and typed references | Runtime engineer | ST1-01 |
| 3 | ST1-03 | Declare and resolve named targets | Runtime engineer | ST1-01, ST1-02 |
| 4 | ST1-04 | Validate technical-only condition grammar | Automation author | ST1-02 |
| 5 | ST1-05 | Execute deterministic bounded control constructs | Runtime engineer | ST1-04 |
| 6 | ST1-06 | Invoke approved custom services synchronously | Automation author | ST1-02, ST1-04 |
| 7 | ST1-07 | Specify asynchronous handoff and continuation event | Integration engineer | ST1-06 |
| 8 | ST1-08 | Produce consistent run/step outcomes and errors | Operations engineer | ST1-02, ST1-06 |
| 9 | ST1-09 | Register workers and advertise capacity | Orchestrator engineer | ST1-01, ST1-08 |
| 10 | ST1-10 | Pull jobs, lease, heartbeat and fence attempts | Runtime engineer | ST1-09 |
| 11 | ST1-11 | Cancel, reconcile and resolve uncertain attempts | Operations engineer | ST1-08, ST1-10 |
| 12 | ST1-12 | Enforce security, policy and secret-reference boundary | Security engineer | ST1-01, ST1-06 |
| 13 | ST1-13 | Generate safe telemetry and evidence references | Support engineer | ST1-08, ST1-10, ST1-12 |
| 14 | ST1-14 | Run mock-Orchestrator conformance fixtures | QA automation engineer | ST1-01–ST1-13 contracts |
| 15 | ST1-15 | Publish and govern compatible contract releases | Lead architect | ST1-01, ST1-09, ST1-14 |

Stories 01–03 provide the envelope foundation; 04–08 define the execution semantics; 09–11 establish Orchestrator interoperability; 12–13 cut across all implementation paths; 14–15 certify and release the contract. Security reviews can start when the relevant draft shapes exist; no need to wait until story 11.

---

# Capability Group A — Script and Step Contract

## ST1-01 — Publish and validate a trusted automation envelope

**User story:** As an **Automation Publisher**, I want every Tracto script to use a typed, signed and immutable outer envelope, so that the Orchestrator and the intended Runtime can identify, validate and execute the exact approved automation version.

**Functional scope:** Define an engine-neutral schema with `schemaVersion`, `automationId`, `version`, `runtimeType`, `inputs`, `outputs`, `steps`, `policyRefs`, optional `targets` and `metadata`, plus separately verified publication digest/signer. Validate runtime type, version, declared types, policy references and artifact trust before any side effect. Do not define engine-specific actions or selectors.

**Acceptance criteria**

- **ST1-01-AC01 [U] Complete envelope.** Given a correctly typed envelope containing every required field and unique `automationId`/version, when validated, then the validator returns `VALID` and a normalized artifact descriptor; optional metadata cannot alter execution semantics.
- **ST1-01-AC02 [U] Missing/invalid fields.** Given each required field removed or wrong-typed in turn, when validated, then validation fails deterministically with `errorCategory=VALIDATION`, a field path and stable detail code, before steps execute. Negative cases also include automationId supplied as a filename, executable path, URL or path-traversal value; these are rejected as VALIDATION.
- **ST1-01-AC03 [U] Single runtime.** Given `runtimeType=desktop` containing a `browser.*` or `terminal.*` action, when validated, then the entire artifact is rejected; a single script cannot change runtime type mid-run.
- **ST1-01-AC04 [S] Trust verification.** Given an unsigned, tampered, invalid-digest or untrusted-signer artifact, when publication or worker validation runs, then it is rejected without an action being invoked; no script field may override trust policy. For an isolated/offline signed package import, verify trusted publisher, signed digest, runtimeType, schema, policy and allowlist before publication; reject an invalid bundle atomically, with no published executable version.
- **ST1-01-AC05 [R] Schema compatibility.** Given an artifact with the certified current or immediately preceding major `schemaVersion`, when validated against a compatible Runtime descriptor, then it is accepted if structurally valid; an older/unknown major receives a republish/incompatibility diagnostic.
- **ST1-01-AC06 [U] Immutable identity.** Given a published script, when metadata, `runtimeType`, inputs or steps are modified without republishing, then hash verification fails; the running attempt remains pinned to its original artifact reference.
- **ST1-01-AC07 [U] Typed declarations.** Given input/output declarations with supported primitive/structured types, required/default rules and no cleartext secret declaration, then a type-correct invocation passes; missing required or incompatible values fail with the variable name and expected type. Every output also declares an approved evidence/telemetry classification; recognized classifications pass policy checks, while missing, unknown or incompatible classifications and any plaintext secret output are rejected.
- **ST1-01-AC08 [S] Publication policy.** Given an artifact naming an unknown/unapproved `policyRef`, when validation runs, then it fails as `POLICY_DENIED` or `VALIDATION` according to the published contract mapping; it must not silently fall back to an unrestricted policy.

**Tests and delivery evidence:** JSON/schema fixture set including minimal valid, all-required-fields, tampered-signature, mixed-runtime and N/N−1/older-major cases; automated validator tests; canonical signed-artifact example. Do not build the Designer publishing UI in this story.

**Traceability:** CEC-001, CEC-008, CEC-010, CEC-HOST-001; Contract §§2–3, 11.

## ST1-02 — Normalize typed step definitions and data references

**User story:** As a **Runtime Engineer**, I want one typed step envelope and a safe way to reference declared inputs and previous step outputs, so that all three independently delivered engines can consume consistent control metadata without inventing their own expression language.

**Functional scope:** Specify `stepId`, `actionType`, `target` when applicable, `parameters`, `preconditions`, `postconditions`, `timeoutMs`, `retryClass`, optional `outputBindings` and `onFailure`; use typed `{"ref":"..."}` only for approved paths (`inputs`, `vars`, preceding `steps.*.outputs`, approved `secrets`). All runtime-specific action payloads remain opaque to the common contract.

**Acceptance criteria**

- **ST1-02-AC01 [U] Valid normalized step.** Given every mandatory step field with valid types and namespaced `actionType`, when parsed, then the result preserves the original `stepId`, typed arguments and runtime action namespace.
- **ST1-02-AC02 [U] Unique ordering.** Given duplicate `stepId` values, an unknown action namespace or a step specifying a different runtime, when validated, then the script fails with precise step-path diagnostics.
- **ST1-02-AC03 [U] Supported refs.** Given a declared input and preceding step output of matching type, when resolving `{"ref":"inputs.recordId"}` and `{"ref":"steps.s1.outputs.value"}`, then the resolver returns typed values without interpreting executable expressions. A typed outputBinding from a completed action into a declared vars.<name> makes that variable available to later steps; writing to undeclared vars, type mismatch, forward binding or a computing/transformation binding fails.
- **ST1-02-AC04 [U] Invalid/forward refs.** Given an undefined variable, missing output field, cross-script ref, reference to a future step, cyclic ref or incorrect type, when validated or bound, then it is rejected before its consuming action executes.
- **ST1-02-AC05 [U] Safe secret refs.** Given a `secrets.<approvedRef>` reference, when validated, then the normalized descriptor contains only the opaque reference; secret plaintext is neither accepted as a reference expression nor serialized into output bindings.
- **ST1-02-AC06 [U] Timeouts and retries.** Given `timeoutMs` greater than job deadline, negative duration, or a `retryClass` not in `NEVER`, `TRANSIENT_SAFE`, `IDEMPOTENT`, then validation rejects it; accepted step timeout is bounded by job policy.
- **ST1-02-AC07 [U] Safe failure handling.** Given `onFailure` selecting a declared bounded technical retry or safe recovery action, then the schema accepts it; dynamic script selection, domain compensation or unbounded recursive recovery is rejected.
- **ST1-02-AC08 [U] No general expression engine.** Given parameter strings containing arithmetic expressions, dynamic property access, arbitrary code or template evaluation, when parsed, then they remain inert literals or are rejected where references are required—never executed.
- **ST1-02-AC09 [U] Typed vars binding lifecycle.** Given a declared typed output and a valid outputBinding into vars.savedValue, when the source step completes and a later step resolves {"ref":"vars.savedValue"}, then the bound value retains its type. Computed mappings, undeclared variable writes, forward reads or secret plaintext bindings fail before the consuming action.

**Tests and delivery evidence:** DTO/model schema, reference resolver and type-checker unit tests; valid and invalid examples for all four reference namespaces, retry classifications and timeouts. Runtime-specific action schemas are not implemented here.

**Traceability:** CEC-002, CEC-011, CEC-012; Contract §§3–5.

## ST1-03 — Declare and resolve named runtime targets

**User story:** As a **Runtime Engineer**, I want scripts to reference reusable named targets while leaving selector details to the chosen engine, so that steps remain readable without forcing the Orchestrator to understand Windows UIA, Playwright DOM or shell command targets.

**Functional scope:** Optional top-level `targets` map, `selectorRef` lookup, target names and namespaces, opaque runtime-specific payload preservation, reference validation, controlled error semantics. Does not design actual Desktop/Browser/Terminal selector algorithms.

**Acceptance criteria**

- **ST1-03-AC01 [U] Named target success.** Given `targets.mainWindow` with a valid runtime-typed body and a step referencing `mainWindow`, when validated, then the reference resolves to exactly that definition.
- **ST1-03-AC02 [U] Unknown or empty target.** Given an undefined `selectorRef`, empty target name or duplicate name after normalization, when validated, then the artifact is rejected with the relevant step and target paths.
- **ST1-03-AC03 [U] Namespace isolation.** Given a `desktop` script pointing to a `browser` target definition, when validated against its declared runtime type, then the mismatch is rejected without invoking any engine.
- **ST1-03-AC04 [C] Opaque payload.** Given a well-formed runtime target extension unknown to the Orchestrator, when the Orchestrator reads the script descriptor, then it checks only basic shape/runtime identity and preserves the target payload without applying Desktop/Browser-specific interpretation.
- **ST1-03-AC05 [C] Engine validation.** Given an engine that does not support a named target's selector subtype, when it validates capabilities, then it rejects the script with a namespaced runtime detail code under `VALIDATION` or `TARGET_*`, rather than guessing a fallback.
- **ST1-03-AC06 [U] No executable target values.** Given a target definition containing a dynamically constructed command, arbitrary source code or a service-returned script target, when checked against the common restrictions, then the executable form is rejected.
- **ST1-03-AC07 [R] Publication stability.** Given a published script whose target map is changed without versioning and re-signing, when verified, then artifact hash validation fails; run-time target values are not silently overwritten.

**Tests and delivery evidence:** Target-reference validator, opaque preservation round-trip tests and three representative engine target fixture shapes; actual UI element resolution is deferred to individual Runtime specifications.

**Traceability:** CEC-001, CEC-012, CEC-HOST-001; Contract §§3.1–4.

---

# Capability Group B — Deterministic Execution Semantics

## ST1-04 — Enforce a closed technical-only condition grammar

**User story:** As an **Automation Author**, I want a small, predictable set of technical conditions, so that scripts can react to interface state and service outcomes without making business decisions inside Tracto.

**Functional scope:** Validate namespaced observable state predicates (per runtime), technical equality such as UI text/URL/exit code for interaction verification, equality on service outcome category and allowlisted action code, plus finite `all`/`any`/`not`. No domain calculation, arbitrary code, comparison operators, dynamic script destinations or business value selection.

**Acceptance criteria**

- **ST1-04-AC01 [U] Technical state allowed.** Given registered `desktop.elementExists`, `browser.elementVisible` or `terminal.processExited` predicates with correct arguments, when parsed for the matching engine, then validation succeeds; a predicate belonging to another engine fails.
- **ST1-04-AC02 [U] Technical equality.** Given `desktop.uiTextEquals` for an expected UI confirmation label, `browser.urlEquals` or `terminal.exitCodeEquals`, when evaluated against a supplied technical-state fixture, then the result is a deterministic boolean.
- **ST1-04-AC03 [U] Service outcome equality.** Given `api.outcomeCategoryEquals` against a supported service outcome and `api.actionCodeEquals` against an artifact-published allowlist, then validation/evaluation succeed; unlisted action codes fail safely.
- **ST1-04-AC04 [U] Finite boolean structure.** Given finite nested `all`/`any`/`not` expressions within configured maximum depth, then evaluation is deterministic; excessive depth, unsupported predicate or malformed operator fails validation.
- **ST1-04-AC05 [S] Domain comparisons rejected.** Given `response.amount > 10000`, customer grade comparisons, arithmetic, threshold expressions or arbitrary field-comparison operators, when submitted to any engine schema, then validation fails before execution.
- **ST1-04-AC06 [S] UI text is not business authorization.** Given a condition that compares UI text for an apparent domain approval or financial threshold, when reviewed under the contract's declared technical-purpose rules, then it is not an allowed domain authorization decision; only navigation/verification predicates are approved; structural bans are automated, and intent-based misuse is a publishing-review check, not a purely machine-testable claim. An author must request the decision from a custom service.
- **ST1-04-AC07 [S] No injected code.** Given C#, JavaScript, Python, `eval` or arbitrary user-defined functions inside a condition object, when validated, then parsing fails; no source code is compiled or executed.
- **ST1-04-AC08 [C] Capability advertisement.** Given different runtime technical predicates, when capabilities are advertised and validated, then unsupported predicate types receive `VALIDATION` with runtime-specific detail, not a permissive generic fallback.

**Tests and delivery evidence:** Closed grammar specification, condition parser and evaluator unit tests with stubbed technical observations, malicious expression fixtures and per-runtime predicate capability samples.

**Traceability:** CEC-002, CEC-011, CEC-HOST-009; Contract §§2, 5.

## ST1-05 — Execute bounded branches, iteration and safe value composition

**User story:** As a **Runtime Engineer**, I want predictable control steps with explicit limits, so that a script can navigate changing UI states without becoming a programming language or a domain workflow engine.

**Functional scope:** `control.branch` with forward-only `thenSteps`/`elseSteps`; `control.repeatBounded` over fixed contiguous technical-action blocks with literal policy-capped iterations; wait-or-fail preconditions; `SKIPPED` only for unselected branch arms; restricted `value.compose` for UI labels and approved file paths.

**Acceptance criteria**

- **ST1-05-AC01 [U] Branch selection.** Given an approved predicate resolving true/false, when `control.branch` evaluates it, then exactly the selected forward step list is scheduled and the unselected list is marked `SKIPPED` with reason `BRANCH_NOT_TAKEN`. Each unselected arm step includes standardized reasonCode=BRANCH_NOT_TAKEN.
- **ST1-05-AC02 [U] Forward-only graph.** Given a branch that references a previous step, dynamically supplied step ID, recursive branch or nonexistent step, when validated, then it is rejected with a control-flow validation error.
- **ST1-05-AC03 [U] Bounded repeat.** Given a fixed block and `maxIterations` within its policy cap (proposed baseline 100, policy-controlled), when executed in a deterministic fixture, then it performs no more than that count and stops earlier on an allowed technical exit predicate.
- **ST1-05-AC04 [U] Unbounded repeat denied.** Given a missing/zero/negative/out-of-policy `maxIterations`, a dynamic value derived from business data, or an unbounded while-loop, when validated, then it fails before running.
- **ST1-05-AC05 [U] Deadline enforcement.** Given a slow step inside a repeat, when the action or job deadline is reached, then iteration stops and emits `TIMED_OUT` or `UNKNOWN` as appropriate; the remaining steps are not silently executed.
- **ST1-05-AC06 [U] Precondition behavior.** Given a precondition initially false then becoming true within timeout, when executing, then the step waits and runs once; if false through timeout, it fails rather than `SKIPPED`. A completed action with a false postcondition returns FAILED with verification detail, never SUCCEEDED/SKIPPED; if an irreversible effect may have occurred and cannot be verified, the outcome becomes UNKNOWN instead of a blind retry.
- **ST1-05-AC07 [U] Restricted compose.** Given literals plus typed declared references intended for a UI text value or approved file path, when `value.compose` executes, then it returns a bounded string; secret values, dynamic endpoints, command text, script references or condition expressions cannot be constructed.
- **ST1-05-AC08 [R] Deterministic replay.** Given identical fixture state and immutable script version, when control execution is repeated, then step order, selected arm and result categories are identical, regardless of whether the engine is Desktop, Browser or Terminal.
- **ST1-05-AC09 [U] Postcondition enforcement.** Given a step whose action returns without the required technical postcondition becoming true, when verification finishes, then its result is FAILED with verification detail, never SUCCEEDED/SKIPPED; if irreversible commit remains uncertain, return UNKNOWN and prohibit blind replay.

**Tests and delivery evidence:** Control-flow validator and small in-memory reference evaluator, boundary tests for iteration caps and time budgets, branch trace fixtures; not a production Runtime scheduler.

**Traceability:** CEC-002, CEC-011; Contract §§4.1–5.

## ST1-06 — Invoke registered custom business services synchronously

**User story:** As an **Automation Author**, I want one common `service.invoke` action for approved custom business services, so that all Tracto Runtimes can hand off processing and use returned instructions without implementing business integrations themselves.

**Functional scope:** `serviceRef`, `operationRef`, `mode=SYNC`, typed `inputBindings`, optional `authSecretRef`, `idempotencyKeyRef` when required, `timeoutMs`, approved `actionCodeAllowlist`; logical endpoint registry; lightweight custom-service response convention (`serviceOutcome`, optional `businessOutcome`, `actionCode`, `data`, `businessJobId`, `correlationId`). The customer service or its adapter owns third-party API transformations; Tracto provides no generic HTTP composer.

**Acceptance criteria**

- **ST1-06-AC01 [C] Approved operation.** Given a registered `serviceRef`/`operationRef` with an approved route, method, TLS and auth policy, when invoked with valid bindings, then the request matches the registry contract and carries the run/correlation context. If the endpoint registry pins an optional response-schema version, an incompatible service response produces serviceOutcome=TECHNICAL_FAILURE, errorCategory=VALIDATION and service.responseSchemaMismatch; no response data is bound.
- **ST1-06-AC02 [S] Destination lockdown.** Given a raw URL, unknown operation, third-party API hostname, direct SQL connection or caller-generated endpoint, when validation occurs, then `service.invoke` fails `POLICY_DENIED` without network connection.
- **ST1-06-AC03 [U] Input binding.** Given typed declared inputs and previous outputs, when a request is assembled, then only referenced values and approved `value.compose` results are included; no script-side domain validation, arithmetic or transformations occur.
- **ST1-06-AC04 [C] Successful outcome.** Given the service returns `serviceOutcome=SUCCESS`, an approved action code and typed data, when normalized, then those fields are available to permitted steps and no business decision is inferred from HTTP 200 alone.
- **ST1-06-AC05 [C] Business rejection.** Given `serviceOutcome=BUSINESS_REJECTION` and `businessOutcome=REJECTED`, when the Runtime receives it, then the result is not automatically a technical run failure; a subsequent approved UI/CLI update may still result in run `SUCCEEDED` with `businessOutcome=REJECTED`.
- **ST1-06-AC06 [C] Failure/uncertainty.** Given `TECHNICAL_FAILURE`, `TRANSIENT_FAILURE` or `UNKNOWN_OUTCOME`, when normalized, then the outcome is distinguished from common errorCategory and handled by the declared retry/failure policy; an unproven effectful request is not retried blindly.
- **ST1-06-AC07 [U] Retry and idempotency.** Given an effectful service operation, when a transient transport interruption occurs, then retry is permitted only where a safe idempotency key/contract establishes repeat safety or noncommitment; otherwise report unknown completion.
- **ST1-06-AC08 [S] Response boundaries.** Given malformed response shape, unrecognized `actionCode`, service-returned script reference or executable content, when decoded, then it is rejected or handled as validation/technical failure without executing any dynamic action.
- **ST1-06-AC09 [M] Customer-owned adapter.** Given a legacy service returning a different internal format, when a customer-owned facade adapts it to the lightweight Tracto response convention, then Tracto still invokes only that registered custom service; it does not add per-provider mappings to the Runtime.

**Tests and delivery evidence:** `service.invoke` schema, registry mock, fake service responses for all five `serviceOutcome` values (including SUCCESS), policy-denial tests and retry/idempotency tests. This story does not implement actual custom business services.

**Traceability:** CEC-005, CEC-013, CEC-014, CEC-HOST-004, CEC-HOST-009; Contract §§6–6.2, 10.1.

## ST1-07 — Support asynchronous business handoff and safe continuation events

**User story:** As an **Integration Engineer**, I want an asynchronous custom-service call to finish the current RPA run cleanly and provide a secure completion event, so that long-running business processing never holds a bot session or allows an external service to select arbitrary scripts.

**Functional scope:** `service.invoke` with `mode=ASYNC`, correlation and idempotency keys, accepted handoff result and `businessJobId`, terminal run state `HANDED_OFF`, authenticated external callback shape, duplicate-event protection and Orchestrator-owned continuation mapping. Stage 1 specifies/tests the interface only; it does not build the production callback router.

**Acceptance criteria**

- **ST1-07-AC01 [M] Accepted handoff.** Given an approved async operation returning an accepted acknowledgement and `businessJobId`, when normalized, then the current run transitions to `HANDED_OFF`, releases its worker lease, and retains the correlation to the external job.
- **ST1-07-AC02 [U] Missing correlation.** Given an async acknowledgement lacking a required identifier or mismatched correlation ID, when validated, then the handoff is not reported as successful and a defined technical/unknown result is emitted.
- **ST1-07-AC03 [C] Callback schema.** Given a completion event containing `eventId`, `eventType`, `businessJobId`, `correlationId`, `resultRef` and signature/key/timestamp/nonce metadata, when schema-validated, then it is accepted as structurally valid without including any Tracto script identifier.
- **ST1-07-AC04 [S] Authentication/replay protection.** Given an invalid signature, stale timestamp, reused nonce or untrusted sender, when the mock callback boundary processes it, then it rejects the event; `authenticated: true` as a self-assertion is never accepted.
- **ST1-07-AC05 [M] Duplicate delivery.** Given the same `eventId` received twice, when processed by the mock handler, then at most one internally configured continuation request is emitted and duplicates are logged without creating another run.
- **ST1-07-AC06 [S] External script selection denied.** Given a business event containing `continuationScriptRef`, `automationId`, command text or an arbitrary action payload, when validated, then those fields cannot control a Tracto execution; the continuation target is resolved from Orchestrator-owned configuration.
- **ST1-07-AC07 [C] Business state ownership.** Given a custom service that completes independently after the first run ends, then its authoritative processing state remains external; Tracto stores only run/handoff status and approved correlation/result references.
- **ST1-07-AC08 [M]** Static technical continuation. Given an Orchestrator-owned on-SUCCEEDED continuation rule tied to an approved source automation/version, when the source run succeeds, then at most one configured next run is requested per source run and rule ID; the target and permitted input bindings are internal, not supplied by a custom service or script.

**Tests and delivery evidence:** Async handoff and callback JSON schemas, mock callback authentication/dedup fixtures, service-to-Orchestrator correlation examples; do not implement a general workflow engine or business compensation.

**Traceability:** CEC-004, CEC-005, CEC-008, CEC-HOST-005; Contract §§7, 9.

## ST1-08 — Standardize run states, step outcomes and error classification

**User story:** As an **Operations Engineer**, I want one stable outcome and error vocabulary across all workers, so that monitoring and recovery can tell a technical failure apart from a valid business rejection or an uncertain completion.

**Functional scope:** Normative run states `CREATED`, `QUEUED`, `LEASED`, `RUNNING`, terminal `SUCCEEDED`, `HANDED_OFF`, `FAILED`, `CANCELLED`, `TIMED_OUT`, `UNKNOWN`; step states `PENDING`, `RUNNING`, terminal `SUCCEEDED`, `FAILED`, `SKIPPED`, `TIMED_OUT`, `CANCELLED`, `UNKNOWN`; `NEEDS_ATTENTION` flag; common `errorCategory`, namespaced detail codes; structured results and transitions.

**Acceptance criteria**

- **ST1-08-AC01 [U] Legal run transitions.** Given each allowed transition in the published state graph, when submitted, then it is accepted; an illegal transition (for example `SUCCEEDED → RUNNING`) is rejected without mutating the stored result. Explicitly test cancellation from CREATED/QUEUED, proof-based requeue after an unstarted LEASED attempt, RUNNING→UNKNOWN, and rejection of unsafe requeue after ambiguous lease expiry; terminal attempt states never reopen.
- **ST1-08-AC02 [U] Stable vocabulary.** Given a worker reporting `COMPLETED` or `SUCCESS` instead of `SUCCEEDED`, when normalized/validated, then the invalid value is rejected; result objects use only normative states.
- **ST1-08-AC03 [U] Step outcomes.** Given an untaken branch, then its step is `SKIPPED`; false preconditions that time out produce failed/timed-out results, not `SKIPPED`.
- **ST1-08-AC04 [C] Domain rejection.** Given a definite business rejection followed by successful prescribed UI interaction, when the run finalizes, then `state=SUCCEEDED` and `businessOutcome=REJECTED` are both present.
- **ST1-08-AC05 [C] Unknown completion.** Given an interrupted irreversible operation whose commit status cannot be proved, when finalized, then the run state is `UNKNOWN` with `unknownReason` and `needsAttention=true`; it is neither retried nor falsely marked `FAILED`.
- **ST1-08-AC06 [U] Error taxonomy.** Given each common category (`VALIDATION`, `POLICY_DENIED`, `TARGET_NOT_FOUND`, `TARGET_AMBIGUOUS`, `TIMEOUT`, `SESSION_UNAVAILABLE`, `ENVIRONMENT`, `TRANSPORT`, `AUTHENTICATION`, `CANCELLED`, `INTERNAL`), when serialized/deserialized, then the category is preserved and optional `desktop.*`, `browser.*`, `terminal.*` detail codes are namespaced.
- **ST1-08-AC07 [U] Service outcomes separate.** Given `serviceOutcome=TRANSIENT_FAILURE` or `BUSINESS_REJECTION`, then it is stored as a service result; it is not silently substituted for the common runtime `errorCategory` enum.
- **ST1-08-AC08 [U] Complete result envelope.** Given a terminal run, when results serialize, then they include run/attempt/worker identity, exact script version, correlation, timestamps, step results, errors (where applicable), evidence references and optional business job ID; secret payloads are absent. Each stepResult includes observedPostcondition and attemptCount; the run includes lastHeartbeatAt (nullable if none observed), and durationMs uses monotonic timing rather than timestamp subtraction.

**Tests and delivery evidence:** State-transition table, enum/serializer tests, exhaustive error-category fixtures, valid and invalid final results for all terminal statuses.

**Traceability:** CEC-005, CEC-009, CEC-016; Contract §§6.2–7.

---

# Capability Group C — Orchestrator / Worker Protocol

## ST1-09 — Register workers, capabilities and interactive capacity

**User story:** As an **Orchestrator Engineer**, I want independent workers to register with compatible protocol versions and honest capability/session information, so that jobs are offered only to workers able to execute them without foreground-input collisions.

**Functional scope:** `register/capability` message fields: worker ID, runtimeType, workerVersion, protocolVersion, supported schema majors, capability digest, host/session class, `interactiveSessionId` where applicable and health; worker identity/compatibility validation; logical scheduling constraints. Does not implement Windows desktop session creation.

**Acceptance criteria**

- **ST1-09-AC01 [C] Valid registration.** Given a Desktop, Browser or Terminal worker with complete supported registration fields, when the mock Orchestrator accepts it, then a registration acknowledgement exposes compatible contract capabilities.
- **ST1-09-AC02 [R] Protocol window.** Given the current protocol or immediately preceding supported protocol major, when registering, then a compatible worker is accepted; older/unknown majors fail with an explicit incompatibility result.
- **ST1-09-AC03 [U]** Identity and capability mismatch. Given a worker declaring runtimeType=browser but a capability list containing terminal-only action namespaces, when validated, then registration is denied or quarantined with a deterministic mismatch diagnostic; a digest is a reported integrity/identity hint, not proof of which engine produced capabilities.
- **ST1-09-AC04 [M] Interactive exclusivity.** Given two headed workers registering the same nonempty `interactiveSessionId`, when both request work, then the mock grants at most one concurrent foreground-input lease to that session.
- **ST1-09-AC05 [M] Headless coexistence.** Given a headless Browser or Terminal worker with no interactive session requirement, when another unrelated interactive worker is busy, then the headless worker remains eligible subject to host capacity and policy.
- **ST1-09-AC06 [C] Missing session identity.** Given a worker requiring foreground input but omitting `interactiveSessionId`, when registering/pulling, then it cannot receive an interactive lease.
- **ST1-09-AC07 [C] Unsupported scripts.** Given a worker lacking the required schema major or action capability, when a job is matched, then the mock withholds it and records a capability mismatch instead of dispatching a doomed run.
- **ST1-09-AC08 [S] Authenticated identity.** Given an unauthenticated registration or worker identity mismatch, when received, then it is rejected and no registration is created; secrets are never carried as plain registration fields.

**Tests and delivery evidence:** Registration message schema, compatibility matrix and capacity-leasing mock tests across three runtime types; interactive Windows session provisioning remains in deployment PoCs.

**Traceability:** CEC-003, CEC-007, CEC-015, CEC-HOST-003; Contract §8.

## ST1-10 — Pull jobs, acquire leases, heartbeat and fence attempts

**User story:** As a **Runtime Engineer**, I want one outbound job-pull and lease protocol with durable attempt identity, so that independently delivered workers execute assigned jobs without needing inbound firewall ports or replaying operations after a network interruption.

**Functional scope:** `pullJob`, `leaseGranted`, `leaseAccepted`, `heartbeat`, `status/finalResult`, `reconcile` message shapes; worker-initiated outbound transport over network or localhost; `runId`, `attemptId`, `leaseId`, deadlines, heartbeat counters/journal checkpoints; at most one concurrent run per worker.

**Acceptance criteria**

- **ST1-10-AC01 [M] Outbound pull.** Given an available registered worker, when it calls `pullJob`, then the mock responds with no job or one eligible immutable leased job; the Orchestrator never needs an inbound connection to the worker.
- **ST1-10-AC02 [C] Lease identity.** Given a lease grant, when acknowledged, then `runId`, `attemptId`, `leaseId`, automation digest and deadline are preserved across every subsequent status and result message. leaseGranted includes typed inputRefs; the worker validates their declared types and resolves only authorized values before beginning an action. pullJob.lastSeenAttemptId conveys the last durably seen attempt and is honored during reconciliation.
- **ST1-10-AC03 [M] Worker concurrency.** Given one active lease for a worker, when a second job is offered, then it is not accepted until the first reaches a terminal state and the worker becomes available.
- **ST1-10-AC04 [M] Heartbeats.** Given an active attempt with monotonically increasing sequence, when heartbeats arrive, then lease health/progress updates; stale or duplicated heartbeat messages cannot move an attempt backwards.
- **ST1-10-AC05 [M] Lost heartbeat.** Given a heartbeat delay exceeding lease policy, when the lease expires, then the mock flags the attempt for reconciliation; it never interprets expiry as proof the GUI or API side effect failed.
- **ST1-10-AC06 [M] Attempt fencing.** Given an old attempt reporting after a replacement attempt has been assigned, when its final result arrives, then stale/fenced results cannot overwrite authoritative newer state or trigger duplicate irreversible actions.
- **ST1-10-AC07 [R] Transport portability.** Given the same logical message fixtures against localhost and a remote mock endpoint, when tested, then the payloads and semantics match without runtime-specific protocol variants.
- **ST1-10-AC08 [M] Journal recovery.** Given a worker reconnecting with its last run/attempt and status sequence, when it calls `reconcile`, then the mock compares persisted identity and emits a safe resolution, not a new blind execution assignment.

**Tests and delivery evidence:** Mock message handlers, lease state recorder, heartbeat/sequence fixtures and fault-injection scenarios CF-01, CF-03, CF-04 and CF-05. This story defines protocol behavior, not production cluster scaling.

**Traceability:** CEC-003, CEC-004, CEC-015, CEC-HOST-002, CEC-HOST-003; Contract §8.

## ST1-11 — Reconcile interrupted work and honor safe cancellation

**User story:** As an **Operations Engineer**, I want interruption, cancellation and restart behavior to produce a truthful execution outcome, so that an uncertain UI save, API submission or shell command is never silently duplicated.

**Functional scope:** Cancel request through outbound response to worker poll/heartbeat, bounded cleanup acknowledgment, logical attempt reconciliation, idempotency-aware retry decision, `UNKNOWN` results with attention flags and journal/step evidence references. Runtime-specific input/process cleanup is delegated to engine specifications.

**Acceptance criteria**

- **ST1-11-AC01 [M] Cancellation request.** Given a currently leased run, when a cancel request is delivered in the next worker heartbeat/poll response, then it is associated with the exact run and attempt and does not cancel unrelated runs.
- **ST1-11-AC02 [U] Safe-boundary behavior.** Given a cancellable wait or repeat block, when cancellation is observed, then the contract prescribes bounded cleanup and a `CANCELLED` result only when completion status is definitive; engine-owned resources are released by its implementation.
- **ST1-11-AC03 [M] Uncertain side effect.** Given cancellation or a connection drop after an irreversible action whose result cannot be proved, when reconciling, then the run becomes `UNKNOWN` with reason/attention information; no blind retry of the action occurs.
- **ST1-11-AC04 [M] Idempotent retry.** Given an `IDEMPOTENT` step and stable idempotency key, when the mock simulates a safely retryable transient interruption, then a bounded retry is allowed without changing the logical operation identity.
- **ST1-11-AC05 [M] Unsafe retry denied.** Given a non-idempotent service POST or UI Submit without a proved safe replay path, when a timeout occurs, then the test asserts zero automatic replays and an unknown/attention result.
- **ST1-11-AC06 [M] Orchestrator restart.** Given a restart mid-run and subsequent worker reconciliation with preserved journal/sequence, when service resumes, then state is restored consistently or marked unknown; stale completion events cannot finalize a different attempt.
- **ST1-11-AC07 [U]** Bounded technical recovery. Given an explicitly configured onFailure technical recovery action that exceeds its permitted timeout, when executed, then recovery stops with FAILED/TIMED_OUT or UNKNOWN where side effects are uncertain; no indefinite lease hold or new generic pause-step type is introduced.
- **ST1-11-AC08 [C] Evidence of outcome.** Given terminal `CANCELLED`, `FAILED`, `TIMED_OUT` or `UNKNOWN`, when the result is emitted, then it includes exact attempt identity and step-level reason, enabling operator review without disclosing secrets.

**Tests and delivery evidence:** Failure-injection fixture suite with orphaned leases, late completions, duplicate messages, cancellation under wait, ambiguous commit and safe retry; production Windows/browser/shell cleanup deferred to Runtime tracks.

**Traceability:** CEC-004, CEC-009, CEC-HOST-002, CEC-HOST-007; Contract §§7–8, 11.

---

# Capability Group D — Security and Observability

## ST1-12 — Enforce service-only integration, secret references and policy boundaries

**User story:** As a **Security Engineer**, I want a common trust and secret-reference contract for every Runtime, so that users can safely invoke permitted custom services without Tracto becoming a general integration platform or exposing customer credentials.

**Functional scope:** Script signature/policy enforcement, allowlisted `serviceRef`/`operationRef`, explicit prohibition of raw third-party API/database integration, approved `secretRef` syntax and Windows-side resolution boundary, minimal use-time secret exposure, standardized denial/redaction behavior. This is a shared behavior contract, not a new secrets product or cross-platform vault implementation.

**Acceptance criteria**

- **ST1-12-AC01 [S] Service boundary.** Given a script requesting direct third-party REST, SQL or custom raw HTTP destination, when policy validation runs, then the step is rejected with `POLICY_DENIED`; registered custom business-service invocation is accepted.
- **ST1-12-AC02 [S] Browser exception clarified.** Given permitted Playwright navigation to a target website, then page-generated network requests remain ordinary browser interaction; a script-supplied direct third-party HTTP integration action is still denied.
- **ST1-12-AC03 [S] Terminal escape blocked.** Given a Terminal action attempting to use `curl`, PowerShell web cmdlets, database tooling or executable script text for unregistered business integration, when checked against approved command policy, then it is denied. Policy authors must explicitly define allowable target interactions rather than relying solely on command names.
- **ST1-12-AC04 [U] Stable secret references.** Given an approved `secretRef`, when serialized into the script/job message, then only the reference identifier appears; literal secret values in scripts or Orchestrator payloads fail validation or are rejected.
- **ST1-12-AC05 [S] Windows-side resolution.** Given an authorized service call requiring a secret, when the Windows-side permitted Runtime provider resolves it at use time, then only that runtime-facing operation sees the value; the Linux Orchestrator and its stored artifacts retain only `secretRef`.
- **ST1-12-AC06 [S] Unauthorized secret.** Given a secret reference not approved for the worker/service operation, when resolving, then access is denied with a stable policy/auth category and no secret-containing diagnostic.
- **ST1-12-AC07 [S] Cross-platform honesty.** Given a worker inside WSL/Linux, when it requests `CurrentUser` DPAPI directly without a documented approved Windows mediation path, then the shared contract does not assume this is possible; the configuration fails capability/security validation.
- **ST1-12-AC08 [S] No dynamic executable data.** Given a service response containing code, shell commands, script IDs, endpoint URLs or permission changes, when bound, then these cannot be executed or installed through common contract actions.
- **ST1-12-AC09 [R] Independent compliance.** Given three different Runtime implementations, when tested against shared secret and service-boundary fixtures, then each must pass the same security behaviors even if it uses a different internal .NET library.

**Tests and delivery evidence:** Security-policy test matrix, mock Windows-side secret-provider interface (not a real vault), forbidden-network fixtures, serialization/redaction tests, security conformance checklist. Business-service container secrets remain external to Tracto.

**Traceability:** CEC-001, CEC-002, CEC-006, CEC-013, CEC-014, CEC-HOST-004, CEC-HOST-006, CEC-HOST-009; Contract §§2, 6, 10.

## ST1-13 — Emit structured telemetry and safe evidence references

**User story:** As a **Support Engineer**, I want consistent, privacy-preserving telemetry across all three Runtimes, so that I can correlate an automation step with an Orchestrator attempt and an external business job without examining sensitive business payloads.

**Functional scope:** Standard telemetry event model, run/attempt/step/correlation IDs, worker/runtime/schema version, event sequence, error category, logical serviceRef/operationRef, evidence references, optional businessJobId and local/exportable buffering shape for isolated mode; retention is policy-driven. It does not implement a commercial monitoring stack.

**Acceptance criteria**

- **ST1-13-AC01 [U] Mandatory event fields.** Given a worker event, when serialized, then `timeUtc`, `eventType`, `workerId`, `workerVersion`, `runtimeType`, `runId`/`attemptId` where relevant, and event sequence are present and correctly typed.
- **ST1-13-AC02 [U] Step/lease identifiers.** Given a step or lease event, then the respective `stepId`, `leaseId` and error category (if applicable) are present; they match the run and attempt reported in the result envelope.
- **ST1-13-AC03 [C] Cross-system correlation.** Given an async service handoff followed by a valid callback, when mock logs are queried by `correlationId` and `businessJobId`, then the originating run, handoff and continuation request can be linked without logging the business payload.
- **ST1-13-AC04 [S] No sensitive leakage.** Given secrets in input references, authentication headers, cookies, screenshot metadata or terminal output fixtures, when events/evidence manifests are emitted, then secret/plaintext values are absent and approved masked/redacted forms are used.
- **ST1-13-AC05 [U] Evidence metadata.** Given a screenshot, browser trace or terminal transcript reference, when recorded, then only an authorized `evidenceRef`, step/run attribution, sensitivity classification and retention policy reference are emitted; capture internals belong to the engine.
- **ST1-13-AC06 [M] Ordered events.** Given duplicated or out-of-order event deliveries, when ingested by the mock, then event sequence and attempt identity permit deduplication/reconciliation; a later stale message cannot overwrite final status.
- **ST1-13-AC07 [M] Isolated export model.** Given an isolated worker with no external monitoring connectivity, when local telemetry is buffered under configured limits and later exported by an authorized operation, then the export preserves correlation and policy redaction; the contract does not require a cloud collector.
- **ST1-13-AC08 [U] Unknown/technical failures.** Given a terminal `UNKNOWN`, `FAILED`, or `CANCELLED` state, when telemetry is emitted, then it includes stable outcome/error taxonomy and pointers to available sanitized evidence, not a false success message.

**Tests and delivery evidence:** Telemetry schema, redaction fixtures, event dedup tests, export format example and per-runtime conformance assertions. Central dashboard design remains an Orchestrator/operations concern.

**Traceability:** CEC-006, CEC-016, CEC-HOST-008; Contract §§7–8.1, 10; isolated local telemetry: Architecture v1.2A §11.3.

---

# Capability Group E — Certification and Contract Governance

## ST1-14 — Certify independent Runtime implementations with a mock Orchestrator

**User story:** As a **QA Automation Engineer**, I want a reusable mock Orchestrator and deterministic conformance suite, so that each Runtime team can verify contract compliance independently without waiting for production Orchestrator development or testing against another Runtime.

**Functional scope:** Reference stub for register, pull, lease, heartbeat, status, cancel and reconcile; fake custom service with declared outcomes; deterministic fault injection; fixture pack CF-01 through CF-10 plus boundary and security extensions; machine-readable pass/fail report per worker version. Not a fourth deployable product component.

**Acceptance criteria**

- **ST1-14-AC01 [M] Job and lease happy path.** Given a compatible registered worker, when the mock returns a job and receives two heartbeats plus a terminal result, then it records one execution and one terminal result (`CF-01`).
- **ST1-14-AC02 [M] Incompatible artifact.** Given wrong runtimeType or unsupported schema, when assigned to a worker fixture, then no side effect occurs and a validation/incompatibility result is produced (`CF-02`).
- **ST1-14-AC03 [M] Uncertain lease expiry.** Given a lease expiry after a possibly committed side effect, when the worker reconnects, then it reconciles to a proved result or `UNKNOWN`; the mock records zero blind repeats (`CF-03`).
- **ST1-14-AC04 [M] Restart and duplicates.** Given mock Orchestrator restart, delayed heartbeat, duplicate lease/status or duplicate callback, when recovery occurs, then run/attempt fencing, message deduplication and continuation uniqueness hold (`CF-04`, `CF-05`).
- **ST1-14-AC05 [M] Service outcomes.** Given accepted service success, definite business rejection, transient failure and ambiguous POST fixtures, when processed, then returned result states follow the contract and unsafe replays are blocked (`CF-06`, `CF-07`). Include SUCCESS among the five normalized service outcomes and a mismatched response-schema-version negative fixture.
- **ST1-14-AC06 [S] Security violation.** Given plaintext secrets, unapproved `serviceRef`, arbitrary script selection or domain expression, when executed against validators, then they are rejected or redacted, with no policy bypass (`CF-08`, `CF-10` plus additional fixtures).
- **ST1-14-AC07 [M] Safe cancel and disconnection.** Given cancellation during active work and a broken worker channel, when reconciled, then cleanup acknowledgment and terminal/unknown outcome follow the contract (`CF-09`).
- **ST1-14-AC08 [R] Per-runtime independence.** Given separate Desktop, Browser and Terminal adapters against the same fixture-version contract, when CI runs them independently, then each produces its own conformance report; a Browser/Terminal release does not require Desktop to be built or tested first.
- **ST1-14-AC09 [U] Deterministic repeatability.** Given the same seeded fixture inputs and fault schedule, when repeated, then expected event ordering assertions and final result categories remain stable; wall-clock timing dependence is simulated or controlled.

**Tests and delivery evidence:** Mock Orchestrator package, fake custom-service package, fixture JSON files, CI runner instructions and reports with exact contract/protocol versions. Do not implement production dispatch, persistence or session provisioning here.

**Traceability:** CEC-003, CEC-004, CEC-007, CEC-009 and all CEC-HOST requirements; Contract §§11–12; fake registered custom-service fixture is Stage 1 test infrastructure.

## ST1-15 — Release a compatible, owned Common Execution Contract package

**User story:** As the **Lead Architect**, I want one versioned, owned and backward-compatible contract package with reproducible conformance evidence, so that three independent Runtime teams and the Orchestrator team can evolve without silently breaking one another.

**Functional scope:** Publish canonical schemas/DTO definitions, enums, message examples, compatibility matrix, test fixtures and change notes; maintain a contract owner and change process; current/previous major artifact compatibility and protocol N/N−1 rule. Do not impose synchronized releases or create a broad governance system.

**Acceptance criteria**

- **ST1-15-AC01 [R] Canonical package.** Given an approved contract version, when packaged, then it contains the authoritative artifact schema, step/ref/condition schemas, `service.invoke`, state/error enums, worker/callback message definitions and the conformance fixture version.
- **ST1-15-AC02 [R] Consumer examples.** Given each runtimeType, when sample scripts and register/result messages are opened, then they are internally consistent with published schemas and can be validated using supplied commands/tests.
- **ST1-15-AC03 [R] Compatibility policy.** Given current and previous major script schema fixtures, when compatibility tests run, then both are supported where certified; older major reports republish required. Worker protocol follows published current/previous-major compatibility rules.
- **ST1-15-AC04 [R] Breaking-change detection.** Given a change removing/renaming a required field or changing a state semantic, when contract diff validation runs, then it is flagged as breaking and cannot be released as a silent compatible patch.
- **ST1-15-AC05 [C] Named accountability.** Given the release manifest, then it identifies a contract-owning role/person, shared host-supervisor owner and mock/fixture maintainer; if individuals are not yet assigned, release readiness remains open rather than inventing names.
- **ST1-15-AC06 [R] Independent conformance.** Given CI reports from each team, when evaluating release eligibility, then each worker version states which schema/protocol/fixture versions it passes; failure in one team does not block unrelated team implementation but does block a false claim that that worker conforms.
- **ST1-15-AC07 [S] Frozen architecture.** Given a proposed contract change that expands Runtime ownership into business processing, direct third-party integration or new product components, when reviewed, then it is rejected from Stage 1 unless the user separately provides explicit architectural approval.
- **ST1-15-AC08 [C] No implementation lock-in.** Given independently implemented .NET Runtime hosts, when tested against the package, then they may use different internal libraries while preserving identical shared contract behavior and security requirements.

**Tests and delivery evidence:** Tagged contract release bundle, changelog, contract-diff/compatibility reports, test fixture version manifest and named owner fields for completion before development handoff.

**Traceability:** CEC-007, CEC-010, CEC-015, CEC-016; Contract §§3, 8.1, 11–12.

---

## 4. Acceptance-test coverage and traceability summary

| Contract area | Primary stories | Relevant source IDs | Test focus |
|---|---|---|---|
| Trust and artifact publication | ST1-01, ST1-15 | CEC-001, CEC-010, CEC-HOST-001 | Valid/invalid schema, sign/hash, N/N−1 |
| Step/ref/target model | ST1-02, ST1-03 | CEC-012 | Types, forward refs, opaque targets |
| Business-rule exclusion | ST1-04, ST1-05, ST1-12 | CEC-002, CEC-011, CEC-HOST-009 | Grammar allowlist, bounded flow, no code |
| Registered custom services only | ST1-06, ST1-12 | CEC-013, CEC-014, CEC-HOST-004 | service.invoke, raw URL denial, result convention |
| Async handoff and continuation | ST1-07 | CEC-005, CEC-008, CEC-HOST-005 | HANDED_OFF, event signature, dedup, no script selection |
| State, errors and reporting | ST1-08, ST1-13 | CEC-005, CEC-009, CEC-016, CEC-HOST-008 | State transitions, error taxonomy, telemetry |
| Worker lifecycle protocol | ST1-09, ST1-10, ST1-11 | CEC-003, CEC-004, CEC-009, CEC-015, CEC-HOST-002/003/007 | Registration, pull, leases, cancellation, UNKNOWN |
| Secret isolation | ST1-12 | CEC-006, CEC-HOST-006 | secretRef, use-time Windows-side resolution |
| Conformance | ST1-14, ST1-15 | CEC-007 and CF-01..CF-10 | Mock Orchestrator, fixtures, independent CI |

## 5. Stage 1 exit gate and handoff

**Functional completion requires:** all 15 stories accepted; canonical schemas and JSON examples published; stable errors and state transitions; contract CI green; mock Orchestrator and fake service fixtures available; negative security tests passing; current/previous compatibility cases documented; one reference .NET adapter proves that the shared interface parses, registers and reports against the mock; each Desktop/Browser/Terminal adapter is certified separately at its own Runtime-track gate, not as a Stage 1 dependency; open owners assigned; traceability report complete.

**Not required for Stage 1 exit:** Windows UIA implementation, Playwright runtime engine, Terminal command engine, production Orchestrator scheduling store, real vault deployment, Windows session provisioning PoC completion, Designer or AI recorder. Those remain separate tracks. **Deployment PoC-01 to POC-10 remain a parallel architecture conditional-freeze gate**, not an invented dependency for authoring this contract.

**Boundary rule for change requests:** A request that merely completes schema semantics belongs here. A request that adds business workflow processing, direct external integrations, arbitrary scripting or a new product component is an architectural change and must not be adopted without explicit user approval.
