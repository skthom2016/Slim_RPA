package handlers

import (
	"bytes"
	"database/sql"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"

	"github.com/go-chi/chi/v5"

	"tracto/orchestrator/internal/db"
)

func testRouter(t *testing.T) (http.Handler, *sql.DB) {
	t.Helper()
	d, err := db.Open(filepath.Join(t.TempDir(), "orch.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { d.Close() })
	if _, err := d.Exec(`INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt) VALUES('report','1.0.0','1','h','2026-01-01T00:00:00Z')`); err != nil {
		t.Fatal(err)
	}
	payload := `{"schemaVersion":"1","script":{"id":"report","version":"1.0.0","targetRuntime":"desktop","steps":[]}}`
	if _, err := d.Exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES('report','1.0.0',?)`, []byte(payload)); err != nil {
		t.Fatal(err)
	}
	r := chi.NewRouter()
	sh := NewScheduleHandler(d)
	r.Post("/admin/schedules", sh.Create)
	r.Get("/admin/schedules", sh.List)
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
	if _, err := d.Exec(`UPDATE ScheduleRecords SET CronExpression='broken', Enabled=1 WHERE ScheduleId=?`, id); err != nil {
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
	if err := d.QueryRow(`SELECT State,TriggerType FROM RunRecords WHERE Id=?`, got["runId"]).Scan(&state, &trigger); err != nil {
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
