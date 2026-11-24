# Moving Tracto from SQLite to PostgreSQL

Everything that has to change to remove SQLite from the repository and run every Tracto service on PostgreSQL. The complete target schema is in [schema.sql](schema.sql).

## Where SQLite is used today

| Component | Status | SQLite file | Tables | Code |
|---|---|---|---|---|
| `orchestrator-go` | Active | `orchestrator.db` (`ORCHESTRATOR_DB_PATH`) | 17: runs, workers, journal, leases, artifacts and their contents, schedules, final results, run outputs, callbacks, continuations, telemetry, run logs, security events, resolutions | `internal/db/db.go`, `internal/db/schema.go`, every file under `internal/handlers`, `internal/scheduler`, `internal/services`, `internal/middleware/audit.go`, `main.go` |
| `sheet-service`, sign-ins | Active | `sheet-service.db` (`SHEET_SERVICE_DB`) | `ms_tokens` | `internal/msauth/store.go`, `internal/msauth/msauth.go` |
| `sheet-service`, batches | Active | `sheet-batches.db` (`SHEET_SERVICE_BATCH_DB`) | `process_batches`, `process_items` | `internal/process/store.go`, `main.go` |
| C# orchestrator, `tracto/orchestrator` | Legacy, replaced by `orchestrator-go` | EF Core SQLite | EF model | `Tracto.Orchestrator.Infrastructure/Persistence/*`, `Program.cs`, tests |
| C# orchestrator, `Tracto.Orchestrator` (repository root) | Legacy copy of the above | EF Core SQLite | EF model | Same files |
| `tracto/run-e2e.ps1` | Drives the C# orchestrator | `tracto-e2e.db` | | Script parameter `-DbPath` |

Nothing else uses SQLite: the desktop, browser and terminal runtimes, the designer, the orchestrator UI and the hello-world API have no database. No `.db` file is committed.

Go driver: `modernc.org/sqlite` in both `go.mod` files.

## Target

| Point | Choice |
|---|---|
| Server | PostgreSQL 16 or later. Locally, the existing PostgreSQL container in Docker Desktop |
| Database | One database, `tracto` |
| Schemas | `orchestrator` for `orchestrator-go`, `sheets` for `sheet-service`. Each service owns its schema; neither reads the other's tables |
| Role | One login role, `tracto`, owning both schemas. Its password is set on the server and given to the services through the environment, never committed |
| Connection | `DATABASE_URL`, for example `postgres://tracto:<password>@localhost:5432/tracto?sslmode=disable`. Each service sets its own `search_path` (`orchestrator` or `sheets`) on its connections |
| Driver | `github.com/jackc/pgx/v5`, used through `database/sql` (`pgx/v5/stdlib`), so the code keeps `*sql.DB`, `Query`, `Exec` and transactions |
| Schema changes | Numbered SQL files embedded in each service (`go:embed`), applied in order at start-up inside a transaction, recorded in the schema's `schema_migrations` table. `schema.sql` is version 1 |
| Existing data | Not carried over. The SQLite files hold local development and test data only; the services start on an empty database. A one-off copy script can be added later if a machine's history is needed |

### Column types

| SQLite today | PostgreSQL | Why |
|---|---|---|
| `TEXT` holding an RFC 3339 time | `TIMESTAMPTZ` | Today times are compared as text. `time.RFC3339Nano` drops trailing zeros, so `...:05Z` sorts after `...:05.5Z` and a text comparison can be wrong by up to a second. A real time column compares correctly |
| `INTEGER` 0/1 flags (`NeedsAttention`, `IsOnline`, `IsDeleted`, `SignatureValid`, `Enabled`, `Processed`, `CancellationRequested`) | `BOOLEAN` | |
| `BLOB` (`ArtifactContents.Payload`) | `BYTEA` | |
| `TEXT` holding JSON (`*Json` columns) | `JSONB` | Valid JSON is enforced; an empty string must be written as `NULL` |
| `INTEGER` Unix seconds (`ms_tokens.access_expires`, `updated_at`) | `TIMESTAMPTZ` | One kind of time everywhere |
| `TEXT` `''` for "not finished" (`process_batches.finished_at`) | `NULL` | |
| `row` column (`process_items`) | `row_no` | `ROW` is a keyword in PostgreSQL |

Table and column names otherwise stay as they are, unquoted. PostgreSQL stores them in lower case, and the existing unquoted queries match them unchanged.

## Code changes: `orchestrator-go`

### Opening the database

- `internal/db/db.go`: replace `Open(path)` with `Open(ctx, databaseURL)`. It opens a pool through `pgx/v5/stdlib`, sets `search_path=orchestrator`, pings, then runs the migrations. Remove `SetMaxOpenConns(1)`; set a pool size (for example 10 open, 5 idle) and a connection lifetime.
- Delete `addColumns`, `hasColumn` (`PRAGMA table_info`) and `addIndexes`: the columns and indexes they add are in version 1 of the schema.
- `internal/db/schema.go`: replace with `internal/db/migrations/001_initial.sql` (the `orchestrator` part of `schema.sql`) and a small migrator. Remove `PRAGMA journal_mode` and `PRAGMA foreign_keys`.
- `main.go`: read `DATABASE_URL` instead of `ORCHESTRATOR_DB_PATH`; refuse to start without it; log the host and database name, never the password.
- `go.mod`: remove `modernc.org/sqlite` and its indirect dependencies (`modernc.org/libc`, `mathutil`, `memory`, `strutil`, `token`, `gc/v3`, `remyoudompheng/bigfft`, `ncruces/go-strftime`, `dustin/go-humanize`, `mattn/go-isatty` where only SQLite pulled them); add `github.com/jackc/pgx/v5`.

### Queries

| Change | Where | Count |
|---|---|---|
| Placeholders `?` become `$1`, `$2`, … | Every query | About 560 placeholders in 20 files: `result.go` 78, `segments.go` 71, `services.go` 53, `artifacts.go` 50, `cancel.go` 44, `schedules.go` 41, `jobs.go` 38, `lease.go` 32, `runs.go` 29, `reconcile.go` 28, `workers.go` 20, `logs.go` 18, `scheduler.go` 16, `admin.go` 10, `continuations.go` 8, `callbacks.go` 8, `audit.go` 7, `enqueue.go` 3, `telemetry.go` 2 |
| `INSERT OR REPLACE` becomes `INSERT … ON CONFLICT (key) DO UPDATE SET …` | `artifacts.go` line 175 (`ArtifactContents`), `result.go` line 143 and `segments.go` line 358 (`RunOutputs`), `segments.go` line 403 (`FinalResultRecords`, conflict on `RunId`) | 4 |
| `substr(r.EventId, 1, instr(r.EventId, ':') - 1)` becomes `split_part(r.EventId, ':', 1)` | `schedules.go` line 276 (completed runs list) | 1 |
| Flag literals `=1` / `=0` become `TRUE` / `FALSE`, and flags are scanned into `bool` | `admin.go`, `artifacts.go`, `cancel.go`, `lease.go`, `reconcile.go`, `services.go`, `workers.go`, `scheduler.go`, `schedules.go` | |
| Times are passed and scanned as `time.Time` (or `sql.NullTime`), not formatted strings. `helpers.go` `nowStr()` becomes `now()` returning `time.Time`; JSON replies format them with `time.RFC3339Nano` as today, so the API does not change | Every file that writes or reads a `…At`, `Deadline` or `NextRunAt` column | |
| `COALESCE(LastHeartbeatAt, AcceptedAt, '')` becomes `COALESCE(LastHeartbeatAt, AcceptedAt)` | `services.go` line 32 (stale lease check) | 1 |
| `cronspec.Format(now)` passed to `NextRunAt<=?` becomes the `time.Time` itself; `cronspec.Format` and `ParseTime` stay only for the API's text form | `scheduler.go`, `schedules.go` | |
| JSON columns: write `NULL` instead of `''`; keep writing the marshalled JSON text | `jobs.go`, `segments.go`, `workers.go`, `callbacks.go`, `reconcile.go`, `services.go`, `result.go` | |
| `ArtifactContents.Payload` is written and read as `[]byte` (unchanged in Go; the column becomes `BYTEA`) | `artifacts.go`, `jobs.go` | |

### Concurrency

SQLite with one connection ran every statement one after another, so the code never needed row locks. PostgreSQL with a pool runs requests at the same time, so four places must be made safe:

1. **A job is taken by two workers.** `jobs.go` (`POST /jobs/pull`) reads the oldest queued run, then writes the lease in a second statement. Replace with one atomic claim inside the transaction:

   ```sql
   SELECT Id, AttemptId, ScriptArtifactId, ScriptVersion, InputRefsJson, SecretRefsJson, InteractiveSessionId
   FROM RunRecords
   WHERE State = 'Queued' AND (RuntimeType = '' OR RuntimeType = $1)
   ORDER BY CreatedAt
   LIMIT 1
   FOR UPDATE SKIP LOCKED
   ```

   followed by the lease insert and the run update in the same transaction.
2. **Lease accept, heartbeat, result and cancel race each other.** `lease.go`, `result.go`, `cancel.go`, `reconcile.go` and `services.go` read a run's state and then update it. Each update carries its expected state and attempt in the `WHERE` clause (`… WHERE Id=$1 AND AttemptId=$2 AND State='Running'`) and checks `RowsAffected`, or the read is `SELECT … FOR UPDATE` inside the transaction.
3. **A schedule fires twice.** `scheduler.go` already advances `NextRunAt` with a compare-and-set update and checks `RowsAffected`; that stays and works as it is.
4. **Errors inside a transaction.** Most `tx.Exec` calls ignore their error. In SQLite a failed statement leaves the transaction usable; in PostgreSQL it aborts the whole transaction, and every later statement fails with "current transaction is aborted". Every `tx.Exec` and `tx.QueryRow` inside a transaction checks its error and returns it, so a failure rolls back and is reported instead of committing half a change or nothing.

### Tests

- `internal/handlers/schedules_test.go` and `internal/scheduler/scheduler_test.go` open a SQLite file in a temporary folder. They switch to a test helper that connects to `TEST_DATABASE_URL`, creates a schema with a random name, runs the migrations into it, and drops it when the test ends. When `TEST_DATABASE_URL` is not set, these tests are skipped with a message.
- New tests for the four concurrency cases above: two pulls at once take two different runs or one run and `NoPendingJob`; a result and a cancel at once leave one consistent state.

## Code changes: `sheet-service`

### Sign-ins (`internal/msauth`)

- `store.go`: `OpenStore(path)` becomes `OpenStore(ctx, databaseURL)` with `search_path=sheets`; the per-path store cache keys on the URL.
- `Locked` uses `BEGIN IMMEDIATE` to stop two processes refreshing the same sign-in at once. It becomes a transaction that first takes `SELECT pg_advisory_xact_lock(hashtext('ms_tokens:' || $1))` for the sign-in name, then reads, calls `fn`, and upserts. The lock is released at commit or rollback.
- Placeholders become `$n`; the upsert keeps `ON CONFLICT (name) DO UPDATE` (`excluded` works the same).
- `access_expires` and `updated_at` are written and read as `time.Time` instead of Unix seconds.
- `msauth.go`: `SHEET_SERVICE_DB` and `DefaultDBFile` go; the store comes from `DATABASE_URL`. `sheet/graph_auth.go` comment updated.

### Batches (`internal/process`)

- `store.go`: `OpenStore(path)` becomes `OpenStore(ctx, databaseURL)` with `search_path=sheets`; `SetMaxOpenConns(1)` goes.
- `now()` returns `time.Time`; `started_at`, `updated_at` and `finished_at` are times, `finished_at` is `NULL` until the batch ends, and the reply formats them as RFC 3339 as today.
- Column `row` is renamed `row_no` in every query.
- `SetItem`'s `CASE WHEN ?='' THEN run_id ELSE ? END` becomes `CASE WHEN $3 = '' THEN run_id ELSE $3 END` (one parameter used twice).
- `main.go`: `SHEET_SERVICE_BATCH_DB` goes; both stores share one pool from `DATABASE_URL`.

### Schema

`internal/db/migrations/001_initial.sql` (the `sheets` part of `schema.sql`) and the same small migrator as the orchestrator, so the service creates its tables at start-up.

### Dependencies

`go.mod`: remove `modernc.org/sqlite` and the indirect packages only it needed; add `github.com/jackc/pgx/v5`.

## Credentials

- The database password is only in the environment of each service (`DATABASE_URL`), or in a `.env` file next to the service that is ignored by git. The existing `.env` loading in `sheet-service` (`internal/envfile`) reads it; the orchestrator gets the same.
- `ms_tokens.refresh_token` is a credential. Moving it to a shared database server makes encryption more important than it was in a local file: the store encrypts it with AES-GCM under a key from `TRACTO_SECRET_KEY` before writing, and decrypts after reading. The SIS mail account table (story H3) uses the same key and helper.

## Removing the legacy C# orchestrator

`tracto/orchestrator` and the repository-root `Tracto.Orchestrator` are the C# orchestrator that `orchestrator-go` replaced; the project map marks it "legacy, not in active use". It is the rest of the SQLite in the repository (EF Core SQLite provider, `OrchestratorDbContext`, its repositories and tests). Both folders are deleted, with `tracto/run-e2e.ps1`, which builds and drives that C# orchestrator against `tracto-e2e.db`. The `tracto.slnx` entries for those projects are removed. The documents under `Final/` and the sprint reviews are historical and stay as they are.

## Documents and scripts to update

| File | Change |
|---|---|
| `tracto/README.md` | Start-up: PostgreSQL first, `DATABASE_URL`; remove the "SQLite lock" restart advice |
| `tracto/CLAUDE.md` | Component table (`Go + PostgreSQL`); remove the "SQLite lock" quirk |
| `tracto/PROJECT-MAP.md`, `tracto/docs/PROJECT-MAP.md` | Orchestrator stack line; remove the C# orchestrator section |
| `tracto/orchestrator-go/README.md` | Configuration: `DATABASE_URL` instead of `ORCHESTRATOR_DB_PATH` |
| `tracto/sheet-service/README.md`, `sheet-service/docs/microsoft-sharepoint-setup.md` | `DATABASE_URL` instead of `SHEET_SERVICE_DB` and `SHEET_SERVICE_BATCH_DB`; where the sign-in is saved |
| `tracto/docs/TEAMMATE-ONBOARDING.md` | Local setup: start PostgreSQL in Docker Desktop, create the database once |
| `tracto/docs/UIPATH-TRACTO-CAPABILITY-MAPPING.md` | Storage line |
| `tracto/docs/SIS/*` | "SQLite table" becomes "PostgreSQL table" for the work queue and the token store |
| `.gitignore` | Ignore `.env` and `*.db`, so a database URL or a stray local file is never committed |

## Local setup

1. Start Docker Desktop and the existing PostgreSQL container.
2. Once, as the database superuser: create the role `tracto`, the database `tracto` and the two schemas (the commented block at the top of `schema.sql`).
3. Put `DATABASE_URL` (and `TRACTO_SECRET_KEY`) in each service's `.env`.
4. Start `orchestrator-go` and `sheet-service`; each applies its migrations and logs the schema version.

## Order of work

1. Database, role and schemas on the local PostgreSQL; `.env` for both services.
2. `sheet-service`: driver, migrator, the two stores, tests. It is the smaller of the two and proves the pattern.
3. `orchestrator-go`: driver, migrator, placeholders, types, the four concurrency fixes, error checks in transactions, tests.
4. Run the full stack: upload a script, run a job on the desktop runtime, a segmented job, a schedule, a sheet batch, and a Microsoft sign-in refresh.
5. Delete the legacy C# orchestrator folders and `run-e2e.ps1`; update the documents above.
6. Check: a search of the repository for `sqlite` (any case) finds only the historical documents.

## Points to confirm

- The PostgreSQL container to use locally: its name, port and superuser, so the role and database can be created.
- Whether the legacy C# orchestrator folders may be deleted, or should be kept unbuilt for reference.
- Whether any machine's existing SQLite history (runs, schedules, artifacts, the saved Microsoft sign-in) needs to be copied over. The default is a fresh start: scripts are published again and the sign-in is done once more.
