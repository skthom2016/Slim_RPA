package handlers

import (
	"bytes"
	"database/sql"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/go-chi/chi/v5"

	"tracto/orchestrator/internal/db/dbtest"
)

func testRouter(t *testing.T) (http.Handler, *sql.DB) {
	t.Helper()
	d := dbtest.Open(t)
	if _, err := d.Exec(`INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt) VALUES('report','1.0.0','1','h','2026-01-01T00:00:00Z')`); err != nil {
		t.Fatal(err)
	}
	payload := `{"schemaVersion":"1","script":{"id":"report","version":"1.0.0","targetRuntime":"desktop","steps":[]}}`
	if _, err := d.Exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES('report','1.0.0',$1)`, []byte(payload)); err != nil {
		t.Fatal(err)
	}
	r := chi.NewRouter()
	sh := NewScheduleHandler(d)
	r.Post("/admin/schedules", sh.Create)
	r.Get("/admin/schedules", sh.List)
	r.Get("/admin/schedules/completed", sh.Completed)
	r.Patch("/admin/schedules/{scheduleId}", sh.Update)
	r.Delete("/admin/schedules/{scheduleId}", sh.Delete)
	jh := NewJobHandler(d)
	r.Post("/jobs", jh.CreateJob)
	return r, d
}

func call(t *testing.T, h http.Handler, method, path string, body any) (int, map[string]any) {
	t.Helper()
	var buf bytes.Buffer
	if body != nil {
		_ = json.NewEncoder(&buf).Encode(body)
	}
	req := httptest.NewRequest(method, path, &buf)
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	var out map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &out)
	return rec.Code, out
}

func valid() map[string]any {
	return map[string]any{
		"cronExpression": "0 8 * * *", "timeZone": "Asia/Kolkata",
		"artifactId": "report", "version": "1.0.0",
	}
}

func TestCreateSchedule_RefusesWhatCannotFire(t *testing.T) {
	h, _ := testRouter(t)
	for name, mutate := range map[string]func(map[string]any){
		"no time zone":         func(m map[string]any) { delete(m, "timeZone") },
		"Local is not a zone":  func(m map[string]any) { m["timeZone"] = "Local" },
		"unknown zone":         func(m map[string]any) { m["timeZone"] = "Mars/Olympus" },
		"zone inside the cron": func(m map[string]any) { m["cronExpression"] = "CRON_TZ=UTC 0 8 * * *" },
		"bad cron":             func(m map[string]any) { m["cronExpression"] = "every morning" },
		"bad overlap":          func(m map[string]any) { m["overlapPolicy"] = "sometimes" },
		"grace too small":      func(m map[string]any) { m["graceSeconds"] = 5 },
	} {
		body := valid()
		mutate(body)
		if code, _ := call(t, h, "POST", "/admin/schedules", body); code != http.StatusBadRequest {
			t.Errorf("%s: got %d, want 400", name, code)
		}
	}
	body := valid()
	body["version"] = "9.9.9"
	if code, _ := call(t, h, "POST", "/admin/schedules", body); code != http.StatusUnprocessableEntity {
		t.Errorf("unpublished version: got %d, want 422", code)
	}
}

func TestCreateSchedule_ReportsTheNextRunInTheScheduleZone(t *testing.T) {
	h, _ := testRouter(t)
	body := valid()
	body["params"] = map[string]string{"month": "November"}
	code, got := call(t, h, "POST", "/admin/schedules", body)
	if code != http.StatusCreated {
		t.Fatalf("got %d: %v", code, got)
	}
	if got["overlapPolicy"] != "skip" || got["graceSeconds"] != float64(900) || got["enabled"] != true {
		t.Errorf("defaults wrong: %v", got)
	}
	if got["nextRunAt"] == nil || got["nextRunLocal"] == nil {
		t.Fatalf("next run missing: %v", got)
	}
	if s, _ := got["nextRunLocal"].(string); len(s) < 16 || s[11:16] != "08:00" {
		t.Errorf("nextRunLocal = %v, want 08:00 local", got["nextRunLocal"])
	}
	if p, _ := got["params"].(map[string]any); p["month"] != "November" {
		t.Errorf("params = %v", got["params"])
	}
}

func TestUpdateSchedule_EnablingWorksOutTheNextRunFromNowAndDisablingClearsIt(t *testing.T) {
	h, d := testRouter(t)
	body := valid()
	body["enabled"] = false
	_, created := call(t, h, "POST", "/admin/schedules", body)
	id := created["scheduleId"].(string)
	if created["nextRunAt"] != nil {
		t.Fatalf("a disabled schedule has no next run, got %v", created["nextRunAt"])
	}

	code, got := call(t, h, "PATCH", "/admin/schedules/"+id, map[string]any{"enabled": true})
	if code != http.StatusOK || got["nextRunAt"] == nil {
		t.Fatalf("enable: %d %v", code, got)
	}
	code, got = call(t, h, "PATCH", "/admin/schedules/"+id, map[string]any{"enabled": false})
	if code != http.StatusOK || got["nextRunAt"] != nil || got["enabled"] != false {
		t.Fatalf("disable: %d %v", code, got)
	}

	// A schedule whose expression is broken in the database can still be switched off.
	if _, err := d.Exec(`UPDATE ScheduleRecords SET CronExpression='broken', Enabled=TRUE WHERE ScheduleId=$1`, id); err != nil {
		t.Fatal(err)
	}
	if code, got = call(t, h, "PATCH", "/admin/schedules/"+id, map[string]any{"enabled": false}); code != http.StatusOK {
		t.Fatalf("disabling a broken schedule: %d %v", code, got)
	}
	if code, _ = call(t, h, "PATCH", "/admin/schedules/nope", map[string]any{"enabled": true}); code != http.StatusNotFound {
		t.Errorf("unknown id: got %d, want 404", code)
	}
}

func TestUpdateSchedule_ChangingTheExpressionRestartsFromNow(t *testing.T) {
	h, _ := testRouter(t)
	_, created := call(t, h, "POST", "/admin/schedules", valid())
	id := created["scheduleId"].(string)
	code, got := call(t, h, "PATCH", "/admin/schedules/"+id, map[string]any{"cronExpression": "30 17 * * *"})
	if code != http.StatusOK {
		t.Fatalf("got %d: %v", code, got)
	}
	if s, _ := got["nextRunLocal"].(string); len(s) < 16 || s[11:16] != "17:30" {
		t.Errorf("nextRunLocal = %v, want 17:30 local", got["nextRunLocal"])
	}
	if code, _ = call(t, h, "PATCH", "/admin/schedules/"+id, map[string]any{"timeZone": "Nowhere/Land"}); code != http.StatusBadRequest {
		t.Errorf("bad zone: got %d, want 400", code)
	}
}

func TestListAndDeleteSchedules(t *testing.T) {
	h, _ := testRouter(t)
	_, created := call(t, h, "POST", "/admin/schedules", valid())
	req := httptest.NewRequest("GET", "/admin/schedules", nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	var list []map[string]any
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil || len(list) != 1 {
		t.Fatalf("list = %s (%v)", rec.Body.String(), err)
	}
	if code, _ := call(t, h, "DELETE", "/admin/schedules/"+created["scheduleId"].(string), nil); code != http.StatusNoContent {
		t.Errorf("delete: got %d", code)
	}
}

// POST /jobs shares its queueing code with the scheduler; it must behave as before.
func TestCreateJob_StillQueuesAScript(t *testing.T) {
	h, d := testRouter(t)
	code, got := call(t, h, "POST", "/jobs", map[string]any{
		"schemaVersion": "1", "scriptArtifactId": "report", "scriptVersion": "1.0.0",
		"triggerType": "Manual", "params": map[string]string{"a": "b"},
	})
	if code != http.StatusOK || got["state"] != "QUEUED" || got["runId"] == nil {
		t.Fatalf("got %d %v", code, got)
	}
	var state, trigger string
	if err := d.QueryRow(`SELECT State,TriggerType FROM RunRecords WHERE Id=$1`, got["runId"]).Scan(&state, &trigger); err != nil {
		t.Fatal(err)
	}
	if state != "Queued" || trigger != "Manual" {
		t.Errorf("run = %s/%s", state, trigger)
	}
	if code, _ := call(t, h, "POST", "/jobs", map[string]any{
		"schemaVersion": "1", "scriptArtifactId": "report", "scriptVersion": "9.9.9"}); code != http.StatusUnprocessableEntity {
		t.Errorf("unknown version: got %d, want 422", code)
	}
}

// ---------------------------------------------------------------------------------------------
// Upcoming and completed views

func rawGet(t *testing.T, h http.Handler, path string) (int, []byte) {
	t.Helper()
	req := httptest.NewRequest(http.MethodGet, path, nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	return rec.Code, rec.Body.Bytes()
}

func addSchedule(t *testing.T, h http.Handler, cron string) string {
	t.Helper()
	body := valid()
	body["cronExpression"] = cron
	code, out := call(t, h, http.MethodPost, "/admin/schedules", body)
	if code != http.StatusCreated {
		t.Fatalf("create %q: %d %v", cron, code, out)
	}
	return out["scheduleId"].(string)
}

func setNext(t *testing.T, d *sql.DB, id, next string) {
	t.Helper()
	if _, err := d.Exec(`UPDATE ScheduleRecords SET NextRunAt=$1 WHERE ScheduleId=$2`, next, id); err != nil {
		t.Fatal(err)
	}
}

func upcoming(t *testing.T, h http.Handler) []map[string]any {
	t.Helper()
	code, body := rawGet(t, h, "/admin/schedules?view=upcoming")
	if code != http.StatusOK {
		t.Fatalf("upcoming: %d %s", code, body)
	}
	var items []map[string]any
	if err := json.Unmarshal(body, &items); err != nil {
		t.Fatal(err)
	}
	return items
}

func insertScheduledRun(t *testing.T, d *sql.DB, id, state, eventID, parent, trigger, updated string) {
	t.Helper()
	if _, err := d.Exec(`INSERT INTO RunRecords(Id,AttemptId,CreatedAt,UpdatedAt,State,TriggerType,ScriptArtifactId,ScriptVersion,EventId,ParentRunId)
		VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)`,
		id, "attempt-"+id, "2026-12-01T00:00:00Z", updated, state, trigger, "report", "1.0.0", eventID, parent); err != nil {
		t.Fatal(err)
	}
}

func TestListUpcoming_SoonestFirstAndASwitchedOffScheduleLast(t *testing.T) {
	h, d := testRouter(t)
	// Created in this order, but they fire in a different one.
	a := addSchedule(t, h, "0 10 * * *")
	b := addSchedule(t, h, "0 8 * * *")
	c := addSchedule(t, h, "0 9 * * *")
	off := addSchedule(t, h, "0 7 * * *")
	setNext(t, d, a, "2026-12-01T10:00:00Z")
	setNext(t, d, b, "2026-12-01T08:00:00Z")
	setNext(t, d, c, "2026-12-02T09:00:00Z")
	if code, out := call(t, h, http.MethodPatch, "/admin/schedules/"+off, map[string]any{"enabled": false}); code != http.StatusOK {
		t.Fatalf("disable: %d %v", code, out)
	}

	got := []string{}
	for _, it := range upcoming(t, h) {
		got = append(got, it["scheduleId"].(string))
	}
	want := []string{b, a, c, off}
	for i := range want {
		if i >= len(got) || got[i] != want[i] {
			t.Fatalf("upcoming order = %v, want %v", got, want)
		}
	}

	// Without a view the list keeps the order the schedules were created in.
	code, body := rawGet(t, h, "/admin/schedules")
	var plain []map[string]any
	_ = json.Unmarshal(body, &plain)
	if code != http.StatusOK || plain[0]["scheduleId"] != a {
		t.Fatalf("plain list should stay in creation order, got %v", plain)
	}
}

func TestListUpcoming_ShowsARunThatHasNotFinished(t *testing.T) {
	h, d := testRouter(t)
	id := addSchedule(t, h, "*/5 * * * *")
	insertScheduledRun(t, d, "run-1", "Queued", id+":2026-12-01T10:00:00Z", "", "Schedule", "2026-12-01T10:00:00Z")
	if _, err := d.Exec(`UPDATE ScheduleRecords SET LastRunId='run-1',LastStatus='Queued' WHERE ScheduleId=$1`, id); err != nil {
		t.Fatal(err)
	}
	if got := upcoming(t, h)[0]["activeRunState"]; got != "QUEUED" {
		t.Fatalf("a queued run should show as QUEUED, got %v", got)
	}

	if _, err := d.Exec(`UPDATE RunRecords SET State='Succeeded' WHERE Id='run-1'`); err != nil {
		t.Fatal(err)
	}
	if got := upcoming(t, h)[0]["activeRunState"]; got != nil {
		t.Fatalf("a finished run should not show as active, got %v", got)
	}
}

func TestListSchedules_RefusesAnUnknownView(t *testing.T) {
	h, _ := testRouter(t)
	code, _ := rawGet(t, h, "/admin/schedules?view=completed")
	if code != http.StatusBadRequest {
		t.Fatalf("view=completed on the schedule list should be refused, got %d", code)
	}
}

func TestCompleted_ListsFinishedScheduledRunsNewestFirst(t *testing.T) {
	h, d := testRouter(t)
	x := addSchedule(t, h, "*/5 * * * *")
	y := addSchedule(t, h, "0 3 * * *")
	if code, _ := call(t, h, http.MethodDelete, "/admin/schedules/"+y, nil); code != http.StatusNoContent {
		t.Fatalf("delete: %d", code)
	}

	insertScheduledRun(t, d, "r1", "Succeeded", x+":2026-12-01T10:00:00Z", "", "Schedule", "2026-12-01T10:05:00Z")
	insertScheduledRun(t, d, "r2", "Failed", x+":2026-12-01T10:05:00Z", "", "Schedule", "2026-12-01T10:10:00Z")
	insertScheduledRun(t, d, "r3", "Queued", x+":2026-12-01T10:10:00Z", "", "Schedule", "2026-12-01T10:10:00Z")
	insertScheduledRun(t, d, "r4", "Succeeded", "", "", "Manual", "2026-12-01T10:20:00Z")
	insertScheduledRun(t, d, "r5", "Succeeded", x+":2026-12-01T10:00:00Z", "r1", "Schedule", "2026-12-01T10:06:00Z")
	insertScheduledRun(t, d, "r6", "Succeeded", y+":2026-12-01T03:00:00Z", "", "Schedule", "2026-12-01T09:00:00Z")

	code, body := rawGet(t, h, "/admin/schedules/completed")
	if code != http.StatusOK {
		t.Fatalf("completed: %d %s", code, body)
	}
	var out struct {
		Total int              `json:"total"`
		Items []map[string]any `json:"items"`
	}
	if err := json.Unmarshal(body, &out); err != nil {
		t.Fatal(err)
	}
	// Queued, manual and segment runs are left out; the rest are newest first.
	if out.Total != 3 || len(out.Items) != 3 {
		t.Fatalf("want 3 completed runs, got total=%d items=%d: %s", out.Total, len(out.Items), body)
	}
	ids := []any{out.Items[0]["runId"], out.Items[1]["runId"], out.Items[2]["runId"]}
	if ids[0] != "r2" || ids[1] != "r1" || ids[2] != "r6" {
		t.Fatalf("order = %v, want r2 r1 r6", ids)
	}
	first := out.Items[0]
	if first["state"] != "FAILED" || first["scheduleId"] != x || first["scheduleExists"] != true ||
		first["cronExpression"] != "*/5 * * * *" || first["scheduledFor"] != "2026-12-01T10:05:00Z" {
		t.Fatalf("first item wrong: %v", first)
	}
	gone := out.Items[2]
	if gone["scheduleExists"] != false || gone["cronExpression"] != nil || gone["scheduleId"] != y {
		t.Fatalf("a run of a deleted schedule should still be listed, without its cron: %v", gone)
	}

	// Filter by schedule, and page.
	_, body = rawGet(t, h, "/admin/schedules/completed?scheduleId="+x)
	_ = json.Unmarshal(body, &out)
	if out.Total != 2 {
		t.Fatalf("filter by schedule: want 2, got %d", out.Total)
	}
	_, body = rawGet(t, h, "/admin/schedules/completed?limit=1&offset=1")
	_ = json.Unmarshal(body, &out)
	if out.Total != 3 || len(out.Items) != 1 || out.Items[0]["runId"] != "r1" {
		t.Fatalf("paging wrong: %s", body)
	}

	for _, bad := range []string{"limit=0", "limit=500", "limit=abc", "offset=-1"} {
		if code, _ := rawGet(t, h, "/admin/schedules/completed?"+bad); code != http.StatusBadRequest {
			t.Fatalf("%s should be refused, got %d", bad, code)
		}
	}
}
