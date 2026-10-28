# Tracto Orchestrator — Go

A full functional replica of the .NET Orchestrator rewritten in Go.

## Why Go

- Single binary, ~50 ms startup (vs ~4 s for dotnet)
- ~10–20 MB RAM (vs ~100 MB)
- No DLL lock issues on Windows
- No EF Core migration state — schema applied as `CREATE TABLE IF NOT EXISTS` on every startup
- Pure-Go SQLite driver (`modernc.org/sqlite`) — no CGO, no external runtime

## Running

```powershell
# Development (isolated, no JWT enforcement)
$env:ORCHESTRATOR_PORT    = "5080"
$env:ORCHESTRATOR_DB_PATH = "orchestrator.db"
$env:TRACTO_MODE          = "Isolated"      # or Connected
go run .
```

## Environment variables

| Variable                   | Default              | Description                                         |
|---------------------------|----------------------|-----------------------------------------------------|
| `ORCHESTRATOR_PORT`        | `5080`               | HTTP listen port                                    |
| `ORCHESTRATOR_DB_PATH`     | `orchestrator.db`    | SQLite database file path                           |
| `TRACTO_MODE`              | `Isolated`           | `Isolated` → bind 127.0.0.1 / `Connected` → 0.0.0.0 |
| `ORCHESTRATOR_JWT_SECRET`  | *(empty)*            | HMAC-SHA256 signing key; empty = parse-only mode   |
| `ORCHESTRATOR_ADMIN_CIDR`  | *(empty)*            | CIDR to allow for /admin; empty = open             |
| `ORCHESTRATOR_HMAC_KEYS`   | *(empty)*            | `keyId1:secret1,keyId2:secret2` for callbacks      |

## API Endpoints (functional parity with .NET)

| Method | Path                                | Description                         |
|--------|-------------------------------------|-------------------------------------|
| GET    | `/health`                           | Health check                        |
| POST   | `/workers/register`                 | Register / re-register worker       |
| POST   | `/workers/{workerId}/offline`       | Mark worker offline                 |
| GET    | `/workers`                          | List all workers                    |
| POST   | `/jobs`                             | Create (enqueue) a job              |
| POST   | `/jobs/pull`                        | Pull next matching job (workers)    |
| GET    | `/jobs/{runId}`                     | Get run state                       |
| POST   | `/jobs/{runId}/lease/accept`        | Accept offered lease                |
| POST   | `/jobs/{runId}/heartbeat`           | Send heartbeat                      |
| POST   | `/jobs/{runId}/result`              | Commit final result                 |
| POST   | `/jobs/{runId}/reconcile`           | Reconcile disconnected run          |
| POST   | `/jobs/{runId}/cancel`              | Cancel run                          |
| PUT    | `/artifacts/{id}/versions/{ver}`    | Publish artifact                    |
| GET    | `/artifacts/{id}/versions/{ver}`    | Fetch artifact                      |
| DELETE | `/artifacts/{id}/versions/{ver}`    | Delete artifact                     |
| POST   | `/callbacks`                        | Receive HMAC-signed callback        |
| GET    | `/admin/runs`                       | List runs needing attention         |
| POST   | `/admin/runs/{runId}/resolve`       | Admin resolve UNKNOWN run           |
| GET    | `/admin/security-events`            | View security event log             |
| GET    | `/admin/telemetry`                  | List telemetry events               |
| GET    | `/admin/telemetry/{runId}`          | Telemetry for a run                 |
| POST   | `/admin/schedules`                  | Create cron schedule                |
| GET    | `/admin/schedules`                  | List schedules                      |
| PATCH  | `/admin/schedules/{scheduleId}`     | Enable/disable schedule             |
| DELETE | `/admin/schedules/{scheduleId}`     | Delete schedule                     |
| POST   | `/admin/continuations`              | Create continuation mapping         |
| GET    | `/admin/continuations`              | List continuation mappings          |
| DELETE | `/admin/continuations/{mappingId}`  | Delete continuation mapping         |

## Background services

- **LeaseExpiryMonitor** — every 15 s: expires stale leases (no heartbeat for 45 s), requeues or marks Unknown
- **DeadlineMonitor** — every 30 s: marks runs past their deadline as TimedOut
- **RestartRecovery** — once at startup: marks absent workers offline, requeues their running jobs

## Compatibility

This Go service speaks the exact same HTTP/JSON protocol as the .NET Orchestrator.
Desktop Runtime, Browser Runtime, and Terminal Runtime all connect without any changes.
