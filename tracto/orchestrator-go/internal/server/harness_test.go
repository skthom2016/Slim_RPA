package server

import (
	"bytes"
	"database/sql"
	"encoding/base64"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"sync"
	"testing"
	"time"

	"tracto/orchestrator/internal/db/dbtest"
)

// env is the real router over an empty PostgreSQL schema.
type env struct {
	t  *testing.T
	h  http.Handler
	db *sql.DB
}

func newEnv(t *testing.T) *env {
	t.Helper()
	d := dbtest.Open(t)
	return &env{t: t, h: NewRouter(d, slog.New(slog.NewTextHandler(io.Discard, nil)), Config{}), db: d}
}

// call sends one request and returns the status and the JSON object of the reply.
func (e *env) call(method, path string, body any) (int, map[string]any) {
	e.t.Helper()
	code, raw := e.raw(method, path, body)
	var out map[string]any
	_ = json.Unmarshal(raw, &out)
	return code, out
}

func (e *env) raw(method, path string, body any) (int, []byte) {
	e.t.Helper()
	var rd io.Reader
	if body != nil {
		b, err := json.Marshal(body)
		if err != nil {
			e.t.Fatal(err)
		}
		rd = bytes.NewReader(b)
	}
	req := httptest.NewRequest(method, path, rd)
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	rec := httptest.NewRecorder()
	e.h.ServeHTTP(rec, req)
	return rec.Code, rec.Body.Bytes()
}

// must is call that fails the test unless the status is want.
func (e *env) must(want int, method, path string, body any) map[string]any {
	e.t.Helper()
	code, out := e.call(method, path, body)
	if code != want {
		e.t.Fatalf("%s %s: status %d, want %d: %v", method, path, code, want, out)
	}
	return out
}

func str(m map[string]any, k string) string {
	s, _ := m[k].(string)
	return s
}

// flatScript is a one-step desktop script.
func flatScript(id, version string) string {
	return `{"id":"` + id + `","name":"` + id + `","version":"` + version + `","targetRuntime":"desktop","steps":[{"stepNumber":1,"action":"delay","parameters":{"ms":"1"}}]}`
}

// segScript is a script of two desktop segments.
func segScript(id, version string) string {
	step := `[{"stepNumber":1,"action":"delay","parameters":{"ms":"1"}}]`
	return `{"id":"` + id + `","name":"` + id + `","version":"` + version + `","params":{"base":"b"},"segments":[` +
		`{"name":"first","runtime":"desktop","steps":` + step + `},` +
		`{"name":"second","runtime":"desktop","steps":` + step + `}]}`
}

func (e *env) publish(id, version, script string) {
	e.t.Helper()
	e.must(http.StatusCreated, "PUT", "/artifacts/"+id+"/versions/"+version, map[string]any{
		"schemaVersion": "1", "payload": base64.StdEncoding.EncodeToString([]byte(script)),
	})
}

func (e *env) register(workerID string) {
	e.t.Helper()
	e.must(http.StatusOK, "POST", "/workers/register", map[string]any{"workerId": workerID, "runtimeType": "desktop"})
}

// create queues a job and returns its run id.
func (e *env) create(artifact, version string, params map[string]string) string {
	e.t.Helper()
	out := e.must(http.StatusOK, "POST", "/jobs", map[string]any{
		"scriptArtifactId": artifact, "scriptVersion": version, "schemaVersion": "1",
		"triggerType": "Manual", "params": params,
	})
	return str(out, "runId")
}

// offer is a job a worker was handed.
type offer struct {
	result, runID, attemptID, leaseID string
	params                            map[string]any
}

func (e *env) pull(workerID string) offer {
	e.t.Helper()
	out := e.must(http.StatusOK, "POST", "/jobs/pull", map[string]any{"workerId": workerID, "runtimeType": "desktop"})
	p, _ := out["params"].(map[string]any)
	return offer{str(out, "result"), str(out, "runId"), str(out, "attemptId"), str(out, "leaseId"), p}
}

func (e *env) accept(workerID string, o offer) {
	e.t.Helper()
	e.must(http.StatusOK, "POST", "/jobs/"+o.runID+"/lease/accept", map[string]any{"attemptId": o.attemptID, "workerId": workerID})
}

func (e *env) finish(o offer, state string, outputs map[string]string) map[string]any {
	e.t.Helper()
	body := map[string]any{"attemptId": o.attemptID, "terminalState": state, "outputs": outputs}
	if state == "Unknown" {
		body["unknownReason"] = "test"
	}
	return e.must(http.StatusOK, "POST", "/jobs/"+o.runID+"/result", body)
}

// runState is the state GET /jobs/{id} reports.
func (e *env) runState(runID string) string {
	e.t.Helper()
	return str(e.must(http.StatusOK, "GET", "/jobs/"+runID, nil), "state")
}

// count runs a SELECT COUNT(*) style query.
func (e *env) count(query string, args ...any) int {
	e.t.Helper()
	var n int
	if err := e.db.QueryRow(query, args...).Scan(&n); err != nil {
		e.t.Fatal(err)
	}
	return n
}

// parallel runs fn n times at once and waits.
func parallel(n int, fn func(i int)) {
	var wg sync.WaitGroup
	start := make(chan struct{})
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			<-start
			fn(i)
		}(i)
	}
	close(start)
	wg.Wait()
}

// eventually polls cond for up to three seconds.
func eventually(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(25 * time.Millisecond)
	}
	t.Fatalf("timed out waiting for %s", what)
}

func TestMain(m *testing.M) {
	code := m.Run()
	dbtest.Cleanup()
	os.Exit(code)
}
