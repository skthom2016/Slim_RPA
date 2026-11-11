package process

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/xuri/excelize/v2"

	"tracto/sheet-service/internal/sheet"
)

// ── a fake Orchestrator ──────────────────────────────────────────────────────

type submitted struct {
	Params                 map[string]string
	BusinessJobID, EventID string
}

// behaviour says how a child ends, decided from the params it was queued with.
type behaviour struct {
	State   string // SUCCEEDED, FAILED, or RUNNING for a child that never ends
	Outputs map[string]string
	ErrLog  string
}

type fakeOrch struct {
	mu          sync.Mutex
	srv         *httptest.Server
	runs        map[string]submitted
	submits     []submitted
	cancels     []string
	decide      func(params map[string]string) behaviour
	rejectJobs  bool
	unpublished bool // the child script is not on the Orchestrator
}

func newFakeOrch(t *testing.T, decide func(map[string]string) behaviour) *fakeOrch {
	t.Helper()
	f := &fakeOrch{runs: map[string]submitted{}, decide: decide}
	mux := http.NewServeMux()
	mux.HandleFunc("POST /jobs", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		defer f.mu.Unlock()
		if f.rejectJobs {
			http.Error(w, "down", http.StatusServiceUnavailable)
			return
		}
		var body struct {
			Params        map[string]string `json:"params"`
			BusinessJobID string            `json:"businessJobId"`
			EventID       string            `json:"eventId"`
		}
		_ = json.NewDecoder(r.Body).Decode(&body)
		s := submitted{Params: body.Params, BusinessJobID: body.BusinessJobID, EventID: body.EventID}
		id := fmt.Sprintf("run-%d", len(f.submits)+1)
		f.runs[id] = s
		f.submits = append(f.submits, s)
		_ = json.NewEncoder(w).Encode(map[string]string{"runId": id, "state": "RUNNING"})
	})
	mux.HandleFunc("GET /artifacts/{id}/versions/{version}", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		missing := f.unpublished
		f.mu.Unlock()
		if missing {
			http.Error(w, "not found", http.StatusNotFound)
			return
		}
		_, _ = w.Write([]byte("{}"))
	})
	mux.HandleFunc("GET /jobs/{id}", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		s := f.runs[r.PathValue("id")]
		f.mu.Unlock()
		b := f.decide(s.Params)
		_ = json.NewEncoder(w).Encode(map[string]any{"state": b.State, "outputs": b.Outputs})
	})
	mux.HandleFunc("POST /jobs/{id}/cancel", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		f.cancels = append(f.cancels, r.PathValue("id"))
		f.mu.Unlock()
		w.WriteHeader(http.StatusOK)
	})
	mux.HandleFunc("GET /admin/logs/{id}", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		s := f.runs[r.PathValue("id")]
		f.mu.Unlock()
		logs := []map[string]string{}
		if e := f.decide(s.Params).ErrLog; e != "" {
			logs = append(logs, map[string]string{"message": e})
		}
		_ = json.NewEncoder(w).Encode(map[string]any{"logs": logs})
	})
	f.srv = httptest.NewServer(mux)
	t.Cleanup(f.srv.Close)
	return f
}

func (f *fakeOrch) submitCount() int {
	f.mu.Lock()
	defer f.mu.Unlock()
	return len(f.submits)
}

func (f *fakeOrch) cancelled() []string {
	f.mu.Lock()
	defer f.mu.Unlock()
	return append([]string(nil), f.cancels...)
}

func succeeds(params map[string]string) behaviour {
	id := params["invoiceId"]
	return behaviour{State: "SUCCEEDED", Outputs: map[string]string{
		"invItem.value": "Item " + id, "invTotal.value": id + " USD", "invDate.value": "2018-01-01",
	}}
}

// ── helpers ──────────────────────────────────────────────────────────────────

var header = []any{"Id", "InvoiceItem", "Total", "Date", "Status", "Message"}

func workbook(t *testing.T, rows ...[]any) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "invoices.xlsx")
	f := excelize.NewFile()
	f.NewSheet("Invoices")
	all := append([][]any{header}, rows...)
	for i, r := range all {
		cell, _ := excelize.CoordinatesToCellName(1, i+1)
		if err := f.SetSheetRow("Invoices", cell, &r); err != nil {
			t.Fatal(err)
		}
	}
	if err := f.SaveAs(path); err != nil {
		t.Fatal(err)
	}
	return path
}

func ids(values ...string) [][]any {
	rows := make([][]any, len(values))
	for i, v := range values {
		rows[i] = []any{v}
	}
	return rows
}

func cell(t *testing.T, path, addr string) string {
	t.Helper()
	f, err := excelize.OpenFile(path)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	v, err := f.GetCellValue("Invoices", addr)
	if err != nil {
		t.Fatal(err)
	}
	return v
}

func request(path string) StartRequest {
	var r StartRequest
	r.Source = sheet.Config{Path: path, Sheet: "Invoices"}
	r.Child.ArtifactID, r.Child.Version = "acme-child", "1.0.0"
	r.Params = map[string]string{"invoiceId": "Id"}
	r.Outputs = map[string]string{"invItem.value": "InvoiceItem", "invTotal.value": "Total", "invDate.value": "Date"}
	r.MessageColumn = "Message"
	r.TimeoutSeconds = 5000
	return r
}

func manager(t *testing.T, f *fakeOrch) *Manager {
	t.Helper()
	dir := t.TempDir()
	store, err := OpenStore(filepath.Join(dir, "batches.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { store.Close() })
	m := NewManager(store, NewOrchestrator(f.srv.URL))
	m.PollEvery, m.TimeoutUnit = 2*time.Millisecond, time.Millisecond
	m.WriteRetries, m.WriteRetryWait = 1, time.Millisecond
	return m
}

func finished(t *testing.T, m *Manager, id string) *Batch {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for time.Now().Before(deadline) {
		b, err := m.Store().Get(id)
		if err != nil {
			t.Fatal(err)
		}
		if b.State != BatchRunning {
			return b
		}
		time.Sleep(5 * time.Millisecond)
	}
	b, _ := m.Store().Get(id)
	dump, _ := json.MarshalIndent(b, "", "  ")
	t.Fatalf("batch did not finish; it is:\n%s", dump)
	return nil
}

func start(t *testing.T, m *Manager, r StartRequest) *StartResult {
	t.Helper()
	res, err := m.Start(context.Background(), r)
	if err != nil {
		t.Fatalf("start: %v", err)
	}
	return res
}

// ── the loop ─────────────────────────────────────────────────────────────────

func TestBatch_RunsAChildPerRowAndWritesTheResultBack(t *testing.T) {
	path := workbook(t, ids("111", "222", "333")...)
	f := newFakeOrch(t, succeeds)
	m := manager(t, f)

	res := start(t, m, request(path))
	if res.Total != 3 || res.State != BatchRunning {
		t.Fatalf("got %+v, want 3 rows running", res)
	}
	b := finished(t, m, res.BatchID)

	if b.State != BatchCompleted || b.Counts.Done != 3 {
		t.Fatalf("got state %s counts %+v", b.State, b.Counts)
	}
	for i, id := range []string{"111", "222", "333"} {
		row := i + 2
		if got := cell(t, path, fmt.Sprintf("B%d", row)); got != "Item "+id {
			t.Errorf("row %d InvoiceItem = %q", row, got)
		}
		if got := cell(t, path, fmt.Sprintf("C%d", row)); got != id+" USD" {
			t.Errorf("row %d Total = %q", row, got)
		}
		if got := cell(t, path, fmt.Sprintf("E%d", row)); got != "Done" {
			t.Errorf("row %d Status = %q", row, got)
		}
	}

	// Every child carries the batch's id, so the runs can be traced together, and a row id of
	// its own, so a repeat of one row cannot start it twice.
	events := map[string]bool{}
	for _, s := range f.submits {
		if s.BusinessJobID != res.BatchID {
			t.Errorf("businessJobId = %q, want %q", s.BusinessJobID, res.BatchID)
		}
		events[s.EventID] = true
	}
	if len(events) != 3 {
		t.Errorf("want 3 distinct eventIds, got %v", events)
	}
	if f.submits[0].Params["invoiceId"] != "111" || f.submits[2].Params["invoiceId"] != "333" {
		t.Errorf("rows must run in sheet order, got %+v", f.submits)
	}
}

func TestBatch_OneFailureDoesNotStopTheRestAndARerunRetriesOnlyIt(t *testing.T) {
	path := workbook(t, ids("111", "222", "333")...)
	var mu sync.Mutex
	broken := true
	f := newFakeOrch(t, func(p map[string]string) behaviour {
		mu.Lock()
		defer mu.Unlock()
		if p["invoiceId"] == "222" && broken {
			return behaviour{State: "FAILED", ErrLog: "locator matched 2 elements"}
		}
		return succeeds(p)
	})
	m := manager(t, f)

	b := finished(t, m, start(t, m, request(path)).BatchID)
	if b.State != BatchCompletedWithErrors || b.Counts.Done != 2 || b.Counts.Failed != 1 {
		t.Fatalf("got state %s counts %+v", b.State, b.Counts)
	}
	if got := cell(t, path, "E3"); got != "Failed" {
		t.Errorf("Status = %q, want Failed", got)
	}
	// The reason a person can act on is the runtime's own error line.
	if msg := cell(t, path, "F3"); !strings.Contains(msg, "ended FAILED") || !strings.Contains(msg, "locator matched 2 elements") {
		t.Errorf("Message = %q", msg)
	}
	if cell(t, path, "E4") != "Done" {
		t.Error("the row after the failure must still have run")
	}

	mu.Lock()
	broken = false
	mu.Unlock()
	before := f.submitCount()
	res := start(t, m, request(path))
	if res.Total != 1 {
		t.Fatalf("a rerun must pick up only the failed row, got %d", res.Total)
	}
	b = finished(t, m, res.BatchID)
	if b.State != BatchCompleted || f.submitCount() != before+1 {
		t.Fatalf("got state %s, submits %d -> %d", b.State, before, f.submitCount())
	}
	if cell(t, path, "E3") != "Done" || cell(t, path, "F3") != "" {
		t.Errorf("a retried row must end Done with its message cleared, got %q / %q", cell(t, path, "E3"), cell(t, path, "F3"))
	}
}

func TestBatch_ASucceededChildThatLacksAnOutputIsAFailureNotASilentGap(t *testing.T) {
	path := workbook(t, ids("111")...)
	f := newFakeOrch(t, func(p map[string]string) behaviour {
		return behaviour{State: "SUCCEEDED", Outputs: map[string]string{"invItem.value": "x", "invTotal.value": "y"}}
	})
	m := manager(t, f)

	b := finished(t, m, start(t, m, request(path)).BatchID)
	if b.Counts.Failed != 1 {
		t.Fatalf("counts %+v", b.Counts)
	}
	if msg := cell(t, path, "F2"); !strings.Contains(msg, `did not return "invDate.value"`) {
		t.Errorf("Message = %q", msg)
	}
	if cell(t, path, "B2") != "" {
		t.Error("a partial result must not be written")
	}
}

func TestBatch_ARowWithNoIdFailsWithoutStartingAJob(t *testing.T) {
	path := workbook(t, []any{"111"}, []any{"", "", "", "", "", "left over note"})
	f := newFakeOrch(t, succeeds)
	m := manager(t, f)

	b := finished(t, m, start(t, m, request(path)).BatchID)
	if f.submitCount() != 1 {
		t.Fatalf("only the row with an id may start a job, got %d", f.submitCount())
	}
	if b.State != BatchCompletedWithErrors {
		t.Fatalf("state %s", b.State)
	}
	if msg := cell(t, path, "F3"); !strings.Contains(msg, `column "Id" is empty`) {
		t.Errorf("Message = %q", msg)
	}
}

func TestBatch_AChildThatNeverEndsIsCancelledAndFailed(t *testing.T) {
	path := workbook(t, ids("111")...)
	f := newFakeOrch(t, func(map[string]string) behaviour { return behaviour{State: "RUNNING"} })
	m := manager(t, f)
	r := request(path)
	r.TimeoutSeconds = 40 // milliseconds in a test

	b := finished(t, m, start(t, m, r).BatchID)
	if b.State != BatchCompletedWithErrors {
		t.Fatalf("state %s", b.State)
	}
	if msg := cell(t, path, "F2"); !strings.Contains(msg, "did not finish within 40 seconds") {
		t.Errorf("Message = %q", msg)
	}
	if got := f.cancelled(); len(got) != 1 || got[0] != "run-1" {
		t.Errorf("the stuck child must be cancelled so it does not hold the worker, got %v", got)
	}
}

func TestBatch_StopsWhenTheOrchestratorCannotBeReached(t *testing.T) {
	path := workbook(t, ids("1", "2", "3", "4", "5")...)
	f := newFakeOrch(t, succeeds)
	f.rejectJobs = true
	m := manager(t, f)

	b := finished(t, m, start(t, m, request(path)).BatchID)
	if b.State != BatchStopped || !strings.Contains(b.Message, "could not reach the Orchestrator") {
		t.Fatalf("state %s message %q", b.State, b.Message)
	}
	if b.Counts.Failed != 3 || b.Counts.Pending != 2 {
		t.Fatalf("it must stop instead of failing every remaining row, got %+v", b.Counts)
	}
}

// ── starting and cancelling ──────────────────────────────────────────────────

func TestStart_RefusesARequestThatCannotRunBeforeAnythingHappens(t *testing.T) {
	path := workbook(t, ids("111")...)
	f := newFakeOrch(t, succeeds)
	m := manager(t, f)

	for name, mutate := range map[string]func(*StartRequest){
		"unknown column":          func(r *StartRequest) { r.Outputs["invItem.value"] = "NoSuchColumn" },
		"param is also an output": func(r *StartRequest) { r.Outputs["invTotal.value"] = "Id" },
		"status column reused":    func(r *StartRequest) { r.StatusColumn = "Total" },
		"no child":                func(r *StartRequest) { r.Child.ArtifactID = "" },
		"no params":               func(r *StartRequest) { r.Params = nil },
		"no outputs":              func(r *StartRequest) { r.Outputs = nil },
	} {
		r := request(path)
		mutate(&r)
		if _, err := m.Start(context.Background(), r); !errors.Is(err, ErrInvalid) {
			t.Errorf("%s: got %v, want ErrInvalid", name, err)
		}
	}
	if f.submitCount() != 0 {
		t.Error("a refused request must not start any job")
	}
	// A refused request must not leave the sheet reserved.
	start(t, m, request(path))
}

func TestStart_RefusesAChildScriptThatIsNotPublished(t *testing.T) {
	path := workbook(t, ids("111", "222")...)
	f := newFakeOrch(t, succeeds)
	f.unpublished = true
	m := manager(t, f)

	_, err := m.Start(context.Background(), request(path))
	if !errors.Is(err, ErrInvalid) || !strings.Contains(err.Error(), "is not published") {
		t.Fatalf("got %v, want ErrInvalid saying the script is not published", err)
	}
	if f.submitCount() != 0 {
		t.Error("no row may be started for a script that does not exist")
	}
	// The refused start must not leave the sheet reserved or any batch behind.
	f.unpublished = false
	start(t, m, request(path))
}

func TestStart_WithNothingPendingCompletesAtOnce(t *testing.T) {
	path := workbook(t, []any{"111", "a", "b", "c", "Done"})
	f := newFakeOrch(t, succeeds)
	m := manager(t, f)

	res := start(t, m, request(path))
	if res.State != BatchCompleted || res.Total != 0 || f.submitCount() != 0 {
		t.Fatalf("got %+v, submits %d", res, f.submitCount())
	}
}

func TestStart_RefusesASecondBatchOnTheSameSheetAndCancelStopsTheFirst(t *testing.T) {
	path := workbook(t, ids("111", "222")...)
	f := newFakeOrch(t, func(map[string]string) behaviour { return behaviour{State: "RUNNING"} })
	m := manager(t, f)

	first := start(t, m, request(path))
	_, err := m.Start(context.Background(), request(path))
	var busy *BusyError
	if !errors.As(err, &busy) || busy.BatchID != first.BatchID {
		t.Fatalf("got %v, want a BusyError naming %s", err, first.BatchID)
	}

	// Wait until the first child is actually running, then cancel.
	for i := 0; i < 500 && f.submitCount() == 0; i++ {
		time.Sleep(2 * time.Millisecond)
	}
	if !m.Cancel(first.BatchID) {
		t.Fatal("cancel reported no running batch")
	}
	b := finished(t, m, first.BatchID)
	if b.State != BatchCancelled {
		t.Fatalf("state %s", b.State)
	}
	if got := f.cancelled(); len(got) != 1 {
		t.Errorf("the child in flight must be cancelled, got %v", got)
	}
	if b.Counts.Cancelled != 1 || b.Counts.Pending != 1 {
		t.Errorf("counts %+v", b.Counts)
	}
	if cell(t, path, "E2") != "" {
		t.Error("a cancelled row is still pending in the sheet, so a restart picks it up")
	}
	if m.Cancel(first.BatchID) {
		t.Error("a finished batch cannot be cancelled")
	}
}

func TestStore_InterruptRunningMarksWhatAPreviousProcessLeft(t *testing.T) {
	store, err := OpenStore(filepath.Join(t.TempDir(), "b.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	if err := store.CreateBatch(BatchRecord{ID: "b1", SourcePath: "x", Sheet: "s", Child: "c@1"},
		[]ItemRecord{{Row: 2}, {Row: 3}}); err != nil {
		t.Fatal(err)
	}
	_ = store.SetItem("b1", 2, ItemRunning, "run-1", "")

	n, err := store.InterruptRunning()
	if err != nil || n != 1 {
		t.Fatalf("got %d, %v", n, err)
	}
	b, _ := store.Get("b1")
	if b.State != BatchInterrupted || b.Counts.Interrupted != 1 || b.Counts.Pending != 1 {
		t.Fatalf("state %s counts %+v", b.State, b.Counts)
	}
	if b.Items[0].RunID != "run-1" {
		t.Error("the run id of the interrupted row must be kept")
	}
	if n, _ := store.InterruptRunning(); n != 0 {
		t.Errorf("a second call must find nothing, got %d", n)
	}
	if _, err := store.Get("nope"); !errors.Is(err, ErrNotFound) {
		t.Errorf("got %v, want ErrNotFound", err)
	}
}
