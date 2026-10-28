package db

// Schema is the complete DDL for the orchestrator SQLite database.
// All migrations from the .NET EF Core project are consolidated here as
// CREATE TABLE IF NOT EXISTS statements, applied on every startup.
const Schema = `
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS RunRecords (
    Id                      TEXT NOT NULL PRIMARY KEY,
    WorkflowId              TEXT NOT NULL DEFAULT '',
    State                   TEXT NOT NULL DEFAULT 'Created',
    WorkerId                TEXT,
    AttemptId               TEXT NOT NULL,
    CreatedAt               TEXT NOT NULL,
    UpdatedAt               TEXT,
    NeedsAttention          INTEGER NOT NULL DEFAULT 0,
    UnknownReason           TEXT,
    BusinessJobId           TEXT,
    CorrelationId           TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    Deadline                TEXT,
    EventId                 TEXT,
    InputRefsJson           TEXT,
    SchemaVersion           TEXT,
    ScriptArtifactId        TEXT,
    ScriptVersion           TEXT,
    SecretRefsJson          TEXT,
    TriggerType             TEXT NOT NULL DEFAULT 'Manual',
    RuntimeType             TEXT NOT NULL DEFAULT '',
    RequiredCapabilitiesJson TEXT,
    InteractiveSessionId    TEXT
);

CREATE INDEX IF NOT EXISTS IX_RunRecords_State ON RunRecords(State);
CREATE INDEX IF NOT EXISTS IX_RunRecords_WorkerId_State ON RunRecords(WorkerId, State);
CREATE INDEX IF NOT EXISTS IX_RunRecords_EventId_CreatedAt ON RunRecords(EventId, CreatedAt);

CREATE TABLE IF NOT EXISTS WorkerRecords (
    WorkerId                TEXT NOT NULL PRIMARY KEY,
    RuntimeType             TEXT NOT NULL,
    State                   TEXT NOT NULL DEFAULT 'Available',
    RegisteredAt            TEXT NOT NULL,
    LastHeartbeatAt         TEXT NOT NULL,
    LastRegisteredAt        TEXT,
    AbsenceThresholdSeconds INTEGER NOT NULL DEFAULT 60,
    IsOnline                INTEGER NOT NULL DEFAULT 1,
    CapabilitiesJson        TEXT
);

CREATE INDEX IF NOT EXISTS IX_WorkerRecords_IsOnline ON WorkerRecords(IsOnline);

CREATE TABLE IF NOT EXISTS JournalEntries (
    Id          TEXT NOT NULL PRIMARY KEY,
    RunId       TEXT NOT NULL,
    AttemptId   TEXT,
    EventType   TEXT NOT NULL,
    OccurredAt  TEXT NOT NULL,
    Payload     TEXT
);

CREATE INDEX IF NOT EXISTS IX_JournalEntries_RunId ON JournalEntries(RunId);
CREATE INDEX IF NOT EXISTS IX_JournalEntries_OccurredAt ON JournalEntries(OccurredAt);

CREATE TABLE IF NOT EXISTS LeaseRecords (
    LeaseId                 TEXT NOT NULL PRIMARY KEY,
    RunId                   TEXT NOT NULL,
    AttemptId               TEXT NOT NULL,
    WorkerId                TEXT NOT NULL,
    ArtifactId              TEXT NOT NULL,
    ArtifactVersion         TEXT NOT NULL,
    ArtifactHash            TEXT NOT NULL,
    OfferedAt               TEXT NOT NULL,
    ExpiresAt               TEXT NOT NULL,
    AcceptedAt              TEXT,
    State                   TEXT NOT NULL DEFAULT 'Offered',
    InteractiveSessionId    TEXT,
    LastHeartbeatAt         TEXT,
    LastSeqNo               INTEGER NOT NULL DEFAULT 0,
    CancellationRequested   INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS IX_LeaseRecords_RunId_State ON LeaseRecords(RunId, State);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_WorkerId_State ON LeaseRecords(WorkerId, State);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_InteractiveSessionId ON LeaseRecords(InteractiveSessionId);
CREATE INDEX IF NOT EXISTS IX_LeaseRecords_State_LastHeartbeatAt ON LeaseRecords(State, LastHeartbeatAt);

CREATE TABLE IF NOT EXISTS ArtifactRecords (
    ArtifactId      TEXT NOT NULL,
    Version         TEXT NOT NULL,
    SchemaVersion   TEXT NOT NULL,
    ContentHash     TEXT NOT NULL,
    StoredAt        TEXT NOT NULL,
    IsDeleted       INTEGER NOT NULL DEFAULT 0,
    SignatureValid  INTEGER NOT NULL DEFAULT 0,
    SelectorContent TEXT,
    PRIMARY KEY (ArtifactId, Version)
);

CREATE INDEX IF NOT EXISTS IX_ArtifactRecords_ArtifactId_IsDeleted ON ArtifactRecords(ArtifactId, IsDeleted);

CREATE TABLE IF NOT EXISTS ArtifactContents (
    ArtifactId TEXT NOT NULL,
    Version    TEXT NOT NULL,
    Payload    BLOB NOT NULL,
    PRIMARY KEY (ArtifactId, Version)
);

CREATE TABLE IF NOT EXISTS ScheduleRecords (
    ScheduleId      TEXT NOT NULL PRIMARY KEY,
    CronExpression  TEXT NOT NULL,
    ArtifactId      TEXT NOT NULL,
    Version         TEXT NOT NULL,
    Enabled         INTEGER NOT NULL DEFAULT 1,
    CreatedAt       TEXT NOT NULL,
    UpdatedAt       TEXT
);

CREATE INDEX IF NOT EXISTS IX_ScheduleRecords_Enabled ON ScheduleRecords(Enabled);

CREATE TABLE IF NOT EXISTS FinalResultRecords (
    Id              TEXT NOT NULL PRIMARY KEY,
    RunId           TEXT NOT NULL,
    AttemptId       TEXT NOT NULL,
    TerminalState   TEXT NOT NULL,
    NeedsAttention  INTEGER NOT NULL DEFAULT 0,
    UnknownReason   TEXT,
    ErrorCategory   TEXT,
    ErrorMessage    TEXT,
    BusinessOutcome TEXT,
    EvidenceRefsJson TEXT NOT NULL DEFAULT '[]',
    BusinessJobId   TEXT,
    CompletedAt     TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_FinalResultRecords_RunId ON FinalResultRecords(RunId);

CREATE TABLE IF NOT EXISTS CallbackRecords (
    Id            TEXT NOT NULL PRIMARY KEY,
    EventId       TEXT NOT NULL,
    CorrelationId TEXT NOT NULL,
    KeyId         TEXT NOT NULL,
    ReceivedAt    TEXT NOT NULL,
    PayloadJson   TEXT NOT NULL,
    Processed     INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_CallbackRecords_EventId ON CallbackRecords(EventId);
CREATE INDEX IF NOT EXISTS IX_CallbackRecords_CorrelationId_Processed ON CallbackRecords(CorrelationId, Processed);

CREATE TABLE IF NOT EXISTS ContinuationMappings (
    MappingId             TEXT NOT NULL PRIMARY KEY,
    TriggerRunId          TEXT,
    TriggerState          TEXT NOT NULL,
    TargetArtifactId      TEXT NOT NULL,
    TargetArtifactVersion TEXT NOT NULL,
    InputTemplate         TEXT,
    CreatedAt             TEXT NOT NULL,
    UpdatedAt             TEXT,
    Version               INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS IX_ContinuationMappings_TriggerRunId_TriggerState ON ContinuationMappings(TriggerRunId, TriggerState);
CREATE INDEX IF NOT EXISTS IX_ContinuationMappings_TriggerState ON ContinuationMappings(TriggerState);

CREATE TABLE IF NOT EXISTS ContinuationRunRecords (
    Id                     TEXT NOT NULL PRIMARY KEY,
    SourceRunId            TEXT NOT NULL,
    TriggerState           TEXT NOT NULL,
    RunBId                 TEXT NOT NULL,
    MappingId              TEXT NOT NULL,
    MappingVersionSnapshot INTEGER NOT NULL,
    CreatedAt              TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ContinuationRunRecords_SourceRunId_TriggerState ON ContinuationRunRecords(SourceRunId, TriggerState);
CREATE INDEX IF NOT EXISTS IX_ContinuationRunRecords_SourceRunId ON ContinuationRunRecords(SourceRunId);

CREATE TABLE IF NOT EXISTS TelemetryEvents (
    Id          TEXT NOT NULL PRIMARY KEY,
    RunId       TEXT NOT NULL,
    WorkerId    TEXT,
    LeaseId     TEXT,
    AttemptId   TEXT,
    EventType   TEXT NOT NULL,
    OccurredAt  TEXT NOT NULL,
    Metadata    TEXT
);

CREATE INDEX IF NOT EXISTS IX_TelemetryEvents_RunId_OccurredAt ON TelemetryEvents(RunId, OccurredAt);
CREATE INDEX IF NOT EXISTS IX_TelemetryEvents_EventType ON TelemetryEvents(EventType);

CREATE TABLE IF NOT EXISTS SecurityEvents (
    Id          TEXT NOT NULL PRIMARY KEY,
    EventType   TEXT NOT NULL,
    SourceIp    TEXT,
    OccurredAt  TEXT NOT NULL,
    Details     TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_SecurityEvents_OccurredAt ON SecurityEvents(OccurredAt);
CREATE INDEX IF NOT EXISTS IX_SecurityEvents_EventType ON SecurityEvents(EventType);

CREATE TABLE IF NOT EXISTS ResolutionRecords (
    Id             TEXT NOT NULL PRIMARY KEY,
    RunId          TEXT NOT NULL,
    Resolution     TEXT NOT NULL,
    ResolvedBy     TEXT,
    ResolvedAt     TEXT NOT NULL,
    Notes          TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResolutionRecords_RunId ON ResolutionRecords(RunId);
`
