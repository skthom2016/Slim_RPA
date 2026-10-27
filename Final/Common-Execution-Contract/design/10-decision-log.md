# 10 — Shared Decision Log

> **Status of C1, C3 and C4:** These are **recommended implementation decisions pending contract-owner approval**. They are not yet normative. Each must be reviewed and approved by the Common Contract owner before its dependent functionality is implemented. They do not require architectural approval or block unrelated Stage 1 work, but they must not be silently adopted as finalized before that review occurs.

This log records centrally all shared interface decisions. C1, C3 and C4 are the items flagged for resolution before implementation. Additional refinement tasks are listed at the end.

---

## C1 — Variable Declarations

**Must resolve before: ST1-01, ST1-02**

### Decision

A `vars` map is declared at the top level of the automation envelope, alongside `inputs` and `outputs`. All decisions below are **implementation choices within the frozen architecture**; no architectural change is required.

### Schema

```json
"vars": {
  "savedValue": { "type": "string" },
  "counter":    { "type": "integer", "nullable": false },
  "flag":       { "type": "boolean", "defaultValue": false }
}
```

The `vars` map is optional. If present, each key is a variable name.

### Declaration Location

Top-level in the automation envelope, defined once per script publication. Validated before any step executes.

### Supported Types

The same primitive type set as `inputs`/`outputs`: `string`, `integer`, `boolean`. Structured types may be introduced in a later contract major once the type catalog is finalized. No `expression`, `function`, `any` or `dynamic` types.

### Initialization and Uninitialized-Read Behavior

- If a `defaultValue` is declared for a var, the var starts at that value.
- If no `defaultValue` is declared, the var is uninitialized.
- Reading an uninitialized var whose step precedes any `outputBinding` that populates it is a **validation error** (caught at schema validation time, not at runtime): errorCategory=`VALIDATION`, detail=`variable.uninitializedRead`, var name included in error path.
- Reading a var that has been declared but not yet written by a preceding step (forward-read) is caught at validation and fails with `variable.forwardRead`.

### Scope and Lifetime

- Vars are **script-scoped**: one var space per run attempt.
- Vars do not persist across run boundaries, are not accessible to external services, and are not carried in Orchestrator state.

### Binding and Assignment Rules

- A var may only be **written** through an explicit `outputBinding` field on a completed step.
- The `outputBinding` references a declared var name (`vars.<name>`) and a step output field (`steps.<stepId>.outputs.<field>`).
- No direct assignment action exists.
- **Reading** a var uses `{"ref":"vars.<name>"}` in step parameters, preconditions or postconditions.

### Duplicate Names

Duplicate var names in the `vars` map are a **validation error** at schema validation time.

### Invalid References

A `{"ref":"vars.undeclaredName"}` fails before the consuming step executes: errorCategory=`VALIDATION`, detail=`variable.undeclaredReference`.

### Type Mismatch

Writing a type-incompatible value to a var via `outputBinding` is a **validation error** at step validation time before execution. The step emits FAILED with errorCategory=`VALIDATION`, detail=`variable.typeMismatch`.

### Nullability

- By default, vars are non-nullable.
- A `"nullable": true` annotation on the var declaration permits a null value.
- Reading a null value where `nullable` is false and no `defaultValue` is set is a runtime VALIDATION error.

### Sensitivity Classification

Vars may carry a `sensitivity` annotation (same classification as `outputs`). Sensitive vars must not be serialized into telemetry or Orchestrator state (same redaction rules as `outputs`).

### Branch-Conditional Writes

A var populated inside a `control.branch` arm is only definitely written when that arm is selected. If a later step reads `{"ref":"vars.name"}` where `name` is written only inside one branch arm:

- If the step is always inside the same arm or a subsequent arm where the write is guaranteed: static validation can confirm definite assignment and allow it.
- If the step can execute on a path where the branch arm was not taken: the var may be uninitialized at that point. This cannot always be resolved statically (branches are dynamic). The implementation must:
  1. Perform static definite-assignment analysis where possible to catch obvious violations at validation time.
  2. At runtime, check whether the var has been assigned before use and, if not, emit errorCategory=`VALIDATION`, detail=`variable.uninitializedRead` for that step.

This means `uninitializedRead` can occur at runtime (not only at validation time) when branch paths are involved. The runtime check is required.

### Computation and Transformation

Vars cannot be assigned by any expression, calculation or business transformation. They hold exactly the typed value produced by a preceding step's output binding. No computation permitted.

---

## C3 — FAILED versus TIMED_OUT (and UNKNOWN)

**Must resolve before: ST1-08**

> **Pending contract-owner approval.** The definitions below are the recommended resolution. They must not be treated as final until the Common Contract owner explicitly approves them. Implementation of ST1-08 must not begin before that approval.

### Recommended Resolution

The following table defines the proposed step state, run-state consequence, error category and retry implications for each distinguishable scenario.

| Scenario | Step State | Run State | Error Category | Detail Code | `needsAttention` | Retry |
|---|---|---|---|---|---|---|
| Precondition remains false until its waiting deadline | `FAILED` | `FAILED` | `TIMEOUT` | `precondition.waitTimeout` | false | Per `retryClass`; if `TRANSIENT_SAFE` and provably uncommitted, bounded retry permitted |
| Action whose execution deadline expires and non-commitment is confirmed | `TIMED_OUT` | `TIMED_OUT` | `TIMEOUT` | `action.executionTimeout` | false | Only if `retryClass=TRANSIENT_SAFE` and no side effect could have committed |
| Action whose execution deadline expires and commitment is uncertain | `UNKNOWN` | `UNKNOWN` | `TIMEOUT` | `action.commitUncertain` | **true** | **Never retry blindly**; reconcile or operator investigation required |
| Postcondition that fails (effect confirmed not committed or effect is reversible) | `FAILED` | `FAILED` | `VALIDATION` | `postcondition.verificationFailed` | false | Per `retryClass` if step is idempotent |
| Postcondition that fails after a possibly-committed irreversible effect | `UNKNOWN` | `UNKNOWN` | `VALIDATION` | `postcondition.irrevocableUncertain` | **true** | **Never retry blindly** |
| Operation side effect committed but cannot be confirmed via any postcondition | `UNKNOWN` | `UNKNOWN` | `TIMEOUT` or `TRANSPORT` (whichever caused loss of confirmation) | `action.commitUncertain` | **true** | **Never retry blindly** |

### Rules

1. `TIMED_OUT` is only used for step or run state when **non-commitment is safely confirmed** (e.g., action had not started, or transport failure occurred before request was dispatched, or idempotency key proves non-execution).
2. `UNKNOWN` is the correct step and run state whenever commitment cannot be established. This is not an error condition; it is the correct truthful outcome.
3. `FAILED` is used when failure is definitively established (no side effect committed).
4. A `FAILED` step with `retryClass=TRANSIENT_SAFE` may trigger a bounded retry if and only if the step has not committed a side effect.
5. A step in `UNKNOWN` state terminates the run as `UNKNOWN`. The attempt is fenced. No second attempt is dispatched without affirmative reconciliation proof.
6. `needsAttention=true` is set on the run result whenever the run terminates in `UNKNOWN`. It may also be set on `FAILED` when the failure requires operator review (e.g., a partially executed sequence that cannot be trivially re-run).
7. `unknownReason` is populated with a stable string describing what could not be confirmed.

### Run-State Consequence

- If a step reaches `UNKNOWN`, the run state becomes `UNKNOWN` unconditionally.
- If a step reaches `FAILED`, the run state becomes `FAILED` unless an `onFailure` handler recovers; handler outcome then governs.
- If a step reaches `TIMED_OUT` with confirmed non-commitment, the run state is `TIMED_OUT`.
- Terminal run states are immutable for the attempt.

---

## C4 — Worker Capability Advertisement

**Must resolve before: ST1-09**

### Decision

Worker capabilities are advertised at registration using the existing `register/capability` message (§8, Contract). The following additions resolve the open question of how capabilities are represented and verified.

### Registration Fields (Complete Set)

```json
{
  "workerId":            "string — stable unique worker identity",
  "runtimeType":        "desktop | browser | terminal",
  "workerVersion":      "semver string — e.g. 1.2.3",
  "protocolVersion":    "major.minor — e.g. 1.0",
  "supportedSchemaMajors": [1, 2],
  "capabilityDigest":   "sha256:<hex> — integrity hint (see below)",
  "hostClass":          "interactive | headless",
  "interactiveSessionId": "string | null — required when hostClass=interactive",
  "advertisedCapabilities": {
    "actionNamespaces": ["desktop.*", "desktop.window.*", "control.*", "service.*", "value.*"],
    "predicateNamespaces": ["desktop.*", "api.*"],
    "maxIterations": 100
  },
  "health": {
    "status": "READY | DEGRADED | UNAVAILABLE",
    "detail": "string | null"
  }
}
```

### Capability Representation and Versioning

- Capabilities are declared as **namespace patterns** (e.g., `desktop.*`, `browser.element.*`). This avoids exhaustive enumeration while enabling the Orchestrator to determine whether a worker can handle a job's action types.
- `actionNamespaces` must include all namespaces whose actions the worker supports. `service.*`, `control.*` and `value.*` are common to all workers; per-runtime namespaces (`desktop.*`, `browser.*`, `terminal.*`) are worker-specific.
- `predicateNamespaces` must include all technical-condition namespaces the worker can evaluate.
- The `maxIterations` field declares the maximum iteration count the worker enforces for `control.repeatBounded`.

### Digest Purpose and Calculation

The `capabilityDigest` is an **integrity and identity hint**, computed as:

```
SHA-256( workerId + runtimeType + workerVersion + canonicalized(advertisedCapabilities) )
```

- Canonical form: UTF-8 JSON with keys sorted alphabetically.
- The digest allows the Orchestrator and conformance tooling to detect accidental capability-map mutation.
- **It is not cryptographic proof of worker authenticity.** Authentication is established through the mutual-TLS or token-authenticated registration channel. The digest must not be treated as evidence of genuine worker identity (ST1-09-AC03).

### Runtime-Type Compatibility Checks

At registration, the Orchestrator validates:
1. `runtimeType` matches the namespace prefixes in `actionNamespaces` and `predicateNamespaces`. A worker declaring `runtimeType=browser` with `desktop.*` action namespaces is rejected.
2. `supportedSchemaMajors` contains the current and/or previous major. A worker supporting only older majors is rejected.
3. `interactiveSessionId` is present and non-empty if `hostClass=interactive`; absent or null if `hostClass=headless`.

### Unsupported Capability Handling

- If a job requires an action namespace absent from `advertisedCapabilities.actionNamespaces`, the Orchestrator withholds the job and records a capability mismatch (ST1-09-AC07).
- If a job uses a condition predicate namespace absent from `advertisedCapabilities.predicateNamespaces`, the worker validates the script before accepting and emits errorCategory=`VALIDATION` with a runtime-specific detail code (ST1-04-AC08).
- Workers must not silently accept jobs they cannot execute and must not guess at unsupported capabilities.

### Dispatch Compatibility Behavior

A job is eligible for dispatch to a worker only when all of the following hold:
1. `runtimeType` matches the job's script `runtimeType`.
2. The job's `schemaVersion` major is in `supportedSchemaMajors`.
3. All action namespaces used by the job's steps are covered by `advertisedCapabilities.actionNamespaces`.
4. For an interactive job: the worker's `interactiveSessionId` does not already hold an active foreground-input lease.

---

## Remaining Refinement Tasks

The following items are recorded as normal refinement tasks. They do not block Stage 1 implementation but must be resolved before ST1-15 release:

| ID | Item | Priority | Resolution |
|---|---|---|---|
| REF-01 | Publishing review: verify no technical predicate is used for business-intent decisions (ST1-04-AC06) | Before ST1-15 | Manual publishing-review checklist; cannot be fully automated |
| REF-02 | Explicit SUCCESS branching coverage: confirm `api.outcomeCategoryEquals(SUCCESS)` is tested as an allowed branch in ST1-06 and ST1-14 | Before ST1-06 implementation | Add to fixture pack; five outcomes explicitly listed in contract §6, table |
| REF-03 | Duplicate acceptance criteria: ST1-08-AC03 appears in two slightly different forms; confirm canonical wording | Before ST1-08 implementation | Use the version in §7 (Contract) as normative |
| REF-04 | Continuation negative tests: explicit test that no continuation is triggered from HANDED_OFF, FAILED, UNKNOWN or CANCELLED without a configured static continuation rule | Add to ST1-07 / ST1-14 fixtures | Add CF-extension fixture |
| REF-05 | `service.*` error detail codes: stable code strings for common service failures not yet enumerated | Before ST1-06 / ST1-08 | Define `service.responseSchemaMismatch`, `service.networkTimeout`, `service.authFailed` in contract schema |
| REF-06 | `needsAttention` applicability: confirm whether FAILED (not just UNKNOWN) may also carry `needsAttention=true` | C3 decision above allows it; confirm in ST1-08 schema | Update ST1-08-AC05 specimen to show conditional FAILED+attention case |
| REF-07 | Empty formatting artifacts in user stories (blank table cells, trailing whitespace) | Cosmetic | Fixed in any editorial pass before ST1-15 release; no behavioral impact |
| REF-08 | Confirm `outputs` must each carry an approved `evidenceClassification` field at declaration time (ST1-01-AC07) | Before ST1-01 implementation | Confirmed by contract field table: "Typed declarations and an explicit approved evidence/telemetry classification for each output" |
| REF-09 | Confirm `pullJob.lastSeenAttemptId` is carried in all pull messages for reconciliation (ST1-10-AC02) | Before ST1-10 implementation | Confirmed by contract §8 message table |
| REF-10 | `durationMs` uses monotonic timing (ST1-08-AC08) — ensure TimeProvider is used in reference implementation | Before ST1-08 implementation | TimeProvider (.NET 8) recommended in `03-technology-selection.md` |
