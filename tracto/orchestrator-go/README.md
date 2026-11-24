# Tracto Orchestrator — Go

The job queue, artifact store and scheduler of Tracto: one Go binary, a REST API on port 5080, and a PostgreSQL database.

## Why Go

- Single binary, ~50 ms startup
- ~10–20 MB RAM
- No DLL lock issues on Windows
- The schema is built from numbered SQL files embedded in the binary and applied at start-up (`internal/db/migrations`), so there is no separate migration step

## Database

The orchestrator keeps everything in PostgreSQL 16 or later, in a schema of its own (`orchestrator`) inside the `tracto` database. It creates the schema and its tables when it starts. Locally:

```powershell
cd ..                                   # the tracto folder
$env:TRACTO_DB_PASSWORD = "<choose one>"
docker compose up -d postgres
```

Then put the connection string in `orchestrator-go\.env` (copy `.env.example`; `.env` is ignored by git):

```
DATABASE_URL=postgres://tracto:<password>@localhost:5432/tracto?sslmode=disable
```

## Running

```powershell
go run .
```

The service reads `.env` next to the executable and in the working directory; real environment variables win.

## Environment variables

| Variable                   | Default              | Description                                         |
|---------------------------|----------------------|-----------------------------------------------------|
| `DATABASE_URL`             | *(required)*         | PostgreSQL connection string                        |
| `ORCHESTRATOR_SCHEMA`      | `orchestrator`       | Schema for the orchestrator's tables                |
| `ORCHESTRATOR_PORT`        | `5080`               | HTTP listen port                                    |
| `TRACTO_MODE`              | `Isolated`           | `Isolated` → bind 127.0.0.1 / `Connected` → 0.0.0.0 |
| `ORCHESTRATOR_JWT_SECRET`  | *(empty)*            | HMAC-SHA256 signing key; empty = parse-only mode   |
| `ORCHESTRATOR_ADMIN_CIDR`  | *(empty)*            | CIDR to allow for /admin; empty = open             |
| `ORCHESTRATOR_HMAC_KEYS`   | *(empty)*            | `keyId1:secret1,keyId2:secret2` for callbacks      |
| `ORCHESTRATOR_ENV_FILE`    | *(empty)*            | Read this file instead of the default `.env` files |

## Tests

```powershell
go test ./...
```

The tests run against a real PostgreSQL: they use `TEST_DATABASE_URL`, or `DATABASE_URL`, from the environment or from `.env`. Each test package creates a schema of its own and drops it at the end, so they never touch the orchestrator's real data. Without a database the tests that need one are skipped. They cover the whole API over HTTP (`internal/server`), including several workers pulling, results, publishes and callbacks arriving at the same moment, plus the lease expiry, deadline and restart recovery services, the schedules and the migrations.

The end-to-end suite in `..\e2e` runs against this orchestrator through `..\run-e2e.ps1`, on a schema of its own.

## API Endpoints

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

## How concurrent workers are kept apart

Several workers (and several requests) run against the database at the same time:

- A worker takes a job with `SELECT … FOR UPDATE SKIP LOCKED`; a run that was offered to another worker and whose offer has not run out (`OfferExpiresAt`) is skipped, so a run is never offered to two workers.
- Accepting a lease, committing a result, cancelling, expiring a lease and timing a run out each update the run only when it is still in the state they expect, and say so when it is not.
- A transaction keeps the first error of its statements and rolls back, instead of committing half a change.

## Compatibility

The HTTP/JSON protocol is the one the Desktop, Browser and Terminal runtimes already use; they connect without changes.
