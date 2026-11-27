# Many sites, one login script

Created: 2026-11-27 | Last updated: 2026-11-27

Signs in to every site in a list with that site's own user id and password, kept as secrets and group variables in the Orchestrator, using one child script. Two hundred sites are the same script and two hundred rows.

| File | What it is |
|---|---|
| `many-sites.xlsx` | The sites (columns `site` and `url`). The bot fills `Welcome`, `Status` and `Message`. `site` is the middle of two variable names: `SITE1` gives `SITE1_USERNAME` and `SITE1_PASSWORD`, read by the scripts as `GP100_SITE1_USERNAME` and `GP100_SITE1_PASSWORD`. |
| `portal-login-child_1.0.0.json` | The **child** job. Signs in to one site with `${env:GP100_${param.site}_USERNAME}` and `${env:GP100_${param.site}_PASSWORD}`, checks the page accepted it and reads the welcome line. |
| `many-sites-main_1.0.0.json` | The **main** job. One desktop step that asks sheet-service to run the child once per row, then ends. |
| `site/a.html`, `b.html`, `c.html` | Three demo portals, each accepting one demo account. |
| `desktop-credential-check_1.0.0.json` | A desktop step that sends a stored user id and password to a URL (a local listener in the test). |
| `desktop-credential-echo-guard_1.0.0.json` | The same, but the target echoes the password back and the step stores the reply: the run is refused with `CREDENTIAL_IN_OUTPUT`. |

## Before the first run

1. On the **Secrets** page of the Orchestrator UI, add three secrets: `site1-password`, `site2-password`, `site3-password`. The demo accounts (user id and password) are written in `site/a.html`, `b.html` and `c.html`.
2. On the **Groups** page, add a group (for example `demo portals`). Under **Scripts allowed**, add `portal-login-child`. The page shows the group's number (the first group created gets 100). The scripts in this folder use `GP100`; if your group has another number, change `GP100` in the three scripts to it.
3. In that group, under **Environment variables**, add six variables (the page shows `GP100_` in front of each name box; type only the rest):

   | Variable | Value |
   |---|---|
   | `SITE1_USERNAME`, `SITE2_USERNAME`, `SITE3_USERNAME` | the user id, typed as a plain value |
   | `SITE1_PASSWORD`, `SITE2_PASSWORD`, `SITE3_PASSWORD` | choose the matching secret from the drop-down |

4. Publish the child and set its `latest` tag, then publish the main job.
5. Serve the portals: `python -m http.server 8780 --directory site`.
6. Run sheet-service, a browser worker and (for the main job) a desktop worker.
7. Start the main job. Each row signs in with its own account; the result is in the sheet.

A row whose variable is missing, or not readable by the child, fails with a message naming the variable and the other rows go on. No password appears in a log, an output or the database.

The two desktop scripts take `site` as a parameter (`SITE1` by default) and need their script id in the group's allowed scripts too.

Design: `docs/credential-store-design.md`. Skill: `tracto-browser-rpa`, recipe 15.
