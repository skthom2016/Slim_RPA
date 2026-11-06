# Signing in, without putting a password in the script

**A password never belongs in a script.** A script is published to the Orchestrator, stored as
an artifact, and read by anyone who can see the job. Treat it as public.

There are two ways to get past a sign-in, and they are tried in this order.

---

## 1. First choice: a signed-in browser profile

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

## 2. Fallback: the password in an environment variable on the worker

Some sites keep no usable cookie and present a login form on every visit. For those, and only
those, the script types a password it never contains.

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
recorded login already follows the rule above, and only the environment variable is left to set.

---

## Choosing between them

| Situation | Use |
|---|---|
| The site keeps you signed in (Microsoft 365, Google, most portals) | A profile |
| Several segments need the same account | One profile, shared; they run one at a time |
| Nothing needs signing in (a search, a public page) | No profile. Runs can go in parallel |
| The site logs you out every time, or needs a fresh login per run | `${secret:NAME}` |
| Several accounts on the same site | One profile each, or a secret per account |

If you are asked to put a password straight into a script, say no and offer these two. The
validator in this skill rejects a parameter that looks like a password with a literal value.
