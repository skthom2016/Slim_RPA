# Secrets, group variables and Orchestrator sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

NeeliRPA keeps the passwords, keys and plain settings that scripts need in the Orchestrator, the way a container platform keeps secrets and environment variables. A **secret** is a named, sealed value. A **group** lists the scripts allowed to read it and holds **environment variables**: each variable is a name with either a plain value or a pointer to a secret. A script reads a variable as `${env:NAME}`; the worker receives the value only while the run is going.

Because people manage secrets through the Orchestrator UI, the UI has user sign-in, a password change page and roles.

The stories that build this are in [credential-store-stories.md](credential-store-stories.md).

---

## 1. Concepts

| Concept | What it holds | Shown in the UI as |
|---|---|---|
| **Secret** | A name, a kind (`text`, or `totp` for a one-time-code key), a sealed value, a description, an expiry date or "no expiry" | The value as `●●●●●●` with an eye icon |
| **Group** | A name, a description, the scripts allowed to read it, and its environment variables | A page opened from the Groups list |
| **Environment variable** | A name, and either a plain value or a pointer to a secret | A row: name, then a value box or a secret drop-down |
| **User** | A person who signs in to the Orchestrator UI, with a role | Users page |

### Names
- A secret name uses small letters, digits, `-` and `.`, starts with a letter or digit, and is at most 100 characters: `acme-password`, `bank.hdfc`.
- A variable name is written like an operating-system environment variable: letters, digits and `_`, not starting with a digit, at most 100 characters: `ACME_USERNAME`, `SITE1_PASSWORD`. Capital letters are allowed.
- A group name is any text up to 100 characters without a comma: `acme with login`, `finance portals`.
- Every group has a **group number**, assigned automatically when the group is created, starting at 100 (`GP100`, `GP101`, ...). A number is never given again, even after the group is deleted, and renaming a group keeps it.
- A variable's **full name** is `GP<group number>_<name>`: the variable `ACME_USERNAME` in group 100 is `GP100_ACME_USERNAME`. The group page shows the `GP100_` part in front of the name box, and the user types only the rest. The database keeps the short name; scripts and the worker call use the full name. Full names are unique across all groups, so two groups can hold the same short name.
- Where script environment variables live: these values are kept in the Orchestrator database as group variables and secrets. They are separate from the settings of the services themselves (`.env`, `PORT`, `DATABASE_URL`, `CREDENTIAL_MASTER_KEY`), which a script cannot read.
- In a script, the name in `${env:GP100_NAME}` is written out, or built only from job parameters after the prefix: `${env:GP100_${param.site}_PASSWORD}`. The `GP<number>_` prefix is always written out. No other variable may form a name, so the Orchestrator can check which variables a run is entitled to.
- A variable belongs to exactly one group. Access is granted to groups, so 200 portals need one access decision.
- A script may read a variable only when the name appears in the script and the variable sits in a group that lists the script (or `*`, every script).
- A secret cannot be deleted while a variable points at it.

---

## 2. Orchestrator sign-in

### Users and the first administrator
- Users are stored in the Orchestrator's database with a password hash (bcrypt, cost 12). No password is ever stored or logged in clear.
- On start-up, when the users table is empty, the Orchestrator creates one administrator: username **`admin`**, password **`Admin@123#`**, role **Administrator**. It is created once; later start-ups do not touch it. The password is changed from the password page.

### Login page
- The Orchestrator UI opens on a login page (username, password, **Sign in**) for anyone not signed in. Every other page and every API call through the UI needs a signed-in session.
- `POST /auth/login {username, password}` checks the hash and returns a signed session token (8 hours, holding the username and role). The UI keeps it in an `HttpOnly`, `SameSite=Strict` cookie and the UI server adds it as `Authorization: Bearer` when it passes calls to the Orchestrator.
- A wrong username or password gives one message, "Username or password is wrong", without saying which.
- **Sign out** in the header ends the session.
- A session is required on `/auth/me`, `/auth/logout`, `/auth/password`, `/admin/users`, `/admin/secrets` and `/admin/credential-groups`. The other `/admin` calls (runs, logs, schedules, telemetry) keep the token check they had, and the UI server asks for a sign-in before it forwards any call.
- The session signing key is derived from `ORCHESTRATOR_JWT_SECRET`, so a session is never valid as a worker token, nor the other way round. When that is empty, a random key is made at start-up and sessions end when the Orchestrator restarts.

### Password change page
- **Change password** in the header opens a page with: current password, new password, new password again.
- The new password must be at least 8 characters with a capital letter, a small letter, a digit and a symbol, and must differ from the current one. The page says which rule is not met.
- `POST /auth/password {currentPassword, newPassword}` checks the current password and stores the new hash. Other sessions of that user end.

### Users page (Administrator)
- Lists users with their role and last sign-in; adds a user with a first password; changes a user's role; disables a user; sets a new password for a user who has forgotten theirs.
- The last enabled Administrator cannot be disabled or lose the role.

### Workers
Workers keep the token they use today for `/jobs` and `/workers`. They never sign in as a user.

---

## 3. Who may do what

| Permission | Allows |
|---|---|
| View | See secrets (names, kinds, expiry, where used) and groups with their variables. Values of secrets stay hidden |
| Reveal | Use the eye icon and the copy button on a secret |
| Edit secrets | Add, change and delete secrets; import a file |
| Edit groups | Create groups and decide which scripts may read them |
| Edit variables | Add, change and remove the environment variables of a group |
| Manage users | The Users page |

| Role | Permissions |
|---|---|
| **Viewer** | View |
| **Operator** | View, Reveal |
| **Credential admin** | View, Reveal, Edit secrets, Edit groups, Edit variables |
| **Administrator** | everything |

A user's access can be limited to listed groups, so a finance operator sees `finance portals` and nothing else. The other Orchestrator pages (jobs, artifacts, schedules) need only a signed-in user, as before.

---

## 4. Data model

Migration `003_users_credentials.sql` created the users and group tables. Migration `004_secrets_variables.sql` replaces the credential and setting tables with secrets and group variables.

```sql
-- kept from 003
Users(Username, PasswordHash, Role, AllowedGroups, Disabled, SessionVersion, LastSignInAt, CreatedAt, UpdatedAt)
CredentialGroups(GroupName PK, GroupNo UNIQUE, Description, CreatedAt, CreatedBy)  -- GroupNo from a sequence starting at 100 (migration 005)
CredentialGroupAccess(GroupName, ArtifactId, PK(GroupName, ArtifactId))   -- ArtifactId '*' = every script
CredentialEvents(EventId, At, Kind, Credential, Actor, Detail)             -- Credential = secret name, or the group name

-- new in 004
CREATE TABLE Secrets (
    Name         TEXT        PRIMARY KEY,
    Kind         TEXT        NOT NULL,   -- text, totp
    ValueCipher  TEXT        NOT NULL,   -- sealed value, see section 5
    KeyId        TEXT        NOT NULL,   -- which master key sealed this row
    Description  TEXT,
    ExpiresOn    DATE,                   -- NULL = no expiry
    Version      INTEGER     NOT NULL DEFAULT 1,
    UpdatedAt    TIMESTAMPTZ NOT NULL,
    UpdatedBy    TEXT        NOT NULL
);

CREATE TABLE GroupVariables (
    GroupName    TEXT NOT NULL REFERENCES CredentialGroups(GroupName) ON DELETE CASCADE,
    VarName      TEXT NOT NULL,
    PlainValue   TEXT,                   -- a plain value, or
    SecretName   TEXT,                   -- the name of a secret; exactly one of the two is set
    PRIMARY KEY (GroupName, VarName),
    CHECK ((PlainValue IS NULL) <> (SecretName IS NULL))
);
```

Only secrets are encrypted. Plain variable values (a user id, a URL, a folder) are stored in clear and shown in clear.

---

## 5. Encryption

- **Algorithm:** AES-256-GCM, a fresh random nonce for every value, the same sealed format as `sheet-service/internal/secretbox` (`v1:` followed by base64 of nonce, ciphertext and tag). The secret name is bound to the value as additional authenticated data, so a sealed value copied onto another row does not open.
- **Master key:** 32 random bytes, base64 encoded, in the environment variable **`CREDENTIAL_MASTER_KEY`** of the Orchestrator, set in the environment or in the Orchestrator's `.env` file. The same on Windows, Linux and macOS. It is never stored in the database. A key is made with:
  - PowerShell: `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)`
  - Linux / macOS: `openssl rand -base64 32`
- **Without the key** the Orchestrator starts and runs jobs, but every secret endpoint, and every run that reads a secret-backed variable, answers `503 CREDENTIAL_STORE_LOCKED`. Plain variables still work. With a key that does not open the stored rows, it refuses to start and names the problem without printing any value.
- **Key id and replacing the key:** each row records the `KeyId` it was sealed with (`CREDENTIAL_MASTER_KEY_ID`, default `1`). To replace the key: put the old key in `CREDENTIAL_MASTER_KEY_OLD` (its id in `CREDENTIAL_MASTER_KEY_OLD_ID`, default `1`), the new key in `CREDENTIAL_MASTER_KEY` with a new `CREDENTIAL_MASTER_KEY_ID`, run the Orchestrator with the argument `rekey` (`go run . rekey`), which re-seals every row, then remove `CREDENTIAL_MASTER_KEY_OLD`.
- **What a database copy reveals:** secret names, descriptions, plain variable values and which group holds what. Not the values of secrets.
- **Backup:** the master key is backed up separately from database backups. Losing it loses every secret value, which then have to be entered again.

---

## 6. Orchestrator UI

The left tabs are **Secrets** and **Groups**, with the Users page for administrators.

### Secrets tab

```
 Secrets                                         [ + Add secret ]  [ Import file ]
 ──────────────────────────────────────────────────────────────────────────────────
 Name            Kind   Description      Value        Expires       Used by     ⋯
 acme-password   text   ACME test site   ●●●●●●●●  👁 ⧉  No expiry    1 variable  ✎ 🗑
 llm-api-key     text   LLM provider     ●●●●●●●●  👁 ⧉  ⚠ 2026-12-05 2 variables ✎ 🗑
```

- **👁 eye icon** (Reveal permission): asks the Orchestrator for the value (`POST /admin/secrets/{name}/reveal`) and shows it in place. It hides itself again after **30 seconds**, when the icon is clicked again, or when the page loses focus. Each reveal is recorded with the user's name. The value is never part of the list response.
- **⧉ copy:** same check, copies to the clipboard without showing.
- **Add / edit:** a form with name, kind, value (masked, with its own eye icon for what is being typed), description and expiry. Saving an edit with the value left empty keeps the old value.
- **Expiry:** **No expiry** (the default) or a date. A dated secret within 14 days of its date shows ⚠, and one past its date shows ⛔ "Expired".
- **Delete:** refused with a message naming the group and variable that still use the secret.
- **Import file:** CSV with `name,value,kind,description,expiresOn`; an empty `expiresOn` means no expiry. The import shows a check of every row with values masked, saves all rows or none, and reminds the user to delete the file.
- **History per secret:** who changed it, who revealed or copied it, which runs read it.

### Groups tab
A list of groups with the number of scripts and variables. Clicking a group opens its page:

```
 Group: acme with login
 Description  [ ACME test site                      ]
 Scripts allowed   [ acme-login-child-fetch-invoice ▾ ] −  +
 Environment variables
   [ ACME_USERNAME ]  ( Value  | Secret )  [ <the e-mail>        ]   −
   [ ACME_PASSWORD ]  ( Value  | Secret )  [ acme-password ▾     ]   −  +
                                                                   [ Save ]
```

- **Scripts allowed:** a type-ahead box listing the published scripts with name, id and versions, `*` for every script, `+` to add another and `−` to remove one.
- **Environment variables:** one row per variable. A row has a name, a choice between **Value** (a text box) and **Secret** (a drop-down of the secrets), `+` to add a row and `−` to remove it. **Save** replaces the group's list in one step.

### Header
Signed-in username, **Change password**, **Users** (Administrator), **Sign out**.

---

## 7. API

`/admin` calls for secrets, groups and users need a signed-in user and pass the admin address allowlist and the audit log. The audit log blanks `password`, `newPassword`, `currentPassword` and `value` fields of secrets in logged bodies.

| Method and path | Purpose |
|---|---|
| `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`, `POST /auth/password` | Sign in, out, who am I, change one's own password |
| `GET/POST /admin/users`, `PUT /admin/users/{username}` | Users |
| `GET /admin/secrets` | List, **without** values: name, kind, description, expiry, status, where used |
| `POST /admin/secrets` | Create `{name, kind, value, description?, expiresOn?}`; no `expiresOn` = no expiry |
| `PUT /admin/secrets/{name}` | Change; an omitted `value` keeps the old one |
| `DELETE /admin/secrets/{name}` | Delete (409 while a variable uses it) |
| `POST /admin/secrets/{name}/reveal`, `/copy` | Return the value to a user with Reveal; recorded |
| `POST /admin/secrets/import` | Many at once, all or nothing, with `dryRun` and `replaceExisting` |
| `GET /admin/secrets/{name}/events` | History |
| `GET/POST/PUT/DELETE /admin/credential-groups...` | Groups and their script access |
| `GET /admin/credential-groups/{name}` | The group with its scripts and variables |
| `PUT /admin/credential-groups/{name}/variables` | Replace the group's variables `{variables:[{name, value?, secret?}]}` with short names. A group carries its `number`, and a variable carries its `fullName` |

Reveal is a `POST`, not a `GET`, so it is never cached by a browser or a proxy and never ends up in a link.

### For workers

```
POST /jobs/{runId}/env
{ "workerId": "browser-runtime-001", "names": ["GP100_ACME_USERNAME", "GP100_ACME_PASSWORD"] }
→ { "values": { "GP100_ACME_USERNAME": { "value": "...", "secret": false },
                "GP100_ACME_PASSWORD": { "value": "...", "secret": true } } }
```

The Orchestrator answers only when all of these hold, and records `READ_BY_RUN` or `READ_DENIED` for each name:

1. the run is in a running state, leased to that `workerId`;
2. the names asked for appear in that run's script (after parameters are filled in);
3. each name is `GP<number>_<name>`, the group with that number lists the run's main script (or `*`), and the group holds the variable.

A name the script may not read answers `403 VARIABLE_DENIED`; an unknown name, or one without the `GP<number>_` prefix, answers `404 UNKNOWN_VARIABLE`. An expired secret is still returned; the run log gets a warning naming it and its date. A variable that points at a `totp` secret returns the **current code**, not the key, so the key never leaves the Orchestrator.

---

## 8. In a script

```json
{ "action": "fill", "parameters": { "label": "User ID",  "value": "${env:GP100_ACME_USERNAME}" } },
{ "action": "fill", "parameters": { "label": "Password", "value": "${env:GP100_ACME_PASSWORD}" } },
{ "action": "fill", "parameters": { "label": "Code",     "value": "${env:GP100_ACME_CODE}" } },
{ "action": "navigate", "parameters": { "url": "${env:GP100_ACME_URL}" } }
```

- `${env:GP100_NAME}` gives the plain value, or the value of the secret the variable points at. For a `totp` secret it gives the current 6-digit code.
- The name may come from a parameter: `${env:GP100_${param.site}_PASSWORD}`. Parameters are filled in first, then variables.
- Works in browser and desktop segments alike, so `type` can enter a password into a desktop application.
- `${secret:NAME}` keeps working, read from the worker's environment as before.
- The script holds names only. Publishing, exporting or sharing a script shares no password.

### On the worker

1. After accepting the lease, the worker collects every `${env:...}` name in the script (after parameters) and asks for them in one call. A refusal fails the run before a browser or window opens, naming the variable.
2. Values are held in memory for that run only, never written to disk.
3. Every value that comes from a secret is added to the run's mask list: any console line, run-log line, step error message, run output or result containing it has the value replaced by `●●●●` before it leaves the worker. A value shorter than 4 characters is not masked. Plain values are not masked.
4. A step whose `storeAs` output would contain a secret value fails with `CREDENTIAL_IN_OUTPUT`, so a secret cannot become a run output.
5. Failure screenshots stay as they are: password fields show dots in the page itself.

---

## 9. Many sites (200 portals)

One login script, not 200:

1. A **list** of sites (a sheet): `site`, `url`, plus whatever the run needs. `site` is the prefix of two variables: `SITE1` gives `SITE1_USERNAME` and `SITE1_PASSWORD`.
2. A **main job** starts a sheet-service batch that runs a **child script** once per row.
3. The child signs in with `${env:GP100_${param.site}_USERNAME}` and `${env:GP100_${param.site}_PASSWORD}`, does the work, and signs out.
4. All the variables sit in one group, readable only by that child script. Each `_PASSWORD` variable points at its own secret.

Sites that differ too much get their own child script; the variable handling stays the same.

An API key for one script (for example an LLM key) is a secret plus a variable (`LLM_API_KEY`) in a group that lists only that script.

---

## 10. Things secrets alone do not solve

| Situation | Handling |
|---|---|
| Code from an authenticator app (TOTP) | Store the setup key as a `totp` secret and point a variable at it; the run reads `${env:GP100_NAME}` |
| Code by SMS or e-mail | No stored value helps. A person enters it, or the site is reached through a signed-in browser profile that keeps the session |
| Forced password change | The expiry date and the ⚠ warning; the change itself is done by a person, then the secret is updated |
| CAPTCHA | Not automatable; the site needs a profile kept signed in, or an API |

---

## 11. Phases

| Phase | Delivers |
|---|---|
| **1. Sign-in** | Users table, first administrator, login page, password change page, users page, roles and permissions, session checks |
| **2. Secrets and groups** | Encryption and master key, secrets API and tab with the eye icon, copy, history, expiry or no expiry, CSV import; groups with script access and environment variables |
| **3. Runtime** | `/jobs/{runId}/env`, `${env:...}` in the browser and desktop runtimes, masking, the `storeAs` guard, validator, skills and Designer updates |
| **4. One-time codes and the many-sites flow** | Code generation, the main job and child script pattern proven on test sites |

---

## 12. Tests

- Sign-in with the first administrator; a wrong password is refused with one message; the password change rules; an old session ends after a password change; the last administrator cannot be disabled.
- Seal and open round trip; a sealed value moved to another row fails; a wrong key fails; rekey re-seals every row.
- Names: a secret name outside the rules is refused; a variable name outside the rules is refused; a group name with a comma is refused; a duplicate is refused.
- The list endpoints never return a secret value; the audit log never contains one.
- Reveal without the Reveal permission is refused and recorded; with it, recorded with the user.
- A variable with both a value and a secret, or with neither, is refused; a variable pointing at a missing secret is refused; a secret still in use cannot be deleted.
- No expiry: never warns; a date within 14 days warns; a past date shows expired.
- A worker read is refused for: a run not leased to that worker, a finished run, a script not allowed for the group, a name not in the script, an unknown name, a name without the `GP<number>_` prefix.
- A browser and a desktop run sign in to a local test page with `${env:...}`; the run's logs, outputs and telemetry contain no password; a `storeAs` that would capture it fails.
- The many-sites pattern with three local test sites, one child script and one group.
