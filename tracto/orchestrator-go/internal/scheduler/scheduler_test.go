package scheduler

import (
	"database/sql"
	"io"
	"log/slog"
	"path/filepath"
	"testing"
	"time"

	"tracto/orchestrator/internal/cronspec"
	"tracto/orchestrator/internal/db"
)

func openDB(t *testing.T) *sql.DB {
	t.Helper()
	d, err := db.Open(filepath.Join(t.TempDir(), "orch.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { d.Close() })
	return d
}

func newScheduler(d *sql.DB) *Scheduler {
	return New(d, slog.New(slog.NewTextHandler(io.Discard, nil)))
}

func publish(t *testing.T, d *sql.DB, id, version string) {
	t.Helper()
	if _, err := d.Exec(`INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt) VALUES(?,?,?,?,?)`,
		id, version, "1", "h", "2026-01-01T00:00:00Z"); err != nil {
		t.Fatal(err)
	}
	payload := `{"schemaVersion":"1","script":{"id":"` + id + `","version":"` + version + `","targetRuntime":"desktop","steps":[]}}`
	if _, err := d.Exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES(?,?,?)`, id, version, []byte(payload)); err != nil {
		t.Fatal(err)
	}
}

type sched struct {
	id, expr, zone, artifact, version, overlap string
	next                                       string // "" means NULL
	grace                                      int
}

func addSchedule(t *testing.T, d *sql.DB, s sched) {
	t.Helper()
	if s.zone == "" {
		s.zone = "UTC"
	}
	if s.overlap == "" {
		s.overlap = "skip"
	}
	if s.grace == 0 {
		s.grace = 900
	}
	if s.version == "" {
		s.version = "1.0.0"
	}
	var next any
	if s.next != "" {
		next = s.next
	}
	if _, err := d.Exec(`INSERT INTO ScheduleRecords(ScheduleId,CronExpression,TimeZone,ArtifactId,Version,ParamsJson,Enabled,
		OverlapPolicy,GraceSeconds,NextRunAt,CreatedAt) VALUES(?,?,?,?,?,'{"who":"sched"}',1,?,?,?,'2026-01-01T00:00:00Z')`,
		s.id, s.expr, s.zone, s.artifact, s.version, s.overlap, s.grace, next); err != nil {
		t.Fatal(err)
	}
}

func at(t *testing.T, s string) time.Time {
	t.Helper()
	v, err := time.Parse(time.RFC3339, s)
	if err != nil {
		t.Fatal(err)
	}
	return v
}

type run struct {
	id, artifact, trigger, event, params string
}

func runs(t *testing.T, d *sql.DB) []run {
	t.Helper()
	rows, err := d.Query(`SELECT Id,ScriptArtifactId,TriggerType,COALESCE(EventId,''),COALESCE(ParamsJson,'') FROM RunRecords ORDER BY CreatedAt`)
	if err != nil {
		t.Fatal(err)
	}
	defer rows.Close()
	var out []run
	for rows.Next() {
		var r run
		if err := rows.Scan(&r.id, &r.artifact, &r.trigger, &r.event, &r.params); err != nil {
			t.Fatal(err)
		}
		out = append(out, r)
	}
	return out
}

type state struct {
	next, lastFire, lastRun, status, errText string
}

func stateOf(t *testing.T, d *sql.DB, id string) state {
	t.Helper()
	var s state
	err := d.QueryRow(`SELECT COALESCE(NextRunAt,''),COALESCE(LastFireAt,''),COALESCE(LastRunId,''),
		COALESCE(LastStatus,''),COALESCE(LastError,'') FROM ScheduleRecords WHERE ScheduleId=?`, id).
		Scan(&s.next, &s.lastFire, &s.lastRun, &s.status, &s.errText)
	if err != nil {
		t.Fatal(err)
	}
	return s
}

func TestTick_FiresADueScheduleOnceAndMovesOn(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "report", next: "2026-11-12T09:00:00Z"})
	s := newScheduler(d)

	s.Tick(at(t, "2026-11-12T09:00:20Z"))
	s.Tick(at(t, "2026-11-12T09:00:35Z")) // a second tick must not fire it again

	got := runs(t, d)
	if len(got) != 1 {
		t.Fatalf("got %d runs, want 1: %+v", len(got), got)
	}
	if got[0].trigger != "Schedule" || got[0].artifact != "report" {
		t.Errorf("run = %+v", got[0])
	}
	if got[0].event != "s1:2026-11-12T09:00:00Z" {
		t.Errorf("eventId = %q", got[0].event)
	}
	if got[0].params == "" || got[0].params == "{}" {
		t.Errorf("the schedule's params did not reach the run: %q", got[0].params)
	}
	st := stateOf(t, d, "s1")
	if st.next != "2026-11-13T09:00:00Z" || st.status != "Queued" || st.lastRun != got[0].id || st.lastFire != "2026-11-12T09:00:00Z" {
		t.Errorf("state = %+v", st)
	}
}

func TestTick_LeavesAScheduleThatIsNotDueYet(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "report", next: "2026-11-12T09:00:00Z"})
	newScheduler(d).Tick(at(t, "2026-11-12T08:59:59Z"))
	if n := len(runs(t, d)); n != 0 {
		t.Fatalf("got %d runs, want 0", n)
	}
}

func TestTick_DowntimeRunsOnlyTheLatestMissedFire(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	// Fires every 15 minutes; the Orchestrator was down from before 08:00 until 08:16.
	addSchedule(t, d, sched{id: "s1", expr: "*/15 * * * *", artifact: "report", next: "2026-11-12T08:00:00Z"})

	newScheduler(d).Tick(at(t, "2026-11-12T08:16:00Z"))

	got := runs(t, d)
	if len(got) != 1 || got[0].event != "s1:2026-11-12T08:15:00Z" {
		t.Fatalf("want one run for 08:15, got %+v", got)
	}
	if st := stateOf(t, d, "s1"); st.next != "2026-11-12T08:30:00Z" {
		t.Errorf("next = %s, want 08:30", st.next)
	}
}

func TestTick_AMissedFireBeyondTheGraceWindowIsSkippedNotRun(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "*/15 * * * *", artifact: "report", next: "2026-11-12T08:00:00Z", grace: 900})

	newScheduler(d).Tick(at(t, "2026-11-12T09:05:00Z")) // latest missed fire is 09:00, five minutes late: inside grace
	if n := len(runs(t, d)); n != 1 {
		t.Fatalf("a fire five minutes late must run, got %d runs", n)
	}

	d2 := openDB(t)
	publish(t, d2, "report", "1.0.0")
	addSchedule(t, d2, sched{id: "s1", expr: "0 8 * * *", artifact: "report", next: "2026-11-12T08:00:00Z", grace: 900})
	newScheduler(d2).Tick(at(t, "2026-11-12T10:00:00Z")) // two hours late: outside grace
	if n := len(runs(t, d2)); n != 0 {
		t.Fatalf("a fire two hours late must not run, got %d runs", n)
	}
	st := stateOf(t, d2, "s1")
	if st.status != "Skipped" || st.errText == "" || st.next != "2026-11-13T08:00:00Z" {
		t.Errorf("state = %+v", st)
	}
}

func TestTick_SchedulesDueTogetherAreQueuedOldestFirst(t *testing.T) {
	d := openDB(t)
	publish(t, d, "late-script", "1.0.0")
	publish(t, d, "early-script", "1.0.0")
	// Inserted in the opposite order to their due times.
	addSchedule(t, d, sched{id: "b", expr: "15 8 * * *", artifact: "late-script", next: "2026-11-12T08:15:00Z"})
	addSchedule(t, d, sched{id: "a", expr: "0 8 * * *", artifact: "early-script", next: "2026-11-12T08:00:00Z", grace: 1800})

	newScheduler(d).Tick(at(t, "2026-11-12T08:16:00Z"))

	got := runs(t, d)
	if len(got) != 2 || got[0].artifact != "early-script" || got[1].artifact != "late-script" {
		t.Fatalf("want early-script then late-script, got %+v", got)
	}
}

func TestTick_OverlapSkipWaitsForThePreviousRunButQueueDoesNot(t *testing.T) {
	for _, tc := range []struct {
		overlap string
		runs    int
	}{{"skip", 1}, {"queue", 2}} {
		d := openDB(t)
		publish(t, d, "report", "1.0.0")
		addSchedule(t, d, sched{id: "s1", expr: "*/5 * * * *", artifact: "report", next: "2026-11-12T09:00:00Z", overlap: tc.overlap})
		s := newScheduler(d)

		s.Tick(at(t, "2026-11-12T09:00:10Z"))
		s.Tick(at(t, "2026-11-12T09:05:10Z")) // the first run is still Queued

		if n := len(runs(t, d)); n != tc.runs {
			t.Errorf("overlap %s: got %d runs, want %d", tc.overlap, n, tc.runs)
		}
		if tc.overlap == "skip" {
			if st := stateOf(t, d, "s1"); st.status != "Skipped" {
				t.Errorf("status = %q, want Skipped", st.status)
			}
			// Once the first run has ended the next fire goes ahead.
			if _, err := d.Exec(`UPDATE RunRecords SET State='Succeeded'`); err != nil {
				t.Fatal(err)
			}
			s.Tick(at(t, "2026-11-12T09:10:10Z"))
			if n := len(runs(t, d)); n != 2 {
				t.Errorf("after the first run ended: got %d runs, want 2", n)
			}
		}
	}
}

func TestTick_AScriptThatIsNotPublishedIsRecordedAndTheScheduleMovesOn(t *testing.T) {
	d := openDB(t)
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "gone", next: "2026-11-12T09:00:00Z"})
	newScheduler(d).Tick(at(t, "2026-11-12T09:00:10Z"))

	if n := len(runs(t, d)); n != 0 {
		t.Fatalf("got %d runs, want 0", n)
	}
	st := stateOf(t, d, "s1")
	if st.status != "Failed" || st.errText == "" || st.next != "2026-11-13T09:00:00Z" {
		t.Errorf("state = %+v", st)
	}
}

func TestTick_AScheduleThatCanNeverFireIsDisabledWithAReason(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "not a cron", artifact: "report", next: "2026-11-12T09:00:00Z"})
	addSchedule(t, d, sched{id: "s2", expr: "0 9 * * *", zone: "Mars/Olympus", artifact: "report", next: "2026-11-12T09:00:00Z"})
	newScheduler(d).Tick(at(t, "2026-11-12T09:00:10Z"))

	for _, id := range []string{"s1", "s2"} {
		var enabled int
		if err := d.QueryRow(`SELECT Enabled FROM ScheduleRecords WHERE ScheduleId=?`, id).Scan(&enabled); err != nil {
			t.Fatal(err)
		}
		if st := stateOf(t, d, id); enabled != 0 || st.status != "Disabled" || st.errText == "" {
			t.Errorf("%s: enabled=%d state=%+v", id, enabled, st)
		}
	}
	if n := len(runs(t, d)); n != 0 {
		t.Errorf("got %d runs, want 0", n)
	}
}

func TestTick_AScheduleWithNoFireTimeStartsFromNowAndOwesNothing(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "report"}) // NextRunAt is NULL
	newScheduler(d).Tick(at(t, "2026-11-12T12:00:00Z"))

	if n := len(runs(t, d)); n != 0 {
		t.Fatalf("got %d runs, want 0", n)
	}
	if st := stateOf(t, d, "s1"); st.next != "2026-11-13T09:00:00Z" {
		t.Errorf("next = %s, want tomorrow 09:00", st.next)
	}
}

func TestClaim_OnlyOneCallerWinsAFire(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "report", next: "2026-11-12T09:00:00Z"})
	s := newScheduler(d)
	old := sql.NullString{String: "2026-11-12T09:00:00Z", Valid: true}
	next := at(t, "2026-11-13T09:00:00Z")

	if !s.claim("s1", old, next) {
		t.Fatal("the first claim must win")
	}
	if s.claim("s1", old, next) {
		t.Fatal("a second claim of the same fire must lose")
	}
}

func TestTick_ADisabledScheduleNeverFires(t *testing.T) {
	d := openDB(t)
	publish(t, d, "report", "1.0.0")
	addSchedule(t, d, sched{id: "s1", expr: "0 9 * * *", artifact: "report", next: "2026-11-12T09:00:00Z"})
	if _, err := d.Exec(`UPDATE ScheduleRecords SET Enabled=0`); err != nil {
		t.Fatal(err)
	}
	newScheduler(d).Tick(at(t, "2026-11-12T09:00:10Z"))
	if n := len(runs(t, d)); n != 0 {
		t.Fatalf("got %d runs, want 0", n)
	}
}

// ── time zones ───────────────────────────────────────────────────────────────

func TestNext_UsesTheNamedZoneNotTheServers(t *testing.T) {
	// 08:00 in Kolkata (UTC+5:30) is 02:30 UTC.
	got, err := cronspec.Next("0 8 * * *", "Asia/Kolkata", at(t, "2026-11-12T01:00:00Z"))
	if err != nil {
		t.Fatal(err)
	}
	if want := at(t, "2026-11-12T02:30:00Z"); !got.Equal(want) {
		t.Errorf("got %s, want %s", got, want)
	}
}

// firesBetween lists every fire time of a schedule in [from, to).
func firesBetween(t *testing.T, expr, zone string, from, to time.Time) []time.Time {
	t.Helper()
	var out []time.Time
	cur := from.Add(-time.Second)
	for {
		n, err := cronspec.Next(expr, zone, cur)
		if err != nil {
			t.Fatal(err)
		}
		if !n.Before(to) {
			return out
		}
		out = append(out, n)
		cur = n
	}
}

func TestNext_DaylightSavingFiresOncePerDay(t *testing.T) {
	ny, err := time.LoadLocation("America/New_York")
	if err != nil {
		t.Fatal(err)
	}
	// Clocks go forward on 2026-03-08 (02:30 does not exist) and back on 2026-11-01 (01:30 happens twice).
	day := func(y int, m time.Month, d int) time.Time { return time.Date(y, m, d, 0, 0, 0, 0, ny) }
	for _, tc := range []struct {
		name, expr string
		day        time.Time
		want       string // the one fire, in UTC
	}{
		{"spring forward", "30 2 * * *", day(2026, 3, 8), "2026-03-08T07:30:00Z"},
		{"fall back", "30 1 * * *", day(2026, 11, 1), "2026-11-01T05:30:00Z"},
		{"an ordinary day", "30 2 * * *", day(2026, 3, 9), "2026-03-09T06:30:00Z"},
	} {
		fires := firesBetween(t, tc.expr, "America/New_York", tc.day, tc.day.AddDate(0, 0, 1))
		if len(fires) != 1 || !fires[0].Equal(at(t, tc.want)) {
			t.Errorf("%s: fires = %v, want exactly %s", tc.name, fires, tc.want)
		}
	}
}

func TestNext_AFrequentScheduleHasNoRepeatsAcrossTheClockChange(t *testing.T) {
	// Every 15 minutes across the spring-forward gap: strictly increasing, no duplicates.
	from := at(t, "2026-03-08T06:00:00Z")
	prev := from
	for i := 0; i < 20; i++ {
		n, err := cronspec.Next("*/15 * * * *", "America/New_York", prev)
		if err != nil {
			t.Fatal(err)
		}
		if !n.After(prev) {
			t.Fatalf("fire %d (%s) is not after the one before (%s)", i, n, prev)
		}
		prev = n
	}
}
