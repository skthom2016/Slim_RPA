# Tracto — Claude Code Context

Tracto is a Windows desktop RPA platform. Scripts are authored in JSON, uploaded to the Orchestrator, and executed by the Desktop Runtime.

## Components

| Component | Language | Port | Purpose |
|-----------|----------|------|---------|
| Orchestrator | Go + SQLite | 5080 | Job queue, artifact store, REST API |
| Orchestrator UI | Node.js / Express | 4000 | Web dashboard |
| Desktop Runtime | C# / .NET 10 | — | Executes scripts on Windows |
| Designer | C# / WPF / .NET 10 | — | Visual script editor with recorder |
| Hello World API | C# / .NET 10 | 3000 | Sample REST endpoint for test scripts |

## Reference Docs

Read these before acting on any script-related task:

| Task | Read this file |
|------|---------------|
| Writing or editing a script | `docs/script-authoring.md` |
| Cleaning up a Designer recording | `docs/recording-cleanup.md` |
| Starting services / uploading / submitting jobs | `docs/services.md` |
| Full action reference with examples | `docs/action-catalog.md` |
| Validating a script | `script.schema.json` |
| All sub-project file locations | `PROJECT-MAP.md` |
| Kill/restart commands | `README.md` |

## Quick Rules

- **Never write the ScriptEnvelope format.** Only write Designer format (flat JSON with `id`, `name`, `version`, `steps`).
- **All parameter values are strings** — even numbers: `"ms": "1000"` not `"ms": 1000`.
- **Use the Go Orchestrator** (`orchestrator-go/`). The C# Orchestrator (`orchestrator/`) is legacy.
- **Start order matters:** Orchestrator → UI → Hello World API → Desktop Runtime.
