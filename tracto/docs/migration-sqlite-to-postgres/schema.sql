-- Tracto on PostgreSQL: the complete schema replacing the three SQLite files
-- (orchestrator.db, sheet-service.db, sheet-batches.db).
--
-- One database, two schemas:
--   orchestrator  tables of orchestrator-go
--   sheets        tables of sheet-service
--
-- Names stay as they are in the code, unquoted, so PostgreSQL stores them in lower case and
-- the existing queries match them without quoting. Times are TIMESTAMPTZ (UTC), flags are
-- BOOLEAN, the script payload is BYTEA, and columns that hold a JSON document are JSONB.
--
-- This file is the first migration (version 1). Each service applies its own schema at start-up
-- and records it in its schema's schema_migrations table.

-- ---------------------------------------------------------------- database and roles (run once, as a superuser)
-- CREATE ROLE tracto LOGIN PASSWORD '<set on the server, never in the repository>';
-- CREATE DATABASE tracto OWNER tracto;
-- \c tracto
-- CREATE SCHEMA orchestrator AUTHORIZATION tracto;
-- CREATE SCHEMA sheets AUTHORIZATION tracto;

-- ================================================================ orchestrator
SET search_path = orchestrator;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version     INTEGER     PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS RunRecords (
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
    InputRefsJson            JSONB,
    SchemaVersion            TEXT,
    ScriptArtifactId         TEXT,
    ScriptVersion            TEXT,
    SecretRefsJson           JSONB,
    TriggerType              TEXT        NOT NULL DEFAULT 'Manual',
    RuntimeType              TEXT        NOT NULL DEFAULT '',
    RequiredCapabilitiesJson JSONB,
    InteractiveSessionId     TEXT,
    ParamsJson               JSONB,
    ParentRunId              TEXT,
    SegmentIndex             INTEGER
);
CREATE INDEX IF NOT EXISTS IX_RunRecords_State              ON RunRecords (State);
CREATE INDEX IF NOT EXISTS IX_RunRecords_WorkerId_State     ON RunRecords (WorkerId, State);
CREATE INDEX IF NOT EXISTS IX_RunRecords_EventId_CreatedAt  ON RunRecords (EventId, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_RunRecords_Trigger            ON RunRecords (TriggerType, UpdatedAt);
CREATE INDEX IF NOT EXISTS IX_RunRecords_Queue              ON RunRecords (State, RuntimeType, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_RunRecords_ParentRunId        ON RunRecords (ParentRunId);

CREATE TABLE IF NOT EXISTS WorkerRecords (
    WorkerId                TEXT        PRIMARY KEY,
    RuntimeType             TEXT        NOT NULL,
    State                   TEXT        NOT NULL DEFAULT 'Available',
    RegisteredAt            TIMESTAMPTZ NOT NULL,
    LastHeartbeatAt         TIMESTAMPTZ NOT NULL,
    LastRegisteredAt        TIMESTAMPTZ,
    AbsenceThresholdSeconds INTEGER     NOT NULL DEFAULT 60,
    IsOnline                BOOLEAN     NOT NULL DEFAULT TRUE,
    CapabilitiesJson        JSONB
);
CREATE INDEX IF NOT EXISTS IX_WorkerRecords_IsOnline ON WorkerRecords (IsOnline);

CREATE TABLE IF NOT EXISTS JournalEntries (
    Id          TEXT        PRIMARY KEY,
    RunId       TEXT        NOT NULL,
    AttemptId   TEXT,
    EventType   TEXT        NOT NULL,
    OccurredAt  TIMESTAMPTZ NOT NULL,
    Payload     TEXT
);
CREATE INDEX IF NOT EXISTS IX_JournalEntries_RunId      ON JournalEntries (RunId);
CREATE INDEX IF NOT EXISTS IX_JournalEntries_OccurredAt ON JournalEntries (OccurredAt);

CREATE TABLE IF NOT EXISTS LeaseRecords (
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
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_RunId_State             ON LeaseRecords (RunId, State);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_WorkerId_State          ON LeaseRecords (WorkerId, State);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_InteractiveSessionId    ON LeaseRecords (InteractiveSessionId);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_State_LastHeartbeatAt   ON LeaseRecords (State, LastHeartbeatAt);

CREATE TABLE IF NOT EXISTS ArtifactRecords (
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
CREATE INDEX IF NOT EXISTS IX_ArtifactRecords_ArtifactId_IsDeleted ON ArtifactRecords (ArtifactId, IsDeleted);
CREATE INDEX IF NOT EXISTS IX_ArtifactRecords_ParentArtifactId     ON ArtifactRecords (ParentArtifactId);

CREATE TABLE IF NOT EXISTS ArtifactContents (
    ArtifactId TEXT  NOT NULL,
    Version    TEXT  NOT NULL,
    Payload    BYTEA NOT NULL,
    PRIMARY KEY (ArtifactId, Version)
);

CREATE TABLE IF NOT EXISTS ScheduleRecords (
    ScheduleId     TEXT        PRIMARY KEY,
    CronExpression TEXT        NOT NULL,
    ArtifactId     TEXT        NOT NULL,
    Version        TEXT        NOT NULL,
    Enabled        BOOLEAN     NOT NULL DEFAULT TRUE,
    CreatedAt      TIMESTAMPTZ NOT NULL,
    UpdatedAt      TIMESTAMPTZ,
    ParamsJson     JSONB,
    TimeZone       TEXT,
    NextRunAt      TIMESTAMPTZ,
    LastFireAt     TIMESTAMPTZ,
    LastRunId      TEXT,
    LastStatus     TEXT,
    LastError      TEXT,
    OverlapPolicy  TEXT,
    GraceSeconds   INTEGER
);
CREATE INDEX IF NOT EXISTS IX_ScheduleRecords_Enabled ON ScheduleRecords (Enabled);
CREATE INDEX IF NOT EXISTS IX_ScheduleRecords_Next    ON ScheduleRecords (Enabled, NextRunAt);

CREATE TABLE IF NOT EXISTS FinalResultRecords (
    Id               TEXT        PRIMARY KEY,
    RunId            TEXT        NOT NULL,
    AttemptId        TEXT        NOT NULL,
    TerminalState    TEXT        NOT NULL,
    NeedsAttention   BOOLEAN     NOT NULL DEFAULT FALSE,
    UnknownReason    TEXT,
    ErrorCategory    TEXT,
    ErrorMessage     TEXT,
    BusinessOutcome  TEXT,
    EvidenceRefsJson JSONB       NOT NULL DEFAULT '[]',
    BusinessJobId    TEXT,
    CompletedAt      TIMESTAMPTZ NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_FinalResultRecords_RunId ON FinalResultRecords (RunId);

-- Values a run produced (storeAs outputs). For a segmented script the parent run collects the
-- outputs of every finished segment, and later segments receive them as params.
CREATE TABLE IF NOT EXISTS RunOutputs (
    RunId     TEXT        NOT NULL,
    Name      TEXT        NOT NULL,
    Value     TEXT        NOT NULL DEFAULT '',
    CreatedAt TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (RunId, Name)
);

CREATE TABLE IF NOT EXISTS CallbackRecords (
    Id            TEXT        PRIMARY KEY,
    EventId       TEXT        NOT NULL,
    CorrelationId TEXT        NOT NULL,
    KeyId         TEXT        NOT NULL,
    ReceivedAt    TIMESTAMPTZ NOT NULL,
    PayloadJson   JSONB       NOT NULL,
    Processed     BOOLEAN     NOT NULL DEFAULT FALSE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_CallbackRecords_EventId                 ON CallbackRecords (EventId);
CREATE INDEX        IF NOT EXISTS IX_CallbackRecords_CorrelationId_Processed ON CallbackRecords (CorrelationId, Processed);

CREATE TABLE IF NOT EXISTS ContinuationMappings (
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
CREATE INDEX IF NOT EXISTS IX_ContinuationMappings_TriggerRunId_TriggerState ON ContinuationMappings (TriggerRunId, TriggerState);
CREATE INDEX IF NOT EXISTS IX_ContinuationMappings_TriggerState              ON ContinuationMappings (TriggerState);

CREATE TABLE IF NOT EXISTS ContinuationRunRecords (
    Id                     TEXT        PRIMARY KEY,
    SourceRunId            TEXT        NOT NULL,
    TriggerState           TEXT        NOT NULL,
    RunBId                 TEXT        NOT NULL,
    MappingId              TEXT        NOT NULL,
    MappingVersionSnapshot INTEGER     NOT NULL,
    CreatedAt              TIMESTAMPTZ NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ContinuationRunRecords_SourceRunId_TriggerState ON ContinuationRunRecords (SourceRunId, TriggerState);
CREATE INDEX        IF NOT EXISTS IX_ContinuationRunRecords_SourceRunId              ON ContinuationRunRecords (SourceRunId);

CREATE TABLE IF NOT EXISTS TelemetryEvents (
    Id         TEXT        PRIMARY KEY,
    RunId      TEXT        NOT NULL,
    WorkerId   TEXT,
    LeaseId    TEXT,
    AttemptId  TEXT,
    EventType  TEXT        NOT NULL,
    OccurredAt TIMESTAMPTZ NOT NULL,
    Metadata   TEXT
);
CREATE INDEX IF NOT EXISTS IX_TelemetryEvents_RunId_OccurredAt ON TelemetryEvents (RunId, OccurredAt);
CREATE INDEX IF NOT EXISTS IX_TelemetryEvents_EventType        ON TelemetryEvents (EventType);

-- What a runtime logged while it ran a job (Warning and Error only).
CREATE TABLE IF NOT EXISTS RunLogs (
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
CREATE INDEX IF NOT EXISTS IX_RunLogs_RunId_OccurredAt ON RunLogs (RunId, OccurredAt);
CREATE INDEX IF NOT EXISTS IX_RunLogs_Level            ON RunLogs (Level);

CREATE TABLE IF NOT EXISTS SecurityEvents (
    Id         TEXT        PRIMARY KEY,
    EventType  TEXT        NOT NULL,
    SourceIp   TEXT,
    OccurredAt TIMESTAMPTZ NOT NULL,
    Details    TEXT        NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SecurityEvents_OccurredAt ON SecurityEvents (OccurredAt);
CREATE INDEX IF NOT EXISTS IX_SecurityEvents_EventType  ON SecurityEvents (EventType);

CREATE TABLE IF NOT EXISTS ResolutionRecords (
    Id         TEXT        PRIMARY KEY,
    RunId      TEXT        NOT NULL,
    Resolution TEXT        NOT NULL,
    ResolvedBy TEXT,
    ResolvedAt TIMESTAMPTZ NOT NULL,
    Notes      TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ResolutionRecords_RunId ON ResolutionRecords (RunId);

INSERT INTO schema_migrations (version) VALUES (1) ON CONFLICT DO NOTHING;

-- ================================================================ sheets (sheet-service)
SET search_path = sheets;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version     INTEGER     PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Saved Microsoft sign-ins (was sheet-service.db). The refresh token is a credential; see
-- "Credentials" in README.md for its encryption.
CREATE TABLE IF NOT EXISTS ms_tokens (
    name           TEXT        PRIMARY KEY,
    tenant         TEXT        NOT NULL,
    client_id      TEXT        NOT NULL,
    refresh_token  TEXT        NOT NULL,
    access_token   TEXT        NOT NULL DEFAULT '',
    access_expires TIMESTAMPTZ NOT NULL DEFAULT 'epoch',
    updated_at     TIMESTAMPTZ NOT NULL
);

-- Batch progress (was sheet-batches.db).
CREATE TABLE IF NOT EXISTS process_batches (
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
CREATE INDEX IF NOT EXISTS ix_process_batches_state ON process_batches (state);

CREATE TABLE IF NOT EXISTS process_items (
    batch_id   TEXT        NOT NULL REFERENCES process_batches (id) ON DELETE CASCADE,
    row_no     INTEGER     NOT NULL,
    label      TEXT        NOT NULL DEFAULT '',
    run_id     TEXT        NOT NULL DEFAULT '',
    state      TEXT        NOT NULL,
    message    TEXT        NOT NULL DEFAULT '',
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (batch_id, row_no)
);
CREATE INDEX IF NOT EXISTS ix_process_items_state ON process_items (state);

INSERT INTO schema_migrations (version) VALUES (1) ON CONFLICT DO NOTHING;
