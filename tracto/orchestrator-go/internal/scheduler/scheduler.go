// Package scheduler fires the schedules stored in ScheduleRecords.
//
// Every few seconds it looks for enabled schedules whose NextRunAt has passed and queues the
// schedule's script through the same path POST /jobs uses. Rules:
//
//   - A schedule fires at most once per fire time. NextRunAt is moved on with a compare-and-swap
//     before anything is queued, so two ticks (or two processes) cannot both fire it.
//   - After downtime, all the fire times a schedule missed are collapsed into the most recent
//     one, which runs once if it is inside the schedule's grace window and is skipped otherwise.
//     Missed fires are never replayed one by one.
//   - Schedules that are due together are handled oldest first.
//   - With overlap policy "skip" (the default) a fire is skipped while the schedule's previous
//     run is still active; with "queue" it is queued behind it.
//   - A schedule that cannot be queued (for example its script version is gone) records why and
//     still moves on to its next fire time.
package scheduler

import (
	"context"
	"database/sql"
	"encoding/json"
	"fmt"
	"log/slog"
	"time"

	"tracto/orchestrator/internal/cronspec"
	"tracto/orchestrator/internal/handlers"
)

const (
	// maxCatchUp bounds how many missed fire times are stepped over to find the latest one.
	maxCatchUp = 200000
)

// Scheduler fires due schedules.
type Scheduler struct {
	DB     *sql.DB
	Logger *slog.Logger
	// Now is the clock; tests replace it.
	Now func() time.Time
	// Every is how often Run looks for due schedules.
	Every time.Duration
}

// New returns a scheduler that checks every 15 seconds, like the other monitors.
func New(db *sql.DB, logger *slog.Logger) *Scheduler {
	return &Scheduler{DB: db, Logger: logger, Now: time.Now, Every: 15 * time.Second}
}

// Run ticks until ctx ends. It ticks once at once, so a schedule that came due while the
// Orchestrator was down is handled at start-up.
func (s *Scheduler) Run(ctx context.Context) {
	s.Tick(s.Now())
	t := time.NewTicker(s.Every)
	defer t.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-t.C:
			s.Tick(s.Now())
		}
	}
}

type due struct {
	id, expr, zone, artifact, version, paramsJSON, overlap string
	next, lastRunID                                        sql.NullString
	grace                                                  int
}

// Tick handles every schedule that is due at now.
func (s *Scheduler) Tick(now time.Time) {
	rows, err := s.DB.Query(`
		SELECT ScheduleId, CronExpression, COALESCE(TimeZone,'UTC'), ArtifactId, Version,
		       COALESCE(ParamsJson,'{}'), COALESCE(OverlapPolicy,'skip'), NextRunAt, LastRunId,
		       COALESCE(GraceSeconds,?)
		FROM ScheduleRecords
		WHERE Enabled=1 AND (NextRunAt IS NULL OR NextRunAt<=?)
		ORDER BY NextRunAt ASC, ScheduleId ASC`,
		cronspec.DefaultGraceSeconds, cronspec.Format(now))
	if err != nil {
		s.Logger.Error("scheduler: query failed", "err", err)
		return
	}
	var list []due
	for rows.Next() {
		var d due
		if err := rows.Scan(&d.id, &d.expr, &d.zone, &d.artifact, &d.version, &d.paramsJSON,
			&d.overlap, &d.next, &d.lastRunID, &d.grace); err != nil {
			s.Logger.Error("scheduler: scan failed", "err", err)
			continue
		}
		list = append(list, d)
	}
	rows.Close()

	for _, d := range list {
		s.fire(d, now)
	}
}

func (s *Scheduler) fire(d due, now time.Time) {
	sched, err := cronspec.Parse(d.expr, d.zone)
	if err != nil {
		s.disable(d.id, err.Error())
		return
	}
	next := sched.Next(now)
	if next.IsZero() {
		s.disable(d.id, fmt.Sprintf("cronExpression %q never fires", d.expr))
		return
	}

	// A schedule that has never been given a fire time starts from now: nothing is owed.
	scheduled, perr := time.Time{}, error(nil)
	if d.next.Valid {
		scheduled, perr = cronspec.ParseTime(d.next.String)
	}
	if !d.next.Valid || perr != nil {
		s.claim(d.id, d.next, next)
		return
	}

	// Collapse every fire time that was missed into the latest one.
	latest := scheduled
	for i := 0; i < maxCatchUp; i++ {
		n := sched.Next(latest)
		if n.After(now) {
			break
		}
		latest = n
	}

	// Claim the fire before queueing anything, so it cannot happen twice.
	if !s.claim(d.id, d.next, next) {
		return
	}

	late := now.Sub(latest)
	grace := time.Duration(d.grace) * time.Second
	if late > grace {
		s.record(d.id, latest, "Skipped", fmt.Sprintf(
			"fire time %s was missed by %s, beyond the %ds grace window",
			cronspec.Format(latest), late.Truncate(time.Second), d.grace))
		s.Logger.Warn("schedule fire skipped: too late", "schedule", d.id, "fire", cronspec.Format(latest))
		return
	}

	if d.overlap != cronspec.OverlapQueue && d.lastRunID.Valid {
		active, err := handlers.RunIsActive(s.DB, d.lastRunID.String)
		if err != nil {
			s.record(d.id, latest, "Failed", "could not check the previous run: "+err.Error())
			return
		}
		if active {
			s.record(d.id, latest, "Skipped", "previous run "+d.lastRunID.String+" is still active")
			s.Logger.Info("schedule fire skipped: previous run active", "schedule", d.id)
			return
		}
	}

	params := map[string]string{}
	if err := json.Unmarshal([]byte(d.paramsJSON), &params); err != nil {
		s.record(d.id, latest, "Failed", "params are not a JSON object of strings: "+err.Error())
		return
	}
	runID, err := handlers.EnqueueScheduled(s.DB, handlers.ScheduledRun{
		ArtifactID: d.artifact,
		Version:    d.version,
		Params:     params,
		EventID:    d.id + ":" + cronspec.Format(latest),
	})
	if err != nil {
		msg := err.Error()
		if err == handlers.ErrArtifactNotFound {
			msg = fmt.Sprintf("script %s/%s is not published", d.artifact, d.version)
		}
		s.record(d.id, latest, "Failed", msg)
		s.Logger.Error("schedule fire failed", "schedule", d.id, "err", msg)
		return
	}
	if _, err := s.DB.Exec(
		`UPDATE ScheduleRecords SET LastFireAt=?, LastRunId=?, LastStatus='Queued', LastError=NULL WHERE ScheduleId=?`,
		cronspec.Format(latest), runID, d.id); err != nil {
		s.Logger.Error("scheduler: could not record the run", "schedule", d.id, "err", err)
	}
	s.Logger.Info("schedule fired", "schedule", d.id, "run", runID, "fire", cronspec.Format(latest))
}

// claim moves NextRunAt from old to next, and reports whether this caller made the change.
func (s *Scheduler) claim(id string, old sql.NullString, next time.Time) bool {
	var res sql.Result
	var err error
	if old.Valid {
		res, err = s.DB.Exec(
			`UPDATE ScheduleRecords SET NextRunAt=? WHERE ScheduleId=? AND Enabled=1 AND NextRunAt=?`,
			cronspec.Format(next), id, old.String)
	} else {
		res, err = s.DB.Exec(
			`UPDATE ScheduleRecords SET NextRunAt=? WHERE ScheduleId=? AND Enabled=1 AND NextRunAt IS NULL`,
			cronspec.Format(next), id)
	}
	if err != nil {
		s.Logger.Error("scheduler: claim failed", "schedule", id, "err", err)
		return false
	}
	n, _ := res.RowsAffected()
	return n == 1
}

func (s *Scheduler) record(id string, fire time.Time, status, msg string) {
	if _, err := s.DB.Exec(
		`UPDATE ScheduleRecords SET LastFireAt=?, LastStatus=?, LastError=? WHERE ScheduleId=?`,
		cronspec.Format(fire), status, msg, id); err != nil {
		s.Logger.Error("scheduler: could not record status", "schedule", id, "err", err)
	}
}

// disable stops a schedule that can never fire, and says why, instead of retrying every tick.
func (s *Scheduler) disable(id, why string) {
	s.Logger.Error("schedule disabled", "schedule", id, "why", why)
	if _, err := s.DB.Exec(
		`UPDATE ScheduleRecords SET Enabled=0, NextRunAt=NULL, LastStatus='Disabled', LastError=? WHERE ScheduleId=?`,
		why, id); err != nil {
		s.Logger.Error("scheduler: could not disable", "schedule", id, "err", err)
	}
}
