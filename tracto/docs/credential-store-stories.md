# Epic CS: credential store, settings and Orchestrator sign-in

Created: 2026-11-27 | Last updated: 2026-11-27

Scope: everything in [credential-store-design.md](credential-store-design.md): sign-in to the
Orchestrator UI with users, roles and a password change page; an encrypted credential store with
groups, an eye icon and a history; a plain settings store; handing credentials and settings to
browser and desktop runs; one-time codes; the many-sites pattern.
Paths are relative to `tracto/`. Section numbers refer to the design document.

| ID | Title | Phase |
|---|---|---|
| CS1 | Users table and the first administrator | 1 |
| CS2 | Login page and sessions | 1 |
| CS3 | Password change page | 1 |
| CS4 | Users page | 1 |
| CS5 | Roles and permissions | 1 |
| CS6 | Encryption and the master key | 2 |
| CS7 | Credential groups and script access | 2 |
| CS8 | Credentials API | 2 |
| CS9 | Credentials page with the eye icon | 2 |
| CS10 | Expiry or no expiry | 2 |
| CS11 | Import credentials from a file | 2 |
| CS12 | Settings store and page | 2 |
| CS13 | Credentials and settings for a run | 3 |
| CS14 | Browser runtime: credentials, settings and masking | 3 |
| CS15 | Desktop runtime: credentials, settings and masking | 3 |
| CS16 | Validator, skills and Designer | 3 |
| CS17 | One-time codes (TOTP) | 4 |
| CS18 | Many sites with one login script | 4 |

---

## CS1 — Users table and the first administrator
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
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
| Epic | CS: credential store and sign-in |
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
| Epic | CS: credential store and sign-in |
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
| Epic | CS: credential store and sign-in |
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
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS2 |
| Capability today | None |

**User story.** As an administrator, I want four roles made of six permissions, optionally limited
to some credential groups, so that viewing, revealing and editing credentials can be given to
different people.

**Acceptance criteria.**
1. Given the roles Viewer, Operator, Credential admin and Administrator, then they hold exactly
   the permissions in section 3 of the design.
2. Given `GET /auth/me`, then it returns the username, role, permissions and allowed groups, and
   the UI hides buttons the user may not use.
3. Given a call needing a permission the user lacks, then it answers `403 FORBIDDEN` naming the
   permission, whatever the UI shows.
4. Given a user limited to group `finance-portals`, then lists, reveals and edits of credentials
   in other groups are refused or left out, and creating a credential in another group is refused.
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
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | none |
| Capability today | Partly. `sheet-service/internal/secretbox` seals values with AES-256-GCM under `TRACTO_SECRET_KEY`; the orchestrator has nothing |

**User story.** As a security owner, I want credential passwords and TOTP keys sealed with a key
that lives only in the orchestrator's environment, so that a copy of the database alone gives
none of them away.

**Acceptance criteria.**
1. Given `CREDENTIAL_MASTER_KEY` set (32 random bytes, base64) in the environment or `.env`, on
   Windows, Linux or macOS, then values are sealed with AES-256-GCM in the `secretbox` format,
   with the credential name as additional authenticated data.
2. Given a sealed value copied onto another credential's row, then opening it fails.
3. Given the key is missing, then the orchestrator starts, jobs run, and every credential endpoint
   answers `503 CREDENTIAL_STORE_LOCKED`.
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

## CS7 — Credential groups and script access
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS5 |
| Capability today | None |

**User story.** As a credential admin, I want to put credentials into groups and say which
scripts may read each group, so that a finance script can read the finance portals and nothing
else.

**Acceptance criteria.**
1. Given `credentials.groups`, then groups can be created, renamed in description, and deleted
   when empty; deleting a group that holds credentials is refused naming how many.
2. Given a group, then scripts are allowed by `artifactId`, or all scripts by `*`.
3. Given the Groups tab, then it lists each group with its number of credentials and allowed
   scripts.
4. Given group names, then the naming rules of section 1 apply (small letters, digits, `-` and `.`);
   a duplicate is refused.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS7.1 | `CredentialGroups`, `CredentialGroupAccess` tables |  |
| CS7.2 | Groups API |  |
| CS7.3 | Groups tab |  |
| CS7.4 | Tests |  |

---

## CS8 — Credentials API
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS5, CS6, CS7 |
| Capability today | None |

**User story.** As a credential admin, I want to add, change, delete, list and reveal credentials
through the API, with every change and reveal recorded, so that the page and any tool use the
same rules.

**Acceptance criteria.**
1. Given `POST /admin/credentials` with name, group, username, password and optional TOTP key,
   URL, notes and expiry, then the password and TOTP key are sealed before storing and the reply
   holds no secret.
2. Given a name breaking the rules of section 1 (a capital letter, a character outside small letters, digits, `-` and `.`), or a duplicate, then `422`
   names the problem.
3. Given `GET /admin/credentials`, then no reply ever contains a password or TOTP key.
4. Given `PUT` without `password`, then the stored password is kept; with one, it is replaced and
   `Version` goes up.
5. Given `POST /admin/credentials/{name}/reveal` by a user with `credentials.reveal`, then the
   password is returned with `Cache-Control: no-store` and `REVEALED` (or `COPIED` for
   `{"purpose":"copy"}`) is recorded with the username; without the permission, `403` and a
   refusal recorded.
6. Given any change, then a `CredentialEvents` row records kind, credential, user and time, and
   `GET /admin/credentials/{name}/events` lists them newest first.
7. Given the audit log, then no request or reply body in it contains a password or TOTP key.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS8.1 | `Credentials`, `CredentialEvents` tables |  |
| CS8.2 | Create, change, delete, list |  |
| CS8.3 | Reveal with permission check and event |  |
| CS8.4 | Events endpoint |  |
| CS8.5 | Audit log blanking for `totpKey` |  |
| CS8.6 | Tests |  |

---

## CS9 — Credentials page with the eye icon
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS8 |
| Capability today | None |

**User story.** As an operator, I want a page listing every site's credential with the password
hidden behind an eye icon, so that I can check or copy a password when I need it and everyone can
see who did.

**Acceptance criteria.**
1. Given the Credentials page, then it lists name, site, username, the password as `●●●●●●●●`,
   expiry and actions, filterable by group and searchable by name, site or username.
2. Given a user with `credentials.reveal`, when the eye icon is clicked, then the password is
   fetched by the reveal call and shown in place; it hides again after 30 seconds, on a second
   click, or when the page loses focus.
3. Given the copy icon, then the password goes to the clipboard without being shown, recorded as
   `COPIED`.
4. Given a user without `credentials.reveal`, then neither icon is shown.
5. Given **Add** or **✎**, then a form with masked password (with its own eye icon), optional TOTP
   key, URL, notes and expiry saves the credential; an edit with the password left empty keeps
   the old one.
6. Given **🗑**, then the page asks for confirmation naming the credential and deletes it.
7. Given **History**, then the credential's events are shown with user and time.
8. Given the store is locked (CS6 criterion 3), then the page says so and offers no action.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS9.1 | List, filter, search |  |
| CS9.2 | Eye icon with timed hide; copy |  |
| CS9.3 | Add / edit form, delete with confirmation |  |
| CS9.4 | History tab |  |
| CS9.5 | UI tests in a browser |  |

---

## CS10 — Expiry or no expiry
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS9 |
| Capability today | None |

**User story.** As a credential admin, I want to give a credential an expiry date or mark it as
never expiring, so that passwords a site forces to change are flagged in time and the rest never
raise a warning.

**Acceptance criteria.**
1. Given the form, then expiry is **No expiry** by default, or a date.
2. Given no expiry, then the list shows "No expiry" and the credential never warns.
3. Given a date within 14 days, then the row shows ⚠ and the date; given a past date, ⛔ "Expired".
4. Given the page header, then it shows the number of credentials expiring soon and expired,
   and clicking it filters the list to them.
5. Given a run reads an expired credential, then the values are still given and the run log
   warns naming the credential and the date.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS10.1 | Expiry field with No expiry in form and API (`expiresOn: null`) |  |
| CS10.2 | Warnings, header counts, filter |  |
| CS10.3 | Run log warning (with CS13) |  |
| CS10.4 | Tests |  |

---

## CS11 — Import credentials from a file
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS8 |
| Capability today | None |

**User story.** As a credential admin, I want to load many credentials from one CSV file, so that
200 portals do not have to be typed in one by one.

**Acceptance criteria.**
1. Given a CSV with `name,group,username,password,siteUrl,notes,expiresOn`, then the page shows a
   preview with passwords masked and a check per row (name rules, duplicate, missing group,
   date format).
2. Given any row in error, then nothing is saved and the errors are listed by row.
3. Given all rows valid, when **Import** is pressed, then all are saved in one transaction and one
   `CREATED` event is recorded per credential; an empty `expiresOn` means no expiry.
4. Given an existing name in the file, then the import refuses it unless **Replace existing** is
   ticked, in which case those credentials are updated.
5. Given the import is done, then the page reminds the user to delete the file; the server keeps
   no copy and the audit log holds no password.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS11.1 | `POST /admin/credentials/import`, all or nothing |  |
| CS11.2 | Import page with preview |  |
| CS11.3 | Audit log: import bodies not logged |  |
| CS11.4 | Tests with 200 rows |  |

---

## CS12 — Settings store and page
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS5 |
| Capability today | None. Scripts take values from `params` or from worker environment variables |

**User story.** As a flow developer, I want plain named settings (URLs, folders, addresses) kept
in the Orchestrator and editable on a page, so that a value used by many scripts is changed in
one place.

**Acceptance criteria.**
1. Given the Settings page, then every signed-in user sees name, value, notes and last change;
   users with `settings.edit` can add, change and delete.
2. Given setting names, then the rules of section 1 apply.
3. Given `GET/PUT/DELETE /admin/settings/{name}`, then they behave as the page.
4. Given a value that looks like a password (the patterns of `handlers/result.go`), then saving
   is refused with a message to use a credential instead.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS12.1 | `Settings` table and API |  |
| CS12.2 | Settings page |  |
| CS12.3 | Secret-pattern check |  |
| CS12.4 | Tests |  |

---

## CS13 — Credentials and settings for a run
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS7, CS8, CS12 |
| Capability today | None |

**User story.** As a worker, I want to fetch exactly the credentials and settings my current run
needs, so that a script can sign in without the password ever being in the script or on the
worker's disk.

**Acceptance criteria.**
1. Given `POST /jobs/{runId}/credentials {workerId, names}` for a run leased to that worker and
   running, whose script names those credentials and may read their groups, then the reply holds
   username and password (and the current TOTP code, CS17) per name, and `READ_BY_RUN` is recorded
   per name.
2. Given a run not leased to that worker, a run not running, a name not in the script, or a group
   the script may not read, then the reply is `403` naming the first failing name and reason, and
   `READ_DENIED` is recorded.
3. Given a name with a capital letter, then it is refused as unknown.
4. Given `POST /jobs/{runId}/settings`, then settings named in the script are returned under the
   same leased-run check.
5. Given the replies, then they carry `Cache-Control: no-store` and are not written to the audit
   log body.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS13.1 | Name extraction from a run's script after parameters | Same resolution order as the runtimes |
| CS13.2 | `/jobs/{runId}/credentials` and `/settings` with checks |  |
| CS13.3 | Events and audit exclusions |  |
| CS13.4 | Tests for each refusal |  |

---

## CS14 — Browser runtime: credentials, settings and masking
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | Partly. `${secret:NAME}` from the worker environment (`browser-runtime/src/TractoRuntime.Browser.Host/BrowserRunEngine.cs`, `ResolveSecrets`) |

**User story.** As a flow developer, I want `${credential:NAME.field}` and `${setting:NAME}` in
browser steps, so that a browser script signs in to any stored site and nothing secret leaves the
worker.

**Acceptance criteria.**
1. Given a script using `${credential:NAME.username|password|totp}` or `${setting:NAME}`, then
   after accepting the lease the worker fetches every name in one call each and fills the steps;
   `${param...}` inside a name is filled in first.
2. Given a refused or unknown name, then the run fails before the browser opens with a message
   naming the credential.
3. Given any log line, error message, telemetry or run output containing a credential value, then
   the value is replaced by `●●●●` before it leaves the worker.
4. Given a `storeAs` whose output would contain a password or TOTP code, then the step fails with
   `CREDENTIAL_IN_OUTPUT`.
5. Given the run ends, then the values are dropped from memory; nothing is written to disk.
6. Given `${secret:NAME}`, then it works as before.
7. Given a local test page with a login form, then a run signs in with a stored credential, and a
   search of its logs, outputs and telemetry finds neither the username nor the password.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS14.1 | Orchestrator client calls for credentials and settings |  |
| CS14.2 | Resolver in `BrowserRunEngine` beside `ResolveSecrets` |  |
| CS14.3 | Run mask list applied to logs, errors, telemetry, outputs |  |
| CS14.4 | `storeAs` guard |  |
| CS14.5 | Tests including the local login page |  |

---

## CS15 — Desktop runtime: credentials, settings and masking
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS13 |
| Capability today | None. The desktop runtime has no `${secret:...}` either |

**User story.** As a flow developer, I want the same `${credential:...}` and `${setting:...}` in
desktop steps, so that a desktop application can be signed in to with a stored password.

**Acceptance criteria.** As CS14 criteria 1 to 5 and 7, for the desktop runtime: `type` and
`notepad.type` accept credential values, the value is masked in every log and output, and a
desktop test signs in to a local test form.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS15.1 | Orchestrator client calls |  |
| CS15.2 | Resolver in the desktop run engine |  |
| CS15.3 | Masking and `storeAs` guard |  |
| CS15.4 | Tests |  |

---

## CS16 — Validator, skills and Designer
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS14, CS15 |
| Capability today | The validators and the Designer know `${secret:...}` only |

**User story.** As a script author (person or Claude), I want the tools to know the new
references, so that a script using credentials is checked before it is published.

**Acceptance criteria.**
1. Given the browser and desktop validators, then `${credential:NAME.field}` with a field other
   than `username`, `password`, `totp`, or a name breaking the rules, is an error; `${setting:NAME}`
   is accepted.
2. Given a literal password in a script, then the validators and publishing still refuse it (as
   SIS story H3 criterion 4).
3. Given the `tracto-browser-rpa` and `tracto-desktop-rpa` skills, then their credential pages
   describe the store, the syntax and the many-sites pattern, and say to never put a password in a
   script.
4. Given the Designer, then its data-flow check accepts the new references.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS16.1 | Validators |  |
| CS16.2 | Skills: `references/credentials.md` in both |  |
| CS16.3 | Designer `DataFlow` |  |
| CS16.4 | README and CLAUDE.md |  |

---

## CS17 — One-time codes (TOTP)
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS8, CS13 |
| Capability today | None |

**User story.** As a flow developer, I want a site that asks for an authenticator-app code to be
signed in to automatically, so that sites with two-step sign-in by app can be automated.

**Acceptance criteria.**
1. Given a credential with a TOTP key (base32 setup key from the site's QR code), then
   `${credential:NAME.totp}` gives the current 6-digit code (RFC 6238, 30 seconds, SHA-1 unless
   the credential says otherwise).
2. Given the code is asked for in the last 5 seconds of its 30-second window, then the next code
   is waited for and returned, so the site does not receive one about to expire.
3. Given the TOTP key, then it never leaves the orchestrator: neither the reveal call nor the run
   call returns it.
4. Given the RFC 6238 test vectors, then the codes match.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS17.1 | TOTP generation in the orchestrator |  |
| CS17.2 | Window-edge wait |  |
| CS17.3 | Tests with RFC vectors |  |

---

## CS18 — Many sites with one login script
| Field | Value |
|---|---|
| Epic | CS: credential store and sign-in |
| Type | Story |
| Depends on | CS14 |
| Capability today | The main-job and child-job batch exists (sheet-service batches); per-site credentials do not |

**User story.** As a finance operations lead, I want one login script run once per portal from a
list, each run using that portal's stored credential, so that 200 portals are handled without 200
scripts.

**Acceptance criteria.**
1. Given a list with `site` and `url` per row and a child script using
   `${credential:${param.site}.username}` and `.password`, when the main job runs, then each row's
   child run signs in with that row's credential.
2. Given a row whose credential is missing or not readable by the child script, then that row's
   run fails naming the credential and the other rows go on.
3. Given three local test sites with different logins, then all three runs succeed and the logs
   hold no password.
4. Given the `tracto-browser-rpa` skill, then it has this pattern as a recipe with a template.

**Tasks.**
| ID | Task | Notes |
|---|---|---|
| CS18.1 | Three local test sites with login forms |  |
| CS18.2 | Child script and main job |  |
| CS18.3 | Recipe and template in the skill |  |

---
