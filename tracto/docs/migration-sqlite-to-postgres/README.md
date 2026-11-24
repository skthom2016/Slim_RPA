# Moving Tracto from SQLite to PostgreSQL

Tracto's services now keep all their data in PostgreSQL, and SQLite is gone from the repository. This document says what was there, what it became, what behaves differently, and how to run and test it. The full schema, in one place for reading, is in [schema.sql](schema.sql).

## What was there

| Component | SQLite file | Tables |
|---|---|---|
| `orchestrator-go` | `orchestrator.db` (`ORCHESTRATOR_DB_PATH`) | 16: runs, workers, journal, leases, artifacts and their contents, schedules, final results, run outputs, callbacks, continuations, telemetry, run logs, security events, resolutions |
| `sheet-service`, sign-ins | `sheet-service.db` (`SHEET_SERVICE_DB`) | `ms_tokens` |
| `sheet-service`, batch progress | `sheet-batches.db` (`SHEET_SERVICE_BATCH_DB`) | `process_batches`, `process_items` |
| The C# orchestrator (`tracto/orchestrator`, and a copy at the repository root, `Tracto.Orchestrator`) | EF Core SQLite | Legacy: `orchestrator-go` had replaced it |

Nothing else used SQLite: the desktop, browser and terminal runtimes, the designer, the orchestrator UI and the hello-world API have no database.

## What it is now

| Point | Choice |
|---|---|
| Server | PostgreSQL 16 or later. Locally: `docker compose up -d postgres` (see `tracto/docker-compose.yml`) |
| Database and schemas | One database, `tracto`. Schema `orchestrator` for `orchestrator-go`, schema `sheets` for `sheet-service`. A service creates its own schema and tables when it starts; neither reads the other's tables |
| Connection | `DATABASE_URL`, for example `postgres://tracto:<password>@localhost:5432/tracto?sslmode=disable`, read from the environment or from the service's git-ignored `.env`. Each pool sets its `search_path` to its schema |
| Driver | `github.com/jackc/pgx/v5` through `database/sql`; `modernc.org/sqlite` and its dependencies are removed from both `go.mod` files |
| Schema changes | Numbered SQL files embedded in each binary, applied at start-up in a transaction under an advisory lock, recorded in the schema's `schema_migrations` table. A schema change is a new file; start-up applies only what is missing. Two services starting together do not collide |
| Existing data | Not carried over (agreed). Start empty: publish the scripts again and sign in to Microsoft once more |

### Column types

| Before | Now | Why |
|---|---|---|
| `TEXT` holding an RFC 3339 time | `TIMESTAMPTZ` | Times were compared as text. `time.RFC3339Nano` drops trailing zeros, so a text comparison could be wrong by up to a second. The API still returns the same strings (UTC, ending in `Z`) |
| `INTEGER` 0/1 flags | `BOOLEAN` | |
| `BLOB` (`ArtifactContents.Payload`) | `BYTEA` | |
| `TEXT` holding JSON (`*Json` columns) | `TEXT`, unchanged | Callback payloads are HMAC-signed, so they are stored exactly as received, and the columns accept what they always accepted. The handlers read them as JSON |
| `INTEGER` Unix seconds (`ms_tokens`) | `TIMESTAMPTZ` | One kind of time everywhere |
| `TEXT` `''` for "not finished" (`process_batches.finished_at`) | `NULL` | |
| column `row` (`process_items`) | `row_no` | `ROW` is a keyword in PostgreSQL |
| — | `RunRecords.OfferExpiresAt` (new) | See "Concurrent workers" below |

Table and column names otherwise stay as they were, unquoted; PostgreSQL stores them in lower case and the queries match them as written.

## What changed in the code

**`orchestrator-go`**

- `internal/db`: PostgreSQL pool and migrator (`db.go`), the schema as `migrations/001_initial.sql`; the SQLite schema, the `addColumns` and `PRAGMA table_info` machinery are gone.
- `internal/server`: the HTTP router moved out of `main.go`, so the binary and the tests serve the same API. `main.go` reads `DATABASE_URL`, loads `.env`, and runs in UTC (the whole service keeps and reports UTC).
- Every query: placeholders `?` became `$1, $2, …` (about 530), `INSERT OR REPLACE` became `ON CONFLICT … DO UPDATE`, `substr`/`instr` became `split_part`, flag literals became `TRUE`/`FALSE`, flags scan into `bool`.
- Transactions use a small helper (`txRun`) that keeps the first error of a run of statements and rolls back. PostgreSQL aborts a transaction at its first failed statement, so a half-written change cannot be committed, and a handler reports the real cause.
- `internal/services`: the deadline monitor's work is now `TimeOutOverdue`, so it can be tested; every background update names the state it expects.

**`sheet-service`**

- `internal/pg`: pool, migrator and the embedded `sheets` migration. `internal/msauth` and `internal/process` use it.
- The Microsoft token refresh lock moved from `BEGIN IMMEDIATE` on a file to a PostgreSQL advisory lock per sign-in, held for the transaction, so two processes never refresh the same single-use refresh token at once.
- The saved Microsoft tokens are now **encrypted** (AES-256-GCM, `internal/secretbox`) under `TRACTO_SECRET_KEY` before they are written: the database is a server several people can reach, unlike a local file. The service refuses to start without the key. The same helper is meant for the mail account passwords of the SIS flows.

**Repository**

- Deleted: the C# orchestrator (both copies) and its entries in `tracto.slnx`.
- `run-e2e.ps1` now starts the Go orchestrator on a schema of its own (`e2e_<number>`, dropped at the end) instead of the C# one.
- `docker-compose.yml` (PostgreSQL), `.env.example` for both services, `.gitignore` for `.env` and stray `*.db`.
- README, CLAUDE.md, the project maps, the onboarding guide, the SharePoint setup note and the SIS documents say PostgreSQL where they said SQLite.

## Behaviour that is different

The migration found things SQLite had been hiding, because it ran one statement at a time. The fixes change behaviour in these cases:

| Case | Before | Now |
|---|---|---|
| Two workers pull one after another while a run is offered but not yet accepted | Both were offered the same run (the run stays `Queued` until accepted) | The offer is recorded on the run (`WorkerId`, `OfferExpiresAt`); another worker is not offered it until the offer runs out |
| Two workers pull at the same instant | Could not happen | `SELECT … FOR UPDATE SKIP LOCKED`; each gets a different run, or `NoPendingJob` |
| Result posted twice, or twice at once | A second final result could be written | The run is closed once; the repeat gets the idempotent answer |
| Accept after the offer was cancelled or expired | Moved the run to Running regardless | `409 LEASE_EXPIRED` |
| Cancel of a queued run that a worker has just started | Overwrote Running with Cancelled | `409 STATE_CHANGED` |
| Two people resolve the same Unknown run | Both succeeded | One `200`, the others `409 INVALID_STATE` |
| Lease expiry racing a result; deadline racing a result | Could requeue or time out a finished run | Only a run still in the expected state is touched |
| A database error inside a handler | Often ignored; the response claimed success | `500 DB_ERROR` with the cause, the transaction rolled back |
| A log entry whose `occurredAt` is not a valid time | Stored as sent | Stored with the receive time instead (the column is a real time now) |
| Publishing the same version from two requests at once | Undefined | One `201`; the other `201` or `409 VERSION_CONFLICT` |

## Concurrent workers

PostgreSQL serves many requests at once, so the code had to be told what the single-connection SQLite file provided for free:

- A worker claims a run with `FOR UPDATE SKIP LOCKED`. When a second worker finally locks a row the first has just released, PostgreSQL re-checks that row's own columns, not other tables: this is why the offer lives on the run row (`WorkerId`, `OfferExpiresAt`) and not only in `LeaseRecords`. A check against the lease table passed in about one concurrent pull in six; the test below caught it.
- Accept, heartbeat, result, cancel, expiry, timeout and resolve each update the run only when it is in the state they expect, and look at the row count.
- The schedule claim (compare-and-set on `NextRunAt`) already worked and is unchanged.

## Running it

```powershell
cd tracto
$env:TRACTO_DB_PASSWORD = "<choose one>"
docker compose up -d postgres
# orchestrator-go\.env and sheet-service\.env: DATABASE_URL=postgres://tracto:<password>@localhost:5432/tracto?sslmode=disable
# sheet-service\.env also: TRACTO_SECRET_KEY=<32 random bytes, base64>   (how: sheet-service\.env.example)
cd orchestrator-go ; go run .
cd ..\sheet-service ; go run .
```

## Tests

| Where | What |
|---|---|
| `orchestrator-go/internal/server` | The whole API over HTTP on a real PostgreSQL: the life of a job; fences and validation; queue-full, oldest-first and runtime matching; eight workers pulling at once, fifteen rounds, never the same run; an offer that runs out; cancel; duplicate results at once; Unknown and five people resolving at once; filters and limits; reconcile; artifacts including eight publishes at once; segmented scripts (order, outputs passed on, failure, cancel); workers; callbacks including eight identical at once; logs; continuations; schedules; admin |
| `orchestrator-go/internal/services` | Lease expiry (requeue, Unknown, a parent taken with its segment, a healthy lease left alone, no second requeue), deadlines, restart recovery |
| `orchestrator-go/internal/handlers`, `…/scheduler` | The schedule tests that already existed, now on PostgreSQL |
| `orchestrator-go/internal/db` | Every table and index exists; migrations repeatable; six services starting at once apply the schema once; a bad URL or schema name is refused; the password never appears in an error; times come back in UTC |
| `sheet-service` | The existing suite on PostgreSQL, plus: migrations, concurrent start-up, token encryption at rest, a wrong key cannot read the tokens, the refresh lock serialises twelve concurrent refreshes with no update lost, a failed refresh saves nothing |
| `tracto/e2e` through `run-e2e.ps1` | The 13 end-to-end tests (health, job lifecycle against the real orchestrator, Hello World, the Notepad workflow) |

**About the end-to-end suite.** It passes against the migrated orchestrator (13 of 13 on a clean run), but two of its tests are not reliable, and neither is caused by the migration:

- `JobLifecycleTests` now and then fails one test within a few milliseconds with "the I/O operation has been aborted": the first request of the test's shared HTTP client is dropped before the orchestrator sees it. The same failure appears against the original SQLite orchestrator built from the commit before the migration (checked: it failed one run in three there as well), so it is in the test client, not in the server. The orchestrator log shows no error.
- `NotepadHelloWorldTests.TC03` opens a real Notepad window and sends keystrokes; it fails at its last check (the saved file is empty) whenever the desktop does not give Notepad the focus in time. Every orchestrator step before it passes. Run it on an idle, unlocked desktop, or skip it with `.\run-e2e.ps1 -Filter "FullyQualifiedName!~Notepad"`.

`run-e2e.ps1` stops the services and drops its schema however the run ends, and refuses to start if port 5100 or 5200 is already in use (an earlier version could leave a stale orchestrator behind and test against it).

The Go tests need a database: `TEST_DATABASE_URL`, or `DATABASE_URL`, from the environment or a `.env` in the project or a parent folder; without one they are skipped, not failed. Each test package creates a schema of its own and drops it at the end, so tests never touch real data. Result at the time of writing: all pass, none skipped.

## Points to confirm

- **Backups.** The data now sits in a server, not in files beside the services. Where the volume lives, how it is backed up and who may reach port 5432 are decisions for whoever runs it; the compose file binds it to `127.0.0.1`.
- **The encryption key.** `TRACTO_SECRET_KEY` is kept in the service's `.env` or environment. Losing it means signing in to Microsoft again; leaking it with the database exposes the tokens. Whether it should instead be protected by DPAPI or held in a vault is open.
- **Other settings that were in the old files.** `ORCHESTRATOR_DB_PATH`, `SHEET_SERVICE_DB` and `SHEET_SERVICE_BATCH_DB` are no longer read.
