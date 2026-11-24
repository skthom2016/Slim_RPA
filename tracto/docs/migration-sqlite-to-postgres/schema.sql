-- Tracto on PostgreSQL: the complete schema, for reading in one place.
--
-- THIS FILE IS A COPY. The services build their own tables at start-up from the migration files
-- they carry, and those files are the source of truth:
--   orchestrator-go/internal/db/migrations/001_initial.sql      schema "orchestrator"
--   sheet-service/internal/pg/migrations/sheets/001_initial.sql  schema "sheets"
-- Do not run this file; it exists so the whole data model can be read without opening two projects.
--
-- One database (tracto), two schemas, one login role (tracto) that owns both. A service creates its
-- schema when it starts, so the only manual step is the database and the role:
--
--   CREATE ROLE tracto LOGIN PASSWORD '<set on the server, never in the repository>';
--   CREATE DATABASE tracto OWNER tracto;
--
-- (docker compose up -d postgres does both.)
--
-- Names are unquoted, so PostgreSQL stores them in lower case and the queries match them as written.
-- Each service also keeps a schema_migrations table in its schema (version, applied_at).

-- ================================================================ schema orchestrator (orchestrator-go)
SET search_path = orchestrator;

-- orchestrator-go tables. Applied into the schema the connection's search_path names (orchestrator).
-- Columns named *Json hold the JSON text exactly as it was received.


CREATE TABLE RunRecords (
    Id                       TEXT        PRIMARY KEY,
    WorkflowId               TEXT        NOT NULL DEFAULT '',
    State                    TEXT        NOT NULL DEFAULT 'Created',
    WorkerId                 TEXT,
    AttemptId                TEXT        NOT NULL,
    CreatedAt                TIMESTAMPTZ NOT NULL,
    UpdatedAt                TIMESTAMPTZ,
    NeedsAttention           BOOLEAN     NOT NULL DEFAULT FALSE,
    UnknownReason            TEXT,
    BusinessJobId            TEXT,
    CorrelationId            TEXT        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    Deadline                 TIMESTAMPTZ,
    EventId                  TEXT,
    InputRefsJson            TEXT,
    SchemaVersion            TEXT,
    ScriptArtifactId         TEXT,
    ScriptVersion            TEXT,
    SecretRefsJson           TEXT,
    TriggerType              TEXT        NOT NULL DEFAULT 'Manual',
    RuntimeType              TEXT        NOT NULL DEFAULT '',
    RequiredCapabilitiesJson TEXT,
    InteractiveSessionId     TEXT,
    ParamsJson               TEXT,
    ParentRunId              TEXT,
    SegmentIndex             INTEGER,
    -- While a run is offered to a worker (Queued, WorkerId set) this is when the offer runs out;
    -- the run is claimable again after it. It lives on the run row so that two workers pulling at
    -- the same moment cannot both pass the check: PostgreSQL re-reads the locked row, not other tables.
    OfferExpiresAt           TIMESTAMPTZ
);
CREATE INDEX IX_RunRecords_State              ON RunRecords (State);
CREATE INDEX IX_RunRecords_WorkerId_State     ON RunRecords (WorkerId, State);
CREATE INDEX IX_RunRecords_EventId_CreatedAt  ON RunRecords (EventId, CreatedAt);
CREATE INDEX IX_RunRecords_Trigger            ON RunRecords (TriggerType, UpdatedAt);
CREATE INDEX IX_RunRecords_Queue              ON RunRecords (State, RuntimeType, CreatedAt);
CREATE INDEX IX_RunRecords_ParentRunId        ON RunRecords (ParentRunId);

CREATE TABLE WorkerRecords (
    WorkerId                TEXT        PRIMARY KEY,
    RuntimeType             TEXT        NOT NULL,
    State                   TEXT        NOT NULL DEFAULT 'Available',
    RegisteredAt            TIMESTAMPTZ NOT NULL,
    LastHeartbeatAt         TIMESTAMPTZ NOT NULL,
    LastRegisteredAt        TIMESTAMPTZ,
    AbsenceThresholdSeconds INTEGER     NOT NULL DEFAULT 60,
    IsOnline                BOOLEAN     NOT NULL DEFAULT TRUE,
    CapabilitiesJson        TEXT
);
CREATE INDEX IX_WorkerRecords_IsOnline ON WorkerRecords (IsOnline);

CREATE TABLE JournalEntries (
    Id          TEXT        PRIMARY KEY,
    RunId       TEXT        NOT NULL,
    AttemptId   TEXT,
    EventType   TEXT        NOT NULL,
    OccurredAt  TIMESTAMPTZ NOT NULL,
    Payload     TEXT
);
CREATE INDEX IX_JournalEntries_RunId      ON JournalEntries (RunId);
CREATE INDEX IX_JournalEntries_OccurredAt ON JournalEntries (OccurredAt);

CREATE TABLE LeaseRecords (
    LeaseId               TEXT        PRIMARY KEY,
    RunId                 TEXT        NOT NULL,
    AttemptId             TEXT        NOT NULL,
    WorkerId              TEXT        NOT NULL,
    ArtifactId            TEXT        NOT NULL,
    ArtifactVersion       TEXT        NOT NULL,
    ArtifactHash          TEXT        NOT NULL,
    OfferedAt             TIMESTAMPTZ NOT NULL,
    ExpiresAt             TIMESTAMPTZ NOT NULL,
    AcceptedAt            TIMESTAMPTZ,
    State                 TEXT        NOT NULL DEFAULT 'Offered',
    InteractiveSessionId  TEXT,
    LastHeartbeatAt       TIMESTAMPTZ,
    LastSeqNo             BIGINT      NOT NULL DEFAULT 0,
    CancellationRequested BOOLEAN     NOT NULL DEFAULT FALSE
);
CREATE INDEX IX_LeaseRecords_RunId_State             ON LeaseRecords (RunId, State);
CREATE INDEX IX_LeaseRecords_WorkerId_State          ON LeaseRecords (WorkerId, State);
CREATE INDEX IX_LeaseRecords_InteractiveSessionId    ON LeaseRecords (InteractiveSessionId);
CREATE INDEX IX_LeaseRecords_State_LastHeartbeatAt   ON LeaseRecords (State, LastHeartbeatAt);

CREATE TABLE ArtifactRecords (
    ArtifactId       TEXT        NOT NULL,
    Version          TEXT        NOT NULL,
    Name             TEXT,
    ParentArtifactId TEXT,
    SchemaVersion    TEXT        NOT NULL,
    ContentHash      TEXT        NOT NULL,
    StoredAt         TIMESTAMPTZ NOT NULL,
    IsDeleted        BOOLEAN     NOT NULL DEFAULT FALSE,
    SignatureValid   BOOLEAN     NOT NULL DEFAULT FALSE,
    SelectorContent  TEXT,
    PRIMARY KEY (ArtifactId, Version)
);
CREATE INDEX IX_ArtifactRecords_ArtifactId_IsDeleted ON ArtifactRecords (ArtifactId, IsDeleted);
CREATE INDEX IX_ArtifactRecords_ParentArtifactId     ON ArtifactRecords (ParentArtifactId);

CREATE TABLE ArtifactContents (
    ArtifactId TEXT  NOT NULL,
    Version    TEXT  NOT NULL,
    Payload    BYTEA NOT NULL,
    PRIMARY KEY (ArtifactId, Version)
);

CREATE TABLE ScheduleRecords (
    ScheduleId     TEXT        PRIMARY KEY,
    CronExpression TEXT        NOT NULL,
    ArtifactId     TEXT        NOT NULL,
    Version        TEXT        NOT NULL,
    Enabled        BOOLEAN     NOT NULL DEFAULT TRUE,
    CreatedAt      TIMESTAMPTZ NOT NULL,
    UpdatedAt      TIMESTAMPTZ,
    ParamsJson     TEXT,
    TimeZone       TEXT,
    NextRunAt      TIMESTAMPTZ,
    LastFireAt     TIMESTAMPTZ,
    LastRunId      TEXT,
    LastStatus     TEXT,
    LastError      TEXT,
    OverlapPolicy  TEXT,
    GraceSeconds   INTEGER
);
CREATE INDEX IX_ScheduleRecords_Enabled ON ScheduleRecords (Enabled);
CREATE INDEX IX_ScheduleRecords_Next    ON ScheduleRecords (Enabled, NextRunAt);

CREATE TABLE FinalResultRecords (
    Id               TEXT        PRIMARY KEY,
    RunId            TEXT        NOT NULL,
    AttemptId        TEXT        NOT NULL,
    TerminalState    TEXT        NOT NULL,
    NeedsAttention   BOOLEAN     NOT NULL DEFAULT FALSE,
    UnknownReason    TEXT,
    ErrorCategory    TEXT,
    ErrorMessage     TEXT,
    BusinessOutcome  TEXT,
    EvidenceRefsJson TEXT        NOT NULL DEFAULT '[]',
    BusinessJobId    TEXT,
    CompletedAt      TIMESTAMPTZ NOT NULL
);
CREATE UNIQUE INDEX IX_FinalResultRecords_RunId ON FinalResultRecords (RunId);

-- Values a run produced (storeAs outputs). For a segmented script the parent run collects the
-- outputs of every finished segment, and later segments receive them as params.
CREATE TABLE RunOutputs (
    RunId     TEXT        NOT NULL,
    Name      TEXT        NOT NULL,
    Value     TEXT        NOT NULL DEFAULT '',
    CreatedAt TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (RunId, Name)
);

CREATE TABLE CallbackRecords (
    Id            TEXT        PRIMARY KEY,
    EventId       TEXT        NOT NULL,
    CorrelationId TEXT        NOT NULL,
    KeyId         TEXT        NOT NULL,
    ReceivedAt    TIMESTAMPTZ NOT NULL,
    PayloadJson   TEXT       NOT NULL,
    Processed     BOOLEAN     NOT NULL DEFAULT FALSE
);
CREATE UNIQUE INDEX IX_CallbackRecords_EventId                 ON CallbackRecords (EventId);
CREATE INDEX        IF NOT EXISTS IX_CallbackRecords_CorrelationId_Processed ON CallbackRecords (CorrelationId, Processed);

CREATE TABLE ContinuationMappings (
    MappingId             TEXT        PRIMARY KEY,
    TriggerRunId          TEXT,
    TriggerState          TEXT        NOT NULL,
    TargetArtifactId      TEXT        NOT NULL,
    TargetArtifactVersion TEXT        NOT NULL,
    InputTemplate         TEXT,
    CreatedAt             TIMESTAMPTZ NOT NULL,
    UpdatedAt             TIMESTAMPTZ,
    Version               INTEGER     NOT NULL DEFAULT 1
);
CREATE INDEX IX_ContinuationMappings_TriggerRunId_TriggerState ON ContinuationMappings (TriggerRunId, TriggerState);
CREATE INDEX IX_ContinuationMappings_TriggerState              ON ContinuationMappings (TriggerState);

CREATE TABLE ContinuationRunRecords (
    Id                     TEXT        PRIMARY KEY,
    SourceRunId            TEXT        NOT NULL,
    TriggerState           TEXT        NOT NULL,
    RunBId                 TEXT        NOT NULL,
    MappingId              TEXT        NOT NULL,
    MappingVersionSnapshot INTEGER     NOT NULL,
    CreatedAt              TIMESTAMPTZ NOT NULL
);
CREATE UNIQUE INDEX IX_ContinuationRunRecords_SourceRunId_TriggerState ON ContinuationRunRecords (SourceRunId, TriggerState);
CREATE INDEX        IF NOT EXISTS IX_ContinuationRunRecords_SourceRunId              ON ContinuationRunRecords (SourceRunId);

CREATE TABLE TelemetryEvents (
    Id         TEXT        PRIMARY KEY,
    RunId      TEXT        NOT NULL,
    WorkerId   TEXT,
    LeaseId    TEXT,
    AttemptId  TEXT,
    EventType  TEXT        NOT NULL,
    OccurredAt TIMESTAMPTZ NOT NULL,
    Metadata   TEXT
);
CREATE INDEX IX_TelemetryEvents_RunId_OccurredAt ON TelemetryEvents (RunId, OccurredAt);
CREATE INDEX IX_TelemetryEvents_EventType        ON TelemetryEvents (EventType);

-- What a runtime logged while it ran a job (Warning and Error only).
CREATE TABLE RunLogs (
    Id         TEXT        PRIMARY KEY,
    RunId      TEXT        NOT NULL,
    AttemptId  TEXT,
    WorkerId   TEXT,
    Runtime    TEXT,
    Level      TEXT        NOT NULL,
    StepId     TEXT,
    Message    TEXT        NOT NULL,
    OccurredAt TIMESTAMPTZ NOT NULL,
    ReceivedAt TIMESTAMPTZ NOT NULL
);
CREATE INDEX IX_RunLogs_RunId_OccurredAt ON RunLogs (RunId, OccurredAt);
CREATE INDEX IX_RunLogs_Level            ON RunLogs (Level);

CREATE TABLE SecurityEvents (
    Id         TEXT        PRIMARY KEY,
    EventType  TEXT        NOT NULL,
    SourceIp   TEXT,
    OccurredAt TIMESTAMPTZ NOT NULL,
    Details    TEXT        NOT NULL
);
CREATE INDEX IX_SecurityEvents_OccurredAt ON SecurityEvents (OccurredAt);
CREATE INDEX IX_SecurityEvents_EventType  ON SecurityEvents (EventType);

CREATE TABLE ResolutionRecords (
    Id         TEXT        PRIMARY KEY,
    RunId      TEXT        NOT NULL,
    Resolution TEXT        NOT NULL,
    ResolvedBy TEXT,
    ResolvedAt TIMESTAMPTZ NOT NULL,
    Notes      TEXT
);
CREATE UNIQUE INDEX IX_ResolutionRecords_RunId ON ResolutionRecords (RunId);


-- ================================================================ schema sheets (sheet-service)
SET search_path = sheets;

-- sheet-service tables. Applied into the schema the connection's search_path names (sheets).

-- Saved Microsoft sign-ins. refresh_token and access_token hold encrypted values (see
-- internal/secretbox); the other columns are not secret.
CREATE TABLE ms_tokens (
    name           TEXT        PRIMARY KEY,
    tenant         TEXT        NOT NULL,
    client_id      TEXT        NOT NULL,
    refresh_token  TEXT        NOT NULL,
    access_token   TEXT        NOT NULL DEFAULT '',
    access_expires TIMESTAMPTZ NOT NULL DEFAULT 'epoch',
    updated_at     TIMESTAMPTZ NOT NULL
);

-- Progress of batches that run a child job for each sheet row.
CREATE TABLE process_batches (
    id          TEXT        PRIMARY KEY,
    state       TEXT        NOT NULL,
    source_path TEXT        NOT NULL,
    sheet       TEXT        NOT NULL,
    child       TEXT        NOT NULL,
    total       INTEGER     NOT NULL,
    message     TEXT        NOT NULL DEFAULT '',
    started_at  TIMESTAMPTZ NOT NULL,
    finished_at TIMESTAMPTZ
);
CREATE INDEX ix_process_batches_state ON process_batches (state);

CREATE TABLE process_items (
    batch_id   TEXT        NOT NULL REFERENCES process_batches (id) ON DELETE CASCADE,
    row_no     INTEGER     NOT NULL,
    label      TEXT        NOT NULL DEFAULT '',
    run_id     TEXT        NOT NULL DEFAULT '',
    state      TEXT        NOT NULL,
    message    TEXT        NOT NULL DEFAULT '',
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (batch_id, row_no)
);
CREATE INDEX ix_process_items_state ON process_items (state);
