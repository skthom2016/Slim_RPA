# Tracto — Project Code Location Map

**Repository root:** `tracto\`  
**Full path on disk:** `C:\Users\santh\Downloads\New-NeeliRPA\tracto\`

---

## Designer — C# / WPF / .NET 10

**Folder:** `tracto\designer\src\`

| Project | Purpose |
|---------|---------|
| `Tracto.Designer.App\` | WPF entry point — the visual script designer UI |
| `Tracto.Designer.Core\` | Domain logic (script model, step definitions) |
| `Tracto.Designer.Infrastructure\` | HTTP publisher — uploads scripts to Orchestrator (`OrchestratorPublisher.cs`) |

**How to run:**
```
dotnet run --project designer\src\Tracto.Designer.App
```

---

## Desktop Runtime — C# / .NET 10

**Folder:** `tracto\desktop-runtime\src\`

| Project | Purpose |
|---------|---------|
| `Tracto.Desktop.Host\` | Entry point — polls Orchestrator for jobs and drives execution |
| `Tracto.Desktop.Actions\` | All 10 action handlers: `key`, `type`, `click`, `delay`, `scroll`, `navigate`, `focus`, `app.launch`, `http.get`, `file.saveAs` |
| `Tracto.Desktop.Runtime\` | Script execution engine (ScriptEnvelope deserialization + step runner) |
| `Tracto.Desktop.Input\` | Keyboard and mouse input abstraction (Win32 P/Invoke for `{LWIN}`) |
| `Tracto.Desktop.Controls\` | UI automation / window control helpers |
| `Tracto.Desktop.Element\` | UI element targeting |
| `Tracto.Desktop.AppLifecycle\` | App launch and attach-if-running logic |
| `Tracto.Desktop.Protocol\` | Wire protocol / ScriptEnvelope model |
| `Tracto.Desktop.Ipc\` | IPC between host and runtime |
| `Tracto.Desktop.Session\` | Session and job state management |
| `Tracto.Desktop.Telemetry\` | Per-step execution telemetry |
| `Tracto.Desktop.Recovery\` | Crash recovery and stale-lease auto-expiry |
| `Tracto.Desktop.Security\` | Security checks |

**How to run:**
```
dotnet run --project desktop-runtime\src\Tracto.Desktop.Host --no-launch-profile -p:Platform=x64
```

---

## Orchestrator — Go / SQLite  *(active production version)*

**Folder:** `tracto\orchestrator-go\`

| File | Purpose |
|------|---------|
| `main.go` | Complete Go orchestrator — HTTP API on **port 5080**, SQLite job queue, artifact store |

**How to run:**
```
cd orchestrator-go
go run .
```

**Key API endpoints:**

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `PUT` | `/artifacts/{id}/versions/{version}` | Upload a script artifact |
| `GET` | `/artifacts` | List all artifacts |
| `POST` | `/jobs` | Submit a job |
| `POST` | `/jobs/pull` | Worker polls for next job |
| `GET` | `/jobs/{runId}` | Get job status |
| `GET` | `/jobs/{runId}/telemetry` | Get step-level telemetry |

---

## Orchestrator (C# version) — C# / ASP.NET Core / .NET 10  *(legacy, not in active use)*

**Folder:** `tracto\orchestrator\src\`

| Project | Purpose |
|---------|---------|
| `Tracto.Orchestrator.Api\` | ASP.NET Core REST API |
| `Tracto.Orchestrator.Application\` | Application / use-case layer |
| `Tracto.Orchestrator.Domain\` | Domain entities (Job, Artifact, etc.) |
| `Tracto.Orchestrator.Infrastructure\` | SQLite persistence |
| `tests\` | Unit + integration tests |

> The Go version replaced this. Kept for reference.

---

## Orchestrator UI — Node.js / Express

**Folder:** `tracto\orchestrator-ui\`

| File | Purpose |
|------|---------|
| `server.js` | Express server on **port 4000** — proxies API calls to Go Orchestrator (port 5080) |
| `public\index.html` | Web dashboard HTML |
| `public\app.js` | Dashboard JavaScript (artifact list, Run button, job status) |
| `package.json` | npm dependencies |

**How to run:**
```
cd orchestrator-ui
node server.js
```

---

## Hello World API — C# / ASP.NET Core / .NET 10

**Folder:** `tracto\hello-world\src\Tracto.HelloWorld.Api\`

Sample REST API on **port 3000** — used by test scripts to exercise the `http.get` action.

**How to run:**
```
dotnet run --project hello-world\src\Tracto.HelloWorld.Api
```

---

## Browser Runtime — C# / .NET 10

**Folder:** `tracto\browser-runtime\`

| Path | Purpose |
|------|---------|
| `src\TractoRuntime.Browser\` | Executes scripts targeting a Chromium browser |
| `tests\TractoRuntime.Browser.Tests\` | Test suite |

---

## Terminal Runtime — C# / .NET 10

**Folder:** `tracto\terminal-runtime\`

| Project | Purpose |
|---------|---------|
| `src\Tracto.TerminalRuntime.Host\` | Entry point for terminal (CLI) automation |
| `src\Tracto.TerminalRuntime.Application\` | Use-case / application layer |
| `src\Tracto.TerminalRuntime.Domain\` | Domain model |
| `src\Tracto.TerminalRuntime.Infrastructure\` | Terminal I/O integration |
| `tests\Tracto.TerminalRuntime.Unit.Tests\` | Unit tests |
| `tests\Tracto.TerminalRuntime.Integration.Tests\` | Integration tests |

---

## Supporting Files

| Path | Purpose |
|------|---------|
| `CLAUDE.md` | Claude Code context — script format, all actions, upload PowerShell commands |
| `script.schema.json` | JSON Schema (draft-07) for validating Designer-format script files |
| `docs\action-catalog.md` | Full action reference with parameter tables and examples |
| `contract\` | Shared execution contract specifications |
| `e2e\` | End-to-end test suite |
| `README.md` | Kill-all and start-all service commands |

---

## Start-Up Order

Start services in this order when running the full stack locally:

1. **Orchestrator (Go)** — `cd orchestrator-go && go run .` → port 5080
2. **Orchestrator UI** — `cd orchestrator-ui && node server.js` → port 4000
3. **Hello World API** — `dotnet run --project hello-world\src\Tracto.HelloWorld.Api` → port 3000
4. **Desktop Runtime** — `dotnet run --project desktop-runtime\src\Tracto.Desktop.Host --no-launch-profile -p:Platform=x64`
5. **Designer** *(optional)* — `dotnet run --project designer\src\Tracto.Designer.App`

Open the dashboard at **http://localhost:4000** to upload scripts and submit jobs.

---

## Technology Summary

| Component | Language | Framework | Port |
|-----------|----------|-----------|------|
| Designer | C# | WPF / .NET 10 | — |
| Desktop Runtime | C# | .NET 10 | — |
| Orchestrator | Go | net/http + SQLite | 5080 |
| Orchestrator UI | JavaScript | Node.js / Express | 4000 |
| Hello World API | C# | ASP.NET Core / .NET 10 | 3000 |
| Browser Runtime | C# | .NET 10 | — |
| Terminal Runtime | C# | .NET 10 | — |
