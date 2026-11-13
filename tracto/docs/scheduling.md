# Scheduled runs

The Orchestrator starts a published script on a cron schedule. A loop inside the Orchestrator (`internal/scheduler`) looks every 15 seconds for enabled schedules whose `nextRunAt` has passed and queues the script through the same code `POST /jobs` uses. The run is tagged `triggerType: Schedule`.

A queued run still needs a worker of the script's runtime to be running, and a desktop worker needs an unlocked, logged-in session. The scheduler only queues.

## API

```
POST   /admin/schedules
GET    /admin/schedules
PATCH  /admin/schedules/{scheduleId}
DELETE /admin/schedules/{scheduleId}
```

Create:

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

| Field | Meaning |
|---|---|
| `cronExpression` | Five fields (minute hour day-of-month month day-of-week) or a descriptor such as `@hourly`. Read as wall-clock time in `timeZone`. |
| `timeZone` | Required. An IANA name such as `Asia/Kolkata` or `UTC`. `Local` is refused, and so is a `CRON_TZ=` prefix inside the expression. |
| `artifactId`, `version` | The published script to run. Refused with `422` if it is not published. |
| `params` | Overrides for the script's params, as a run of `POST /jobs` takes them. |
| `overlapPolicy` | `skip` (default): a fire is skipped while the schedule's previous run is still active. `queue`: it is queued behind it. |
| `graceSeconds` | How late a fire may still run (default 900, between 60 and 86400). See "After downtime". |
| `enabled` | Default true. |

The reply, and each item of `GET`, also carries `nextRunAt` (UTC), `nextRunLocal` (in the schedule's zone), `lastFireAt`, `lastRunId`, `lastStatus` (`Queued`, `Skipped`, `Failed` or `Disabled`) and `lastError`.

`PATCH` takes any of the fields above except `artifactId`. Enabling a schedule, or changing its expression or time zone, works out the next fire from now, so a schedule that was off owes nothing for the time it was off.

## Rules

- **Each fire happens at most once.** `nextRunAt` is moved on with a compare-and-swap before anything is queued, so two ticks, or two Orchestrator processes on one database, cannot both fire it. The run carries `eventId` `<scheduleId>:<fire time>`.
- **Schedules due together run oldest first.**
- **A script that cannot be queued** (its version was deleted) is recorded in `lastStatus` and `lastError`, and the schedule moves on to its next fire.
- **A schedule that can never fire** (a stored expression or zone that does not parse) is disabled with the reason in `lastError`.

## After downtime

If the Orchestrator was down when fires were due, every missed fire of a schedule is collapsed into the most recent one. That one runs once if it is no later than `graceSeconds`; otherwise it is skipped and recorded. Missed fires are never replayed one by one.

Example: a schedule that fires every 15 minutes, with the system down from before 08:00 until 08:16. Only the 08:15 fire runs. Two different schedules due together, one at 08:00 and one at 08:15, are judged separately: each runs if inside its own grace window, the 08:00 one queued first.

Queued runs are taken by workers oldest first. Runs for different runtimes (desktop, browser) are taken by different workers and can overlap in time.

## Daylight saving

A cron time is wall-clock time in the schedule's zone.

- A time that does not exist (clocks go forward over it) fires once, moved later by the length of the gap: `30 2 * * *` in `America/New_York` fires at 03:30 on the day clocks go forward.
- A time that happens twice (clocks go back over it) fires once, the first time.

Windows has no time zone database of its own; the binary embeds one (`time/tzdata`).

## Not covered

- Event triggers (a run started by an incoming event) are a separate piece of work; they can use the same queueing code.
- Ramping up to a target concurrency, and per-schedule retry.
- A console tab for schedules: they are created and read through the API.
