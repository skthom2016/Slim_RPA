# 11 — Remaining Questions, Risks and Proposed Resolutions

## Open Questions

| ID | Question | Blocking | Proposed Resolution |
|---|---|---|---|
| Q1 | **Exact .NET version:** .NET 8 LTS ends November 2026; .NET 9 STS also ends November 2026. Recommendation updated to .NET 10 LTS (support through November 2028). Confirm .NET 10 LTS as the implementation baseline. | Before WP-1 project creation | **Resolved by review:** Use .NET 10 LTS. Verify `JsonSchema.Net`, OpenTelemetry SDK and `System.Security.Cryptography.Pkcs` compatibility against .NET 10 before project creation. |
| Q2 | **Schema major versioning:** The samples in both documents are illustrative, not production schema identifiers. No previous production major exists. | Before WP-1 schema authoring | **Resolved by review:** Start at v1 (current). Do not pre-create a v2 placeholder. The N/N-1 rule applies when an actual second major is published. Declare only the current major in `schema-version-catalogue.json`. |
| Q3 | **Wire transport:** Is HTTP polling/long-poll the confirmed transport for Orchestrator ↔ Worker? The contract says the wire transport is "not frozen here". Does Stage 1 need to implement a specific transport in the mock, or is in-process sufficient? | Before WP-8 mock implementation | In-process (`IOrchestratorServer` interface + `IOrchestratorClient` in-process implementation) is sufficient for Stage 1 conformance. The transport-portability test (ST1-10-AC07) is satisfied by running the same fixture messages against both in-process and a `localhost` HTTP host. No production transport decision is required from Stage 1. |
| Q4 | **Policy registry format:** What is the format and storage for the `policyRefs` registry? Scripts declare `policyRef` names; the validator must resolve them. | Before WP-3 PolicyEnforcer implementation | For Stage 1: a static JSON configuration file (loaded at mock startup) listing approved `policyRef` names and their declared permissions. This is a reference implementation choice, not an architectural change. The production policy registry is an Orchestrator/deployment concern. |
| Q5 | **Service endpoint registry format:** What is the format of the service registry (serviceRef → approved route/method/TLS/auth policy/response schema version)? | Before WP-6 ServiceInvokeValidator | For Stage 1: a static JSON configuration file loaded by the mock, with the same shape as the contract field table in §10.1. Runtime teams consume it as a DI-injected `IServiceRegistry`. |
| Q6 | **Output classification vocabulary:** The contract requires every output to declare an "approved evidence/telemetry classification." What are the approved classification values? | Before WP-3 EnvelopeValidator | **Requires explicit decision.** Proposed closed enum: `TECHNICAL_METADATA`, `BUSINESS_REFERENCE` (opaque correlation/reference identifier — not business data content), `UI_ASSERTION`, `SANITIZED_EVIDENCE`. The classification prohibits **disclosure and processing** of business data through telemetry — it does not prohibit authorized opaque data movement through Tracto (passing a service-returned correlation reference to a subsequent UI step, for example, is permitted and is distinct from logging the business payload). Confirm vocabulary and this interpretation with Lead Architect before WP-3. |
| Q7 | **Breaking-change detection tool:** Is there a preferred .NET NuGet diff/compatibility tool for ST1-15-AC04? | Before WP-11 | Recommend `dotnet-compatibility-check` (Microsoft) or `ApiCompat` (part of .NET SDK). Both can be integrated as CI steps. No architectural decision required. |
| Q8 | **Named owners for ST1-15-AC05:** Lead Architect, host-supervisor owner, and fixture maintainer must be named before release readiness. These are individuals, not implemented in code. | ST1-15 release gate | Out of scope for design phase. Flag as a release gate blocker. Placeholder fields in `conformance-manifest.json` will be left empty until assigned. |
| Q9 | **`maxIterations` policy cap value:** The contract proposes a default cap of 100 for `control.repeatBounded`. Is this confirmed? | Before WP-4b | The user stories say "(proposed baseline 100, policy-controlled)." This is a planning assumption only — not confirmed by the contract owner. Stage 1 tests will use 100 as the working default. Must be confirmed by the Common Contract owner before WP-4b is implemented. |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Schema version ambiguity (Q2 above) causes Runtime teams to implement incompatible validators | Medium | High | Resolve Q2 in WP-1 before any team begins validator work. Schema version catalogue is the single source of truth. |
| R2 | Mock Orchestrator quietly evolves to approximate production Orchestrator behavior, blurring the boundary | Medium | Medium | Explicit package suffix (`-test`); `Tracto.Mock.Orchestrator` is never shipped as a runtime dependency of production packages; CI enforces no production package imports from mock packages |
| R3 | Reference Adapter is adopted by a Runtime team as their production starting point | Low | High | Label clearly in code, documentation and NuGet metadata: "REFERENCE IMPLEMENTATION — NOT FOR PRODUCTION USE." Remove from any Runtime team's package dependency graph. |
| R4 | `JsonSchema.Net` or `OpenTelemetry .NET SDK` incompatibility with .NET 10 (`net10.0`) | Low | Medium | Verify before WP-1 project creation (provisional flag in `03-technology-selection.md`). Alternatives documented. .NET 10 is newer than the design's knowledge cutoff; library compatibility must be confirmed empirically. |
| R5 | C3 TIMED_OUT vs. UNKNOWN distinction is implemented inconsistently across Runtime teams | High | High | C3 resolution in `10-decision-log.md` is normative; `RunStateMachine` enforces it centrally; conformance fixtures CF-03 and CF-ext scenarios test it explicitly. |
| R6 | `interactiveSessionId` fencing logic (C4) is misunderstood — teams treat it as machine identity rather than session identity | Medium | Medium | C4 decision log is explicit; ST1-09-AC03 and AC04 conformance tests cover the distinction; mock enforces it. |
| R7 | Stage 1 exit claimed without completing security negative tests | Medium | High | ST1-12-AC09 explicitly requires all three runtimeType adapters to pass security fixtures independently. Conformance report is per-runtime. A missing report for any runtime blocks the conformance claim for that runtime (not others). |
| R8 | POC gates (POC-01–POC-09) mistakenly listed as Stage 1 blockers | Low | Medium | Explicitly documented in §5 of user stories and `02-stage1-scope.md`: PoC gates are a parallel architecture conditional-freeze gate, not a Stage 1 dependency. |

## Proposals Requiring Architectural Approval

None identified. All design decisions in this phase are **implementation choices within the frozen v1.2A architecture**.

Items that would require explicit architectural approval (not proposed here):
- Adding a fourth Tracto product component
- Adding a general-purpose workflow or BPM engine
- Allowing Runtime-to-Runtime direct communication
- Adding cross-engine steps within a single script in v1
- Allowing the Orchestrator to receive plaintext secrets
- Using machine-scope DPAPI instead of CurrentUser DPAPI

None of these are proposed or required by Stage 1.
