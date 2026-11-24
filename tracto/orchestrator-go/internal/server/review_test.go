package server

import (
	"net/http"
	"testing"
)

// SQLite stored NUL characters; PostgreSQL text refuses them. A worker that sends one must not
// make the write fail (and, for a result, retry for ever): the NUL is dropped, the rest is kept.
func TestNulCharacters_AreDroppedNotFatal(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)

	out := e.must(http.StatusAccepted, "POST", "/jobs/"+runID+"/logs", map[string]any{
		"entries": []map[string]any{{"level": "Error", "message": "bad\x00text", "stepId": "st\x00ep"}},
	})
	if out["stored"] != float64(1) {
		t.Fatalf("log batch: %v", out)
	}
	if n := e.count(`SELECT COUNT(*) FROM RunLogs WHERE RunId=$1 AND Message='badtext' AND StepId='step'`, runID); n != 1 {
		t.Errorf("the log line must be stored without the NUL: %d", n)
	}

	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/result", map[string]any{
		"attemptId": o.attemptID, "terminalState": "Failed", "errorCategory": "E\x00RR", "businessOutcome": "out\x00come",
		"outputs": map[string]string{"k\x00": "v\x00w"},
	})
	job := e.must(http.StatusOK, "GET", "/jobs/"+runID, nil)
	if str(job, "state") != "FAILED" || job["outputs"].(map[string]any)["k"] != "vw" {
		t.Fatalf("job after a result with NUL characters: %v", job)
	}
	if n := e.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId=$1 AND ErrorCategory='ERR' AND BusinessOutcome='outcome'`, runID); n != 1 {
		t.Error("the final result must be stored without the NUL")
	}
}

// Accepting a lease must not move a run that is no longer queued for it, and the half-done accept
// must be rolled back (the lease stays as it was).
func TestAccept_RefusedWhenTheRunIsNoLongerQueued(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")

	// the run is closed by something else between the worker's pull and its accept, leaving the offer behind
	if _, err := e.db.Exec(`UPDATE RunRecords SET State='Cancelled' WHERE Id=$1`, runID); err != nil {
		t.Fatal(err)
	}
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/lease/accept", map[string]any{"attemptId": o.attemptID, "workerId": "w1"})
	if got := e.runState(runID); got != "CANCELLED" {
		t.Fatalf("a refused accept must not change the run: %s", got)
	}
	if n := e.count(`SELECT COUNT(*) FROM LeaseRecords WHERE RunId=$1 AND State='Offered'`, runID); n != 1 {
		t.Errorf("a refused accept must leave the lease as it was, rolled back: %d offered", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=$1 AND EventType='RUN_RUNNING'`, runID); n != 0 {
		t.Errorf("no RUN_RUNNING entry for a refused accept: %d", n)
	}
}

// A result for an attempt the run has moved past (it was requeued) is refused and changes nothing.
func TestResult_ForAnOldAttemptAfterARequeueIsRefused(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)

	// what lease expiry does: back to the queue with a new attempt
	if _, err := e.db.Exec(`UPDATE RunRecords SET State='Queued', AttemptId='new-attempt', WorkerId=NULL WHERE Id=$1`, runID); err != nil {
		t.Fatal(err)
	}
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/result", map[string]any{"attemptId": o.attemptID, "terminalState": "Succeeded"})
	if got := e.runState(runID); got != "QUEUED" {
		t.Fatalf("an old attempt's result must not close the requeued run: %s", got)
	}
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/reconcile", map[string]any{"attemptId": o.attemptID})
	if got := e.runState(runID); got != "QUEUED" {
		t.Fatalf("an old attempt's reconcile must not close the requeued run: %s", got)
	}
}
