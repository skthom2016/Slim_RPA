# Tracto — Browser + Terminal Parallel Sprint Coordination

**This is a coordination view only. The two per-track sprint plans are independent and authoritative for each team's planning.**

| Relative window (illustrative) | Browser | Terminal | Shared dependencies |
|---|---|---|---|
| Setup | B0: spec/CEC/Playwright baseline | T0: spec/CEC/CMD-PS-WSL baseline | Same Stage 1 package version, approved common contract; isolated repos/branches |
| Sprint 1 | B1: worker, trust, modes, contexts | T1: worker, trust, shells, identities | Shared contract-owner review only |
| Sprint 2 | B2: navigation/locators/recovery | T2: allowlisted commands and injection barriers | No cross-runtime dependency |
| Sprint 3 | B3: browser UI + files + sessions | T3: streams/prompts/secrets/output | Independent QA |
| Sprint 4 | B4: UNKNOWN/cancellation/services | T4: deadlines/UNKNOWN/reconciliation | Reuse same Orchestrator/fault scenarios, distinct engine fixtures |
| Sprint 5 | B5: release and offline certification | T5: services + release | Mode-specific real Orchestrator certification per Runtime |

**Do not synchronize sprint completions.** Each sprint is gated locally and each Runtime may be released independently. Common Stage 1 contract changes must use approved shared governance. No Browser task should block Terminal merely because its sprint number matches.

### Source note
The file labelled as a reference sprint plan in this upload is an **alignment register**, not a sprint assignment/effort plan. The detailed plans derive dependency ordering from the full Browser (21 stories) and Terminal (23 stories) approved-story candidates and current architecture decisions. Current v1.0 stories and v1.1 specs still require the narrow baseline sync checks listed in B0/T0 before implementation of affected semantics.

### Shared release standards
- Story review → corrections → independent QA → integrated code → DONE; mandatory sprint review with measurable goal.
- Exactly five CEC service outcomes; exact canonical CEC errors/states; signed trust; one job per worker; safe UNKNOWN and HANDED_OFF semantics.
- No runtime LLM, arbitrary business processing, cross-engine scripts or direct third-party business service API/database calls.
- Conditional deployment POC gates apply only when advertising their mode; mock conformance cannot substitute real Orchestrator integration.
