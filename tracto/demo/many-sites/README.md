# Many sites, one login script

Created: 2026-11-27 | Last updated: 2026-11-27

Signs in to every site in a list with that site's own credential from the Orchestrator's store, using
one child script. Two hundred sites are the same script and two hundred rows.

| File | What it is |
|---|---|
| `many-sites.xlsx` | The sites (columns `site` and `url`). The bot fills `Welcome`, `Status` and `Message`. `site` is the name of a credential. |
| `portal-login-child_1.0.0.json` | The **child** job. Signs in to one site with `${credential:${param.site}.username}` and `.password`, checks the page accepted it and reads the welcome line. |
| `many-sites-main_1.0.0.json` | The **main** job. One desktop step that asks sheet-service to run the child once per row, then ends. |
| `site/a.html`, `b.html`, `c.html` | Three demo portals, each accepting one demo account. |
| `desktop-credential-check_1.0.0.json` | A desktop step that sends a stored user id and password to a URL (a local listener in the test). |
| `desktop-credential-echo-guard_1.0.0.json` | The same, but the target echoes the password back and the step stores the reply: the run is refused with `CREDENTIAL_IN_OUTPUT`. |

## Before the first run

1. On the Orchestrator UI, add a credential group (for example `finance-portals`) that lists the
   scripts allowed to read it (`portal-login-child`), and three credentials in it named `site-a`,
   `site-b`, `site-c` with the demo accounts (user and password are in `site/*.html`).
2. Publish the child and set its `latest` tag, then publish the main job.
3. Serve the portals: `python -m http.server 8780 --directory site`.
4. Run sheet-service, a browser worker and (for the main job) a desktop worker.
5. Start the main job. Each row signs in with its own credential; the result is in the sheet.

A row whose credential is missing, or not readable by the child, fails with a message naming the
credential and the other rows go on. No password appears in a log, an output or the database.

Credentials: `docs/credential-store-design.md`. Skill: `tracto-browser-rpa`, recipe 15.