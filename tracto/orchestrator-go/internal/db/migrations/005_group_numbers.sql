-- Every group gets a number, assigned in order starting at 100 and never given again. A variable of
-- the group is read in scripts as GP<number>_<NAME>, so the full names are unique across groups.
-- The sequence is not rolled back and is not reset when a group is deleted, so a number is never reused.
CREATE SEQUENCE credential_group_no_seq START WITH 100 MINVALUE 100;

ALTER TABLE CredentialGroups ADD COLUMN GroupNo INTEGER;

UPDATE CredentialGroups g SET GroupNo = 99 + s.rn
FROM (SELECT GroupName, row_number() OVER (ORDER BY CreatedAt, GroupName) AS rn FROM CredentialGroups) s
WHERE g.GroupName = s.GroupName;

SELECT setval('credential_group_no_seq', GREATEST(100, COALESCE((SELECT MAX(GroupNo) + 1 FROM CredentialGroups), 100)), false);

ALTER TABLE CredentialGroups ALTER COLUMN GroupNo SET DEFAULT nextval('credential_group_no_seq');
ALTER TABLE CredentialGroups ALTER COLUMN GroupNo SET NOT NULL;
ALTER TABLE CredentialGroups ADD CONSTRAINT UX_CredentialGroups_GroupNo UNIQUE (GroupNo);

-- A group can be renamed: the rows that name it follow, and its number stays.
ALTER TABLE CredentialGroupAccess DROP CONSTRAINT IF EXISTS credentialgroupaccess_groupname_fkey;
ALTER TABLE CredentialGroupAccess ADD CONSTRAINT credentialgroupaccess_groupname_fkey
    FOREIGN KEY (GroupName) REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE ON UPDATE CASCADE;
ALTER TABLE GroupVariables DROP CONSTRAINT IF EXISTS groupvariables_groupname_fkey;
ALTER TABLE GroupVariables ADD CONSTRAINT groupvariables_groupname_fkey
    FOREIGN KEY (GroupName) REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE ON UPDATE CASCADE;
