# Scheduling: running a script on a time, or after another

A script is a straight line that runs when something starts it. This file says what can start one, and when the Orchestrator's scheduler is the right choice. Writing the script does not change: a scheduled script is an ordinary published script.

## The ways a run is started

| Need | Use |
|---|---|
| Run now, once | A person presses **Run** in the console, or calls `POST /jobs`. |
| Run at a time or on a repeating pattern (every weekday at 08:00, every hour, the 1st of the month) | **An Orchestrator schedule**: `POST /admin/schedules`. Details below. |
| Run something once for every row or item | Not a schedule: a main job and a child job with a Go API between them. See `loops-and-conditions.md`. A schedule may start that main job. |
| Run script B when script A ends (succeeded, failed, cancelled) | A **continuation**: `POST /admin/continuations`. The target is queued with no params, so A's outputs do not reach it. For values that must pass on, make both stages segments of one script. |
| Run when something happens (a file arrives, a message is received, an external system calls) | **Not built.** The Orchestrator stores a callback (`POST /callbacks`) but nothing starts a run from it. Say so plainly; do not pretend a schedule polls for it unless a poll every N minutes is acceptable. |
| Keep the Orchestrator and the workers running after a restart or sign-in | Windows Task Scheduler, starting them **at user logon**. Not a Windows service: a service cannot drive the desktop. Task Scheduler does not schedule Tracto scripts. |

If the task says "daily", "every", "each morning", "weekly", "at 6 PM" or "on the 1st", it needs an Orchestrator schedule. If it says "for each", it needs a batch. If it says "when X finishes", it needs a continuation.

## An Orchestrator schedule

The Orchestrator's scheduler wakes every 15 seconds, finds schedules whose next fire time has passed, and queues the published script with `triggerType: Schedule`. A schedule names an **artifact id and a version**, so the script must be published first.

```json
{
  "cronExpression": "0 8 * * 1-5",
  "timeZone": "Asia/Kolkata",
  "artifactId": "email-batch-send",
  "version": "1.0.1",
  "params": { "sheetName": "Sheet1" },
  "overlapPolicy": "skip",
  "graceSeconds": 900,
  "enabled": true
}
```

`POST /admin/schedules` creates it, `GET` lists it with `nextRunAt`, `nextRunLocal`, `lastFireAt`, `lastRunId`, `lastStatus` and `lastError`, `PATCH /admin/schedules/<scheduleId>` changes any field, `DELETE` removes it. Full reference: `tracto/docs/scheduling.md`.

| Field | Rule |
|---|---|
| `cronExpression` | Five fields: minute hour day-of-month month day-of-week (`0 8 * * 1-5` is 08:00 Monday to Friday), or `@hourly`, `@daily`. Wall-clock time in `timeZone`. Never put `CRON_TZ=` in it. |
| `timeZone` | **Required.** An IANA name: `Asia/Kolkata`, `UTC`. `Local` is refused. Ask the developer which zone they mean; do not assume one. |
| `params` | Overrides for the script's `params`, as a job takes them. The script's own `params` defaults apply to anything not set. |
| `overlapPolicy` | `skip` (default): a fire is skipped while the schedule's previous run is still running or queued. `queue`: it is queued behind it. |
| `graceSeconds` | How late a fire may still run, 60 to 86400, default 900. Matters after downtime (below). |

### What the scheduler does and does not do

- **Each fire happens at most once.**
- **After downtime**, all the fires a schedule missed are collapsed into the latest one. It runs once if it is within `graceSeconds`, otherwise it is skipped and `lastStatus` says `Skipped`. Missed fires are never replayed one by one. A schedule that must not lose a day needs a long `graceSeconds`.
- **Schedules due together** run oldest first. Runs for different runtimes (desktop, browser) are taken by different workers and can overlap.
- **Daylight saving:** a time that does not exist fires once, later by the length of the gap; a time that happens twice fires once.
- **It only queues.** A queued run starts only when a worker of that runtime is running, and a desktop run needs an unlocked, logged-in session. If no worker is up the run waits in the queue.
- **A changed script needs a new version**, and the schedule keeps firing the old version until it is moved: `PATCH` the schedule with the new `version`. (A published version can never be replaced; see `loops-and-conditions.md`, "Changing the child later".)
- **A deleted or missing version** is recorded in `lastStatus` `Failed` and `lastError`; the schedule keeps its next fire.
- **No console tab yet:** schedules are created and read through the API.

## Writing a script that will run on a schedule

Nobody is watching a scheduled run, so the script has to be able to run alone.

1. **Every input has a default** in the script's `params`, or the schedule's `params` supplies it. A `${param.x}` with no value is left in the step as literal text; it does not fail.
2. **Nothing needs a person.** No sign-in a person must type, no dialog to dismiss. A browser site uses a signed-in `profile`, which expires: someone has to sign it in again when it does, and a schedule that runs for weeks will meet that. A site with no cookie uses `${secret:NAME}` from the worker's environment.
3. **A desktop script owns the keyboard** (desktop rule 8). Schedule it for a time the machine is idle and unlocked.
4. **It is safe to run again.** A skipped, late or repeated fire runs the whole script from the start.
5. **For a loop, the scheduled script is the main job** that starts the batch and ends. Overlap is then also guarded by the batch: a second start on the same sheet is refused (`409`).
6. **Date-dependent values** use `${now.date}` and similar, read when the step starts, in the worker machine's local time.

## What to hand back

The skills write scripts; they do not publish or run them. For a scheduled task, hand back:

- the script file, validated;
- the schedule request body, as a file next to the script (for example `<id>_schedule.json`), not sent;
- what must be true beforehand: the script published at that version, the workers running, the profile signed in, the machine unlocked at that time;
- what to check after the first fire: `GET /admin/schedules` shows `lastStatus` `Queued` and a `lastRunId`, and the run reaches `SUCCEEDED`.

Creating the schedule is an action on the live Orchestrator, so it is done by the developer or after they say so.

## Not built

- Starting a run from an event.
- Retry of a failed scheduled run.
- Ramping to a target number of concurrent runs.
- A console tab for schedules.

The browser actions a child uses are in the `tracto-browser-rpa` skill.
