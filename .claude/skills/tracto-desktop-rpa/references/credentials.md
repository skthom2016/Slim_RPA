# Credentials and settings in a desktop script

Created: 2026-11-27 | Last updated: 2026-11-27

**A password never belongs in a script.** A script is published to the Orchestrator and read by
anyone who can see the job. When a step has to type or send a password, it names a credential kept
in the Orchestrator's store and the worker fills in the value while the run goes.

```json
{ "stepNumber": 3, "action": "type", "description": "User id",
  "parameters": { "value": "${credential:erp-login.username}", "window": "ERP - Sign in" } },
{ "stepNumber": 4, "action": "type", "description": "Password",
  "parameters": { "value": "${credential:erp-login.password}", "window": "ERP - Sign in" } },
{ "stepNumber": 5, "action": "http.post", "description": "Sign in to an API",
  "parameters": { "url": "${setting:erp-api}/login",
                  "body": "user=${credential:erp-login.username}&pw=${credential:erp-login.password}",
                  "storeAs": "session" } }
```

- Fields: `username`, `password`, `totp` (the current 6-digit code, when the credential has a
  one-time-code key). `${setting:NAME}` reads a plain, non-secret setting (a URL, a folder).
- Credentials and settings are added on the Orchestrator UI (Credentials and Settings pages). A
  credential belongs to a **group**, and the group lists the scripts allowed to read it.
- **A name is written out, or built from job parameters only:** `${credential:${param.site}.password}`.
  Nothing else may form a name: the Orchestrator checks, before the first step, which credentials
  the script is entitled to. Names use small letters, digits, `-` and `.`.
- The worker fetches them after it accepts the job and **before the first step**. A refused or
  unknown name fails the run at once; the run's first step result is `credentials`, with the
  credential named.
- The values are held in memory for the run. Every log line, step error and output has them
  replaced by `●●●●`.
- **A password or one-time code cannot be stored as an output.** A step with `storeAs` whose
  output would hold one fails with `CREDENTIAL_IN_OUTPUT`. A step without `storeAs` can pass one to
  its handler (type it, send it).
- A value is inserted as plain text: a password that contains `${...}` is typed as it is.
- `type` with a credential sends the keys to whatever window has the keyboard. Name the `window`,
  and put a `delay` after anything that opens a window (SKILL.md rule 3 and 8).
- A credential past its expiry date is still given, with a warning in the run log.

**What it cannot do:** read a code sent by SMS or e-mail, or get past a CAPTCHA.

**Many items:** a repeat over many sites or accounts is a main job and a child job; the child uses
`${credential:${param.site}.password}` (see the browser skill, recipe 15, and
`tracto/demo/many-sites/`).

The validator in this skill checks the field, the name rule and that a name is built from
parameters only.