# Signing in, without putting a password in the script

Created: 2026-11-06 | Last updated: 2026-11-27

**A password never belongs in a script.** A script is published to the Orchestrator, stored as
an artifact, and read by anyone who can see the job. Treat it as public.

There are three ways to get past a sign-in. For a site with a user id and a password, use
Orchestrator secrets and group variables; the other two are for sites where that does not work.

---

## 1. First choice for a user id and password: secrets and variables kept in the Orchestrator

### Where a script's environment variables live

**The values a script reads at run time (a site address, a user id, a password, an API key for an
LLM) are kept in the Orchestrator database, as group variables and secrets.** They are not
environment variables of the machine, and not settings of the services. The `.env` files, `PORT`,
`DATABASE_URL`, `CREDENTIAL_MASTER_KEY` and the like belong to the Orchestrator, sheet-service
and worker processes themselves; a script cannot read them and nothing for a script goes there.

People work on two pages of the Orchestrator UI:

- **Secrets**: a named, sealed value. A secret has a **name** (small letters, digits, `-` and `.`:
  `acme-password`), a value that shows as dots and appears only behind the eye icon (for users
  allowed to see it), a kind (`text`, or `totp` for a one-time-code key), an optional expiry and a
  description.
- **Groups**: a group has a name (any text), a **group number**, a list of **scripts allowed to
  read it**, and a list of **environment variables**. A variable is a name (`ACME_USERNAME`;
  letters, digits and `_`) with either a plain value or a pointer to a secret, chosen from a
  drop-down.

### Group numbers and variable names

- Every group gets a number automatically, starting at 100 (`GP100`, `GP101`, ...). A number is
  never given again, even after the group is deleted, and renaming the group keeps it. The group
  list and the group page show it.
- A variable's **full name** is `GP<group number>_<name>`. In group 100, the variable
  `ACME_USERNAME` is read as `GP100_ACME_USERNAME`. The group page shows the `GP100_` part in
  front of the name box, and you type only the rest.
- Because the number is part of the name, two groups can never hold the same name, and a script
  always says which group it reads from.
- **Always ask the user for the group number** (or read it off the group page). Never guess it.
  A script that moves to another group needs its names changed.

A script names what it needs and never holds a value:

```json
{ "stepNumber": 1, "action": "navigate", "description": "Open the site",
  "parameters": { "url": "${env:GP100_ACME_URL}" } },
{ "stepNumber": 2, "action": "fill", "description": "User id",
  "parameters": { "label": "User ID", "value": "${env:GP100_ACME_USERNAME}" } },
{ "stepNumber": 3, "action": "fill", "description": "Password",
  "parameters": { "label": "Password", "value": "${env:GP100_ACME_PASSWORD}" } },
{ "stepNumber": 4, "action": "fill", "description": "Code from the authenticator app",
  "parameters": { "label": "Code", "value": "${env:GP100_ACME_CODE}" } }
```

- `${env:GP100_NAME}` gives the variable's value. A variable that points at a `totp` secret gives
  the current 6-digit code.
- **A name is written out, or built from job parameters after the prefix only:**
  `${env:GP100_${param.site}_PASSWORD}`. The `GP100_` part is always written out. Nothing else
  may form a name, because the Orchestrator checks, before the run starts, which variables the
  script is entitled to.
- The worker fetches the variables after it accepts the job and **before the browser opens**. A
  refused or unknown name fails the run at once and names the variable. The Orchestrator
  refuses when the script is not allowed to read the variable's group, when the name does not
  appear in the script, or when no variable has that name.
- Values that come from a secret live in the worker's memory for that run. Every log line, error
  message, output and result has them replaced by `●●●●`. Plain values are not masked.
- **A secret cannot become an output.** A step with `storeAs` that would keep one (for example
  `getValue` on the password field) fails with `CREDENTIAL_IN_OUTPUT`.
- A secret past its expiry date is still given, with a warning in the run log.
- The validator checks that the name starts with `GP<digits>_`, follows the name rule and is
  built from parameters only, and rejects the old `${credential:...}` and `${setting:...}` forms.

**Many sites, one script:** write one child script that signs in with
`${env:GP100_${param.site}_USERNAME}` and `${env:GP100_${param.site}_PASSWORD}`, list the sites in
a sheet (`site` = `SITE1`, `SITE2`, ..., and `url`), and run the child once per row from a main
job (recipe 15 in `recipes.md`, template `templates/many-sites-child.json`). 200 sites need 400
variables in one group but one script.

**An API key for one child script (for example an LLM key):** make a secret for the key, make a
group that allows that script, add a variable such as `LLM_API_KEY` pointing at the secret, and
read `${env:GP100_LLM_API_KEY}` in the step that sends it.

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
- **The folder is a secret.** Anyone who can read it, or sign in as the Windows account the
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
manage as Orchestrator secrets. For those, the script types a password it never contains.

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
recorded login already follows the rule above. To keep the password in an Orchestrator secret
instead, replace the reference with `${env:GP100_NAME_PASSWORD}` (and the user id with
`${env:GP100_NAME_USERNAME}`).

---

## Choosing between them

| Situation | Use |
|---|---|
| A site with a user id and a password, one site or two hundred | A secret and group variables: `${env:GP100_NAME_PASSWORD}` |
| The site keeps you signed in (Microsoft 365, Google, most portals) | A profile |
| Several segments need the same account | One profile, shared; they run one at a time |
| Nothing needs signing in (a search, a public page) | No profile. Runs can go in parallel |
| The site logs you out every time, or needs a fresh login per run | Group variables, or `${secret:NAME}` |
| Several accounts on the same site | One pair of variables each (one profile each if the cookie is what matters) |
| A code from an authenticator app | A `totp` secret behind a variable: `${env:GP100_NAME_CODE}` |
| A code by SMS or e-mail | A person, or a signed-in profile |

If you are asked to put a password straight into a script, say no and offer these. The
validator in this skill rejects a parameter that looks like a password with a literal value.
