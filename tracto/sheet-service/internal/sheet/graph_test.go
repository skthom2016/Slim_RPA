package sheet

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
)

// fakeGraph imitates the parts of Microsoft Graph the connector uses: the app-only token
// endpoint, the share lookup, the download redirect and the content upload.
type fakeGraph struct {
	srv *httptest.Server

	mu        sync.Mutex
	file      []byte
	version   int
	downloads int
	uploads   int
	ignoreIf  bool // behave like a server that does not honour If-Match
}

func (f *fakeGraph) etag() string { return fmt.Sprintf(`"tag-%d"`, f.version) }

// replace simulates somebody else saving a new version of the file.
func (f *fakeGraph) replace(data []byte) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.file = data
	f.version++
}

func newFakeGraph(t *testing.T, workbook string) *fakeGraph {
	t.Helper()
	data, err := os.ReadFile(workbook)
	if err != nil {
		t.Fatal(err)
	}
	f := &fakeGraph{file: data, version: 1}
	mux := http.NewServeMux()
	mux.HandleFunc("/tenant-1/oauth2/v2.0/token", func(w http.ResponseWriter, r *http.Request) {
		_ = r.ParseForm()
		if r.Form.Get("grant_type") != "client_credentials" || r.Form.Get("client_secret") != "secret" ||
			r.Form.Get("scope") != "https://graph.microsoft.com/.default" {
			http.Error(w, `{"error_description":"bad request"}`, http.StatusBadRequest)
			return
		}
		_, _ = w.Write([]byte(`{"access_token":"app-token","expires_in":3600}`))
	})
	authed := func(h http.HandlerFunc) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			if r.Header.Get("Authorization") != "Bearer app-token" {
				http.Error(w, "unauthorized", http.StatusUnauthorized)
				return
			}
			h(w, r)
		}
	}
	mux.HandleFunc("/v1.0/shares/", authed(func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		defer f.mu.Unlock()
		switch {
		case strings.HasSuffix(r.URL.Path, "/driveItem"):
			_ = json.NewEncoder(w).Encode(map[string]any{
				"id": "ITEM1", "eTag": f.etag(), "size": len(f.file),
				"parentReference": map[string]string{"driveId": "DRIVE1"},
			})
		case strings.HasSuffix(r.URL.Path, "/driveItem/content"):
			f.downloads++
			http.Redirect(w, r, "/blob", http.StatusFound) // real Graph redirects to a pre-authenticated URL
		default:
			http.NotFound(w, r)
		}
	}))
	mux.HandleFunc("/blob", func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		defer f.mu.Unlock()
		if r.Header.Get("Authorization") != "" {
			http.Error(w, "the pre-authenticated URL must not receive the bearer token", http.StatusBadRequest)
			return
		}
		_, _ = w.Write(f.file)
	})
	mux.HandleFunc("/v1.0/drives/DRIVE1/items/ITEM1/content", authed(func(w http.ResponseWriter, r *http.Request) {
		f.mu.Lock()
		defer f.mu.Unlock()
		if !f.ignoreIf && r.Header.Get("If-Match") != f.etag() {
			http.Error(w, "precondition failed", http.StatusPreconditionFailed)
			return
		}
		body, _ := io.ReadAll(r.Body)
		f.file = body
		f.version++
		f.uploads++
		_ = json.NewEncoder(w).Encode(map[string]string{"eTag": f.etag()})
	}))
	f.srv = httptest.NewServer(mux)
	t.Cleanup(f.srv.Close)
	return f
}

func graphConfig(f *fakeGraph, work string) Config {
	return Config{
		Path:      "https://contoso.sharepoint.com/sites/team/Shared%20Documents/" + filepath.Base(work) + ".xlsx",
		Sheet:     "Data",
		Env:       env("MS_TENANT_ID", "tenant-1", "MS_CLIENT_ID", "app", "MS_CLIENT_SECRET", "secret"),
		GraphBase: f.srv.URL,
		LoginBase: f.srv.URL,
		WorkDir:   work,
	}
}

func cloudRow(t *testing.T, f *fakeGraph, row int, col string) string {
	t.Helper()
	f.mu.Lock()
	data := append([]byte(nil), f.file...)
	f.mu.Unlock()
	tmp := filepath.Join(t.TempDir(), "check.xlsx")
	if err := os.WriteFile(tmp, data, 0o600); err != nil {
		t.Fatal(err)
	}
	b, err := (&xlsxSource{cfg: Config{Path: tmp, Sheet: "Data", HeaderRow: 1}}).ReadRows(context.Background(), 2, 100)
	if err != nil {
		t.Fatal(err)
	}
	for _, r := range b.Rows {
		if r.Number == row {
			return r.Values[col]
		}
	}
	return ""
}

func TestGraph_ReadDownloadsOnceAndWriteUploadsTheChange(t *testing.T) {
	f := newFakeGraph(t, small(t))
	src, err := Open(graphConfig(f, t.TempDir()))
	if err != nil {
		t.Fatal(err)
	}
	ctx := context.Background()

	b, err := src.ReadRows(ctx, 2, 100)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 3 || b.Rows[1].Number != 3 {
		t.Fatalf("got %+v", b.Rows)
	}
	if _, err := src.ReadRows(ctx, 2, 3); err != nil { // a second batch of the same file
		t.Fatal(err)
	}
	if f.downloads != 1 {
		t.Fatalf("file downloaded %d times, want 1", f.downloads)
	}

	err = src.Write(ctx, []CellUpdate{
		{Row: 3, Column: "Approved_by_Finance", Value: "Approved"},
		{Row: 3, Column: "Bot_Status", Value: "SUCCESS"},
	})
	if err != nil {
		t.Fatal(err)
	}
	if f.uploads != 1 {
		t.Fatalf("uploads = %d, want 1", f.uploads)
	}
	if got := cloudRow(t, f, 3, "Bot_Status"); got != "SUCCESS" {
		t.Fatalf("cloud copy not updated: %q", got)
	}

	// The upload changed the cloud version; our own next read must not download again.
	if _, err := src.ReadRows(ctx, 2, 100); err != nil {
		t.Fatal(err)
	}
	if f.downloads != 1 {
		t.Fatalf("file downloaded %d times after own upload, want 1", f.downloads)
	}
}

func TestGraph_WriteIsRefusedWhenTheFileChangedAfterTheRead(t *testing.T) {
	f := newFakeGraph(t, small(t))
	src, _ := Open(graphConfig(f, t.TempDir()))
	ctx := context.Background()
	if _, err := src.ReadRows(ctx, 2, 100); err != nil {
		t.Fatal(err)
	}

	changed, _ := os.ReadFile(small(t))
	f.replace(changed) // somebody saves a new version

	err := src.Write(ctx, []CellUpdate{{Row: 3, Column: "Bot_Status", Value: "SUCCESS"}})
	if !errors.Is(err, ErrConflict) {
		t.Fatalf("got %v, want ErrConflict", err)
	}
	if f.uploads != 0 {
		t.Fatal("nothing may be uploaded after a conflict")
	}

	// A fresh read picks up the new version, after which writing works again.
	if _, err := src.ReadRows(ctx, 2, 100); err != nil {
		t.Fatal(err)
	}
	if f.downloads != 2 {
		t.Fatalf("downloads = %d, want 2 after the file changed", f.downloads)
	}
	if err := src.Write(ctx, []CellUpdate{{Row: 3, Column: "Bot_Status", Value: "SUCCESS"}}); err != nil {
		t.Fatal(err)
	}
}

func TestGraph_ServerRejectingIfMatchIsReportedAsConflict(t *testing.T) {
	f := newFakeGraph(t, small(t))
	src, _ := Open(graphConfig(f, t.TempDir()))
	ctx := context.Background()
	if _, err := src.ReadRows(ctx, 2, 100); err != nil {
		t.Fatal(err)
	}
	// The version tag check passes, then the file changes before the upload arrives.
	f.mu.Lock()
	f.ignoreIf = false
	f.mu.Unlock()
	hook := &racingUpload{f: f}
	src.(*graphSource).client.Transport = hook

	err := src.Write(ctx, []CellUpdate{{Row: 3, Column: "Bot_Status", Value: "SUCCESS"}})
	if !errors.Is(err, ErrConflict) {
		t.Fatalf("got %v, want ErrConflict", err)
	}
}

// racingUpload bumps the cloud version just before the PUT reaches the server.
type racingUpload struct{ f *fakeGraph }

func (r *racingUpload) RoundTrip(req *http.Request) (*http.Response, error) {
	if req.Method == http.MethodPut {
		r.f.mu.Lock()
		r.f.version++
		r.f.mu.Unlock()
	}
	return http.DefaultTransport.RoundTrip(req)
}

func TestGraph_BadAppCredentialsAreReported(t *testing.T) {
	f := newFakeGraph(t, small(t))
	cfg := graphConfig(f, t.TempDir())
	cfg.Env = env("MS_TENANT_ID", "tenant-1", "MS_CLIENT_ID", "app", "MS_CLIENT_SECRET", "wrong")
	src, err := Open(cfg)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := src.ReadRows(context.Background(), 2, 10); err == nil || !strings.Contains(err.Error(), "token request failed") {
		t.Fatalf("expected a token error, got %v", err)
	}
}

func TestGraph_AccessTokenFromEnvironmentIsUsedAsIs(t *testing.T) {
	f := newFakeGraph(t, small(t))
	cfg := graphConfig(f, t.TempDir())
	cfg.Env = env("MS_ACCESS_TOKEN", "app-token")
	src, err := Open(cfg)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := src.ReadRows(context.Background(), 2, 10); err != nil {
		t.Fatal(err)
	}
}
