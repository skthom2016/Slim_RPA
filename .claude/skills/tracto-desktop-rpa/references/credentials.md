# Secrets and variables in a desktop script

Created: 2026-11-27 | Last updated: 2026-11-27

**A password never belongs in a script.** A script is published to the Orchestrator and read by
anyone who can see the job. When a step has to type or send a password, it names an environment
variable kept in the Orchestrator and the worker fills in the value while the run goes.

```json
{ "stepNumber": 3, "action": "type", "description": "User id",
  "parameters": { "value": "${env:GP100_ERP_USERNAME}", "window": "ERP - Sign in" } },
{ "stepNumber": 4, "action": "type", "description": "Password",
  "parameters": { "value": "${env:GP100_ERP_PASSWORD}", "window": "ERP - Sign in" } },
{ "stepNumber": 5, "action": "http.post", "description": "Sign in to an API",
  "parameters": { "url": "${env:GP100_ERP_API}/login",
                  "body": "user=${env:GP100_ERP_USERNAME}&pw=${env:GP100_ERP_PASSWORD}",
                  "storeAs": "session" } }
```

**Where a script's environment variables live.** The values a script reads at run time (a
folder, a site address, a user id, a password, an API key for an LLM) are kept in the Orchestrator
database, as group variables and secrets. They are not environment variables of the machine and
not settings of the services. `.env` files, `PORT`, `DATABASE_URL` and `CREDENTIAL_MASTER_KEY`
belong to the Orchestrator, sheet-service and worker processes themselves; a script cannot read
them and nothing for a script goes there.

- Passwords and keys are **secrets** (Secrets page of the Orchestrator UI): a named, sealed value.
  A **group** (Groups page) has a **group number**, lists the scripts allowed to read it and holds
  **environment variables**. A variable is a name (letters, digits and `_`) with either a plain
  value (a URL, a folder, a user id) or a pointer to a secret.
- **Group numbers and variable names.** Every group gets a number automatically, starting at 100
  (`GP100`, `GP101`, ...), never given again and kept when the group is renamed. A variable's
  full name is `GP<group number>_<name>`: the variable `ERP_PASSWORD` in group 100 is read as
  `GP100_ERP_PASSWORD`. The group page shows the `GP100_` prefix in front of the name box. Two
  groups can never hold the same name. **Ask the user for the group number**; never guess it.
- `${env:GP100_NAME}` gives the variable's value. A variable that points at a `totp` secret gives
  the current 6-digit code.
- **A name is written out, or built from job parameters after the prefix only:**
  `${env:GP100_${param.site}_PASSWORD}`. The `GP100_` part is always written out. Nothing else may
  form a name: the Orchestrator checks, before the first step, which variables the script is
  entitled to.
- The worker fetches them after it accepts the job and **before the first step**. A refused or
  unknown name fails the run at once; the run's first step result is `credentials`, with the
  variable named. The Orchestrator refuses when the script is not allowed to read the group, when
  the name does not appear in the script, or when no variable has that name.
- Values from a secret are held in memory for the run. Every log line, step error and output has
  them replaced by `●●●●`. Plain values are not masked.
- **A secret cannot be stored as an output.** A step with `storeAs` whose output would hold one
  fails with `CREDENTIAL_IN_OUTPUT`. A step without `storeAs` can pass one to its handler (type
  it, send it).
- A value is inserted as plain text: a password that contains `${...}` is typed as it is.
- `type` with a secret sends the keys to whatever window has the keyboard. Name the `window`,
  and put a `delay` after anything that opens a window (SKILL.md rule 3 and 8).
- A secret past its expiry date is still given, with a warning in the run log.
- An API key for one child script: a secret for the key, a group that allows that script, a
  variable such as `LLM_API_KEY` pointing at the secret, and `${env:GP100_LLM_API_KEY}` in the
  step that sends it.

**What it cannot do:** read a code sent by SMS or e-mail, or get past a CAPTCHA.

**Many items:** a repeat over many sites or accounts is a main job and a child job; the child uses
`${env:GP100_${param.site}_PASSWORD}` (see the browser skill, recipe 15, and
`tracto/demo/many-sites/`).

The validator in this skill checks that a name starts with `GP<digits>_`, follows the name rule and is built from parameters only. It
rejects the old `${credential:...}` and `${setting:...}` forms.
