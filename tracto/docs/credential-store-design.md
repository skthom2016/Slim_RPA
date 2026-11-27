# Credential store, settings and Orchestrator sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

Tracto keeps the usernames and passwords that scripts sign in with, and the plain settings
scripts read, in the Orchestrator. Scripts name a credential; the worker receives the values only
while the run is going. Today the only option is `${secret:NAME}`, read from the environment
variable `TRACTO_SECRET_NAME` on each worker machine. That stays for single-machine use, but it
does not scale to many sites, has no screen, no access control and no record of changes.

The design follows what the established RPA platforms ship: an encrypted store owned by the
orchestrator, credentials kept apart from ordinary settings, access granted per group of
credentials, values handed to the robot at run time only, every change and every read recorded.
Because people manage credentials through the Orchestrator UI, the UI gets user sign-in, a
password change page and roles first.

The stories that build this are in [credential-store-stories.md](credential-store-stories.md).

---

## 1. Concepts

| Concept | What it holds | Shown in the UI as |
|---|---|---|
| **Credential** | A name, a username, a password, and optionally a one-time-code key (TOTP), the site URL, notes and an expiry date or "no expiry" | Username in clear, password as `●●●●●●` with an eye icon |
| **Credential group** | A set of credentials with one access list, for example `finance-portals` | A filter and an access tab |
| **Setting** | A name and a plain value: a URL, a folder, an e-mail address, a number | Value in clear, editable |
| **User** | A person who signs in to the Orchestrator UI, with a role | Users page |

### Names
- Credential, group and setting names use small letters, digits, `-` and `.`, start with a
  letter or digit, and are at most 100 characters: `givewell-portal`, `bank.hdfc`,
  `finance-portals`. A name with a capital letter is refused.
- In a script, the name in `${credential:NAME.field}` is written out, or built only from job
  parameters: `${credential:${param.site}.password}`. No other variable may form a credential
  name, so the orchestrator can check which credentials a run is entitled to.
- A credential belongs to exactly one group. Access is granted to groups, not to single
  credentials, so 200 portals need one access decision, not 200.
- A setting is never secret. Anything that must not be seen goes into a credential.

---

## 2. Orchestrator sign-in

### Users and the first administrator
- Users are stored in the orchestrator's database with a password hash (bcrypt, cost 12). No
  password is ever stored or logged in clear.
- On start-up, when the users table is empty, the orchestrator creates one administrator:
  username **`admin`**, password **`Admin@123#`**, role **Administrator**. It is created once;
  later start-ups do not touch it. The password is changed from the password page.

### Login page
- The Orchestrator UI opens on a login page (username, password, **Sign in**) for anyone not
  signed in. Every other page and every API call through the UI needs a signed-in session.
- `POST /auth/login {username, password}` checks the hash and returns a signed session token
  (JWT, 8 hours, holding the username and role). The UI keeps it in an `HttpOnly`, `SameSite=Strict`
  cookie and the UI server adds it as `Authorization: Bearer` when it passes calls to the
  orchestrator.
- A wrong username or password gives one message, "Username or password is wrong", without
  saying which. Every failed and successful sign-in is written to `SecurityEvents`.
- **Sign out** in the header ends the session.
- A session is required on `/auth/me`, `/auth/logout`, `/auth/password`, `/admin/users`,
  `/admin/credentials`, `/admin/credential-groups` and `/admin/settings`, whatever
  `ORCHESTRATOR_JWT_SECRET` says. The other `/admin` calls (runs, logs, schedules, telemetry) keep
  the token check they had, and the UI server asks for a sign-in before it forwards any call.
- The session signing key is derived from `ORCHESTRATOR_JWT_SECRET` (so a session is never valid
  as a worker token, nor the other way round), or, when that is empty, from a random key made at
  start-up (sessions then end when the orchestrator restarts).

### Password change page
- **Change password** in the header opens a page with: current password, new password, new
  password again.
- The new password must be at least 8 characters with a capital letter, a small letter, a digit
  and a symbol, and must differ from the current one. The page says which rule is not met.
- `POST /auth/password {currentPassword, newPassword}` checks the current password, stores the
  new hash and records `PASSWORD_CHANGED`. Other sessions of that user end.

### Users page (Administrator)
- Lists users with their role and last sign-in; adds a user with a first password; changes a
  user's role; disables a user; sets a new password for a user who has forgotten theirs
  (recorded as `PASSWORD_RESET` with the administrator's name).
- The last enabled Administrator cannot be disabled or lose the role.

### Workers
Workers keep the token they use today for `/jobs` and `/workers`. They never sign in as a user.

---

## 3. Who may do what

| Permission | Allows |
|---|---|
| `credentials.view` | See the list: names, usernames, URLs, groups, expiry. Passwords stay hidden |
| `credentials.reveal` | Use the eye icon and the copy button |
| `credentials.edit` | Add, change and delete credentials; import a file |
| `credentials.groups` | Create groups and decide which scripts may read them |
| `settings.edit` | Add, change and delete settings (everyone signed in may read them) |
| `users.manage` | The users page |

| Role | Permissions |
|---|---|
| **Viewer** | `credentials.view` |
| **Operator** | `credentials.view`, `credentials.reveal` |
| **Credential admin** | every `credentials.*` permission, `settings.edit` |
| **Administrator** | everything |

A user's credential permissions can be limited to listed groups, so a finance operator sees
`finance-portals` and nothing else. The other Orchestrator pages (jobs, artifacts, schedules)
need only a signed-in user, as before.

---

## 4. Data model

New migration `003_users_credentials.sql` in `orchestrator-go/internal/db/migrations/`.

```sql
CREATE TABLE Users (
    Username        TEXT        PRIMARY KEY,          -- compared ignoring capitals
    PasswordHash    TEXT        NOT NULL,             -- bcrypt
    Role            TEXT        NOT NULL,             -- Viewer, Operator, CredentialAdmin, Administrator
    AllowedGroups   TEXT,                             -- comma separated group names; NULL = every group
    Disabled        BOOLEAN     NOT NULL DEFAULT FALSE,
    SessionVersion  INTEGER     NOT NULL DEFAULT 1,   -- raised on password change: old sessions end
    LastSignInAt    TIMESTAMPTZ,
    CreatedAt       TIMESTAMPTZ NOT NULL,
    UpdatedAt       TIMESTAMPTZ NOT NULL
);

CREATE TABLE CredentialGroups (
    GroupName    TEXT        PRIMARY KEY,
    Description  TEXT,
    CreatedAt    TIMESTAMPTZ NOT NULL,
    CreatedBy    TEXT        NOT NULL
);

-- Which scripts may read the credentials of a group. ArtifactId '*' allows every script.
CREATE TABLE CredentialGroupAccess (
    GroupName    TEXT NOT NULL REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE,
    ArtifactId   TEXT NOT NULL,
    PRIMARY KEY (GroupName, ArtifactId)
);

CREATE TABLE Credentials (
    Name             TEXT        PRIMARY KEY,
    GroupName        TEXT        NOT NULL,   -- a group that exists; a group that holds credentials cannot be deleted
    Username         TEXT        NOT NULL,
    PasswordCipher   TEXT        NOT NULL,   -- sealed value, see section 5
    TotpCipher       TEXT,                   -- optional authenticator key, sealed the same way
    KeyId            TEXT        NOT NULL,   -- which master key sealed this row
    SiteUrl          TEXT,
    Notes            TEXT,
    ExpiresOn        DATE,                   -- NULL = no expiry
    Version          INTEGER     NOT NULL DEFAULT 1,
    UpdatedAt        TIMESTAMPTZ NOT NULL,
    UpdatedBy        TEXT        NOT NULL
);

CREATE TABLE Settings (
    Name       TEXT        PRIMARY KEY,
    Value      TEXT        NOT NULL,
    Notes      TEXT,
    UpdatedAt  TIMESTAMPTZ NOT NULL,
    UpdatedBy  TEXT        NOT NULL
);

-- Every change, every reveal in the UI and every read by a run.
CREATE TABLE CredentialEvents (
    EventId     UUID        PRIMARY KEY,
    At          TIMESTAMPTZ NOT NULL,
    Kind        TEXT        NOT NULL,  -- CREATED, UPDATED, DELETED, REVEALED, COPIED, READ_BY_RUN, READ_DENIED
    Credential  TEXT        NOT NULL,
    Actor       TEXT        NOT NULL,  -- a username, or "run:<runId>@<workerId>"
    Detail      JSONB
);
```

Usernames of people are kept as typed and compared ignoring capitals (unique index on
`lower(Username)`).

The credential's username is stored in clear: it is shown on the list, searched and sorted, and
it is not enough on its own to sign in. Only the password and the TOTP key are encrypted.

---

## 5. Encryption

- **Algorithm:** AES-256-GCM, a fresh random nonce for every value, the same sealed format as
  `sheet-service/internal/secretbox` (`v1:` followed by base64 of nonce, ciphertext and tag).
  The credential name is bound to the value as additional authenticated data, so a sealed value
  copied onto another row does not open.
- **Master key:** 32 random bytes, base64 encoded, in the environment variable
  **`CREDENTIAL_MASTER_KEY`** of the orchestrator, set in the environment or in the
  orchestrator's `.env` file. The same on Windows, Linux and macOS. It is never stored in the
  database. A key is made with:
  - PowerShell: `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)`
  - Linux / macOS: `openssl rand -base64 32`
- **Without the key** the orchestrator starts and runs jobs, but every credential endpoint
  answers `503 CREDENTIAL_STORE_LOCKED` and the Credentials page says the store is locked. With a
  key that does not open the stored rows, it refuses to start and names the problem without
  printing any value.
- **Key id and replacing the key:** each row records the `KeyId` it was sealed with
  (`CREDENTIAL_MASTER_KEY_ID`, default `1`). To replace the key: put the old key in
  `CREDENTIAL_MASTER_KEY_OLD` (its id in `CREDENTIAL_MASTER_KEY_OLD_ID`, default `1`), the new key
  in `CREDENTIAL_MASTER_KEY` with a new `CREDENTIAL_MASTER_KEY_ID`, run the orchestrator with the
  argument `rekey` (`go run . rekey`), which re-seals every row, then remove
  `CREDENTIAL_MASTER_KEY_OLD`. Rows sealed with either key open while both are set.
- **What a database copy reveals:** names, usernames, URLs and notes. Not passwords or TOTP keys.
- **Backup:** the master key is backed up separately from database backups. Losing it loses every
  password, which then have to be entered again.

---

## 6. Orchestrator UI

### Credentials page

```
 Credentials                                   [ + Add ]  [ Import file ]   Group: finance-portals ▾
 ─────────────────────────────────────────────────────────────────────────────────────────────────
 Name               Site                         Username          Password        Expires      ⋯
 givewell-portal    https://portal.givewell.org  fin.ops@acme.com  ●●●●●●●●  👁 ⧉  2027-01-31   ✎ 🗑
 bank.hdfc          https://netbanking.hdfc...   ACME001           ●●●●●●●●  👁 ⧉  No expiry    ✎ 🗑
 bank.icici         https://infinity.icici...    ACME_FIN          ●●●●●●●●  👁 ⧉  ⚠ 2026-12-05  ✎ 🗑
```

- **👁 eye icon** (needs `credentials.reveal`): asks the orchestrator for the password
  (`POST /admin/credentials/{name}/reveal`) and shows it in place. It hides itself again after
  **30 seconds**, when the user clicks the icon again, or when the page loses focus. Each reveal
  is recorded as a `REVEALED` event with the user's name. The password is never part of the list
  response; it is fetched only by this action.
- **⧉ copy:** same check, copies to the clipboard without showing, recorded as `COPIED`.
- **Add / edit:** a form with the password field masked, its own eye icon for what is being
  typed, and an optional TOTP key. Saving an edit with the password field left empty keeps the
  old password.
- **Expiry:** the form offers **No expiry** (the default) or a date. A credential with no expiry
  shows "No expiry" and never warns. A dated credential within 14 days of its date shows ⚠, and
  one past its date shows ⛔ "Expired"; the page header counts both.
- **Import file:** CSV with `name,group,username,password,siteUrl,notes,expiresOn`; an empty
  `expiresOn` means no expiry. The import shows a preview with passwords masked, saves all rows
  or none, and reminds the user to delete the file. The file is read in the browser and sent
  once; it is not kept.
- **History tab per credential:** its `CredentialEvents` rows: who changed it, who revealed or
  copied it, which runs read it.

### Groups tab
Lists groups, their number of credentials and the scripts allowed to read them (`artifactId` or `*`).

### Settings page
A plain table of name, value, notes, last change, editable with `settings.edit`.

### Header
Signed-in username, **Change password**, **Users** (Administrator), **Sign out**.

---

## 7. API

`/admin` calls need a signed-in user (or, for the existing admin calls, the token they use today)
and pass the admin address allowlist and the audit log. The audit log already blanks a
`"password"` field in logged bodies; `newPassword`, `currentPassword` and `totpKey` are added.

| Method and path | Purpose |
|---|---|
| `POST /auth/login` | Sign in, returns the session token |
| `POST /auth/logout` | End the session |
| `GET /auth/me` | Username, role and permissions of the session |
| `POST /auth/password` | Change one's own password |
| `GET/POST /admin/users`, `PUT /admin/users/{username}` | Users: list, add, change role or groups, disable, set a new password |
| `GET /admin/credentials?group=` | List, **without** passwords or TOTP keys |
| `POST /admin/credentials` | Create `{name, group, username, password, totpKey?, siteUrl?, notes?, expiresOn?}`; no `expiresOn` = no expiry |
| `PUT /admin/credentials/{name}` | Change; an omitted `password` keeps the old one; `expiresOn: null` sets no expiry |
| `DELETE /admin/credentials/{name}` | Delete (the event history stays) |
| `POST /admin/credentials/{name}/reveal` | Return the password to a user with `credentials.reveal`; records `REVEALED` or `COPIED` (`{"purpose":"copy"}`) |
| `POST /admin/credentials/import` | Many at once, all or nothing: `{rows, replaceExisting, dryRun}`; `dryRun` checks every row and saves nothing; errors name the row |
| `GET /admin/credentials/{name}/events` | History |
| `GET/POST/PUT/DELETE /admin/credential-groups...` | Groups and their script access |
| `GET/PUT/DELETE /admin/settings/{name}` | Settings |

Reveal is a `POST`, not a `GET`, so it is never cached by a browser or a proxy and never ends up
in a link.

### For workers

```
POST /jobs/{runId}/credentials
{ "workerId": "browser-runtime-001", "names": ["givewell-portal"] }
→ { "credentials": { "givewell-portal": { "username": "...", "password": "...", "totp": "123456" } } }
```

The orchestrator answers only when all of these hold, and records `READ_BY_RUN` or
`READ_DENIED` for each name:

1. the run is in a leased, running state, leased to that `workerId`;
2. the run's script is allowed to read the credential's group;
3. the names asked for appear in that run's script (after parameters are filled in).

An expired credential is still returned; the run log gets a warning naming it and its date.
For a credential with a TOTP key the orchestrator returns the **current code**, not the key, so
the key never leaves the orchestrator.

Settings for a run: `POST /jobs/{runId}/settings` with the same leased-run check, no group check.

---

## 8. In a script

```json
{ "action": "fill", "parameters": { "label": "User ID",  "value": "${credential:givewell-portal.username}" } },
{ "action": "fill", "parameters": { "label": "Password", "value": "${credential:givewell-portal.password}" } },
{ "action": "fill", "parameters": { "label": "Code",     "value": "${credential:givewell-portal.totp}" } },
{ "action": "navigate", "parameters": { "url": "${setting:givewell-url}" } }
```

- Fields: `username`, `password`, `totp`. `${setting:NAME}` reads a setting.
- The name may come from a parameter: `${credential:${param.site}.password}`. Parameters are
  filled in first, then credentials and settings.
- Works in browser and desktop segments alike. The desktop runtime gains the same resolver,
  so `type` can enter a password into a desktop application.
- `${secret:NAME}` keeps working, read from the worker's environment as today.
- The script holds names only. Publishing, exporting or sharing a script shares no password.

### On the worker

1. After accepting the lease, the worker collects every `${credential:...}` and `${setting:...}`
   name in the script (after parameters) and asks for them in one call each. A refusal fails the
   run before a browser or window opens, naming the credential.
2. Values are held in memory for that run only, never written to disk.
3. Every credential value is added to the run's mask list: any console line, run-log line, step
   error message, run output or result containing it has the value replaced by `●●●●` before it
   leaves the worker. This covers a page that echoes the username or an error that quotes a
   field. A value shorter than 4 characters is not masked (it would blank out ordinary words), and
   usernames are masked as well as passwords.
4. A step whose `storeAs` output would contain a credential password or TOTP code fails, so
   neither can become a run output.
5. Failure screenshots stay as they are: password fields show dots in the page itself, but a
   username shown on a page is visible in the picture.

---

## 9. Many sites (200 portals)

One login script, not 200:

1. A **list** of sites (a sheet or a table): `site`, `url`, plus whatever the run needs, for
   example the report to download. `site` is the credential name.
2. A **main job** starts a sheet-service batch that runs a **child script** once per row.
3. The child script signs in with `${credential:${param.site}.username}` and
   `${credential:${param.site}.password}`, does the work, and signs out.
4. All 200 credentials sit in group `finance-portals`, readable only by that child script.

Sites that differ too much get their own child script; the credential handling stays the same.

---

## 10. Things credentials alone do not solve

| Situation | Handling |
|---|---|
| Code from an authenticator app (TOTP) | Store the setup key as the credential's TOTP key; the run uses `${credential:NAME.totp}` |
| Code by SMS or e-mail | No stored value helps. Either a person enters it, or the site is reached through a signed-in browser profile that keeps the session, or the code mailbox is read by a separate step |
| Forced password change | The expiry date and the ⚠ warning; the change itself is done by a person, then the credential is updated |
| CAPTCHA | Not automatable; the site needs a profile kept signed in, or an API |

---

## 11. Phases

| Phase | Delivers |
|---|---|
| **1. Sign-in** | Users table, first administrator, login page, password change page, users page, roles and permissions, session checks |
| **2. Store and pages** | Encryption and master key, credentials / groups / settings APIs and pages, eye icon with timed hide, copy, history, expiry or no expiry, CSV import |
| **3. Runtime** | `/jobs/{runId}/credentials` and `/settings`, `${credential:...}` and `${setting:...}` in the browser and desktop runtimes, masking, the `storeAs` guard, validator, skill and Designer updates |
| **4. TOTP and the many-sites flow** | Code generation, the main job and child script pattern proven on test sites |

Phase 2 can be used by people before phase 3 exists; phase 3 needs phase 1, because a worker's
read must be tied to a real, authenticated run and every reveal to a real user.

---

## 12. Tests

- Sign-in with the first administrator; a wrong password is refused with one message and
  recorded; the password change rules; an old session ends after a password change; the last
  administrator cannot be disabled.
- Seal and open round trip; a sealed value moved to another row fails; a wrong key fails;
  rekey re-seals every row and the old key is no longer needed.
- Names: a name with a capital letter or a character outside the rules is refused; a duplicate is refused.
- The list endpoint never returns a password or TOTP key; the audit log never contains one.
- Reveal without `credentials.reveal` is refused and recorded; with it, recorded with the user.
- No expiry: never warns; a date within 14 days warns; a past date shows expired.
- A worker read is refused for: a run not leased to that worker, a finished run, a script not
  allowed for the group, a name not in the script.
- A browser and a desktop run sign in to a local test page with `${credential:...}`; the run's
  logs, outputs and telemetry contain no password; a `storeAs` that would capture it fails.
- The many-sites pattern with three local test sites, one child script and one group.
