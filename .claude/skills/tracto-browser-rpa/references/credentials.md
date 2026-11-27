# Signing in, without putting a password in the script

Created: 2026-11-06 | Last updated: 2026-11-27

**A password never belongs in a script.** A script is published to the Orchestrator, stored as
an artifact, and read by anyone who can see the job. Treat it as public.

There are three ways to get past a sign-in. For a site with a user id and a password, use the
credential store; the other two are for sites where that does not work.

---

## 1. First choice for a user id and password: a credential kept in the Orchestrator

People add credentials on the Orchestrator UI (**Credentials** page; the password shows as dots
and appears only behind the eye icon, for users allowed to see it). A credential has a **name**
(small letters, digits, `-` and `.`: `givewell-portal`), a username, a password, an optional
one-time-code key, and a **group** (`finance-portals`). Each group lists the scripts allowed to read
it. Plain, non-secret values (a URL, a folder) are **settings** on the Settings page.

A script names what it needs and never holds a value:

```json
{ "stepNumber": 2, "action": "fill", "description": "User id",
  "parameters": { "label": "User ID", "value": "${credential:givewell-portal.username}" } },
{ "stepNumber": 3, "action": "fill", "description": "Password",
  "parameters": { "label": "Password", "value": "${credential:givewell-portal.password}" } },
{ "stepNumber": 4, "action": "fill", "description": "Code from the authenticator app",
  "parameters": { "label": "Code", "value": "${credential:givewell-portal.totp}" } },
{ "stepNumber": 1, "action": "navigate", "description": "Open the site",
  "parameters": { "url": "${setting:givewell-url}" } }
```

- Fields: `username`, `password`, `totp` (the current 6-digit code, for a credential that has a
  one-time-code key). `${setting:NAME}` reads a setting.
- **A name is written out, or built from job parameters only:**
  `${credential:${param.site}.password}`. Nothing else may form a name, because the Orchestrator
  checks, before the run starts, which credentials the script is entitled to.
- The worker fetches the credentials after it accepts the job and **before the browser opens**. A
  refused or unknown name fails the run at once and names the credential. The Orchestrator
  refuses when the script is not allowed to read the credential's group, or the name does not
  appear in the script.
- Values live in the worker's memory for that run. Every log line, error message, output and
  result has them replaced by `●●●●`.
- **A password or one-time code cannot become an output.** A step with `storeAs` that would keep
  one (for example `getValue` on the password field) fails with `CREDENTIAL_IN_OUTPUT`.
- A credential past its expiry date is still given, with a warning in the run log.
- The validator checks the field, the name rule and that the name is built from parameters only.

**Many sites, one script:** write one child script that signs in with
`${credential:${param.site}.username}` and `${credential:${param.site}.password}`, list the sites
in a sheet (`site`, `url`), and run the child once per row from a main job (recipe 15 in
`recipes.md`, template `templates/many-sites-child.json`). 200 sites need 200 credentials but one
script.

**What it cannot do:** a site that sends a code by SMS or e-mail needs a person, or a signed-in
profile (next section). A site that shows a CAPTCHA cannot be automated.

---

## 2. A signed-in browser profile

A **profile** is a browser folder on the worker machine that keeps cookies, exactly as your own
browser remembers a login. A person signs in once, by hand, and the script never sees a password
at all.

Sign it in once, on the machine that runs the browser worker:

```powershell
cd tracto\browser-runtime\src\TractoRuntime.Browser.Host
dotnet run -- --login default https://outlook.office.com
```

A browser opens. Sign in to whatever the scripts need — several sites in the one session if you
like — then close the window. Then name that profile on the segment:

```json
{ "name": "Read the mail", "runtime": "browser", "profile": "default", "steps": [ ... ] }
```

That segment starts already signed in. The script holds no password and needs no sign-in steps.

**What to know:**

- **One run at a time per profile.** The browser locks the folder while it is open, so a second
  run naming the same profile fails with a clear message until the first finishes. Close the
  sign-in window before running anything.
- **Sign-ins expire.** Microsoft and others end a session or ask for MFA after a while. The
  script then lands on a login page and fails. Run `--login` again. Nothing in the runtime can
  renew it for you.
- **The folder is a credential.** Anyone who can read it, or sign in as the Windows account the
  worker runs as, can act as whoever is signed in. It lives under
  `%LOCALAPPDATA%\Tracto\browser-profiles` by default; keep it per-user.
- **Your own Chrome cannot be used.** Since Chrome 136 the debugging port is ignored on a normal
  Chrome profile, so automation cannot drive the browser you use day to day. A Tracto profile is
  always a separate browser folder owned by the worker.
- **Without a profile, every run gets an empty browser** with nothing signed in. That is the
  right choice for a search or any public page, and several such runs can go at once.

---

## 3. Fallback: the password in an environment variable on the worker

Some sites keep no usable cookie and present a login form on every visit and are too few to
manage in the credential store. For those, the script types a password it never contains.

Write the reference in the script:

```json
{ "stepNumber": 4, "action": "fill", "description": "Type the password",
  "parameters": { "label": "Password", "value": "${secret:PORTAL_PASSWORD}" } }
```

Set the value as an environment variable **on the machine that runs the browser worker**, named
`TRACTO_SECRET_` plus the name in capitals:

```powershell
# for the current session
$env:TRACTO_SECRET_PORTAL_PASSWORD = 'the real password'

# or for the account the worker runs as, so it survives a restart
[Environment]::SetEnvironmentVariable('TRACTO_SECRET_PORTAL_PASSWORD', 'the real password', 'User')
```

Then restart the worker so it picks the variable up.

**How the runtime treats it:**

- `${secret:NAME}` is replaced from `TRACTO_SECRET_NAME` just before the step runs.
- The value is **never logged** and never comes back in a result or an output.
- If the variable is not set, the script is **refused before a browser opens**, naming the
  variable you need to define. You find out at once, not halfway through a run.
- The name may contain letters, digits and underscores.

**What this does and does not buy you:** the password is out of the script, out of the artifact
store and out of the logs. It is still readable by anyone who can read that account's
environment on the worker. That is the trade, and it is worth stating plainly to whoever asks
for the automation.

---

## Recording a sign-in

When a flow is recorded in the Designer, **password fields are never captured as text**. The
recorder writes a `${secret:NAME}` reference in their place, named after the field's label. So a
recorded login already follows the rule above. To keep the password in the credential store
instead, replace the reference with `${credential:NAME.password}` (and the user id with
`${credential:NAME.username}`).

---

## Choosing between them

| Situation | Use |
|---|---|
| A site with a user id and a password, one site or two hundred | A credential in the store: `${credential:NAME.password}` |
| The site keeps you signed in (Microsoft 365, Google, most portals) | A profile |
| Several segments need the same account | One profile, shared; they run one at a time |
| Nothing needs signing in (a search, a public page) | No profile. Runs can go in parallel |
| The site logs you out every time, or needs a fresh login per run | A credential, or `${secret:NAME}` |
| Several accounts on the same site | One credential each (one profile each if the cookie is what matters) |
| A code from an authenticator app | A credential with a one-time-code key: `${credential:NAME.totp}` |
| A code by SMS or e-mail | A person, or a signed-in profile |

If you are asked to put a password straight into a script, say no and offer these. The
validator in this skill rejects a parameter that looks like a password with a literal value.
