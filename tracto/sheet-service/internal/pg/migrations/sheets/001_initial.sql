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
