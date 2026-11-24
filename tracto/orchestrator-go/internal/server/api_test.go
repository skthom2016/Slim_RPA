package server

import (
	"encoding/base64"
	"net/http"
	"sync"
	"testing"
)

func TestArtifacts_PublishGetListDelete(t *testing.T) {
	e := newEnv(t)
	script := flatScript("s1", "1.0.0")
	e.publish("s1", "1.0.0", script)

	got := e.must(http.StatusOK, "GET", "/artifacts/s1/versions/1.0.0", nil)
	raw, _ := base64.StdEncoding.DecodeString(str(got, "payload"))
	if string(raw) != script || str(got, "contentHash") == "" {
		t.Fatalf("payload round trip lost data: %v", got)
	}

	// Publishing the same content again is accepted; different content under the same version is not.
	e.publish("s1", "1.0.0", script)
	e.must(http.StatusConflict, "PUT", "/artifacts/s1/versions/1.0.0", map[string]any{
		"schemaVersion": "1", "payload": base64.StdEncoding.EncodeToString([]byte(flatScript("s1", "1.0.0") + " ")),
	})
	e.must(http.StatusBadRequest, "PUT", "/artifacts/s2/versions/1.0.0", map[string]any{"schemaVersion": "1", "payload": "%%%not base64"})
	e.must(http.StatusBadRequest, "PUT", "/artifacts/s2/versions/1.0.0", map[string]any{"payload": "e30="})

	list := e.must(http.StatusOK, "GET", "/artifacts", nil)
	arts := list["artifacts"].([]any)
	if len(arts) != 1 {
		t.Fatalf("list: %v", list)
	}
	a := arts[0].(map[string]any)
	if a["artifactId"] != "s1" || a["name"] != "s1" || a["isDeleted"] != false || a["signatureValid"] != false {
		t.Fatalf("listed artifact: %v", a)
	}

	// A version in use by a queued run cannot be deleted; once the run is gone it can.
	runID := e.create("s1", "1.0.0", nil)
	e.must(http.StatusConflict, "DELETE", "/artifacts/s1/versions/1.0.0", nil)
	e.must(http.StatusOK, "POST", "/jobs/"+runID+"/cancel", map[string]any{"requestedBy": "t"})
	if code, _ := e.call("DELETE", "/artifacts/s1/versions/1.0.0", nil); code != http.StatusNoContent {
		t.Fatalf("delete: %d", code)
	}
	if code, _ := e.call("DELETE", "/artifacts/s1/versions/1.0.0", nil); code != http.StatusNoContent {
		t.Fatalf("repeat delete must be idempotent: %d", code)
	}
	e.must(http.StatusNotFound, "GET", "/artifacts/s1/versions/1.0.0", nil)
	if n := len(e.must(http.StatusOK, "GET", "/artifacts", nil)["artifacts"].([]any)); n != 0 {
		t.Errorf("a deleted artifact must not be listed: %d", n)
	}
	if n := len(e.must(http.StatusOK, "GET", "/artifacts?includeDeleted=true", nil)["artifacts"].([]any)); n != 1 {
		t.Errorf("includeDeleted: %d", n)
	}
	e.must(http.StatusUnprocessableEntity, "POST", "/jobs", map[string]any{"scriptArtifactId": "s1", "scriptVersion": "1.0.0", "schemaVersion": "1"})
	e.must(http.StatusNotFound, "DELETE", "/artifacts/nope/versions/1", nil)
}

// Several requests publishing the same version together leave one artifact and no server error.
func TestArtifacts_ConcurrentPublishOfOneVersion(t *testing.T) {
	e := newEnv(t)
	payload := base64.StdEncoding.EncodeToString([]byte(flatScript("s1", "1.0.0")))
	codes := map[int]int{}
	var mu sync.Mutex
	parallel(8, func(int) {
		code, _ := e.call("PUT", "/artifacts/s1/versions/1.0.0", map[string]any{"schemaVersion": "1", "payload": payload})
		mu.Lock()
		codes[code]++
		mu.Unlock()
	})
	for code := range codes {
		if code != http.StatusCreated && code != http.StatusConflict {
			t.Errorf("unexpected status %d: %v", code, codes)
		}
	}
	if codes[http.StatusCreated] == 0 {
		t.Errorf("nobody published: %v", codes)
	}
	if n := e.count(`SELECT COUNT(*) FROM ArtifactRecords WHERE ArtifactId='s1'`); n != 1 {
		t.Errorf("artifact rows: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM ArtifactContents WHERE ArtifactId='s1'`); n != 1 {
		t.Errorf("content rows: %d", n)
	}
}

func TestArtifacts_SegmentedScriptIsStoredPerSegment(t *testing.T) {
	e := newEnv(t)
	e.publish("seg1", "1.0.0", segScript("seg1", "1.0.0"))
	if n := e.count(`SELECT COUNT(*) FROM ArtifactRecords WHERE ParentArtifactId='seg1'`); n != 2 {
		t.Fatalf("segment artifacts: %d", n)
	}
	// Segments are hidden from the list unless asked for, and cannot be deleted on their own.
	if n := len(e.must(http.StatusOK, "GET", "/artifacts", nil)["artifacts"].([]any)); n != 1 {
		t.Errorf("list: %d", n)
	}
	if n := len(e.must(http.StatusOK, "GET", "/artifacts?includeSegments=true", nil)["artifacts"].([]any)); n != 3 {
		t.Errorf("list with segments: %d", n)
	}
	e.must(http.StatusConflict, "DELETE", "/artifacts/seg1.seg1/versions/1.0.0", nil)
	// The script's own id and version must match the artifact's.
	e.must(http.StatusUnprocessableEntity, "PUT", "/artifacts/other/versions/1.0.0", map[string]any{
		"schemaVersion": "1", "payload": base64.StdEncoding.EncodeToString([]byte(segScript("seg1", "1.0.0")))})
	// Same content again is idempotent; deleting the script removes its segments with it.
	e.publish("seg1", "1.0.0", segScript("seg1", "1.0.0"))
	if code, _ := e.call("DELETE", "/artifacts/seg1/versions/1.0.0", nil); code != http.StatusNoContent {
		t.Fatalf("delete: %d", code)
	}
	if n := e.count(`SELECT COUNT(*) FROM ArtifactRecords WHERE ParentArtifactId='seg1' AND NOT IsDeleted`); n != 0 {
		t.Errorf("segments left: %d", n)
	}
}

// A script of two segments: the second sees what the first produced, and the parent closes when
// the last one ends.
func TestSegmentedJob_RunsSegmentsInOrderAndPassesOutputsOn(t *testing.T) {
	e := newEnv(t)
	e.publish("seg1", "1.0.0", segScript("seg1", "1.0.0"))
	e.register("w1")
	parent := e.create("seg1", "1.0.0", map[string]string{"extra": "e"})
	if got := e.runState(parent); got != "RUNNING" {
		t.Fatalf("parent: %s", got)
	}

	first := e.pull("w1")
	if first.result != "JobOffered" || first.runID == parent || first.params["base"] != "b" || first.params["extra"] != "e" {
		t.Fatalf("first segment: %+v", first)
	}
	e.accept("w1", first)
	e.finish(first, "Succeeded", map[string]string{"made": "by-first"})

	second := e.pull("w1")
	if second.result != "JobOffered" || second.params["made"] != "by-first" {
		t.Fatalf("second segment must receive the first's outputs: %+v", second)
	}
	e.accept("w1", second)
	e.finish(second, "Succeeded", map[string]string{"last": "yes"})

	job := e.must(http.StatusOK, "GET", "/jobs/"+parent, nil)
	if str(job, "state") != "SUCCEEDED" {
		t.Fatalf("parent: %v", job)
	}
	outs := job["outputs"].(map[string]any)
	if outs["made"] != "by-first" || outs["last"] != "yes" {
		t.Fatalf("parent outputs: %v", outs)
	}
	segs := job["segments"].([]any)
	if len(segs) != 2 || segs[0].(map[string]any)["state"] != "SUCCEEDED" || segs[1].(map[string]any)["state"] != "SUCCEEDED" {
		t.Fatalf("segments: %v", segs)
	}
	if n := e.count(`SELECT COUNT(*) FROM FinalResultRecords WHERE RunId=$1`, parent); n != 1 {
		t.Errorf("parent final results: %d", n)
	}
}

func TestSegmentedJob_FailureInASegmentFailsTheParent(t *testing.T) {
	e := newEnv(t)
	e.publish("seg1", "1.0.0", segScript("seg1", "1.0.0"))
	e.register("w1")
	parent := e.create("seg1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	e.finish(o, "Failed", nil)
	if got := e.runState(parent); got != "FAILED" {
		t.Fatalf("parent: %s", got)
	}
	if next := e.pull("w1"); next.result != "NoPendingJob" {
		t.Fatalf("no second segment after a failure: %+v", next)
	}
}

func TestSegmentedJob_CancelWhileTheFirstSegmentIsQueued(t *testing.T) {
	e := newEnv(t)
	e.publish("seg1", "1.0.0", segScript("seg1", "1.0.0"))
	e.register("w1")
	parent := e.create("seg1", "1.0.0", nil)
	e.must(http.StatusOK, "POST", "/jobs/"+parent+"/cancel", map[string]any{"requestedBy": "t"})
	if got := e.runState(parent); got != "CANCELLED" {
		t.Fatalf("parent: %s", got)
	}
	if o := e.pull("w1"); o.result != "NoPendingJob" {
		t.Fatalf("the cancelled segment must not be offered: %+v", o)
	}
}

func TestWorkers_RegisterListOffline(t *testing.T) {
	e := newEnv(t)
	e.must(http.StatusBadRequest, "POST", "/workers/register", map[string]any{"runtimeType": "desktop"})
	e.register("w1")
	e.register("w1") // registering again is allowed

	code, raw := e.raw("GET", "/workers", nil)
	if code != http.StatusOK || !contains(string(raw), `"workerId":"w1"`) || !contains(string(raw), `"isOnline":true`) {
		t.Fatalf("list: %d %s", code, raw)
	}
	e.must(http.StatusOK, "POST", "/workers/w1/offline", nil)
	_, raw = e.raw("GET", "/workers", nil)
	if !contains(string(raw), `"isOnline":false`) || !contains(string(raw), `"state":"OFFLINE"`) {
		t.Fatalf("after offline: %s", raw)
	}
	e.must(http.StatusNotFound, "POST", "/workers/ghost/offline", nil)
	e.register("w1") // comes back
	if n := e.count(`SELECT COUNT(*) FROM WorkerRecords WHERE WorkerId='w1' AND IsOnline AND State='Available'`); n != 1 {
		t.Error("a re-registered worker must be online and available again")
	}
}

func TestCallbacks_DeduplicateByEventId(t *testing.T) {
	e := newEnv(t)
	body := map[string]any{"eventId": "ev-1", "correlationId": "c-1", "keyId": "k1", "payload": map[string]any{"a": 1}}
	if out := e.must(http.StatusAccepted, "POST", "/callbacks", body); str(out, "status") != "accepted" {
		t.Fatalf("got %v", out)
	}
	if out := e.must(http.StatusOK, "POST", "/callbacks", body); str(out, "status") != "duplicate" {
		t.Fatalf("got %v", out)
	}
	e.must(http.StatusBadRequest, "POST", "/callbacks", map[string]any{"eventId": "x"})
	// A callback with no payload at all is still stored.
	e.must(http.StatusAccepted, "POST", "/callbacks", map[string]any{"eventId": "ev-2", "correlationId": "c-2", "keyId": "k1"})

	// The same event arriving many times at once is stored once and never fails.
	parallel(8, func(int) {
		code, out := e.call("POST", "/callbacks", map[string]any{"eventId": "ev-3", "correlationId": "c-3", "keyId": "k1", "payload": map[string]any{}})
		if code != http.StatusAccepted && code != http.StatusOK {
			t.Errorf("status %d: %v", code, out)
		}
	})
	if n := e.count(`SELECT COUNT(*) FROM CallbackRecords WHERE EventId='ev-3'`); n != 1 {
		t.Errorf("stored %d times", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM CallbackRecords WHERE NOT Processed`); n != 3 {
		t.Errorf("unprocessed callbacks: %d", n)
	}
}

func TestLogs_StoreWarningsAndErrorsOnly(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	runID := e.create("s1", "1.0.0", nil)

	out := e.must(http.StatusAccepted, "POST", "/jobs/"+runID+"/logs", map[string]any{
		"workerId": "w1", "runtime": "desktop",
		"entries": []map[string]any{
			{"level": "Error", "message": "boom", "occurredAt": "not a time"}, // bad time: stored with the receive time
			{"level": "Warning", "message": "careful password=hunter2", "occurredAt": "2026-11-20T10:00:00Z"},
			{"level": "Info", "message": "chatter"},
		},
	})
	if out["received"] != float64(3) || out["stored"] != float64(2) {
		t.Fatalf("got %v", out)
	}
	e.must(http.StatusNotFound, "POST", "/jobs/nope/logs", map[string]any{"entries": []any{}})

	all := e.must(http.StatusOK, "GET", "/admin/logs/"+runID, nil)
	if all["count"] != float64(2) {
		t.Fatalf("run logs: %v", all)
	}
	_, raw := e.raw("GET", "/admin/logs/"+runID, nil)
	if contains(string(raw), "hunter2") {
		t.Fatalf("a credential reached the log: %s", raw)
	}
	if got := e.must(http.StatusOK, "GET", "/admin/logs/"+runID+"?level=Error", nil); got["count"] != float64(1) {
		t.Errorf("level filter: %v", got)
	}
	if got := e.must(http.StatusOK, "GET", "/admin/logs?level=Warning&limit=5", nil); got["count"] != float64(1) {
		t.Errorf("recent warnings: %v", got)
	}
	if got := e.must(http.StatusOK, "GET", "/admin/logs", nil); got["count"] != float64(2) {
		t.Errorf("recent logs: %v", got)
	}
}

// A finished run can start another: the continuation fires once.
func TestContinuation_FiresOnceWhenARunEnds(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.publish("next", "1.0.0", flatScript("next", "1.0.0"))
	e.register("w1")
	e.must(http.StatusBadRequest, "POST", "/admin/continuations", map[string]any{"triggerState": "bogus", "targetArtifactId": "next", "targetArtifactVersion": "1.0.0"})
	e.must(http.StatusCreated, "POST", "/admin/continuations", map[string]any{"triggerState": "Succeeded", "targetArtifactId": "next", "targetArtifactVersion": "1.0.0"})

	code, raw := e.raw("GET", "/admin/continuations", nil)
	if code != http.StatusOK || !contains(string(raw), `"targetArtifactId":"next"`) {
		t.Fatalf("list: %d %s", code, raw)
	}

	e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	e.finish(o, "Succeeded", nil)

	eventually(t, "the continuation run", func() bool {
		return e.count(`SELECT COUNT(*) FROM RunRecords WHERE TriggerType='Continuation' AND ScriptArtifactId='next' AND State='Queued'`) == 1
	})
	if n := e.count(`SELECT COUNT(*) FROM ContinuationRunRecords`); n != 1 {
		t.Errorf("continuation links: %d", n)
	}
}

func TestSchedules_CreateRunAndCompleted(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	out := e.must(http.StatusCreated, "POST", "/admin/schedules", map[string]any{
		"cronExpression": "0 9 * * *", "timeZone": "Australia/Sydney", "artifactId": "s1", "version": "1.0.0",
	})
	id := str(out, "scheduleId")
	if out["enabled"] != true || str(out, "nextRunAt") == "" {
		t.Fatalf("created: %v", out)
	}
	e.must(http.StatusCreated, "POST", "/admin/schedules", map[string]any{"cronExpression": "0 9 * * *", "timeZone": "UTC", "artifactId": "s1", "version": "1.0.0", "enabled": false})
	if code, raw := e.raw("GET", "/admin/schedules?view=upcoming", nil); code != http.StatusOK || !contains(string(raw), id) {
		t.Fatalf("list: %d %s", code, raw)
	}
	e.must(http.StatusOK, "PATCH", "/admin/schedules/"+id, map[string]any{"enabled": false})
	if n := e.count(`SELECT COUNT(*) FROM ScheduleRecords WHERE ScheduleId=$1 AND NOT Enabled AND NextRunAt IS NULL`, id); n != 1 {
		t.Error("a disabled schedule has no next run")
	}
	e.must(http.StatusOK, "PATCH", "/admin/schedules/"+id, map[string]any{"enabled": true})
	if n := e.count(`SELECT COUNT(*) FROM ScheduleRecords WHERE ScheduleId=$1 AND Enabled AND NextRunAt IS NOT NULL`, id); n != 1 {
		t.Error("an enabled schedule has a next run")
	}
	e.must(http.StatusOK, "GET", "/admin/schedules/completed", nil)
	if code, _ := e.call("DELETE", "/admin/schedules/"+id, nil); code != http.StatusNoContent {
		t.Fatalf("delete: %d", code)
	}
	e.must(http.StatusNotFound, "DELETE", "/admin/schedules/"+id, nil)
}

func TestAdmin_SecurityEventsAndTelemetry(t *testing.T) {
	e := newEnv(t)
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	runID := e.create("s1", "1.0.0", nil)
	o := e.pull("w1")
	e.accept("w1", o)
	e.finish(o, "Succeeded", nil)

	if code, _ := e.raw("GET", "/admin/security-events", nil); code != http.StatusOK {
		t.Fatalf("security events: %d", code)
	}
	if code, raw := e.raw("GET", "/admin/telemetry/"+runID, nil); code != http.StatusOK || !contains(string(raw), "RUN_SUCCEEDED") {
		t.Fatalf("telemetry: %d %s", code, raw)
	}
	if code, _ := e.raw("GET", "/admin/telemetry?eventType=RUN_SUCCEEDED", nil); code != http.StatusOK {
		t.Fatalf("telemetry list: %d", code)
	}
}
