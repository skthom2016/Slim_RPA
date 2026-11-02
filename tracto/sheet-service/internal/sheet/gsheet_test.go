package sheet

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// fakeSheets serves a sheet of header + data rows and records batchUpdate bodies.
func fakeSheets(t *testing.T, rows [][]any) (*httptest.Server, *[]string) {
	t.Helper()
	var writes []string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Authorization") != "Bearer tok" {
			http.Error(w, "unauthorized", http.StatusUnauthorized)
			return
		}
		path := r.URL.EscapedPath()
		switch {
		case r.Method == http.MethodPost && strings.HasSuffix(path, "/values:batchUpdate"):
			b, _ := io.ReadAll(r.Body)
			writes = append(writes, string(b))
			_, _ = w.Write([]byte(`{}`))
		case r.Method == http.MethodGet && strings.Contains(path, "/values/"):
			// range looks like 'My Sheet'!5:104
			rng := r.URL.Path[strings.LastIndex(r.URL.Path, "!")+1:]
			var from, to int
			parts := strings.Split(rng, ":")
			from, to = atoi(parts[0]), atoi(parts[1])
			var out [][]any
			for n := from; n <= to && n <= len(rows); n++ {
				out = append(out, rows[n-1])
			}
			_ = json.NewEncoder(w).Encode(map[string]any{"values": out})
		default:
			http.NotFound(w, r)
		}
	}))
	t.Cleanup(srv.Close)
	return srv, &writes
}

func atoi(s string) int {
	n := 0
	for _, c := range s {
		n = n*10 + int(c-'0')
	}
	return n
}

func gsource(t *testing.T, srv *httptest.Server) Source {
	t.Helper()
	src, err := Open(Config{Path: "https://docs.google.com/spreadsheets/d/abc/edit#gid=0", Sheet: "My Sheet", Env: env("GOOGLE_ACCESS_TOKEN", "tok"), GoogleAPIBase: srv.URL})
	if err != nil {
		t.Fatal(err)
	}
	return src
}

func TestGSheet_ReadRowsRequestsOnlyTheRange(t *testing.T) {
	srv, _ := fakeSheets(t, [][]any{
		{"Style_ID", "Approved_by_Finance"},
		{"1", "Approved"},
		{"2"}, // trailing empty cells are omitted by the API
		{"3", ""},
	})
	b, err := gsource(t, srv).ReadRows(context.Background(), 3, 4)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 2 || b.Rows[0].Number != 3 || b.Rows[1].Number != 4 {
		t.Fatalf("got %+v, want rows 3 and 4", b.Rows)
	}
	if b.Rows[0].Values["Approved_by_Finance"] != "" || b.Rows[1].Values["Style_ID"] != "3" {
		t.Fatalf("values wrong: %+v", b.Rows)
	}
	if b.EndOfData {
		t.Fatal("row 4 is the last row of the range, so more data may follow")
	}
}

func TestGSheet_EndOfDataWhenRangeRunsPastTheLastRow(t *testing.T) {
	srv, _ := fakeSheets(t, [][]any{{"Style_ID"}, {"1"}, {"2"}})
	b, err := gsource(t, srv).ReadRows(context.Background(), 2, 1001)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 2 || !b.EndOfData || b.LastRow != 3 {
		t.Fatalf("rows=%d endOfData=%v lastRow=%d", len(b.Rows), b.EndOfData, b.LastRow)
	}
}

func TestGSheet_WriteUsesRowNumberAndColumnLetter(t *testing.T) {
	srv, writes := fakeSheets(t, [][]any{{"Style_ID", "Approved_by_Finance", "Bot_Status"}})
	err := gsource(t, srv).Write(context.Background(), []CellUpdate{{Row: 7, Column: "Bot_Status", Value: "SUCCESS"}})
	if err != nil {
		t.Fatal(err)
	}
	if len(*writes) != 1 || !strings.Contains((*writes)[0], `'My Sheet'!C7`) || !strings.Contains((*writes)[0], `SUCCESS`) {
		t.Fatalf("unexpected write body: %v", *writes)
	}
}

func TestGSheet_RejectsMissingCredentials(t *testing.T) {
	_, err := Open(Config{Type: TypeGSheet, Path: "abc", Sheet: "S", Env: env()})
	if err == nil || !strings.Contains(err.Error(), "GOOGLE_APPLICATION_CREDENTIALS") {
		t.Fatalf("expected an error naming the env vars, got %v", err)
	}
}

func TestGSheet_ApiErrorsAreReported(t *testing.T) {
	srv, _ := fakeSheets(t, nil)
	src, _ := Open(Config{Path: "abc", Type: TypeGSheet, Sheet: "S", Env: env("GOOGLE_ACCESS_TOKEN", "wrong"), GoogleAPIBase: srv.URL})
	if _, err := src.ReadRows(context.Background(), 2, 3); err == nil || !strings.Contains(err.Error(), "401") {
		t.Fatalf("expected a 401 error, got %v", err)
	}
}

// env returns an environment lookup over the given key/value pairs.
func env(kv ...string) func(string) string {
	m := map[string]string{}
	for i := 0; i+1 < len(kv); i += 2 {
		m[kv[i]] = kv[i+1]
	}
	return func(k string) string { return m[k] }
}

func TestSpreadsheetID(t *testing.T) {
	cases := map[string]string{
		"https://docs.google.com/spreadsheets/d/1AbC_-9/edit#gid=0": "1AbC_-9",
		"1AbC_-9":           "1AbC_-9",
		`C:\data\book.xlsx`: "",
		"":                  "",
	}
	for in, want := range cases {
		if got := spreadsheetID(in); got != want {
			t.Errorf("spreadsheetID(%q) = %q, want %q", in, got, want)
		}
	}
}
