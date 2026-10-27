# 07 — Development Dependency and Parallel Execution Plan

## Critical Path

```
[WP-1: Shared decisions + schemas] → [WP-2: Models + validation] → [WP-3: Execution semantics]
                                                                              ↓
[WP-4: Service contract (parallel)]     [WP-5: State machine]     [WP-6: Orchestrator protocol]
         ↘                                     ↓                          ↓
           [WP-7: Security + telemetry (cuts across all)]
                          ↓
           [WP-8: Mock Orchestrator + business service]
                          ↓
           [WP-9: Conformance fixtures + runner]
                          ↓
           [WP-10: Reference adapter + integration]
                          ↓
           [WP-11: Contract package + release governance]
```

## Work Package Table

| Order | Work Package | Story IDs | Prerequisites | Parallel Opportunities | Deliverables | Completion Evidence |
|---|---|---|---|---|---|---|
| WP-1 | Shared decisions, schemas and repository setup | ST1-01, ST1-15 (partial) | None | — | `design/10-decision-log.md` approved; JSON Schema files for all contract artifacts; schema-version catalogue; `Directory.Build.props`; CI pipeline skeleton | Schema files validate using `JsonSchema.Net` validator; CI passes on empty solution |
| WP-2 | C# models, enums and serialization | ST1-01, ST1-02, ST1-08 | WP-1 | None | `Tracto.Contract.Models` NuGet; all enums; all DTO records with `System.Text.Json` source generation; `Tracto.Contract.Testing` (FakeTimeProvider, builders) | All model serialization round-trip tests pass; JSON examples validate against schemas |
| WP-3 | Envelope validator, ref resolver, trust verifier | ST1-01, ST1-02, ST1-03 | WP-2 | None until WP-2 complete | `Tracto.Contract.Validation`; `IArtifactTrustVerifier` reference impl | ST1-01-AC01 through AC08 pass; ST1-02 and ST1-03 unit tests pass |
| WP-4a | Condition grammar parser | ST1-04 | WP-3 | Parallel with WP-4b | `ConditionGrammarParser` in `Tracto.Contract.Validation` | ST1-04-AC01 through AC08 pass; domain expression rejection confirmed |
| WP-4b | Control-flow validator and bounded iteration | ST1-05 | WP-3 | Parallel with WP-4a | `ControlFlowValidator`, `control.branch`, `control.repeatBounded` schemas/validators | ST1-05-AC01 through AC09 pass including precondition/postcondition enforcement and UNKNOWN for uncertain commits |
| WP-5 | Run/step state machine and result normalization | ST1-08 | WP-3, C3 resolved (WP-1) | Parallel with WP-4a+b after WP-3 | `RunStateMachine`, `StepOutcomeNormalizer`, `ResultEnvelopeBuilder` in `Tracto.Contract.Execution` | ST1-08-AC01 through AC08 pass; all state transition table cases tested; UNKNOWN/TIMED_OUT distinction verified |
| WP-6 | Service invoke and async handoff contracts | ST1-06, ST1-07 | WP-4a, WP-5 | None; depends on both WP-4a and WP-5 | `ServiceInvokeValidator`, `service.invoke` schema, async callback schema, `Tracto.Mock.BusinessService` | ST1-06-AC01 through AC09 pass; ST1-07-AC01 through AC08 pass including SUCCESS outcome (REF-02) |
| WP-7 | Security, secrets and telemetry contracts | ST1-12, ST1-13 | WP-3, WP-5 | Can begin security schema review when WP-3 draft exists | `SecretRedactor`, `EvidenceManifestBuilder`, `ITelemetryEmitter`, security fixture matrix | ST1-12-AC01 through AC09 pass; ST1-13-AC01 through AC08 pass; no secret in any serialized output |
| WP-8 | Worker protocol schemas and mock Orchestrator | ST1-09, ST1-10, ST1-11 | WP-5, WP-7, C4 resolved (WP-1) | None; WP-8 depends on state machine (WP-5) | `Tracto.Contract.Orchestrator`; `Tracto.Mock.Orchestrator` with fault injection | ST1-09-AC01 through AC08 pass; ST1-10-AC01 through AC08 pass; ST1-11-AC01 through AC08 pass; lease expiry, fencing, reconcile, duplicate delivery tested |
| WP-9 | Conformance fixtures and CI runner | ST1-14 | All WP-1 through WP-8 contracts complete | Can begin fixture JSON authoring as soon as schemas exist (WP-1) | CF-01 through CF-10 + extensions; conformance-manifest.json; `Tracto.Conformance.Runner` | ST1-14-AC01 through AC09 pass; deterministic repeatability confirmed with FakeTimeProvider; fixture JSON validates against schemas |
| WP-10 | Reference adapter integration | ST1-01 through ST1-13 (end-to-end proof) | WP-8, WP-9 | — | `Tracto.Reference.Adapter`; full round-trip from artifact import through registration, job pull, step execution, result reporting | Reference adapter passes all CF-01 through CF-10 + extensions in conformance runner; reference adapter conformance report generated |
| WP-11 | Contract package release and governance | ST1-15 | WP-10 | — | Versioned NuGet packages; compatibility matrix; changelog; breaking-change detection CI check; named ownership fields populated; reference adapter conformance report | ST1-15-AC01 through AC08 pass; reference adapter conformance report exists; no owner fields are blank. Per-Runtime team CI reports are each Runtime track's own gate, not a Stage 1 prerequisite. |

## Parallel Opportunities Explained

- **WP-4a and WP-4b** can be parallelized by two engineers after WP-3 completes. Neither depends on the other.
- **WP-5** can begin after WP-3 completes, in parallel with WP-4a and WP-4b. **WP-6** depends on both WP-4a and WP-5 and cannot begin until both are complete.
- **WP-7** (security + telemetry) can begin schema and policy review immediately after WP-3 schemas exist. Implementation is complete after WP-5.
- **Fixture JSON authoring** for WP-9 can begin as soon as WP-1 produces schemas, proceeding in parallel with all implementation work.
- **Security review** (ST1-12) can start when draft schemas from WP-3 exist; no need to wait for WP-8.
- **Each Runtime team** (Desktop, Browser, Terminal) can begin their own conformance runner execution independently once WP-9 delivers the runner and fixtures. No Runtime team blocks another. Importantly, Runtime team conformance results are **not** Stage 1 exit conditions — each Runtime's certification is the gate for that Runtime's own release track. Stage 1 exits on the reference adapter passing, not on all three production Runtimes passing.

## Role Assignments (Responsible Roles, Not Named Individuals)

| Work Package | Lead Role | Supporting Role |
|---|---|---|
| WP-1 | Lead Architect | Common Contract owner |
| WP-2 | Common Contract owner | All teams review |
| WP-3 | Common Contract owner | Security engineer reviews trust verifier |
| WP-4a | Common Contract owner / Automation author | QA reviews grammar fixtures |
| WP-4b | Runtime engineer | Common Contract owner |
| WP-5 | Operations engineer | Common Contract owner reviews state machine |
| WP-6 | Integration engineer | Security engineer reviews ST1-06-AC02 |
| WP-7 | Security engineer | Support engineer (telemetry) |
| WP-8 | Orchestrator engineer | Common Contract owner |
| WP-9 | QA automation engineer | All teams provide fixture acceptance evidence |
| WP-10 | Common Contract owner / Lead Architect | All Runtime teams run independently |
| WP-11 | Lead Architect | All teams provide CI reports |

## Estimation Assumptions

No precise dates are given without staffing and capacity information. Relative sequence:

- **Foundation (WP-1 + WP-2):** ~1 sprint
- **Core validation and state machine (WP-3 + WP-4a + WP-4b + WP-5):** ~2 sprints, parallelizable
- **Service and protocol (WP-6 + WP-7 + WP-8):** ~2 sprints, partially parallelizable
- **Conformance and release (WP-9 + WP-10 + WP-11):** ~1–2 sprints

**Minimum critical path (sequential):** WP-1 → WP-2 → WP-3 → WP-5 → WP-8 → WP-9 → WP-10 → WP-11, approximately 6–7 sprints for a single engineer. With parallelism across 3–4 engineers, approximately 4–5 sprints.

## Points Requiring Shared Integration

1. **Decision log sign-off (WP-1):** All teams must acknowledge C1/C3/C4 decisions before any work begins on ST1-01, ST1-08, ST1-09.
2. **Schema publication (WP-1 → WP-9):** JSON Schema files in `Tracto.Contract.Schemas` are the integration point; all validators and fixtures reference them.
3. **Conformance runner contract version:** The runner must specify which contract and fixture version it tests; Runtime teams pin to that version.
4. **Breaking-change gate (WP-11):** Before ST1-15 release, all teams must have passing CI conformance reports. A failure in one team blocks that team's conformance claim but not unrelated teams' claims.
