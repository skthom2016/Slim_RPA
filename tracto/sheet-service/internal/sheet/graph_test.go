package sheet

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/xuri/excelize/v2"

	"tracto/sheet-service/internal/msauth"
)

// fakeGraph imitates the parts of Microsoft Graph the connector uses: the share lookup and the
// Excel range API (used range, range read, range update, workbook sessions).
type fakeGraph struct {
	srv *httptest.Server

	mu       sync.Mutex
	grid     [][]string // grid[0] is row 1
	reads    []string   // addresses requested with GET
	patches  []patch
	sessions int
	closed   int
	throttle int // answer the next N requests with 429
}

type patch struct {
	address string
	session string
	values  [][]any
}

var addrRe = regexp.MustCompile(`range\(address='([A-Z]+)(\d+)(?::([A-Z]+)(\d+))?'\)`)

func newFakeGraph(t *testing.T, grid [][]string) *fakeGraph {
	t.Helper()
	f := &fakeGraph{grid: grid}
	const wb = "/v1.0/drives/DRIVE1/items/ITEM1/workbook"
	f.srv = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		defer f.mu.Unlock()
		if r.Header.Get("Authorization") != "Bearer tok" {
			http.Error(w, "unauthorized", http.StatusUnauthorized)
			return
		}
		if f.throttle > 0 {
			f.throttle--
			w.Header().Set("Retry-After", "0")
			http.Error(w, "throttled", http.StatusTooManyRequests)
			return
		}
		p := r.URL.Path
		switch {
		case strings.HasPrefix(p, "/v1.0/shares/") && strings.HasSuffix(p, "/driveItem"):
			_ = json.NewEncoder(w).Encode(map[string]any{"id": "ITEM1", "parentReference": map[string]string{"driveId": "DRIVE1"}})
		case p == wb+"/createSession" && r.Method == http.MethodPost:
			f.sessions++
			_, _ = w.Write([]byte(`{"id":"S1"}`))
		case p == wb+"/closeSession" && r.Method == http.MethodPost:
			f.closed++
			w.WriteHeader(http.StatusNoContent)
		case strings.HasPrefix(p, wb+"/worksheets/Data/usedRange"):
			lastCol := 0
			for _, row := range f.grid {
				lastCol = max(lastCol, len(row))
			}
			name, _ := excelize.ColumnNumberToName(lastCol)
			_ = json.NewEncoder(w).Encode(map[string]string{"address": fmt.Sprintf("Data!A1:%s%d", name, len(f.grid))})
		case strings.HasPrefix(p, wb+"/worksheets/Data/range"):
			m := addrRe.FindStringSubmatch(p)
			if m == nil {
				http.NotFound(w, r)
				return
			}
			c1, _ := excelize.ColumnNameToNumber(m[1])
			r1, _ := strconv.Atoi(m[2])
			c2, r2 := c1, r1
			if m[3] != "" {
				c2, _ = excelize.ColumnNameToNumber(m[3])
				r2, _ = strconv.Atoi(m[4])
			}
			if r.Method == http.MethodGet {
				f.reads = append(f.reads, m[0])
				var out [][]string
				for row := r1; row <= r2; row++ {
					line := make([]string, c2-c1+1)
					for c := c1; c <= c2; c++ {
						if row <= len(f.grid) && c <= len(f.grid[row-1]) {
							line[c-c1] = f.grid[row-1][c-1]
						}
					}
					out = append(out, line)
				}
				_ = json.NewEncoder(w).Encode(map[string]any{"text": out})
				return
			}
			body, _ := io.ReadAll(r.Body)
			var req struct {
				Values [][]any `json:"values"`
			}
			_ = json.Unmarshal(body, &req)
			f.patches = append(f.patches, patch{address: m[1] + m[2] + ":" + m[3] + m[4], session: r.Header.Get("Workbook-Session-Id"), values: req.Values})
			for i, line := range req.Values {
				for j, v := range line {
					if v == nil {
						continue // null leaves the cell as it is
					}
					row, col := r1+i, c1+j
					for len(f.grid) < row {
						f.grid = append(f.grid, nil)
					}
					for len(f.grid[row-1]) < col {
						f.grid[row-1] = append(f.grid[row-1], "")
					}
					f.grid[row-1][col-1] = fmt.Sprint(v)
				}
			}
			_, _ = w.Write([]byte(`{}`))
		default:
			http.NotFound(w, r)
		}
	}))
	t.Cleanup(f.srv.Close)
	return f
}

func (f *fakeGraph) cell(row, col int) string {
	f.mu.Lock()
	defer f.mu.Unlock()
	if row > len(f.grid) || col > len(f.grid[row-1]) {
		return ""
	}
	return f.grid[row-1][col-1]
}

func itemsGrid() [][]string {
	return [][]string{
		{"Item_ID", "Item_Name", "Approval_Status", "Bot_Status"},
		{"ITM-1", "One", "Approved", ""},
		{"ITM-2", "Two", "", ""},
		{"ITM-3", "Three", "Hold", ""},
		{"ITM-4", "Four", "Rejected", "OLD"},
		{"ITM-5", "Five", "", ""},
	}
}

func graphSrc(t *testing.T, f *fakeGraph) Source {
	t.Helper()
	retryWait = func(context.Context, time.Duration) error { return nil }
	t.Cleanup(func() { retryWait = realRetryWait })
	src, err := Open(Config{
		Path:      "https://contoso.sharepoint.com/sites/team/Shared%20Documents/items.xlsx",
		Sheet:     "Data",
		Env:       env("MS_ACCESS_TOKEN", "tok"),
		GraphBase: f.srv.URL,
	})
	if err != nil {
		t.Fatal(err)
	}
	return src
}

var realRetryWait = retryWait

func TestGraph_ReadRowsRequestsOnlyTheRange(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	b, err := graphSrc(t, f).ReadRows(context.Background(), 3, 4)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 2 || b.Rows[0].Number != 3 || b.Rows[1].Number != 4 {
		t.Fatalf("got %+v, want rows 3 and 4", b.Rows)
	}
	if b.Rows[1].Values["Approval_Status"] != "Hold" || b.Rows[0].Values["Item_ID"] != "ITM-2" {
		t.Fatalf("values wrong: %+v", b.Rows)
	}
	if b.EndOfData {
		t.Fatal("row 4 is not the last row of the sheet")
	}
	if got := strings.Join(f.reads, " "); got != "range(address='A1:D1') range(address='A3:D4')" {
		t.Fatalf("requested %q: only the header and rows 3-4 may be read", got)
	}
}

func TestGraph_RangeIsCappedAtTheLastUsedRow(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	b, err := graphSrc(t, f).ReadRows(context.Background(), 2, 1001)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 5 || !b.EndOfData || b.LastRow != 6 {
		t.Fatalf("rows=%d endOfData=%v lastRow=%d", len(b.Rows), b.EndOfData, b.LastRow)
	}
	if got := f.reads[len(f.reads)-1]; got != "range(address='A2:D6')" {
		t.Fatalf("requested %q, want the range cut at the last used row", got)
	}
}

func TestGraph_StartPastTheLastRowReadsNoData(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	b, err := graphSrc(t, f).ReadRows(context.Background(), 100, 199)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 0 || !b.EndOfData || len(f.reads) != 1 {
		t.Fatalf("rows=%d endOfData=%v reads=%v", len(b.Rows), b.EndOfData, f.reads)
	}
}

func TestGraph_NeighbouringRowsAreWrittenInOnePatchWithNullForUnchangedCells(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	err := graphSrc(t, f).Write(context.Background(), []CellUpdate{
		{Row: 3, Column: "Approval_Status", Value: "Approved"},
		{Row: 3, Column: "Bot_Status", Value: "SUCCESS"},
		{Row: 4, Column: "Bot_Status", Value: "SUCCESS"},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(f.patches) != 1 {
		t.Fatalf("%d patches, want 1: %+v", len(f.patches), f.patches)
	}
	p := f.patches[0]
	if p.address != "C3:D4" || p.session != "S1" {
		t.Fatalf("patch %+v: want range C3:D4 inside session S1", p)
	}
	if p.values[1][0] != nil {
		t.Fatalf("row 4 column C is not being changed and must be null: %v", p.values)
	}
	if f.cell(4, 3) != "Hold" || f.cell(4, 4) != "SUCCESS" || f.cell(3, 3) != "Approved" || f.cell(3, 4) != "SUCCESS" || f.cell(5, 4) != "OLD" {
		t.Fatalf("sheet wrong: %v", f.grid)
	}
	if f.sessions != 1 || f.closed != 1 {
		t.Fatalf("sessions opened %d, closed %d, want 1 and 1", f.sessions, f.closed)
	}
}

func TestGraph_ScatteredRowsAreSeparatePatches(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	err := graphSrc(t, f).Write(context.Background(), []CellUpdate{
		{Row: 2, Column: "Bot_Status", Value: "A"},
		{Row: 6, Column: "Bot_Status", Value: "B"},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(f.patches) != 2 || f.patches[0].address != "D2:D2" || f.patches[1].address != "D6:D6" {
		t.Fatalf("patches: %+v", f.patches)
	}
}

func TestGraph_BadColumnWritesNothing(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	err := graphSrc(t, f).Write(context.Background(), []CellUpdate{
		{Row: 3, Column: "Bot_Status", Value: "SUCCESS"},
		{Row: 4, Column: "No_Such_Column", Value: "x"},
	})
	if err == nil || !strings.Contains(err.Error(), "No_Such_Column") {
		t.Fatalf("got %v", err)
	}
	if len(f.patches) != 0 || f.sessions != 0 {
		t.Fatalf("nothing may be written: patches=%d sessions=%d", len(f.patches), f.sessions)
	}
}

func TestGraph_ThrottledRequestsAreRetried(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	f.throttle = 2
	if _, err := graphSrc(t, f).ReadRows(context.Background(), 2, 3); err != nil {
		t.Fatalf("a throttled request must be retried: %v", err)
	}
	f.throttle = 100
	_, err := graphSrc(t, f).ReadRows(context.Background(), 2, 3)
	if err == nil || !strings.Contains(err.Error(), "429") {
		t.Fatalf("got %v, want the 429 after the retries run out", err)
	}
}

func TestGraph_MissingSettingsNameTheVariables(t *testing.T) {
	_, err := Open(Config{Path: "https://contoso.sharepoint.com/x.xlsx", Sheet: "Data", Env: env()})
	if err == nil || !strings.Contains(err.Error(), "MS_TENANT_ID") {
		t.Fatalf("got %v", err)
	}
}

// The saved sign-in is used when no ready access token is given, and an expired access token is
// renewed from it.
func TestGraph_UsesTheSavedSignIn(t *testing.T) {
	f := newFakeGraph(t, itemsGrid())
	var refreshes int
	login := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_ = r.ParseForm()
		refreshes++
		if r.Form.Get("refresh_token") != "saved-refresh" {
			w.WriteHeader(http.StatusBadRequest)
			_, _ = w.Write([]byte(`{"error":"invalid_grant","error_description":"gone"}`))
			return
		}
		_, _ = w.Write([]byte(`{"access_token":"tok","refresh_token":"next-refresh","expires_in":3600}`))
	}))
	t.Cleanup(login.Close)

	db := filepath.Join(t.TempDir(), "tokens.db")
	store, err := msauth.OpenStore(db)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { store.Close() })
	_ = store.Put(context.Background(), msauth.Token{Name: "default", Tenant: "t1", ClientID: "app", Refresh: "saved-refresh", Expires: time.Now().Add(-time.Hour)})

	retryWait = func(context.Context, time.Duration) error { return nil }
	cfg := Config{
		Path: "https://contoso.sharepoint.com/x.xlsx", Sheet: "Data", GraphBase: f.srv.URL, LoginBase: login.URL,
		Env: env("MS_TENANT_ID", "t1", "MS_CLIENT_ID", "app", "SHEET_SERVICE_DB", db),
	}
	src, err := Open(cfg)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := src.ReadRows(context.Background(), 2, 3); err != nil {
		t.Fatal(err)
	}
	if refreshes != 1 {
		t.Fatalf("refreshed %d times, want 1 (later calls reuse the renewed token)", refreshes)
	}
	saved, _ := store.Get(context.Background(), "default")
	if saved.Refresh != "next-refresh" {
		t.Fatalf("rotated refresh token was not saved: %+v", saved)
	}

	// A revoked sign-in reaches the caller as ErrLoginRequired.
	_ = store.Put(context.Background(), msauth.Token{Name: "other", Tenant: "t1", ClientID: "app", Refresh: "dead", Expires: time.Now().Add(-time.Hour)})
	cfg.Connection = "other"
	src, _ = Open(cfg)
	if _, err := src.ReadRows(context.Background(), 2, 3); !errors.Is(err, msauth.ErrLoginRequired) {
		t.Fatalf("got %v, want ErrLoginRequired", err)
	}
}
