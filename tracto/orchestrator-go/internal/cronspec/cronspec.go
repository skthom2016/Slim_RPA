// Package cronspec turns a cron expression and an IANA time zone into fire times.
package cronspec

import (
	"fmt"
	"strings"
	"time"
	// Windows has no zone database of its own, so a named zone needs the one built into the binary.
	_ "time/tzdata"

	"github.com/robfig/cron/v3"
)

// Limits and defaults shared by the schedule API and the scheduler.
const (
	// DefaultGraceSeconds is how late a fire may still run when a schedule sets no grace window.
	DefaultGraceSeconds = 900
	MinGraceSeconds     = 60
	MaxGraceSeconds     = 86400

	OverlapSkip  = "skip"
	OverlapQueue = "queue"
)

// Parse reads a five-field cron expression (or an @hourly-style descriptor) as wall-clock time
// in the named IANA zone, for example "Asia/Kolkata". The zone is a separate argument so it is
// never guessed: a CRON_TZ= or TZ= prefix inside the expression is refused.
func Parse(expr, zone string) (cron.Schedule, error) {
	expr = strings.TrimSpace(expr)
	zone = strings.TrimSpace(zone)
	if expr == "" {
		return nil, fmt.Errorf("cronExpression is required")
	}
	if zone == "" {
		return nil, fmt.Errorf("timeZone is required, for example \"Asia/Kolkata\" or \"UTC\"")
	}
	up := strings.ToUpper(expr)
	if strings.HasPrefix(up, "CRON_TZ=") || strings.HasPrefix(up, "TZ=") {
		return nil, fmt.Errorf("put the time zone in timeZone, not in the expression")
	}
	if strings.EqualFold(zone, "local") {
		return nil, fmt.Errorf("timeZone must be a named zone such as \"Asia/Kolkata\" or \"UTC\", not \"Local\"")
	}
	if _, err := time.LoadLocation(zone); err != nil {
		return nil, fmt.Errorf("unknown timeZone %q", zone)
	}
	loc, _ := time.LoadLocation(zone)
	// The expression is read as plain wall-clock time ("08:00") and mapped to real instants by
	// zoned below, because the cron library's own zone handling skips a schedule on the day
	// clocks go forward and fires it twice on the day they go back.
	inner, err := cron.ParseStandard("CRON_TZ=UTC " + expr)
	if err != nil {
		return nil, fmt.Errorf("invalid cronExpression %q: %v", expr, err)
	}
	return zoned{inner: inner, loc: loc}, nil
}

// zoned fires a wall-clock schedule in one time zone.
//
//   - A wall-clock time that does not exist (clocks went forward over it) fires once, moved
//     later by the length of the gap (02:30 becomes 03:30 when 02:00 jumps to 03:00).
//   - A wall-clock time that happens twice (clocks went back over it) fires once, the first time.
type zoned struct {
	inner cron.Schedule
	loc   *time.Location
}

// Next returns the first fire time strictly after t.
func (z zoned) Next(t time.Time) time.Time {
	lt := t.In(z.loc)
	wall := time.Date(lt.Year(), lt.Month(), lt.Day(), lt.Hour(), lt.Minute(), lt.Second(), 0, time.UTC)
	for i := 0; i < 10; i++ {
		w := z.inner.Next(wall)
		if w.IsZero() {
			return w
		}
		inst := time.Date(w.Year(), w.Month(), w.Day(), w.Hour(), w.Minute(), 0, 0, z.loc)
		if l := inst.In(z.loc); l.Hour() != w.Hour() || l.Minute() != w.Minute() {
			// The wall-clock time falls in the gap where clocks went forward. Go maps it to an
			// instant before the gap; use the offset from before the change so it lands the
			// length of the gap later instead.
			wallUTC := time.Date(w.Year(), w.Month(), w.Day(), w.Hour(), w.Minute(), 0, 0, time.UTC)
			_, off := wallUTC.Add(-24 * time.Hour).In(z.loc).Zone()
			inst = wallUTC.Add(-time.Duration(off) * time.Second)
		}
		if inst.After(t) {
			return inst
		}
		wall = w
	}
	return time.Time{}
}

// Next returns the first fire time strictly after t, in UTC.
func Next(expr, zone string, t time.Time) (time.Time, error) {
	sched, err := Parse(expr, zone)
	if err != nil {
		return time.Time{}, err
	}
	n := sched.Next(t)
	if n.IsZero() {
		return time.Time{}, fmt.Errorf("cronExpression %q never fires", expr)
	}
	return n.UTC(), nil
}

// Format is the one text form used for fire times stored in the database. Fixed-width UTC, so
// two stored times compare correctly as text.
func Format(t time.Time) string { return t.UTC().Format("2006-01-02T15:04:05Z") }

// ParseTime reads a time written by Format.
func ParseTime(s string) (time.Time, error) { return time.Parse("2006-01-02T15:04:05Z", s) }
