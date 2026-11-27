-- Secrets and group variables replace credentials and settings. A secret is a named sealed value;
-- a group holds variables, each a plain value or a reference to a secret, and lists the scripts
-- allowed to read them. See tracto/docs/credential-store-design.md.

DROP TABLE IF EXISTS Credentials;
DROP TABLE IF EXISTS Settings;

-- Kind is 'text' or 'totp' (a one-time-code key). ValueCipher is sealed (AES-256-GCM) with the
-- master key named by KeyId; the secret name is bound to the sealed value. ExpiresOn NULL means the
-- secret never expires.
CREATE TABLE Secrets (
    Name        TEXT        PRIMARY KEY,
    Kind        TEXT        NOT NULL CHECK (Kind IN ('text','totp')),
    ValueCipher TEXT        NOT NULL,
    KeyId       TEXT        NOT NULL,
    Description TEXT,
    ExpiresOn   DATE,
    Version     INTEGER     NOT NULL DEFAULT 1,
    UpdatedAt   TIMESTAMPTZ NOT NULL,
    UpdatedBy   TEXT        NOT NULL
);

-- One environment variable of a group: a plain value, or the name of a secret. SecretName is not a
-- foreign key: the handlers refuse to delete a secret a variable still uses.
CREATE TABLE GroupVariables (
    GroupName  TEXT NOT NULL REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE,
    VarName    TEXT NOT NULL,
    PlainValue TEXT,
    SecretName TEXT,
    PRIMARY KEY (GroupName, VarName),
    CHECK ((PlainValue IS NULL) <> (SecretName IS NULL))
);
CREATE INDEX IX_GroupVariables_VarName ON GroupVariables (VarName);
CREATE INDEX IX_GroupVariables_SecretName ON GroupVariables (SecretName);
