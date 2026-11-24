package server

import (
	"fmt"
	"net/http"
	"sync"
	"testing"
)

func TestHealth(t *testing.T) {
	e := newEnv(t)
	if out := e.must(http.StatusOK, "GET", "/health", nil); str(out, "status") != "Healthy" {
		t.Fatalf("got %v", out)
	}
}

// The whole life of one job: queued, offered to a worker, accepted, heartbeat, finished.
func TestJobLifecycle(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", map[string]string{"k": "v"})

	job := e.must(http.StatusOK, "GET", "/jobs/"+runID, nil)
	if str(job, "state") != "QUEUED" || job["params"].(map[string]any)["k"] != "v" {
		t.Fatalf("new job: %v", job)
	}

	o := e.pull("w1")
	if o.result != "JobOffered" || o.runID != runID || o.leaseID == "" || o.params["k"] != "v" {
		t.Fatalf("offer: %+v", o)
	}
	e.accept("w1", o)
	if got := e.runState(runID); got != "RUNNING" {
		t.Fatalf("after accept: %s", got)
	}
	if hb := e.must(http.StatusOK, "POST", "/jobs/"+runID+"/heartbeat", map[string]any{"attemptId": o.attemptID, "workerId": "w1", "seqNo": 1}); hb["cancellationRequested"] != false {
		t.Fatalf("heartbeat: %v", hb)
	}

	res := e.finish(o, "Succeeded", map[string]string{"out": "x"})
	if str(res, "terminalState") != "SUCCEEDED" || res["slotReleased"] != true {
		t.Fatalf("result: %v", res)
	}
	job = e.must(http.StatusOK, "GET", "/jobs/"+runID, nil)
	if str(job, "state") != "SUCCEEDED" || job["outputs"].(map[string]any)["out"] != "x" {
		t.Fatalf("finished job: %v", job)
	}

	if n := e.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId=$1 AND NOT NeedsAttention`, runID); n != 1 {
		t.Errorf("final results: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM LeaseRecords WHERE RunId=$1 AND State<>'Expired'`, runID); n != 0 {
		t.Errorf("live leases left: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM WorkerRecords WHERE WorkerId='w1' AND State='Available'`); n != 1 {
		t.Error("worker slot not released")
	}
	for _, ev := range []string{"RUN_CREATED", "RUN_QUEUED", "LEASE_OFFERED", "RUN_RUNNING", "RUN_SUCCEEDED"} {
		if n := e.count(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=$1 AND EventType=$2`, runID, ev); n != 1 {
			t.Errorf("journal %s: %d", ev, n)
		}
	}
	if n := e.count(`SELECT COUNT(*) FROM TelemetryEvents WHERE RunId=$1`, runID); n != 1 {
		t.Errorf("telemetry events: %d", n)
	}

	// The same result posted again changes nothing.
	again := e.finish(o, "Succeeded", nil)
	if str(again, "terminalState") != "SUCCEEDED" || again["slotReleased"] != false {
		t.Fatalf("repeat: %v", again)
	}
	if n := e.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId=$1`, runID); n != 1 {
		t.Errorf("repeat wrote a second final result: %d", n)
	}
}

func TestJob_FencesAndValidation(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	e.register("w2")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")

	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/lease/accept", map[string]any{"attemptId": "wrong", "workerId": "w1"})
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/lease/accept", map[string]any{"attemptId": o.attemptID, "workerId": "w2"})
	e.accept("w1", o)
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/result", map[string]any{"attemptId": "wrong", "terminalState": "Succeeded"})
	e.must(http.StatusUnprocessableEntity, "POST", "/jobs/"+runID+"/result", map[string]any{"attemptId": o.attemptID, "terminalState": "Nonsense"})
	e.must(http.StatusUnprocessableEntity, "POST", "/jobs/"+runID+"/result", map[string]any{"attemptId": o.attemptID, "terminalState": "Unknown"})
	e.must(http.StatusUnprocessableEntity, "POST", "/jobs/"+runID+"/result", map[string]any{
		"attemptId": o.attemptID, "terminalState": "Succeeded", "outputs": map[string]string{"k": "password=hunter2"}})
	if got := e.runState(runID); got != "RUNNING" {
		t.Fatalf("rejected results must not close the run: %s", got)
	}

	e.must(http.StatusUnprocessableEntity, "POST", "/jobs", map[string]any{"scriptArtifactId": "nope", "scriptVersion": "1", "schemaVersion": "1"})
	e.must(http.StatusUnprocessableEntity, "POST", "/jobs", map[string]any{"scriptArtifactId": "s1", "scriptVersion": "1.0.0", "schemaVersion": "9"})
	e.must(http.StatusNotFound, "GET", "/jobs/nope", nil)
}

func TestPull_NothingQueued_AndUnknownWorker(t *testing.T) {
	e := newEnv(t)
	e.register("w1")
	if o := e.pull("w1"); o.result != "NoPendingJob" {
		t.Fatalf("got %+v", o)
	}
	e.must(http.StatusBadRequest, "POST", "/jobs/pull", map[string]any{"workerId": "ghost", "runtimeType": "desktop"})
}

// A worker holds one job at a time.
func TestPull_BusyWorkerGetsQueueFull(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	e.create("s1", "1.0.0", nil)
	e.create("s1", "1.0.0", nil)
	if o := e.pull("w1"); o.result != "JobOffered" {
		t.Fatalf("got %+v", o)
	}
	if o := e.pull("w1"); o.result != "QueueFull" {
		t.Fatalf("a worker with a lease must get QueueFull, got %+v", o)
	}
}

// Jobs are offered oldest first.
func TestPull_OldestFirst(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	first := e.create("s1", "1.0.0", nil)
	e.create("s1", "1.0.0", nil)
	if o := e.pull("w1"); o.runID != first {
		t.Fatalf("expected the oldest run %s, got %s", first, o.runID)
	}
}

// An offer nobody accepted runs out, and the run is then offered to another worker. Until it
// runs out, no other worker is offered it.
func TestPull_AnOfferThatRunsOutIsOfferedAgain(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	e.register("w2")
	runID := e.create("s1", "1.0.0", nil)

	first := e.pull("w1")
	if first.result != "JobOffered" || first.runID != runID {
		t.Fatalf("first offer: %+v", first)
	}
	if o := e.pull("w2"); o.result != "NoPendingJob" {
		t.Fatalf("a live offer must not be offered to a second worker: %+v", o)
	}

	// w1 never accepts; its offer runs out.
	if _, err := e.db.Exec(`UPDATE RunRecords SET OfferExpiresAt = now() - interval '1 minute' WHERE Id=$1`, runID); err != nil {
		t.Fatal(err)
	}
	second := e.pull("w2")
	if second.result != "JobOffered" || second.runID != runID || second.attemptID == first.attemptID {
		t.Fatalf("an expired offer must be offered again with a new attempt: %+v", second)
	}
	// The first worker is fenced out by the new attempt.
	e.must(http.StatusConflict, "POST", "/jobs/"+runID+"/lease/accept", map[string]any{"attemptId": first.attemptID, "workerId": "w1"})
	e.accept("w2", second)
}

// A job with a declared runtime is not offered to a worker of another runtime.
func TestPull_RuntimeMustMatch(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0")) // targetRuntime desktop
	e.must(http.StatusOK, "POST", "/workers/register", map[string]any{"workerId": "b1", "runtimeType": "browser"})
	e.create("s1", "1.0.0", nil)
	out := e.must(http.StatusOK, "POST", "/jobs/pull", map[string]any{"workerId": "b1", "runtimeType": "browser"})
	if str(out, "result") != "NoPendingJob" {
		t.Fatalf("a browser worker must not get a desktop job: %v", out)
	}
}

// Two workers pulling at the same moment must never be handed the same run. This is the property
// a database that ran one statement at a time gave for free, and that PostgreSQL has to be told to keep.
func TestPull_ConcurrentWorkersNeverShareARun(t *testing.T) {
	const workers = 8
	for round := 0; round < 15; round++ {
		e := newEnv(t)
		e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
		for i := 0; i < workers; i++ {
			e.register(fmt.Sprintf("w%d", i))
		}
		queued := 3
		for i := 0; i < queued; i++ {
			e.create("s1", "1.0.0", nil)
		}

		var mu sync.Mutex
		offered := map[string]int{}
		none := 0
		parallel(workers, func(i int) {
			o := e.pull(fmt.Sprintf("w%d", i))
			mu.Lock()
			defer mu.Unlock()
			if o.result == "JobOffered" {
				offered[o.runID]++
			} else if o.result == "NoPendingJob" {
				none++
			}
		})
		if len(offered) != queued || none != workers-queued {
			t.Fatalf("round %d: %d runs offered, %d empty (want %d and %d): %v", round, len(offered), none, queued, workers-queued, offered)
		}
		for id, n := range offered {
			if n != 1 {
				t.Fatalf("round %d: run %s was offered to %d workers", round, id, n)
			}
		}
		if n := e.count(`SELECT COUNT(*) FROM LeaseRecords WHERE State='Offered'`); n != queued {
			t.Fatalf("round %d: %d leases for %d runs", round, n, queued)
		}
	}
}

func TestCancel_QueuedRunIsCancelledAtOnce(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	runID := e.create("s1", "1.0.0", nil)
	e.must(http.StatusBadRequest, "POST", "/jobs/"+runID+"/cancel", map[string]any{})
	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/cancel", map[string]any{"requestedBy": "tester"})
	if got := e.runState(runID); got != "CANCELLED" {
		t.Fatalf("got %s", got)
	}
	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/cancel", map[string]any{"requestedBy": "tester"}) // idempotent
	e.register("w1")
	if o := e.pull("w1"); o.result != "NoPendingJob" {
		t.Fatalf("a cancelled run must not be offered: %+v", o)
	}
}

func TestCancel_RunningRunIsToldToStop(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/cancel", map[string]any{"requestedBy": "tester"})
	if hb := e.must(http.StatusOK, "POST", "/jobs/"+runID+"/heartbeat", map[string]any{"attemptId": o.attemptID, "workerId": "w1", "seqNo": 2}); hb["cancellationRequested"] != true {
		t.Fatalf("heartbeat must carry the cancel: %v", hb)
	}
	e.finish(o, "Cancelled", nil)
	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/cancel", map[string]any{"requestedBy": "tester"}) // already cancelled: idempotent

	// A run that finished some other way can no longer be cancelled.
	r2 := e.create("s1", "1.0.0", nil)
	o2 := e.pull("w1")
	e.accept("w1", o2)
	e.finish(o2, "Succeeded", nil)
	e.must(http.StatusConflict, "POST", "/jobs/"+r2+"/cancel", map[string]any{"requestedBy": "tester"})
}

// Several identical results arriving together close the run once.
func TestResult_ConcurrentDuplicatesWriteOnce(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)

	released := 0
	var mu sync.Mutex
	parallel(8, func(int) {
		code, out := e.call("POST", "/jobs/"+runID+"/result", map[string]any{"attemptId": o.attemptID, "terminalState": "Succeeded"})
		if code != http.StatusOK {
			t.Errorf("status %d: %v", code, out)
			return
		}
		if out["slotReleased"] == true {
			mu.Lock()
			released++
			mu.Unlock()
		}
	})
	if released != 1 {
		t.Errorf("the worker slot was released %d times", released)
	}
	if n := e.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId=$1`, runID); n != 1 {
		t.Errorf("final results: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=$1 AND EventType='RUN_SUCCEEDED'`, runID); n != 1 {
		t.Errorf("RUN_SUCCEEDED journal entries: %d", n)
	}
}

func TestResult_UnknownNeedsAttention_AndAdminResolve(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	e.finish(o, "Unknown", nil)

	code, raw := e.raw("GET", "/admin/runs", nil)
	if code != http.StatusOK || !contains(string(raw), runID) {
		t.Fatalf("admin list: %d %s", code, raw)
	}
	e.must(http.StatusBadRequest, "POST", "/admin/runs/"+runID+"/resolve", map[string]any{"resolution": "bogus"})

	// Five people resolving at once: one wins, the rest are told the run is no longer Unknown.
	wins, conflicts := 0, 0
	var mu sync.Mutex
	parallel(5, func(int) {
		code, _ := e.call("POST", "/admin/runs/"+runID+"/resolve", map[string]any{"resolution": "Succeeded", "resolvedBy": "ops"})
		mu.Lock()
		defer mu.Unlock()
		switch code {
		case http.StatusOK:
			wins++
		case http.StatusConflict:
			conflicts++
		default:
			t.Errorf("unexpected status %d", code)
		}
	})
	if wins != 1 || conflicts != 4 {
		t.Fatalf("wins=%d conflicts=%d", wins, conflicts)
	}
	if got := e.runState(runID); got != "SUCCEEDED" {
		t.Fatalf("got %s", got)
	}
	if n := e.count(`SELECT COUNT(*) FROM ResolutionRecords WHERE RunId=$1`, runID); n != 1 {
		t.Errorf("resolution records: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM RunRecords WHERE Id=$1 AND NeedsAttention`, runID); n != 0 {
		t.Error("NeedsAttention must be cleared")
	}
}

func contains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
}

func TestListJobs_FiltersAndLimit(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	a := e.create("s1", "1.0.0", nil)
	e.create("s1", "1.0.0", nil)
	e.create("s1", "1.0.0", nil)
	e.must(http.StatusOK, "POST", "/jobs/"+a+"/cancel", map[string]any{"requestedBy": "t"})

	count := func(q string) int {
		out := e.must(http.StatusOK, "GET", "/jobs"+q, nil)
		return int(out["count"].(float64))
	}
	if n := count(""); n != 3 {
		t.Errorf("all: %d", n)
	}
	if n := count("?state=queued"); n != 2 {
		t.Errorf("queued (case-insensitive): %d", n)
	}
	if n := count("?state=CANCELLED"); n != 1 {
		t.Errorf("cancelled: %d", n)
	}
	if n := count("?limit=2"); n != 2 {
		t.Errorf("limit: %d", n)
	}
	out := e.must(http.StatusOK, "GET", "/jobs?state=Queued", nil)
	for _, j := range out["jobs"].([]any) {
		if j.(map[string]any)["needsAttention"] != false {
			t.Errorf("needsAttention must be a boolean: %v", j)
		}
	}
}

func TestReconcile(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	out := e.must(http.StatusOK, "POST", "/jobs/"+runID+"/reconcile", map[string]any{
		"attemptId":      o.attemptID,
		"journalEntries": []map[string]any{{"eventType": "RUN_SUCCEEDED", "attemptId": o.attemptID}},
	})
	if str(out, "reconciledState") != "SUCCEEDED" {
		t.Fatalf("got %v", out)
	}
	again := e.must(http.StatusOK, "POST", "/jobs/"+runID+"/reconcile", map[string]any{"attemptId": o.attemptID})
	if str(again, "reconciledState") != "SUCCEEDED" {
		t.Fatalf("repeat: %v", again)
	}

	// A journal that does not confirm success leaves the run Unknown and needing attention.
	r2 := e.create("s1", "1.0.0", nil)
	e.register("w2")
	o2 := e.pull("w2")
	e.accept("w2", o2)
	u := e.must(http.StatusOK, "POST", "/jobs/"+r2+"/reconcile", map[string]any{"attemptId": o2.attemptID})
	if str(u, "reconciledState") != "UNKNOWN" {
		t.Fatalf("got %v", u)
	}
	if n := e.count(`SELECT COUNT(*) FROM RunRecords WHERE Id=$1 AND NeedsAttention`, r2); n != 1 {
		t.Error("an unconfirmed run needs attention")
	}
}
