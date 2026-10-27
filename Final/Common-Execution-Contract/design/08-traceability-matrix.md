# 08 — Story and Acceptance-Criterion Traceability Matrix

AC IDs are reproduced exactly as in TRACTO-ST1-US-001. Duplicates or ambiguities are flagged without renumbering.

| Story | AC ID | Test Type | Work Package | Module Under Test | Notes / Flags |
|---|---|---|---|---|---|
| ST1-01 | ST1-01-AC01 | U | WP-3 | EnvelopeValidator | Positive: all required fields, unique automationId |
| ST1-01 | ST1-01-AC02 | U | WP-3 | EnvelopeValidator | Negative: each required field removed/wrong-typed in turn; automationId as path/URL rejected |
| ST1-01 | ST1-01-AC03 | U | WP-3 | EnvelopeValidator | Mixed-runtime action rejection |
| ST1-01 | ST1-01-AC04 | S | WP-3 | IArtifactTrustVerifier + EnvelopeValidator | Untrusted/tampered/invalid-digest/offline-import |
| ST1-01 | ST1-01-AC05 | R | WP-3 | EnvelopeValidator + schema-version-catalogue | N/N-1 compatibility; older major → republish diagnostic |
| ST1-01 | ST1-01-AC06 | U | WP-3 | EnvelopeValidator + AttemptJournal | Artifact hash verification after modification |
| ST1-01 | ST1-01-AC07 | U | WP-3 | EnvelopeValidator | Output classification present and approved; plaintext secret rejected |
| ST1-01 | ST1-01-AC08 | S | WP-3 | EnvelopeValidator | Unknown/unapproved policyRef → POLICY_DENIED or VALIDATION |
| ST1-02 | ST1-02-AC01 | U | WP-3 | StepValidator | Valid normalized step round-trip |
| ST1-02 | ST1-02-AC02 | U | WP-3 | StepValidator | Duplicate stepId, unknown namespace, cross-runtime step |
| ST1-02 | ST1-02-AC03 | U | WP-3 | RefResolver | Typed refs from inputs and previous outputs; outputBinding into vars; C1 rules |
| ST1-02 | ST1-02-AC04 | U | WP-3 | RefResolver | Undefined var, missing output, forward ref, cyclic ref, type mismatch |
| ST1-02 | ST1-02-AC05 | U | WP-3 | RefResolver | `secrets.*` ref → opaque; no plaintext in output binding |
| ST1-02 | ST1-02-AC06 | U | WP-3 | StepValidator | timeoutMs > job deadline, negative, invalid retryClass |
| ST1-02 | ST1-02-AC07 | U | WP-3 | StepValidator | Valid bounded onFailure; dynamic/domain/unbounded recovery rejected |
| ST1-02 | ST1-02-AC08 | U | WP-3 | StepValidator / ConditionGrammarParser | Arithmetic/dynamic expressions remain inert literals or are rejected |
| ST1-02 | ST1-02-AC09 | U | WP-3 | RefResolver + StepValidator | vars binding lifecycle; C1 rules for uninitialized/forward/plaintext |
| ST1-03 | ST1-03-AC01 | U | WP-3 | StepValidator / TargetMap | Named target resolves to definition |
| ST1-03 | ST1-03-AC02 | U | WP-3 | StepValidator | Undefined selectorRef, empty name, duplicate name |
| ST1-03 | ST1-03-AC03 | U | WP-3 | EnvelopeValidator | Cross-runtime target mismatch rejected |
| ST1-03 | ST1-03-AC04 | C | WP-8 | Mock Orchestrator (opaque payload preservation) | Orchestrator does not parse runtime-specific target content |
| ST1-03 | ST1-03-AC05 | C | WP-8 | Reference Adapter / Mock | Unsupported selector subtype → VALIDATION or TARGET_* |
| ST1-03 | ST1-03-AC06 | U | WP-3 | StepValidator | Executable target value rejected |
| ST1-03 | ST1-03-AC07 | R | WP-3 | ArtifactTrustVerifier | Target map change without re-signing → hash fails |
| ST1-04 | ST1-04-AC01 | U | WP-4a | ConditionGrammarParser | Allowed technical-state predicates per runtime; cross-runtime predicate fails |
| ST1-04 | ST1-04-AC02 | U | WP-4a | ConditionGrammarParser + stub | Technical equality predicates evaluate deterministically |
| ST1-04 | ST1-04-AC03 | U | WP-4a | ConditionGrammarParser | `api.outcomeCategoryEquals` against all 5 outcomes; `api.actionCodeEquals` against allowlist |
| ST1-04 | ST1-04-AC04 | U | WP-4a | ConditionGrammarParser | Finite all/any/not within depth cap; excessive depth fails |
| ST1-04 | ST1-04-AC05 | S | WP-4a | ConditionGrammarParser | Domain arithmetic/comparison rejected for all runtimeTypes |
| ST1-04 | ST1-04-AC06 | S | WP-4a + Human review | ConditionGrammarParser (structural) + Publishing review checklist | **Manual review required**: intent-based misuse cannot be fully automated; structural ban is automated |
| ST1-04 | ST1-04-AC07 | S | WP-4a | ConditionGrammarParser | C#/JS/Python/eval in condition → parse fails |
| ST1-04 | ST1-04-AC08 | C | WP-8 | Mock Orchestrator capability + Reference Adapter | Unsupported predicate → VALIDATION with runtime detail |
| ST1-05 | ST1-05-AC01 | U | WP-4b | ControlFlowValidator + reference evaluator | Branch selection; unselected arm → SKIPPED/BRANCH_NOT_TAKEN; each step includes reasonCode |
| ST1-05 | ST1-05-AC02 | U | WP-4b | ControlFlowValidator | Backward/dynamic/recursive/nonexistent step ref rejected |
| ST1-05 | ST1-05-AC03 | U | WP-4b | ControlFlowValidator + FakeTimeProvider | Fixed block, maxIterations cap, early technical exit |
| ST1-05 | ST1-05-AC04 | U | WP-4b | ControlFlowValidator | Missing/zero/negative/out-of-policy/dynamic maxIterations rejected |
| ST1-05 | ST1-05-AC05 | U | WP-4b | ControlFlowValidator + FakeTimeProvider | Deadline enforcement during repeat → TIMED_OUT or UNKNOWN (C3) |
| ST1-05 | ST1-05-AC06 | U | WP-4b | StepOutcomeNormalizer | False precondition: wait → fail on timeout; false postcondition → FAILED or UNKNOWN if irreversible |
| ST1-05 | ST1-05-AC07 | U | WP-4b | StepValidator | value.compose: literals + typed refs only; secret/command/endpoint rejected |
| ST1-05 | ST1-05-AC08 | R | WP-9 | Conformance runner + FakeTimeProvider | Deterministic replay with same fixture and script version |
| ST1-05 | ST1-05-AC09 | U | WP-4b | StepOutcomeNormalizer | False postcondition after irreversible action → UNKNOWN; no SUCCEEDED/SKIPPED |
| ST1-06 | ST1-06-AC01 | C | WP-6 | ServiceInvokeValidator + Mock BusinessService | Approved operation with registry contract; response schema version mismatch case |
| ST1-06 | ST1-06-AC02 | S | WP-6 | ServiceInvokeValidator | Raw URL/unknown/third-party/SQL destination → POLICY_DENIED without network |
| ST1-06 | ST1-06-AC03 | U | WP-6 | ServiceInvokeValidator | Input binding: only typed refs + approved compose; no domain transforms |
| ST1-06 | ST1-06-AC04 | C | WP-6 | Mock BusinessService (SUCCESS outcome) | SUCCESS serviceOutcome; no decision from HTTP 200 alone (REF-02: SUCCESS must be tested) |
| ST1-06 | ST1-06-AC05 | C | WP-6 | Mock BusinessService + RunStateMachine | BUSINESS_REJECTION → not automatic technical failure; UI update may still SUCCEED |
| ST1-06 | ST1-06-AC06 | C | WP-6 | StepOutcomeNormalizer | TECHNICAL_FAILURE, TRANSIENT_FAILURE, UNKNOWN_OUTCOME handling; no blind retry on UNKNOWN |
| ST1-06 | ST1-06-AC07 | U | WP-6 | ServiceInvokeValidator | Retry with same idempotencyKey when safe; UNKNOWN if uncertain |
| ST1-06 | ST1-06-AC08 | S | WP-6 | ServiceInvokeValidator | Malformed response, unrecognized actionCode, script ref in response → rejected |
| ST1-06 | ST1-06-AC09 | M | WP-6 | Mock BusinessService (customer-adapter mode) | Customer-owned adapter; Tracto invokes only registered service |
| ST1-07 | ST1-07-AC01 | M | WP-6 | Mock BusinessService + RunStateMachine | HANDED_OFF on acknowledged async; worker lease released |
| ST1-07 | ST1-07-AC02 | U | WP-6 | ServiceInvokeValidator | Missing/mismatched correlation → handoff not reported as successful |
| ST1-07 | ST1-07-AC03 | C | WP-6 | Callback schema | Required callback fields present; no Tracto script identifier |
| ST1-07 | ST1-07-AC04 | S | WP-6 | Mock callback boundary | Invalid sig/stale timestamp/reused nonce/self-asserted authenticated:true rejected |
| ST1-07 | ST1-07-AC05 | M | WP-6 | Mock callback boundary | Same eventId × 2 → at most one continuation; duplicate logged |
| ST1-07 | ST1-07-AC06 | S | WP-6 | Mock Orchestrator | Service-supplied continuationScriptRef/automationId ignored; Orchestrator config governs |
| ST1-07 | ST1-07-AC07 | C | WP-6 | Mock BusinessService state | Business state stays external; Tracto stores only run/handoff/correlation refs |
| ST1-07 | ST1-07-AC08 | M | WP-8 | Mock Orchestrator static continuation | On-SUCCEEDED: exactly one new run per sourceRunId + ruleId; deduped; target from config |
| ST1-08 | ST1-08-AC01 | U | WP-5 | RunStateMachine | All legal transitions tested; illegal transition rejected; cancellation from CREATED/QUEUED; RUNNING→UNKNOWN; unsafe requeue after ambiguous lease rejected; terminal states immutable |
| ST1-08 | ST1-08-AC02 | U | WP-5 | RunStateMachine / StepOutcomeNormalizer | COMPLETED/SUCCESS rejected; only normative states accepted |
| ST1-08 | ST1-08-AC03 | U | WP-5 | StepOutcomeNormalizer | Untaken branch → SKIPPED; false precondition timeout → FAILED/TIMED_OUT; never SKIPPED |
| ST1-08 | ST1-08-AC04 | C | WP-5 + WP-6 | RunStateMachine + Mock BusinessService | Business rejection + successful UI → SUCCEEDED + businessOutcome=REJECTED |
| ST1-08 | ST1-08-AC05 | C | WP-5 | RunStateMachine | Interrupted irreversible operation → UNKNOWN + needsAttention=true; not retried; not FAILED |
| ST1-08 | ST1-08-AC06 | U | WP-5 | ErrorCategory enum + serializer | All 11 categories round-trip; namespace desktop.*/browser.*/terminal.* preserved |
| ST1-08 | ST1-08-AC07 | U | WP-5 | StepOutcomeNormalizer | serviceOutcome distinct from errorCategory; no silent substitution |
| ST1-08 | ST1-08-AC08 | U | WP-5 | ResultEnvelopeBuilder | All mandatory result fields; stepResult includes observedPostcondition + attemptCount; lastHeartbeatAt nullable; durationMs via TimeProvider (REF-10) |
| ST1-09 | ST1-09-AC01 | C | WP-8 | Mock Orchestrator + WorkerRegistration | Valid registration across 3 runtimeTypes; contract capabilities exposed |
| ST1-09 | ST1-09-AC02 | R | WP-8 | CompatibilityMatrix | Current/previous protocol major accepted; older rejected |
| ST1-09 | ST1-09-AC03 | U | WP-8 | Mock Orchestrator + CapabilityDigestCalculator | runtimeType/capability namespace mismatch → denied/quarantined; digest is hint not proof |
| ST1-09 | ST1-09-AC04 | M | WP-8 | InteractiveLeaseEnforcer | Two headed workers same interactiveSessionId → at most one active interactive lease |
| ST1-09 | ST1-09-AC05 | M | WP-8 | Mock Orchestrator | Headless worker remains eligible when interactive busy |
| ST1-09 | ST1-09-AC06 | C | WP-8 | Mock Orchestrator | Headed worker missing interactiveSessionId → cannot receive interactive lease |
| ST1-09 | ST1-09-AC07 | C | WP-8 | Mock Orchestrator capability match | Worker lacks schema major or action capability → job withheld, mismatch recorded |
| ST1-09 | ST1-09-AC08 | S | WP-8 | Mock Orchestrator | Unauthenticated/mismatched registration → rejected; no secrets as plain fields |
| ST1-10 | ST1-10-AC01 | M | WP-8 | Mock Orchestrator | Outbound pull: no job or one eligible leased job; no inbound needed |
| ST1-10 | ST1-10-AC02 | C | WP-8 | Mock Orchestrator + Reference Adapter | runId/attemptId/leaseId/digest preserved; inputRefs typed and validated; lastSeenAttemptId honored |
| ST1-10 | ST1-10-AC03 | M | WP-8 | Mock Orchestrator | One active lease per worker; second job not accepted until terminal state |
| ST1-10 | ST1-10-AC04 | M | WP-8 | Mock Orchestrator + FakeTimeProvider | Monotonically increasing heartbeat sequence; stale/duplicate heartbeat cannot move attempt backwards |
| ST1-10 | ST1-10-AC05 | M | WP-8 | Mock Orchestrator + FakeTimeProvider | Lost heartbeat → flag for reconciliation; not proof of side-effect failure |
| ST1-10 | ST1-10-AC06 | M | WP-8 | Mock Orchestrator | Stale attempt result after replacement → fenced; cannot overwrite authoritative state |
| ST1-10 | ST1-10-AC07 | R | WP-8 | Mock Orchestrator (localhost + remote fixture) | Same logical messages on localhost and network endpoint; no contract variant |
| ST1-10 | ST1-10-AC08 | M | WP-8 | Mock Orchestrator + AttemptJournal | Reconcile with journal → safe resolution or UNKNOWN; no blind new assignment |
| ST1-11 | ST1-11-AC01 | M | WP-8 | Mock Orchestrator + cancelRequest | Cancel associated with exact run/attempt; no cancellation of unrelated runs |
| ST1-11 | ST1-11-AC02 | U | WP-8 | RunStateMachine | Bounded cleanup; CANCELLED only when definitive; engine resources released |
| ST1-11 | ST1-11-AC03 | M | WP-8 | RunStateMachine + Mock | Cancel after irreversible action → UNKNOWN + attention; no blind retry |
| ST1-11 | ST1-11-AC04 | M | WP-8 | Mock + ServiceInvokeValidator | IDEMPOTENT step + stable key → bounded retry permitted on safe transient |
| ST1-11 | ST1-11-AC05 | M | WP-8 | RunStateMachine | Non-idempotent POST timeout → zero auto-replays; UNKNOWN/attention |
| ST1-11 | ST1-11-AC06 | M | WP-8 | Mock Orchestrator restart + AttemptJournal | Reconcile post-restart; stale completion cannot finalize different attempt |
| ST1-11 | ST1-11-AC07 | U | WP-8 | RunStateMachine | onFailure recovery exceeds timeout → FAILED/TIMED_OUT or UNKNOWN; no indefinite hold |
| ST1-11 | ST1-11-AC08 | C | WP-8 | ResultEnvelopeBuilder | Terminal CANCELLED/FAILED/TIMED_OUT/UNKNOWN includes attempt identity + step reason; no secrets |
| ST1-12 | ST1-12-AC01 | S | WP-7 | ServiceInvokeValidator + PolicyEnforcer | Direct third-party REST/SQL rejected POLICY_DENIED; registered custom service accepted |
| ST1-12 | ST1-12-AC02 | S | WP-7 | PolicyEnforcer | Browser Playwright navigation allowed; direct script-supplied HTTP integration denied |
| ST1-12 | ST1-12-AC03 | S | WP-7 | PolicyEnforcer (terminal command policy) | curl/Invoke-WebRequest/DB tooling for unregistered business endpoint denied; allowlist-based |
| ST1-12 | ST1-12-AC04 | U | WP-7 | StepValidator + SecretRedactor | secretRef in script/job → only identifier; literal secret → rejected |
| ST1-12 | ST1-12-AC05 | S | WP-7 | Mock Windows-side secret provider interface | Windows-side resolution only; Linux Orchestrator retains only secretRef |
| ST1-12 | ST1-12-AC06 | S | WP-7 | Secret provider interface | Unauthorized secretRef → denied; no secret in diagnostic |
| ST1-12 | ST1-12-AC07 | S | WP-7 | EnvelopeValidator / deployment config | WSL/Linux worker requesting CurrentUser DPAPI directly → capability/security validation fails |
| ST1-12 | ST1-12-AC08 | S | WP-7 | ServiceInvokeValidator | Service response containing code/endpoint/permissions → rejected; not executed |
| ST1-12 | ST1-12-AC09 | R | WP-9 | Conformance runner (3 runtime adapters) | All 3 runtimes pass same security fixtures independently |
| ST1-13 | ST1-13-AC01 | U | WP-7 | ITelemetryEmitter | Mandatory event fields present and typed |
| ST1-13 | ST1-13-AC02 | U | WP-7 | ITelemetryEmitter | stepId/leaseId/errorCategory present in step/lease events; match run result |
| ST1-13 | ST1-13-AC03 | C | WP-7 + WP-6 | EvidenceManifestBuilder + Mock | Correlation by correlationId/businessJobId links run, handoff, continuation |
| ST1-13 | ST1-13-AC04 | S | WP-7 | SecretRedactor | Secrets/auth headers/cookies/screenshots → absent from events; approved masked forms used |
| ST1-13 | ST1-13-AC05 | U | WP-7 | EvidenceManifestBuilder | evidenceRef + step/run attribution + sensitivity + retention policy; no capture internals |
| ST1-13 | ST1-13-AC06 | M | WP-7 | EventSequenceValidator (mock) | Out-of-order/duplicate events → deduplication; stale message cannot overwrite final status |
| ST1-13 | ST1-13-AC07 | M | WP-7 | ITelemetryEmitter (buffered export mode) | Local buffer + export preserves correlation + redaction; no cloud collector required |
| ST1-13 | ST1-13-AC08 | U | WP-7 | ITelemetryEmitter | UNKNOWN/FAILED/CANCELLED → stable outcome taxonomy + evidence pointers; no false success |
| ST1-14 | ST1-14-AC01 | M | WP-9 | Conformance runner | CF-01: valid job, 2 heartbeats, terminal result → one execution, one terminal |
| ST1-14 | ST1-14-AC02 | M | WP-9 | Conformance runner | CF-02: wrong runtimeType/schema → no action; policy/schema rejection |
| ST1-14 | ST1-14-AC03 | M | WP-9 | Conformance runner | CF-03: lease expiry after possible commit → UNKNOWN/reconcile; zero blind repeats |
| ST1-14 | ST1-14-AC04 | M | WP-9 | Conformance runner | CF-04/05: restart, duplicate lease/status/callback → fencing, dedup, continuation uniqueness |
| ST1-14 | ST1-14-AC05 | M | WP-9 | Conformance runner | CF-06/07: service outcomes SUCCESS + rejection + transient + ambiguous POST (REF-02) |
| ST1-14 | ST1-14-AC06 | S | WP-9 | Conformance runner | CF-08/10 + extensions: plaintext secrets, unapproved serviceRef, domain expression → rejected |
| ST1-14 | ST1-14-AC07 | M | WP-9 | Conformance runner | CF-09: cancel + broken channel → bounded cleanup + correct terminal |
| ST1-14 | ST1-14-AC08 | R | WP-9 | Conformance runner (per-runtime CI) | Desktop/Browser/Terminal adapters run independently; each produces own report |
| ST1-14 | ST1-14-AC09 | U | WP-9 | Conformance runner + FakeTimeProvider | Deterministic repeatability; no wall-clock dependence |
| ST1-15 | ST1-15-AC01 | R | WP-11 | NuGet package contents | Canonical package contains all schema/DTO/enum/message/fixture version |
| ST1-15 | ST1-15-AC02 | R | WP-11 | Package + sample scripts | Per-runtimeType samples internally consistent; validate with supplied commands |
| ST1-15 | ST1-15-AC03 | R | WP-11 | Compatibility test suite | N/N-1 script schema and protocol both supported; older → republish |
| ST1-15 | ST1-15-AC04 | R | WP-11 | Breaking-change detection CI | Removed/renamed required field or changed state → flagged; cannot release as silent patch |
| ST1-15 | ST1-15-AC05 | C | WP-11 | Release manifest | Named contract owner, host-supervisor owner, fixture maintainer; if unassigned → release readiness open |
| ST1-15 | ST1-15-AC06 | R | WP-11 | Per-team CI reports | Each worker version states conformance; failure in one team does not block unrelated teams |
| ST1-15 | ST1-15-AC07 | S | WP-11 | Lead Architect review | Proposed change expanding Runtime into business processing → rejected without explicit approval |
| ST1-15 | ST1-15-AC08 | C | WP-11 | Conformance runner (independent impls) | Different internal .NET libraries permitted; same shared contract behavior required |

## Human Review Responsibilities (Cannot Be Automated)

| AC | Reason |
|---|---|
| ST1-04-AC06 | Intent-based misuse of technical UI predicates for business authorization cannot be detected by structural analysis alone. Requires a publishing reviewer to check authorial intent. |
| ST1-15-AC05 | Named ownership fields are a process gate, not a code check. A human must assign roles before release is approved. |
| ST1-15-AC07 | Architectural boundary assessment of proposed contract changes requires architectural judgment. Structural CI can flag field additions; intent requires review. |
