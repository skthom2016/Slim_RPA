package api

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/xuri/excelize/v2"

	"tracto/sheet-service/internal/msauth"
	"tracto/sheet-service/internal/process"
)

func workbook(t *testing.T) (path string) {
	t.Helper()
	path = filepath.Join(t.TempDir(), "book.xlsx")
	f := excelize.NewFile()
	f.NewSheet("Data")
	rows := [][]any{
		{"Style_ID", "Approved_by_Finance", "Bot_Status", "Bot_Comments"},
		{"1", "", "", ""},
		{"2", "", "", ""},
		{"3", "Approved", "OLD", "keep"},
	}
	for i, r := range rows {
		cell, _ := excelize.CoordinatesToCellName(1, i+1)
		if err := f.SetSheetRow("Data", cell, &r); err != nil {
			t.Fatal(err)
		}
	}
	if err := f.SaveAs(path); err != nil {
		t.Fatal(err)
	}
	return path
}

func post(t *testing.T, h http.Handler, route, body string) *httptest.ResponseRecorder {
	t.Helper()
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodPost, route, strings.NewReader(body)))
	return rec
}

func TestUpdate_ManyRowsWithManyColumnsInOneCall(t *testing.T) {
	path := workbook(t)
	h := New()
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/update", `{"source":`+src+`,"rows":[
		{"row":2,"values":{"Approved_by_Finance":"Approved","Bot_Status":"SUCCESS","Bot_Comments":"done"}},
		{"row":3,"values":{"Bot_Status":"FAILED"}}]}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
	var resp UpdateResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &resp)
	if resp.Rows != 2 || resp.Cells != 4 {
		t.Fatalf("got %+v, want 2 rows and 4 cells", resp)
	}

	rec = post(t, h, "/batch", `{"source":`+src+`,"startRow":2,"endRow":10}`)
	var b BatchResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &b)
	by := map[int]map[string]string{}
	for _, r := range b.Rows {
		by[r.Number] = r.Values
	}
	if by[2]["Bot_Status"] != "SUCCESS" || by[2]["Approved_by_Finance"] != "Approved" || by[2]["Bot_Comments"] != "done" {
		t.Fatalf("row 2 wrong: %v", by[2])
	}
	if by[3]["Bot_Status"] != "FAILED" || by[3]["Bot_Comments"] != "" || by[3]["Approved_by_Finance"] != "" {
		t.Fatalf("row 3 must change only Bot_Status: %v", by[3])
	}
	if by[4]["Bot_Status"] != "OLD" || by[4]["Bot_Comments"] != "keep" {
		t.Fatalf("row 4 was not part of the call and must be untouched: %v", by[4])
	}
}

func TestApply_FillsOnlyTheFirstPendingRows(t *testing.T) {
	path := workbook(t)
	h := New()
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/apply", `{"source":`+src+`,"limit":1,
		"pending":{"column":"Approved_by_Finance","doneValues":["Approved","Rejected"]},
		"set":{"Approved_by_Finance":"Approved","Bot_Status":"SUCCESS"}}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
	var resp ApplyResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &resp)
	if resp.Updated != 1 || resp.Rows[0].Number != 2 || !strings.Contains(resp.Text, "Style_ID") {
		t.Fatalf("got %+v", resp)
	}

	rec = post(t, h, "/batch", `{"source":`+src+`,"startRow":2,"endRow":10}`)
	var b BatchResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &b)
	if b.Rows[0].Values["Bot_Status"] != "SUCCESS" || b.Rows[1].Values["Bot_Status"] != "" {
		t.Fatalf("only row 2 may be filled: %+v", b.Rows)
	}
}

// With no saved Microsoft sign-in the caller gets 401 and a message saying to run login.
func TestBatch_MissingMicrosoftSignInIsUnauthorized(t *testing.T) {
	t.Setenv("MS_ACCESS_TOKEN", "")
	t.Setenv("MS_TENANT_ID", "tenant-1")
	t.Setenv("MS_CLIENT_ID", "app")
	db := filepath.Join(t.TempDir(), "tokens.db")
	t.Setenv("SHEET_SERVICE_DB", db)
	t.Cleanup(func() { // Windows cannot delete an open database file
		if s, err := msauth.OpenStore(db); err == nil {
			s.Close()
		}
	})

	rec := post(t, New(), "/batch", `{"source":{"path":"https://contoso.sharepoint.com/sites/x/items.xlsx","sheet":"Items"}}`)
	if rec.Code != http.StatusUnauthorized || !strings.Contains(rec.Body.String(), "sheet-service login") {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
}

func TestApply_RequiresLimitAndSet(t *testing.T) {
	rec := post(t, New(), "/apply", `{"source":{"path":"x.xlsx","sheet":"Data"},"limit":0,"set":{"a":"b"}}`)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status %d, want 400", rec.Code)
	}
}

func TestUpdate_BadColumnWritesNothing(t *testing.T) {
	path := workbook(t)
	h := New()
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/update", `{"source":`+src+`,"rows":[
		{"row":2,"values":{"Bot_Status":"SUCCESS"}},
		{"row":3,"values":{"No_Such_Column":"x"}}]}`)
	if rec.Code == http.StatusOK {
		t.Fatal("expected an error for the unknown column")
	}
	rec = post(t, h, "/batch", `{"source":`+src+`,"startRow":2,"endRow":2}`)
	var b BatchResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &b)
	if b.Rows[0].Values["Bot_Status"] != "" {
		t.Fatal("row 2 must not be written when another row in the same call is invalid")
	}
}

func TestUpdate_EmptyRequestIsRejected(t *testing.T) {
	rec := post(t, New(), "/update", `{"source":{"type":"xlsx","path":"x","sheet":"s"}}`)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("got %d, want 400", rec.Code)
	}
}

func TestCell_ReadsOneValueAsFlatFields(t *testing.T) {
	path := workbook(t)
	h := New()
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/cell", `{"source":`+src+`,"row":4,"column":"Bot_Comments"}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
	var got map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &got)
	// Flat scalars only: a script reaches nothing nested in a reply.
	if got["value"] != "keep" || got["row"] != float64(4) || got["column"] != "Bot_Comments" || got["found"] != true {
		t.Fatalf("got %v", got)
	}
}

func TestCell_UnknownColumnIsRejected(t *testing.T) {
	path := workbook(t)
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, New(), "/cell", `{"source":`+src+`,"row":2,"column":"No_Such_Column"}`)
	if rec.Code != http.StatusBadRequest || !strings.Contains(rec.Body.String(), "not found in header") {
		t.Fatalf("got %d: %s", rec.Code, rec.Body)
	}
}

func TestCell_EmptyCellIsAnEmptyValueUnlessRequired(t *testing.T) {
	path := workbook(t)
	h := New()
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/cell", `{"source":`+src+`,"row":2,"column":"Bot_Status"}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
	var got CellResponse
	_ = json.Unmarshal(rec.Body.Bytes(), &got)
	if got.Value != "" || got.Found {
		t.Fatalf("got %+v, want an empty value that is not found", got)
	}

	rec = post(t, h, "/cell", `{"source":`+src+`,"row":2,"column":"Bot_Status","required":true}`)
	if rec.Code != http.StatusUnprocessableEntity {
		t.Fatalf("status %d, want 422 for a required empty cell", rec.Code)
	}
}

func TestCell_RowBeyondTheDataIsNotFound(t *testing.T) {
	path := workbook(t)
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, New(), "/cell", `{"source":`+src+`,"row":50,"column":"Style_ID","required":true}`)
	if rec.Code != http.StatusUnprocessableEntity {
		t.Fatalf("status %d: %s, want 422", rec.Code, rec.Body)
	}
}

func TestCell_RequiresRowAndColumn(t *testing.T) {
	rec := post(t, New(), "/cell", `{"source":{"path":"x.xlsx","sheet":"Data"},"row":0,"column":""}`)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status %d, want 400", rec.Code)
	}
}

// ── POST /process/start ──────────────────────────────────────────────────────

func processHandler(t *testing.T) http.Handler {
	t.Helper()
	orch := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch {
		case r.Method == http.MethodGet && strings.HasPrefix(r.URL.Path, "/artifacts/"):
			_, _ = w.Write([]byte("{}"))
		case r.Method == http.MethodPost && r.URL.Path == "/jobs":
			_, _ = w.Write([]byte(`{"runId":"run-1","state":"RUNNING"}`))
		case r.Method == http.MethodGet && r.URL.Path == "/jobs/run-1":
			_, _ = w.Write([]byte(`{"state":"SUCCEEDED","outputs":{"out":"result"}}`))
		default:
			http.NotFound(w, r)
		}
	}))
	t.Cleanup(orch.Close)
	store, err := process.OpenStore(filepath.Join(t.TempDir(), "batches.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { store.Close() })
	m := process.NewManager(store, process.NewOrchestrator(orch.URL))
	m.PollEvery = time.Millisecond
	return NewWithProcess(m)
}

func TestProcess_StartRepliesAtOnceAndTheBatchCanBeReadBack(t *testing.T) {
	path := workbook(t)
	h := processHandler(t)
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	rec := post(t, h, "/process/start", `{"source":`+src+`,
		"child":{"artifactId":"c","version":"1.0.0"},
		"params":{"sid":"Style_ID"},"outputs":{"out":"Bot_Comments"},"statusColumn":"Bot_Status"}`)
	if rec.Code != http.StatusAccepted {
		t.Fatalf("status %d: %s", rec.Code, rec.Body)
	}
	// Flat fields, so a script can read them as ${batch.batchId} and ${batch.total}.
	var started map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &started)
	id, _ := started["batchId"].(string)
	if id == "" || started["state"] != "Running" || started["total"] != float64(3) {
		t.Fatalf("got %v", started)
	}

	var batch struct {
		State  string `json:"state"`
		Counts struct {
			Done int `json:"done"`
		} `json:"counts"`
	}
	for i := 0; i < 500; i++ {
		get := httptest.NewRecorder()
		h.ServeHTTP(get, httptest.NewRequest(http.MethodGet, "/process/"+id, nil))
		_ = json.Unmarshal(get.Body.Bytes(), &batch)
		if batch.State != "Running" {
			break
		}
		time.Sleep(5 * time.Millisecond)
	}
	if batch.State != "Completed" || batch.Counts.Done != 3 {
		t.Fatalf("got %+v", batch)
	}
}

func TestProcess_RoutesRefuseWhatTheyShould(t *testing.T) {
	path := workbook(t)
	h := processHandler(t)
	src := `{"type":"xlsx","path":` + jsonString(path) + `,"sheet":"Data"}`

	if rec := post(t, h, "/process/start", `{"source":`+src+`,"child":{"artifactId":"c","version":"1"},
		"params":{"sid":"No_Such_Column"},"outputs":{"out":"Bot_Comments"},"statusColumn":"Bot_Status"}`); rec.Code != http.StatusBadRequest ||
		!strings.Contains(rec.Body.String(), "not found in header") {
		t.Errorf("unknown column: %d %s", rec.Code, rec.Body)
	}
	if rec := post(t, h, "/process/start", `not json`); rec.Code != http.StatusBadRequest {
		t.Errorf("bad body: %d", rec.Code)
	}

	get := httptest.NewRecorder()
	h.ServeHTTP(get, httptest.NewRequest(http.MethodGet, "/process/nope", nil))
	if get.Code != http.StatusNotFound {
		t.Errorf("unknown batch: %d", get.Code)
	}
	if rec := post(t, h, "/process/nope/cancel", ``); rec.Code != http.StatusNotFound {
		t.Errorf("cancel unknown: %d", rec.Code)
	}
	// Without a Manager the stateless service has no process routes at all.
	if rec := post(t, New(), "/process/start", `{}`); rec.Code != http.StatusNotFound && rec.Code != http.StatusMethodNotAllowed {
		t.Errorf("no manager: %d", rec.Code)
	}
}

func jsonString(s string) string {
	b, _ := json.Marshal(s)
	return string(b)
}
