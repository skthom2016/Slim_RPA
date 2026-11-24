package services

import (
	"database/sql"
	"io"
	"log/slog"
	"testing"
	"time"

	"tracto/orchestrator/internal/db/dbtest"
)

func quiet() *slog.Logger { return slog.New(slog.NewTextHandler(io.Discard, nil)) }

type seed struct {
	t *testing.T
	d *sql.DB
}

func newSeed(t *testing.T) *seed { return &seed{t, dbtest.Open(t)} }

func (s *seed) exec(q string, args ...any) {
	s.t.Helper()
	if _, err := s.d.Exec(q, args...); err != nil {
		s.t.Fatalf("%s: %v", q, err)
	}
}

func (s *seed) str(q string, args ...any) string {
	s.t.Helper()
	var v sql.NullString
	if err := s.d.QueryRow(q, args...).Scan(&v); err != nil {
		s.t.Fatal(err)
	}
	return v.String
}

func (s *seed) count(q string, args ...any) int {
	s.t.Helper()
	var n int
	if err := s.d.QueryRow(q, args...).Scan(&n); err != nil {
		s.t.Fatal(err)
	}
	return n
}

func (s *seed) worker(id, state string, online bool, lastBeat time.Time, thresholdSec int) {
	s.exec(`INSERT INTO WorkerRecords(WorkerId,RuntimeType,State,RegisteredAt,LastHeartbeatAt,AbsenceThresholdSeconds,IsOnline)
		VALUES($1,'desktop',$2,$3,$3,$4,$5)`, id, state, lastBeat.UTC(), thresholdSec, online)
	s.exec(`UPDATE WorkerRecords SET LastHeartbeatAt=$1 WHERE WorkerId=$2`, lastBeat.UTC(), id)
}

func (s *seed) run(id, state, attempt, worker string, deadline *time.Time, parent string) {
	var dl, par, wk any
	if deadline != nil {
		dl = deadline.UTC()
	}
	if parent != "" {
		par = parent
	}
	if worker != "" {
		wk = worker
	}
	s.exec(`INSERT INTO RunRecords(Id,AttemptId,CreatedAt,State,WorkerId,Deadline,ParentRunId,ScriptArtifactId,ScriptVersion)
		VALUES($1,$2,$3,$4,$5,$6,$7,'s','1')`, id, attempt, time.Now().UTC(), state, wk, dl, par)
}

func (s *seed) lease(id, runID, attempt, worker, state string, lastBeat time.Time) {
	now := time.Now().UTC()
	s.exec(`INSERT INTO LeaseRecords(LeaseId,RunId,AttemptId,WorkerId,ArtifactId,ArtifactVersion,ArtifactHash,OfferedAt,ExpiresAt,AcceptedAt,State,LastHeartbeatAt)
		VALUES($1,$2,$3,$4,'s','1','h',$5,$6,$5,$7,$8)`, id, runID, attempt, worker, now, now.Add(time.Hour), state, lastBeat.UTC())
}

func (s *seed) journal(runID, attempt, event string) {
	s.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt) VALUES(gen_random_uuid()::text,$1,$2,$3,$4)`,
		runID, attempt, event, time.Now().UTC())
}

var longAgo = time.Now().Add(-10 * time.Minute)

// A lease that stopped sending heartbeats after the run started puts the run back in the queue
// with a new attempt, and frees the worker.
func TestExpireStaleLeases_RequeuesARunThatHadStarted(t *testing.T) {
	s := newSeed(t)
	s.worker("w1", "Running", true, time.Now(), 60)
	s.run("r1", "Running", "a1", "w1", nil, "")
	s.lease("l1", "r1", "a1", "w1", "Accepted", longAgo)
	s.journal("r1", "a1", "RUN_RUNNING")

	expireStaleLeases(s.d, quiet())

	if got := s.str(`SELECT State FROM RunRecords WHERE Id='r1'`); got != "Queued" {
		t.Fatalf("run state: %s", got)
	}
	if got := s.str(`SELECT AttemptId FROM RunRecords WHERE Id='r1'`); got == "a1" || got == "" {
		t.Errorf("the attempt must change so the old worker is fenced out: %q", got)
	}
	if s.count(`SELECT COUNT(*) FROM RunRecords WHERE Id='r1' AND WorkerId IS NULL`) != 1 {
		t.Error("the run must be released from its worker")
	}
	if got := s.str(`SELECT State FROM LeaseRecords WHERE LeaseId='l1'`); got != "Expired" {
		t.Errorf("lease: %s", got)
	}
	if got := s.str(`SELECT State FROM WorkerRecords WHERE WorkerId='w1'`); got != "Available" {
		t.Errorf("worker: %s", got)
	}
	if s.count(`SELECT COUNT(*) FROM JournalEntries WHERE RunId='r1' AND EventType='RUN_REQUEUED'`) != 1 {
		t.Error("no RUN_REQUEUED journal entry")
	}

	// Running the check again changes nothing: the lease is already expired.
	attempt := s.str(`SELECT AttemptId FROM RunRecords WHERE Id='r1'`)
	expireStaleLeases(s.d, quiet())
	if again := s.str(`SELECT AttemptId FROM RunRecords WHERE Id='r1'`); again != attempt {
		t.Error("a second pass requeued the run again")
	}
}

// A lease that never showed the run starting leaves the outcome unknown, and a person decides.
func TestExpireStaleLeases_NoStartMeansUnknown_AndTakesTheParentWithIt(t *testing.T) {
	s := newSeed(t)
	s.worker("w1", "Running", true, time.Now(), 60)
	s.run("parent", "Running", "pa", "", nil, "")
	s.run("child", "Running", "ca", "w1", nil, "parent")
	s.lease("l1", "child", "ca", "w1", "Accepted", longAgo)

	expireStaleLeases(s.d, quiet())

	if got := s.str(`SELECT State FROM RunRecords WHERE Id='child'`); got != "Unknown" {
		t.Fatalf("child: %s", got)
	}
	if s.count(`SELECT COUNT(*) FROM RunRecords WHERE Id='child' AND NeedsAttention`) != 1 {
		t.Error("the child needs attention")
	}
	if got := s.str(`SELECT State FROM RunRecords WHERE Id='parent'`); got != "Unknown" {
		t.Errorf("a segment that ends Unknown takes its parent with it, parent is %s", got)
	}
}

func TestExpireStaleLeases_LeavesALeaseThatIsStillBeating(t *testing.T) {
	s := newSeed(t)
	s.worker("w1", "Running", true, time.Now(), 60)
	s.run("r1", "Running", "a1", "w1", nil, "")
	s.lease("l1", "r1", "a1", "w1", "Accepted", time.Now())
	expireStaleLeases(s.d, quiet())
	if got := s.str(`SELECT State FROM RunRecords WHERE Id='r1'`); got != "Running" {
		t.Fatalf("a healthy run was touched: %s", got)
	}
	if got := s.str(`SELECT State FROM LeaseRecords WHERE LeaseId='l1'`); got != "Accepted" {
		t.Fatalf("a healthy lease was touched: %s", got)
	}
}

func TestTimeOutOverdue(t *testing.T) {
	s := newSeed(t)
	past, future := time.Now().Add(-time.Minute), time.Now().Add(time.Hour)
	s.worker("w1", "Running", true, time.Now(), 60)
	s.worker("w2", "Running", true, time.Now(), 60)
	s.run("late", "Running", "a1", "w1", &past, "")
	s.lease("l1", "late", "a1", "w1", "Accepted", time.Now())
	s.run("early", "Running", "a2", "w2", &future, "")
	s.run("done", "Succeeded", "a3", "", &past, "")
	// A segmented parent carries the deadline; its live child is timed out with it.
	s.run("parent", "Running", "pa", "", &past, "")
	s.run("kid", "Running", "ka", "", nil, "parent")

	TimeOutOverdue(s.d, quiet())

	for id, want := range map[string]string{"late": "TimedOut", "early": "Running", "done": "Succeeded", "parent": "TimedOut", "kid": "TimedOut"} {
		if got := s.str(`SELECT State FROM RunRecords WHERE Id=$1`, id); got != want {
			t.Errorf("%s: %s, want %s", id, got, want)
		}
	}
	if got := s.str(`SELECT State FROM WorkerRecords WHERE WorkerId='w1'`); got != "Available" {
		t.Errorf("the timed-out run's worker must be freed: %s", got)
	}
	if got := s.str(`SELECT State FROM WorkerRecords WHERE WorkerId='w2'`); got != "Running" {
		t.Errorf("another worker was freed: %s", got)
	}
	if got := s.str(`SELECT State FROM LeaseRecords WHERE LeaseId='l1'`); got != "Expired" {
		t.Errorf("lease: %s", got)
	}
	if s.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId='late' AND TerminalState='TimedOut'`) != 1 {
		t.Error("no final result for the timed-out run")
	}
	if s.count(`SELECT COUNT(*) FROM JournalEntries WHERE RunId='late' AND EventType='RUN_TIMED_OUT'`) != 1 {
		t.Error("no journal entry")
	}

	// A second pass writes nothing more.
	TimeOutOverdue(s.d, quiet())
	if n := s.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId='late'`); n != 1 {
		t.Errorf("final results after a second pass: %d", n)
	}
}

// After a restart, a worker that has been silent longer than its threshold is marked offline and
// the runs it held go back in the queue.
func TestRestartRecovery_RequeuesTheRunsOfAnAbsentWorker(t *testing.T) {
	s := newSeed(t)
	s.worker("gone", "Running", true, longAgo, 60)
	s.worker("here", "Running", true, time.Now(), 60)
	s.run("r1", "Running", "a1", "gone", nil, "")
	s.lease("l1", "r1", "a1", "gone", "Accepted", longAgo)
	s.run("r2", "Running", "a2", "here", nil, "")

	RestartRecovery(s.d, quiet())

	if s.count(`SELECT COUNT(*) FROM WorkerRecords WHERE WorkerId='gone' AND NOT IsOnline AND State='Offline'`) != 1 {
		t.Error("the absent worker must be marked offline")
	}
	if s.count(`SELECT COUNT(*) FROM WorkerRecords WHERE WorkerId='here' AND IsOnline`) != 1 {
		t.Error("a present worker must stay online")
	}
	if got := s.str(`SELECT State FROM RunRecords WHERE Id='r1'`); got != "Queued" {
		t.Errorf("r1: %s", got)
	}
	if got := s.str(`SELECT AttemptId FROM RunRecords WHERE Id='r1'`); got == "a1" {
		t.Error("the attempt must change")
	}
	if got := s.str(`SELECT State FROM LeaseRecords WHERE LeaseId='l1'`); got != "Expired" {
		t.Errorf("lease: %s", got)
	}
	if got := s.str(`SELECT State FROM RunRecords WHERE Id='r2'`); got != "Running" {
		t.Errorf("r2 belongs to a present worker: %s", got)
	}
}
