# Page 8: Flow 5 · International collection accounting run (`carun-main`)

**Purpose.** For the International entity, Southern Cross Protection (SXP), the daily collection accounting run is started by a robot, not by a person. This flow starts it after flow 4 has written that day's workbook, reads two numbers the run leaves behind, and mails the outcome: the bank statement and outstanding reports when it worked, or a message saying what went wrong.

**Reference:** replaces the UiPath process `International_Bot1_SXP`.

## What the started program does

`run_SXP_bot.bat` runs the International copy of the Python collection accounting program (Bot 1), with its own MySQL database `ca_with_pa_sxp`. In one run it:

1. loads today's International hand-off workbooks;
2. downloads from Oracle Fusion the outstanding invoices, the bank statement, the unapplied receipts and the customer master;
3. writes two counts into `Response_Report.xlsx`: cell `B2` = rows of bank statement, cell `B3` = rows of outstanding invoices;
4. creates receipts, matches payments to invoices and posts them, as switched on in its database.

Tracto does not change this program. It only starts it and reads its result.

## What has to exist for this flow

| Piece | Needed for | Exists today? |
|---|---|---|
| Go API, International run start point | The checks, the run and the mail choice below | To build |
| Sheet API | Reading `MSS_SXP_BotRun.xlsx`; writing and reading `Response_Report.xlsx` | Yes |
| Go file API | Deleting the old report; checking the attachments exist | To build |
| Go program runner | Starting `run_SXP_bot.bat` and waiting, with a time limit | To build |
| Go mail API | The result mails | To build |

## The boxes, top to bottom

| Box | What happens | Who does it |
|---|---|---|
| **Orchestrator schedule fires** (green) | At 07:55 Australian time (Australia/Sydney), after flow 4 has written the International workbook. Or Run now. | Orchestrator |
| **Desktop runtime runs carun-main** (purple) | One call to the Go API to start the run for SXP; the job ends at once. | Desktop runtime |
| **Already succeeded today on this database?** (diamond) | Running twice on the same day creates receipts twice, so a second run is refused. | Go API (its own run records) |
| → yes: **Refuse (unless forced); record why** (red) | Nothing is started. A person can override with the force flag on Run now. | Go API |
| **Row for today in MSS_SXP_BotRun.xlsx?** (diamond) | This workbook gives the bank statement date range, one row per day. Without today's row the program would skip the bank statement. | Go API, through the **Sheet API** |
| → no: **Mail: no bank statement window for today** (orange) | The run is not started and the recipients are told why. | Go API, through the **Go mail API** |
| → yes: **Delete the old report; write 0 into B2 and B3** | Clears the previous day's report and resets the two counts, so an old result can never be mistaken for today's. | Go API, through the **Go file API** (delete) and **Sheet API** (write) |
| **Run run_SXP_bot.bat with a timeout** | Starts the Python program described above and waits for it. Its exit code and output are kept. If it runs too long it is stopped. | Go API, through the **Go program runner** |
| **Read B2 and B3** | The two counts the program wrote. | Go API, through the **Sheet API** (`Response_Report.xlsx`) |
| **Finished in time, numbers readable, report newer than the start?** (diamond) | Checks the run really finished and really wrote today's result. | Go API |
| → no: **Mail: the run failed** (orange) | Sent with the reason (timed out, cells empty, report old). | Go API, through the **Go mail API** |
| **B3 = 0?** (diamond) | No outstanding invoices came down: Oracle Fusion or the middleware was down. | Go API |
| → yes: **Mail the API-down recipients** (orange) | "Production APIs are down." | Go API, through the **Go mail API** |
| **B2 = 0?** (diamond) | Outstanding came, but no bank statement. | Go API |
| → yes: **Mail the bank-issue recipients** (orange) | "Bank statement not downloaded." | Go API, through the **Go mail API** |
| **Check both attachments exist** | `BankStatement_Report.xlsx` and `outstanding.xlsx` must be there before the mail is sent. | Go API, through the **Go file API** |
| **Mail the recipients: reports attached** (orange) | The success mail with both reports attached. | Go API, through the **Go mail API** (Microsoft Graph) |
| **Go API records the run result** (grey) | Every path ends here: the five branch lines on the right come back into this box, each on its own line. | Go API |

## The note on the page

- Only one run can use the database at a time, and a second successful run on the same day is refused.
- The Python code is not changed.
- **Open:** Bot 3 (the other International copy that matches, posts and writes the reconciliation reports): who starts it and when. The live `run_SXP_bot.bat` and what sets the active run number in the database. The exact time zone of 07:55.
