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
    SegmentIndex             INTEGER
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
