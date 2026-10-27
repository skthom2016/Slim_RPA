# 03 — Technology Selection and Rationale

## Mandated Technology Choices (from v1.2A — Not Revisited Here)

| Area | Architecture Mandate | Section |
|---|---|---|
| Primary language / runtime | Modern .NET (C#) for all Tracto components | §1, §5, AD-04 |
| Orchestrator packaging | .NET Linux container | §6, AD-01 |
| Browser engine | Playwright for .NET | §5.3, §4.2 |
| Desktop targeting | Windows UI Automation; Java Access Bridge; keyboard fallback | §5.2, AD-02 |
| Secret resolution (isolated) | Windows DPAPI CurrentUser, Windows-side only | §10.2, AD-14 |
| Observability style | OpenTelemetry-style structured fields | §11.3, AD-09 |
| Deployment models | Connected (central Linux container); Isolated (Docker Engine in WSL2) | §9, AD-12 |

These are frozen decisions. Stage 1 does not reconsider them.

## Stage 1 Technology Recommendations

All choices below are **implementation choices permitted within the frozen architecture**.

| Area | Existing Constraint | Recommended Choice | Reason | Alternative Considered |
|---|---|---|---|---|
| .NET version | Modern .NET mandated | **.NET 10 LTS** | LTS lifecycle through November 2028; .NET 8 ends November 2026, .NET 9 (STS) also ends November 2026 — neither is suitable for a multi-team project starting now; Playwright for .NET supports .NET 10 | .NET 9 STS — ends Nov 2026, same date as .NET 8; not appropriate for new work |
| C# version | .NET 10 implied | **C# 14** | Ships with .NET 10; collection expressions, primary constructors, required members and pattern improvements available | C# 13 (.NET 9 only) — unnecessary downgrade |
| Schema format | Not mandated by architecture | **JSON Schema draft 2020-12 (dialect: Draft 7 compatible subset)** | JSON Schema is industry standard; widely supported in .NET; no architectural conflict; readable by all teams | YAML Schema — adds tooling variation; not typical for .NET shops |
| Schema validation library | Not mandated | **JsonSchema.Net (Gregsdennis.JsonSchema)** | Pure .NET 8 compatible; supports draft 2020-12; no native code / OS dependency; actively maintained as of Aug 2025 | NJsonSchema — older API surface, ties to Newtonsoft; System.Text.Json.Schema — .NET 9 only |
| JSON serialization | .NET platform implied | **System.Text.Json** (built-in) | Ships with .NET 8; source-generation mode avoids reflection; no external dependency; sufficient for contract DTOs | Newtonsoft.Json — unnecessary third-party dep for new code |
| Test framework | Not mandated | **xUnit v2** + **FluentAssertions v6** | xUnit is the .NET standard for new projects; FluentAssertions produces readable failure messages; both NuGet-stable | NUnit, MSTest — equivalent; xUnit preferred for new .NET projects |
| Mock service hosting | Not mandated | **Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory)** for integration; in-process for unit | Keeps mock Orchestrator in a .NET host without external infrastructure; same transport as production will use | WireMock.NET — heavier dependency; overkill for deterministic fixtures |
| Dependency injection | .NET platform | **Microsoft.Extensions.DependencyInjection** (built-in) | Already in the .NET ecosystem; no extra dependency | Autofac — adds complexity for contract-stage work |
| Logging (test/fixture) | OpenTelemetry-style mandated | **Microsoft.Extensions.Logging** + **OpenTelemetry .NET SDK 1.x** for field structure | Aligns with AD-09; fields match contract telemetry schema; OpenTelemetry SDK is stable .NET | Serilog — fine alternative but introduces additional dependency |
| Artifact signing (verification) | Trust verification before side effect (CEC-001) | **System.Security.Cryptography** (built-in) for HMAC/hash; **System.Security.Cryptography.Pkcs** for CMS/PKCS#7 signer verification | Both ship with .NET 8 on Windows and Linux; no external native dep required for Stage 1 validation | Bouncy Castle — needed only if non-standard key types required |
| Build tooling | .NET implied | **dotnet CLI** + **MSBuild**; CI via **GitHub Actions** (or Azure DevOps — adapter choice) | Standard .NET toolchain; no proprietary vendor lock for the contract layer | Cake/NUKE — unnecessary abstraction layer at Stage 1 |
| Package distribution | .NET implied | **NuGet** packages (local feed for Stage 1; public or private feed for handoff) | Standard .NET distribution; supports N/N-1 versioning via SemVer; NuGet.Server or GitHub Packages for local | npm/pip — wrong ecosystem |
| Controllable clock | Required for timing tests | **TimeProvider (System.TimeProvider)** (.NET 8 built-in) | Built-in abstraction replaces `DateTime.UtcNow` in production code; inject fake in tests | Custom `IClock` abstraction — reinvents what .NET 8 ships |
| HTTP transport (mock) | Not mandated for wire format | **System.Net.Http.HttpClient** + **Microsoft.AspNetCore** minimal APIs | Standard .NET; no extra dependency; semantic contract is what matters (§8 contract) | gRPC — not mentioned in contract; adds proto tooling without requirement |

## Explicitly Excluded Technologies

The following are **not introduced** because no concrete Stage 1 requirement demands them:

- **Database** (SQL, SQLite, NoSQL) — mock Orchestrator uses in-memory state; no durable persistence at Stage 1
- **Message broker** (RabbitMQ, Azure Service Bus) — protocol is outbound HTTP pull; no queue needed
- **Container platform** — Orchestrator deployment is deferred; Stage 1 tests run in-process or as .NET services
- **Workflow engine** (Durable Functions, Temporal) — prohibited by architecture; AD-01, §3.2
- **Arbitrary expression evaluator** — prohibited; §5, CEC-002
- **ORM** — no database at Stage 1
- **Generic HTTP integration framework** — prohibited; §10.1

## Provisional Choices (Unverified at Time of Design)

The following choices are marked **provisional** because full compatibility verification against the exact target .NET 10 SDK version was not available during design:

- `JsonSchema.Net` — confirm NuGet availability and .NET 10 (`net10.0`) compatibility before project creation
- `OpenTelemetry .NET SDK` — confirm stable release version for .NET 10 before project creation
- `System.Security.Cryptography.Pkcs` — included in .NET 10 runtime; verify exact NuGet version for standalone projects

These are low-risk choices. If any are unavailable, alternatives are noted in the table above.
