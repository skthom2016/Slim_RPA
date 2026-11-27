-- Users of the Orchestrator UI, the credential store, plain settings, and the record of who
-- changed or read what. See tracto/docs/credential-store-design.md.

-- People who sign in. Username is kept as typed and compared ignoring capitals. PasswordHash is a
-- bcrypt hash, never the password. AllowedGroups is a comma separated list of credential group
-- names; NULL means every group. SessionVersion goes up when the password changes, which ends the
-- sessions issued before.
CREATE TABLE Users (
    Username       TEXT        PRIMARY KEY,
    PasswordHash   TEXT        NOT NULL,
    Role           TEXT        NOT NULL,
    AllowedGroups  TEXT,
    Disabled       BOOLEAN     NOT NULL DEFAULT FALSE,
    SessionVersion INTEGER     NOT NULL DEFAULT 1,
    LastSignInAt   TIMESTAMPTZ,
    CreatedAt      TIMESTAMPTZ NOT NULL,
    UpdatedAt      TIMESTAMPTZ NOT NULL
);
CREATE UNIQUE INDEX UX_Users_LowerUsername ON Users (lower(Username));

CREATE TABLE CredentialGroups (
    GroupName   TEXT        PRIMARY KEY,
    Description TEXT,
    CreatedAt   TIMESTAMPTZ NOT NULL,
    CreatedBy   TEXT        NOT NULL
);

-- The scripts allowed to read the credentials of a group. ArtifactId '*' allows every script.
CREATE TABLE CredentialGroupAccess (
    GroupName  TEXT NOT NULL REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE,
    ArtifactId TEXT NOT NULL,
    PRIMARY KEY (GroupName, ArtifactId)
);

-- GroupName is not a foreign key: the handlers refuse to delete a group that still holds credentials.
-- PasswordCipher and TotpCipher are sealed (AES-256-GCM) with the master key named by KeyId; the
-- credential name is bound to the sealed value. ExpiresOn NULL means the credential never expires.
CREATE TABLE Credentials (
    Name           TEXT        PRIMARY KEY,
    GroupName      TEXT        NOT NULL,
    Username       TEXT        NOT NULL,
    PasswordCipher TEXT        NOT NULL,
    TotpCipher     TEXT,
    KeyId          TEXT        NOT NULL,
    SiteUrl        TEXT,
    Notes          TEXT,
    ExpiresOn      DATE,
    Version        INTEGER     NOT NULL DEFAULT 1,
    UpdatedAt      TIMESTAMPTZ NOT NULL,
    UpdatedBy      TEXT        NOT NULL
);
CREATE INDEX IX_Credentials_GroupName ON Credentials (GroupName);

CREATE TABLE Settings (
    Name      TEXT        PRIMARY KEY,
    Value     TEXT        NOT NULL,
    Notes     TEXT,
    UpdatedAt TIMESTAMPTZ NOT NULL,
    UpdatedBy TEXT        NOT NULL
);

-- Every change, reveal and copy in the UI and every read by a run. Credential is not a foreign
-- key: the history stays when the credential is deleted.
CREATE TABLE CredentialEvents (
    EventId    TEXT        PRIMARY KEY,
    At         TIMESTAMPTZ NOT NULL,
    Kind       TEXT        NOT NULL,
    Credential TEXT        NOT NULL,
    Actor      TEXT        NOT NULL,
    Detail     TEXT
);
CREATE INDEX IX_CredentialEvents_Credential ON CredentialEvents (Credential, At);
