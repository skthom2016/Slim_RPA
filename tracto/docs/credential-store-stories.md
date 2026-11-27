# Epic CS: secrets, group variables and Orchestrator sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

Scope: everything in [credential-store-design.md](credential-store-design.md): sign-in to the
Orchestrator UI with users, roles and a password change page; encrypted secrets with an eye icon
and a history; groups that list the scripts allowed to read them and hold environment variables
(a plain value or a pointer to a secret); handing variables to browser and desktop runs as
`${env:GP100_NAME}` (the group number and the variable name); one-time codes; the many-sites pattern.
Paths are relative to `tracto/`. Section numbers refer to the design document.

| ID | Title | Phase |
|---|---|---|
| CS1 | Users table and the first administrator | 1 |
| CS2 | Login page and sessions | 1 |
| CS3 | Password change page | 1 |
| CS4 | Users page | 1 |
| CS5 | Roles and permissions | 1 |
| CS6 | Encryption and the master key | 2 |
| CS7 | Groups and script access | 2 |
| CS8 | Secrets API | 2 |
| CS9 | Secrets tab with the eye icon | 2 |
| CS10 | Expiry or no expiry | 2 |
| CS11 | Import secrets from a file | 2 |
| CS12 | Environment variables of a group | 2 |
| CS13 | Variables for a run | 3 |
| CS14 | Browser runtime: variables and masking | 3 |
| CS15 | Desktop runtime: variables and masking | 3 |
| CS16 | Validator, skills and Designer | 3 |
| CS17 | One-time codes (TOTP) | 4 |
| CS18 | Many sites with one login script | 4 |

---

## CS1 — Users table and the first administrator
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | none |
| Capability today | None. The Orchestrator UI (`orchestrator-ui/server.js`) has no sign-in; the orchestrator checks a bearer token only on `/jobs`, `/workers`, `/admin`, and not at all when `ORCHESTRATOR_JWT_SECRET` is empty (`orchestrator-go/internal/middleware/auth.go`) |

**User story.** As an administrator, I want the orchestrator to hold user accounts with hashed
passwords and to create a first administrator on its first start, so that people can sign in
before anyone has set up users.

**Acceptance criteria.**
1. Given an empty users table, when the orchestrator starts, then it creates user `admin` with
   password `Admin@123#` and role Administrator, and logs "first administrator created" without
   the password.
2. Given the users table has any row, when the orchestrator starts, then no user is created or
   changed.
3. Given any user, then the database holds a bcrypt hash (cost 12) and never the password; no log,
   audit row or API reply contains a password or a hash.
4. Given two usernames that differ only in capitals, then the second is refused as a duplicate,
   and sign-in accepts the username in any capitals.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS1.1 | Migration `003_users_credentials.sql`: `Users` table and unique index on `lower(Username)` | Section 4 |
| CS1.2 | Password hashing helper (bcrypt) | `golang.org/x/crypto/bcrypt` |
| CS1.3 | First administrator on start-up when the table is empty |  |
| CS1.4 | Tests: first start, second start, hash only, capitals |  |

---

## CS2 — Login page and sessions
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS1 |
| Capability today | None |

**User story.** As a user of the Orchestrator UI, I want to sign in with my username and password
and sign out again, so that only known people can use the UI and every action is tied to a name.

**Acceptance criteria.**
1. Given a person not signed in, when any UI page is opened, then the login page is shown; after
   a successful sign-in the page asked for is shown.
2. Given a correct username and password, when **Sign in** is pressed, then `POST /auth/login`
   returns a session token valid for 8 hours holding the username and role, the UI keeps it in an
   `HttpOnly`, `SameSite=Strict` cookie, and the UI server sends it as `Authorization: Bearer`
   on every call it passes to the orchestrator.
3. Given a wrong username or a wrong password, then the page shows "Username or password is wrong"
   in both cases, and a `SecurityEvents` row records the failed sign-in with the username tried and
   the address, never the password.
4. Given a disabled user, when they sign in with the right password, then sign-in is refused with
   the same message.
5. Given a signed-in user, when **Sign out** is pressed, then the cookie is cleared and the next
   page shown is the login page.
6. Given users exist, then `/admin`, `/auth/me` and `/auth/password` refuse a call without a valid
   session (`401 AUTH_REQUIRED`) even when `ORCHESTRATOR_JWT_SECRET` is empty; in that case the
   sessions are signed with a random key made at start-up.
7. Given a session token issued before the user's password changed, then it is refused
   (`SessionVersion` no longer matches).
8. Given a worker using its existing token on `/jobs` and `/workers`, then it works as before.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS2.1 | `POST /auth/login`, `POST /auth/logout`, `GET /auth/me` | Section 7 |
| CS2.2 | Session middleware: user sessions on `/admin` and `/auth`, worker tokens unchanged | Extend `middleware/auth.go` |
| CS2.3 | Login page and cookie handling in `orchestrator-ui` | `public/login.html`, `server.js` |
| CS2.4 | Header: username and **Sign out** |  |
| CS2.5 | Sign-in events in `SecurityEvents` |  |
| CS2.6 | Tests for every criterion |  |

---

## CS3 — Password change page
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS2 |
| Capability today | None |

**User story.** As a signed-in user, I want to change my own password, so that the first
administrator's password and any password given to me can be replaced by one only I know.

**Acceptance criteria.**
1. Given a signed-in user, when **Change password** in the header is pressed, then a page asks
   for the current password, the new password and the new password again, each masked with an
   eye icon to show what was typed.
2. Given the new password is shorter than 8 characters, lacks a capital letter, a small letter, a
   digit or a symbol, equals the current one, or the two new entries differ, then the page names
   the rule not met and nothing is changed.
3. Given a wrong current password, then the change is refused with "Current password is wrong"
   and a `SecurityEvents` row is written.
4. Given valid entries, when **Save** is pressed, then `POST /auth/password` stores the new hash,
   raises `SessionVersion`, records `PASSWORD_CHANGED`, keeps the current session signed in and
   ends the user's other sessions.
5. Given the audit log of the call, then neither password appears in it.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS3.1 | `POST /auth/password` with the rules | Section 2 |
| CS3.2 | Password change page in `orchestrator-ui` |  |
| CS3.3 | Blank `currentPassword` and `newPassword` in the audit log | `middleware/audit.go` |
| CS3.4 | Tests |  |

---

## CS4 — Users page
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS2, CS5 |
| Capability today | None |

**User story.** As an administrator, I want to add users, set their role and groups, disable them
and set a new password for someone who forgot theirs, so that each person signs in as themselves
with only the access they need.

**Acceptance criteria.**
1. Given a user with `users.manage`, when **Users** is opened, then it lists every user with role,
   allowed groups, disabled or not, and last sign-in; no password or hash is shown.
2. Given **Add user** with a username, role, optional groups and a first password that meets the
   rules of CS3, then the user is created and can sign in.
3. Given a change of role, groups or disabled state, then it applies to the user's next call.
4. Given **Set password** for another user, then their hash is replaced, their sessions end, and
   `PASSWORD_RESET` is recorded with the administrator's name.
5. Given the last enabled Administrator, then disabling them or changing their role is refused.
6. Given a user without `users.manage`, then the **Users** link is not shown and
   `/admin/users` answers `403`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS4.1 | `GET/POST /admin/users`, `PUT /admin/users/{username}` |  |
| CS4.2 | Users page |  |
| CS4.3 | Last-administrator guard |  |
| CS4.4 | Tests |  |

---

## CS5 — Roles and permissions
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS2 |
| Capability today | None |

**User story.** As an administrator, I want four roles made of six permissions, optionally limited
to some groups, so that viewing, revealing and editing secrets and variables can be given to
different people.

**Acceptance criteria.**
1. Given the roles Viewer, Operator, Credential admin and Administrator, then they hold exactly
   the permissions in section 3 of the design.
2. Given `GET /auth/me`, then it returns the username, role, permissions and allowed groups, and
   the UI hides buttons the user may not use.
3. Given a call needing a permission the user lacks, then it answers `403 FORBIDDEN` naming the
   permission, whatever the UI shows.
4. Given a user limited to group `finance portals`, then the groups and variables of other groups
   are refused or left out.
5. Given the jobs, artifacts and schedules pages, then any signed-in user can use them as before.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS5.1 | Role and permission table in code | Section 3 |
| CS5.2 | Permission and group check helper for handlers |  |
| CS5.3 | UI reads `/auth/me` and hides what is not allowed |  |
| CS5.4 | Tests per role |  |

---

## CS6 — Encryption and the master key
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | none |
| Capability today | Partly. `sheet-service/internal/secretbox` seals values with AES-256-GCM under `TRACTO_SECRET_KEY`; the orchestrator has nothing |

**User story.** As a security owner, I want secret values sealed with a key
that lives only in the orchestrator's environment, so that a copy of the database alone gives
none of them away.

**Acceptance criteria.**
1. Given `CREDENTIAL_MASTER_KEY` set (32 random bytes, base64) in the environment or `.env`, on
   Windows, Linux or macOS, then values are sealed with AES-256-GCM in the `secretbox` format,
   with the secret name as additional authenticated data.
2. Given a sealed value copied onto another secret's row, then opening it fails.
3. Given the key is missing, then the orchestrator starts, jobs run, and every secret endpoint
   answers `503 CREDENTIAL_STORE_LOCKED` and plain variables still work.
4. Given a key that does not open the stored rows, then the orchestrator refuses to start and
   names the problem without printing a value.
5. Given `CREDENTIAL_MASTER_KEY_OLD`, a new `CREDENTIAL_MASTER_KEY` and a new
   `CREDENTIAL_MASTER_KEY_ID`, when `tracto-orchestrator rekey` runs, then every row is re-sealed
   under the new key with the new `KeyId`, and afterwards the store opens without the old key.
6. Given `.env.example`, then it lists the three variables with the key-making commands and no
   value.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS6.1 | `internal/secretbox` in the orchestrator, same format as the sheet service, with associated data |  |
| CS6.2 | Key loading, locked state, start-up check |  |
| CS6.3 | `rekey` command |  |
| CS6.4 | `.env.example` and README |  |
| CS6.5 | Tests: round trip, moved value, wrong key, missing key, rekey |  |

---

## CS7 — Groups and script access
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS5 |
| Capability today | None |

**User story.** As a credential admin, I want to create groups and say which scripts may read each
group, so that a finance script can read the finance portals and nothing else.

**Acceptance criteria.**
1. Given the Edit groups permission, then groups can be created, their description changed, and
   deleted; deleting a group deletes its variables.
2. Given a group, then scripts are allowed by `artifactId`, or all scripts by `*`.
3. Given the group page, then the scripts are chosen from a type-ahead box that lists the published
   scripts with name, id and versions; `+` adds another script and `−` removes one.
4. Given group names, then any text up to 100 characters without a comma is accepted; an empty
   name or a duplicate is refused.
5. Given the Groups tab, then it lists each group with its group number (`GP100`), its number of
   scripts and variables, and clicking a group opens its page.
6. Given a new group, then it gets the next group number automatically, starting at 100. A number
   is never given again, even after the group is deleted, and renaming a group keeps it.
7. Given the group list and the group page, then the number is shown.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS7.1 | `CredentialGroups`, `CredentialGroupAccess` tables; `GroupNo` from a sequence starting at 100 | migrations 003 and 005 |
| CS7.2 | Groups API |  |
| CS7.3 | Groups tab and the group page with the script picker |  |
| CS7.4 | Tests |  |

---

## CS8 — Secrets API
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS5, CS6 |
| Capability today | None |

**User story.** As a credential admin, I want to add, change and delete named secrets through the
API, so that no password has to be written in a script or a file.

**Acceptance criteria.**
1. Given `POST /admin/secrets {name, kind, value, description?, expiresOn?}`, then the value is
   sealed and stored; a name outside the rule, a duplicate, or an unknown kind is refused.
2. Given `GET /admin/secrets`, then the reply holds names, kinds, descriptions, expiry, status and
   where each secret is used, and never a value.
3. Given `PUT /admin/secrets/{name}` without a `value`, then the old value is kept.
4. Given `DELETE /admin/secrets/{name}` while a variable points at the secret, then it answers 409
   and names the group and variable.
5. Given any change, then a history row records who did it and when, without the value.
6. Given the audit log, then it never contains a secret value.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS8.1 | Migration `004_secrets_variables.sql`: `Secrets` and `GroupVariables`, dropping the credential and setting tables | Section 4 |
| CS8.2 | Secrets handlers | |
| CS8.3 | Audit redaction of `value` | |
| CS8.4 | Tests | |

---

## CS9 — Secrets tab with the eye icon
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS8 |
| Capability today | None |

**User story.** As an operator, I want to see secrets as dots and reveal one only when I click an
eye icon, so that nothing secret is on screen by accident.

**Acceptance criteria.**
1. Given the Secrets tab, then values show as `●●●●●●` and the list response holds no value.
2. Given the Reveal permission, then the eye icon shows the value, and hides it after 30 seconds,
   on a second click, or when the page loses focus; the copy button copies without showing.
3. Given a user without the Reveal permission, then no eye or copy button is shown and the calls
   are refused.
4. Given every reveal and copy, then a history row records the user.
5. Given the add and edit form, then the value is masked with its own eye icon, and an edit with an
   empty value keeps the old one.
6. Given a delete of a secret still in use, then the page shows the refusal message.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS9.1 | Reveal and copy endpoints, `no-store` | |
| CS9.2 | Secrets tab, form, eye with timed hide, copy | |
| CS9.3 | History view | |
| CS9.4 | Tests and a browser check | |

---

## CS10 — Expiry or no expiry
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS8 |
| Capability today | None |

**User story.** As a credential admin, I want a secret to have an expiry date or none, so that
passwords that must be changed are flagged and others are left alone.

**Acceptance criteria.**
1. Given the form, then **No expiry** is the default; a date can be chosen instead.
2. Given no expiry, then the secret never warns.
3. Given a date within 14 days, then the list shows ⚠; past the date it shows ⛔ "Expired".
4. Given an expired secret read by a run, then the value is still given and the run log carries a
   warning naming the secret and its date.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS10.1 | `ExpiresOn` column and status calculation | |
| CS10.2 | Form and list display | |
| CS10.3 | Warning at run time | |
| CS10.4 | Tests | |

---

## CS11 — Import secrets from a file
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS8 |
| Capability today | None |

**User story.** As a credential admin, I want to load many secrets from a CSV file, so that 200
passwords are not typed one by one.

**Acceptance criteria.**
1. Given a CSV with `name,value,kind,description,expiresOn`, then a check run lists every row with
   values masked and every error with its row number, and saves nothing.
2. Given a file with no errors, then all rows are saved or none; a name that already exists is
   refused unless replacing is chosen.
3. Given an empty `expiresOn`, then the secret has no expiry.
4. Given the import page, then it reminds the user to delete the file, and the file is not kept.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS11.1 | Import endpoint with `dryRun` and `replaceExisting` | |
| CS11.2 | Import dialog with the check | |
| CS11.3 | Tests: errors, all-or-none, replace | |

---

## CS12 — Environment variables of a group
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS7, CS8 |
| Capability today | None. Plain settings existed on their own page and are replaced by group variables |

**User story.** As a credential admin, I want to add environment variables to a group, each with a
plain value or a pointer to a secret, so that a script gets its settings and passwords by name.

**Acceptance criteria.**
1. Given a group page, then **Environment variables** lists rows of name, a choice between Value
   and Secret, and a value box or a drop-down of secrets; `+` adds a row and `−` removes one;
   **Save** replaces the group's list in one step.
   In front of every name box the page shows the fixed prefix `GP100_` (the group's own number)
   and the user types only the rest. A variable typed as `ACME_USERNAME` in group 100 is read by
   scripts as `GP100_ACME_USERNAME`, so names never clash between groups.
2. Given a name outside letters, digits and `_`, a name starting with a digit, a duplicate name in
   the group, a row with both or neither of value and secret, or a secret that does not exist,
   then Save is refused and the message names the row.
3. Given the Edit variables permission, then the page is editable; a Viewer sees it read-only; a
   user limited to other groups does not see the group.
4. Given a variable that points at a secret, then the secret's page shows it under "Used by".
5. Given a save, then a history row records the group and the user.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS12.1 | `GroupVariables` table and `PUT /admin/credential-groups/{name}/variables` | |
| CS12.2 | Variables section of the group page | |
| CS12.3 | "Used by" in the secrets list | |
| CS12.4 | Tests | |

---

## CS13 — Variables for a run
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS6, CS7, CS12 |
| Capability today | `${secret:NAME}` from a worker's environment only |

**User story.** As a script author, I want to write `${env:GP100_NAME}` and have the worker receive
the value only while the run is going, so that the script holds no password.

**Acceptance criteria.**
1. Given `POST /jobs/{runId}/env {workerId, names}`, then it answers only when the run is running
   and leased to that worker, each name appears in the run's script, and each name is
   `GP<number>_<name>` where the group with that number lists the run's main script or `*` and
   holds the variable.
2. Given a name the script may not read, then the answer is `403 VARIABLE_DENIED` naming it; given
   an unknown name, or a name without the `GP<number>_` prefix, `404 UNKNOWN_VARIABLE` naming it.
3. Given a variable that points at a secret, then the reply holds the value with `secret: true`;
   given a plain value, `secret: false`.
4. Given the master key is missing and a secret-backed variable is asked for, then the answer is
   `503 CREDENTIAL_STORE_LOCKED`; plain variables still work.
5. Given every read or refusal, then a history row records the run and worker; the reply is
   `Cache-Control: no-store`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS13.1 | `/jobs/{runId}/env` handler and entitlement check | |
| CS13.2 | Events `READ_BY_RUN` and `READ_DENIED` | |
| CS13.3 | Tests per refusal | |

---

## CS14 — Browser runtime: variables and masking
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | None |

**User story.** As an operator, I want a browser script to sign in with `${env:...}` and nothing
secret to reach a log or an output, so that a run leaves no password behind.

**Acceptance criteria.**
1. Given `${env:GP100_NAME}` in a step, then the worker fetches the names in one call before the browser
   opens; a refusal fails the run at once, naming the variable (category POLICY_DENIED).
2. Given a secret value, then it is held in memory only and replaced by `●●●●` in the console, the
   run log, step errors, outputs and the result; values shorter than 4 characters and plain
   values are not masked.
3. Given a secret value containing `${...}`, then it is typed as it is and never expanded.
4. Given a step with `storeAs` whose output would hold a secret value, then it fails with
   `CREDENTIAL_IN_OUTPUT`.
5. Given a name built from `${param.x}`, then parameters are filled in first and the resolved
   names are fetched.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS14.1 | Collect and fetch `${env:...}` names | |
| CS14.2 | Stand-in values and the mask list | |
| CS14.3 | `storeAs` guard | |
| CS14.4 | Tests with a fake Orchestrator and a headless browser | |

---

## CS15 — Desktop runtime: variables and masking
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | None |

**User story.** As an operator, I want a desktop script to type or send a stored password without
holding it, so that desktop automation follows the same rule as browser automation.

**Acceptance criteria.**
1. Given `${env:GP100_NAME}` in `type`, `key` or `http.post`, then the worker fetches the names before
   the first step; a refusal fails the run with a first step result `credentials` naming the
   variable.
2. Given a secret value, then it is masked as in CS14 in logs, step errors and outputs.
3. Given a step with `storeAs` whose output would hold a secret value, then it fails with
   `CREDENTIAL_IN_OUTPUT`.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS15.1 | Shared resolver and mask in the protocol project | |
| CS15.2 | Engine integration and masking console formatter | |
| CS15.3 | Tests | |

---

## CS16 — Validator, skills and Designer
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | The validators know `${secret:NAME}` |

**User story.** As a script author, I want the validators and skills to know `${env:GP100_NAME}`, so that
a script is checked before it is published.

**Acceptance criteria.**
1. Given `${env:GP100_NAME}` with a valid name, or a name whose part after the `GP100_` prefix is
   built from `${param.x}`, then both validators accept it; a name without the written-out
   `GP<digits>_` prefix, a name built from anything else, or a name outside letters, digits and
   `_`, is an error.
2. Given `${credential:...}` or `${setting:...}`, then the validators report that the form is no
   longer supported.
3. Given a literal value in a password field, then the browser validator still reports it.
4. Given the skills and `CLAUDE.md`, then they describe secrets, groups, group numbers, variables, the
   `GP<number>_` naming, where a script's environment variables are kept (the Orchestrator
   database, not the services' own settings) and masking, and ship a many-sites template.
5. Given the Designer's data-flow check, then `env:` references are not reported as unknown.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS16.1 | `checkNamedRefs` in both validators | |
| CS16.2 | Skills, references, recipes, template | |
| CS16.3 | Designer `DataFlow.cs` | |
| CS16.4 | `CLAUDE.md` section | |

---

## CS17 — One-time codes (TOTP)
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | None |

**User story.** As an operator, I want a script to sign in to a site that asks for an
authenticator code, so that the code is made when needed and the key never leaves the
Orchestrator.

**Acceptance criteria.**
1. Given a secret of kind `totp` and a variable pointing at it, then `${env:GP100_NAME}` gives the
   current 6-digit code.
2. Given a code about to change within 5 seconds, then the Orchestrator waits for the next window.
3. Given the code, then it is masked and cannot become an output like any secret value.
4. Given the RFC 6238 test vectors, then the codes match.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS17.1 | Code generation and the window wait | |
| CS17.2 | `totp` kind in the secret form | |
| CS17.3 | Tests with the RFC vectors | |

---

## CS18 — Many sites with one login script
| Field | Value |
|---|---|
| Epic | CS: secrets, variables and sign-in |
| Type | Story |
| Depends on | CS14, CS16 |
| Capability today | Sheet-service starts a child job once per row |

**User story.** As an operator, I want one child script that signs in to every site in a list with
that site's own account, so that 200 sites are one script and 200 rows.

**Acceptance criteria.**
1. Given a sheet with `site` (`SITE1`, `SITE2`, ...) and `url`, and one group holding
   `SITEn_USERNAME` and `SITEn_PASSWORD` (the password pointing at its own secret), then a main
   job starts the batch and each row signs in with its own account.
2. Given a row whose variable is missing or not readable by the child, then that row fails with a
   message naming the variable and the other rows go on.
3. Given the whole run, then no password is found in any log, output, result or database table.
4. Given the demo folder `demo/many-sites/`, then it holds the sheet, three test portals, the main
   and child scripts and a README.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS18.1 | Child script, main job, sheet and test portals | |
| CS18.2 | Live run over three portals | |
| CS18.3 | Search of logs and tables for the passwords | |
