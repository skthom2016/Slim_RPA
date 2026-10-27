# 12 — Stage 1 Readiness and Exit Criteria

## Stage 1 is Complete When

From TRACTO-ST1-US-001 §5, paraphrased with implementation detail:

1. **All 15 stories accepted** — ST1-01 through ST1-15, with automated evidence for all U/C/M/S/R tests and explicit human-review records for ST1-04-AC06, ST1-15-AC05, ST1-15-AC07.
2. **Canonical schemas published** — versioned JSON Schema files and C# DTO NuGet package; current and previous major documented in `schema-version-catalogue.json`.
3. **State transition table stable and tested** — all legal run and step transitions verified; illegal transitions rejected; C3 distinctions (FAILED vs. TIMED_OUT vs. UNKNOWN) confirmed.
4. **Contract CI green** — build + all test projects pass; conformance fixture JSON validates against schemas; breaking-change detection step runs.
5. **Mock Orchestrator and fake business-service available** — `Tracto.Mock.Orchestrator` and `Tracto.Mock.BusinessService` packages tagged and released to the internal NuGet feed.
6. **Security negative tests pass via reference adapter** — `Tracto.Reference.Adapter` passes the ST1-12 security fixture matrix. Individual Runtime team security conformance is each Runtime track's own release gate.
7. **N/N-1 compatibility baseline confirmed** — Stage 1 begins with one schema major (v1). The test suite verifies that an artifact carrying an unsupported schema major is rejected with a republish diagnostic. Full two-major N/N-1 testing applies when a second major is actually published.
8. **One reference .NET adapter proves full contract round-trip** — `Tracto.Reference.Adapter` passes all CF-01 through CF-10 and extension fixtures via `Tracto.Conformance.Runner`.
9. **Conformance tooling available to Runtime teams** — `Tracto.Conformance.Runner` and fixtures are published and Runtime teams can run them independently. Per-Runtime certification is each Runtime team's release gate, not a Stage 1 gate.
10. **Named ownership fields populated** — Lead Architect, host-supervisor owner and fixture maintainer named in `conformance-manifest.json` (ST1-15-AC05).
11. **Traceability report complete** — every AC in TRACTO-ST1-US-001 links to its test or explicit human-review record.

## Not Required for Stage 1 Exit

- Windows UIA implementation or Desktop action library
- Playwright or Browser action library
- Terminal command engine
- Production Orchestrator scheduling store or cluster deployment
- Real Windows DPAPI secret provider (interface specified; implementation deferred)
- Windows interactive session provisioning
- Connected-mode enterprise vault integration
- Designer or AI Recorder
- POC-01 through POC-10 outcomes (parallel architectural gate, not Stage 1 dependency)
- Commercial monitoring stack

## Pre-Implementation Checklist

The following items must be resolved **before implementation begins** (WP-1):

- [ ] Q1: .NET 10 LTS confirmed; library compatibility verified (JsonSchema.Net, OpenTelemetry SDK, Cryptography.Pkcs)
- [ ] Q2: First schema major (v1) declared as current in version catalogue; no placeholder previous major
- [ ] Q6: Output classification vocabulary approved (including confirmation that opaque data movement is permitted)
- [ ] C1 recommended decision reviewed and approved by Common Contract owner (including branch-conditional write / runtime unavailability check)
- [ ] C3 recommended decision reviewed and approved by Common Contract owner
- [ ] C4 recommended decision reviewed and approved by Common Contract owner
- [ ] Repository location and NuGet feed configured
- [ ] CI pipeline target (GitHub Actions / Azure DevOps) confirmed

## Approval Gate

This design is presented for review. **Implementation begins only after explicit approval of this design phase.** Approval of the planning prompt does not constitute approval to begin implementation.
