# 05 — Proposed Repository and Package Structure

## Repository Layout

```
tracto-contract/                          ← Root: Stage 1 common contract repository
│
├── src/
│   ├── Tracto.Contract.Schemas/          ← JSON Schema files (production deliverable)
│   │   ├── schemas/
│   │   │   └── v1/                       ← First (current) major; declared in schema-version-catalogue.json
│   │   │       ├── automation-envelope.schema.json  ← A v2/ is added only when an actual second major is published
│   │   │       ├── step-envelope.schema.json
│   │   │       ├── target-map.schema.json
│   │   │       ├── technical-condition.schema.json
│   │   │       ├── service-invoke.schema.json
│   │   │       ├── async-callback.schema.json
│   │   │       ├── run-result.schema.json
│   │   │       ├── worker-registration.schema.json
│   │   │       ├── pull-lease-protocol.schema.json
│   │   │       └── telemetry-event.schema.json
│   │   ├── schema-version-catalogue.json ← Declares current major (v1); previous-major entry added only when a second major is published
│   │   └── Tracto.Contract.Schemas.csproj
│   │
│   ├── Tracto.Contract.Models/           ← C# DTOs and enums (production deliverable)
│   │   ├── Envelope/
│   │   │   ├── AutomationEnvelope.cs
│   │   │   ├── StepEnvelope.cs
│   │   │   ├── TargetMap.cs
│   │   │   └── VarDeclaration.cs
│   │   ├── Execution/
│   │   │   ├── RunState.cs
│   │   │   ├── StepState.cs
│   │   │   ├── ErrorCategory.cs
│   │   │   ├── ServiceOutcome.cs
│   │   │   ├── BusinessOutcome.cs
│   │   │   ├── RetryClass.cs
│   │   │   └── TechnicalCondition.cs
│   │   ├── Protocol/
│   │   │   ├── WorkerRegistration.cs
│   │   │   ├── AdvertisedCapabilities.cs
│   │   │   ├── LeaseGranted.cs
│   │   │   ├── Heartbeat.cs
│   │   │   └── FinalResult.cs
│   │   ├── Telemetry/
│   │   │   └── TelemetryEvent.cs
│   │   └── Tracto.Contract.Models.csproj
│   │
│   ├── Tracto.Contract.Validation/       ← Validators and parsers (production deliverable)
│   │   ├── Artifact/
│   │   │   ├── EnvelopeValidator.cs
│   │   │   └── IArtifactTrustVerifier.cs
│   │   ├── Steps/
│   │   │   ├── StepValidator.cs
│   │   │   └── RefResolver.cs
│   │   ├── Conditions/
│   │   │   └── ConditionGrammarParser.cs
│   │   ├── Services/
│   │   │   ├── ServiceInvokeValidator.cs
│   │   │   └── IServiceRegistry.cs
│   │   └── Tracto.Contract.Validation.csproj
│   │
│   ├── Tracto.Contract.Execution/        ← State machine and execution contracts (production)
│   │   ├── StateMachine/
│   │   │   ├── RunStateMachine.cs
│   │   │   └── StepOutcomeNormalizer.cs
│   │   ├── IStepExecutor.cs
│   │   ├── IAttemptJournal.cs
│   │   ├── ResultEnvelopeBuilder.cs
│   │   └── Tracto.Contract.Execution.csproj
│   │
│   ├── Tracto.Contract.Orchestrator/     ← Protocol message contracts (production deliverable)
│   │   ├── IOrchestratorClient.cs
│   │   ├── IOrchestratorServer.cs
│   │   ├── CapabilityDigestCalculator.cs
│   │   ├── CompatibilityMatrix.cs
│   │   └── Tracto.Contract.Orchestrator.csproj
│   │
│   ├── Tracto.Contract.Telemetry/        ← Telemetry and evidence contracts (production)
│   │   ├── ITelemetryEmitter.cs
│   │   ├── SecretRedactor.cs
│   │   ├── EvidenceManifestBuilder.cs
│   │   └── Tracto.Contract.Telemetry.csproj
│   │
│   └── Tracto.Reference.Adapter/        ← Reference implementation (clearly labelled)
│       ├── ReferenceOrchestratorClient.cs
│       ├── ReferenceStepExecutor.cs
│       └── Tracto.Reference.Adapter.csproj
│
├── test/
│   ├── Tracto.Contract.Testing/          ← Shared test helpers (test infrastructure)
│   │   ├── FakeTimeProvider.cs
│   │   ├── ArtifactBuilder.cs
│   │   ├── FixtureValidator.cs
│   │   └── Tracto.Contract.Testing.csproj
│   │
│   ├── Tracto.Contract.Schemas.Tests/    ← JSON Schema unit tests
│   ├── Tracto.Contract.Models.Tests/     ← DTO serialization tests
│   ├── Tracto.Contract.Validation.Tests/ ← Validator and parser unit tests
│   ├── Tracto.Contract.Execution.Tests/  ← State machine and ref resolver tests
│   └── Tracto.Contract.Orchestrator.Tests/ ← Protocol message tests
│
├── mock/
│   ├── Tracto.Mock.Orchestrator/         ← Mock Orchestrator (conformance tool)
│   │   ├── InMemoryRunStore.cs
│   │   ├── MockOrchestratorServer.cs
│   │   ├── LeaseExpirySimulator.cs
│   │   └── Tracto.Mock.Orchestrator.csproj
│   │
│   └── Tracto.Mock.BusinessService/      ← Fake custom service (conformance tool)
│       ├── FakeBusinessServiceHandler.cs
│       └── Tracto.Mock.BusinessService.csproj
│
├── conformance/
│   ├── Tracto.Conformance.Fixtures/      ← CF-01..CF-10 + extensions (JSON files)
│   │   ├── cf-01-happy-path.json
│   │   ├── cf-02-incompatible-artifact.json
│   │   ├── cf-03-uncertain-lease-expiry.json
│   │   ├── cf-04-orchestrator-restart.json
│   │   ├── cf-05-duplicate-delivery.json
│   │   ├── cf-06-business-rejection.json
│   │   ├── cf-07-idempotent-retry.json
│   │   ├── cf-08-secret-redaction.json
│   │   ├── cf-09-cancel-disconnect.json
│   │   ├── cf-10-domain-expression.json
│   │   ├── cf-ext-success-branch.json
│   │   ├── cf-ext-continuation-negative.json
│   │   ├── cf-ext-response-schema-mismatch.json
│   │   └── conformance-manifest.json
│   │
│   └── Tracto.Conformance.Runner/        ← CI runner (conformance tool)
│       ├── ConformanceTestHost.cs
│       ├── FixtureExecutor.cs
│       ├── ConformanceReporter.cs
│       └── Tracto.Conformance.Runner.csproj
│
├── design/                               ← These design artifacts (checked in)
│   ├── 01-source-inventory.md
│   ├── 02-stage1-scope.md
│   ├── 03-technology-selection.md
│   ├── 04-implementation-architecture.md
│   ├── 05-repository-structure.md
│   ├── 06-diagrams.md
│   ├── 07-development-plan.md
│   ├── 08-traceability-matrix.md
│   ├── 09-testing-strategy.md
│   ├── 10-decision-log.md
│   ├── 11-risks-questions.md
│   └── 12-exit-criteria.md
│
├── Tracto.Contract.sln                   ← Solution file
├── Directory.Build.props                 ← Shared MSBuild properties (.NET 10, nullable, warnings as errors)
├── Directory.Packages.props              ← Central NuGet package version management
├── nuget.config                          ← Local/CI feed configuration
└── README.md
```

## Dependency Direction (Enforced)

```
Schemas ← Models ← Validation ← Execution ← Orchestrator ← Telemetry
                                                    ↑             ↑
                                              Contract.Testing  (all)
                                                    ↑
                                           Mock.Orchestrator
                                           Mock.BusinessService
                                                    ↑
                                          Conformance.Fixtures
                                          Conformance.Runner
                                                    ↑
                                          Reference.Adapter (uses all contract packages)
```

No package at a lower level may import from a higher level. Runtime-specific packages (Desktop/Browser/Terminal) are separate repositories that consume the NuGet packages from this repository.

## Versioning Strategy

- All packages share the same major.minor version within a release (e.g., `1.0.x`).
- The schema version major in `schema-version-catalogue.json` governs N/N-1 compatibility. Stage 1 declares only the current major (v1); the catalogue is extended to include a previous major only when an actual second major is published.
- Breaking changes (removed required field, changed state semantics) require a major version bump and compatibility assessment across all runtime teams.
- `Tracto.Contract.Testing`, `Tracto.Mock.*`, and `Tracto.Conformance.*` packages carry a `-test` suffix to prevent accidental production inclusion.
- Each Runtime team's CI references a specific contract package version; upgrade to a new major requires explicit team opt-in.

## Build Tooling

- `dotnet build` / `dotnet test` — standard .NET CLI
- `Directory.Build.props`: `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `Directory.Packages.props`: central version pinning for all NuGet dependencies
- CI: a single workflow builds all projects, runs all tests, and validates conformance fixture JSON against schemas
