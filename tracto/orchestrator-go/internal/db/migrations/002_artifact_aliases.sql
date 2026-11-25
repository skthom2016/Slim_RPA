-- Movable version tags for scripts. A row says "for this script, the tag <Alias> currently means
-- version <Version>". Only the tag "latest" is used. Script versions themselves never change;
-- moving a tag only replaces this one row. PreviousVersion is what the tag pointed at before the
-- last move, so a move can be undone.

CREATE TABLE ArtifactAliases (
    ArtifactId      TEXT        NOT NULL,
    Alias           TEXT        NOT NULL,
    Version         TEXT        NOT NULL,
    PreviousVersion TEXT,
    UpdatedAt       TIMESTAMPTZ NOT NULL,
    UpdatedBy       TEXT,
    PRIMARY KEY (ArtifactId, Alias)
);
