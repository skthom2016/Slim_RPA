package sheet

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

// The Excel range API in Microsoft Graph accepts only signed-in users, so an unattended service
// cannot use it. This connector works on the file instead: it downloads a copy of the workbook,
// reads and edits the copy with the local xlsx code, and uploads the result back. Graph's simple
// upload accepts files up to 250 MB.
const graphMaxUpload = 250 << 20

type graphSource struct {
	cfg    Config
	tokens tokenSource
	client *http.Client
	base   string
	share  string // encoded sharing URL: "u!" + base64url(path)
	entry  *graphEntry
}

// graphEntry is the downloaded copy of one remote file. It is kept between requests so that
// reading a large sheet batch by batch downloads the file once, and so that a write can tell
// whether the file changed after it was read.
type graphEntry struct {
	mu      sync.Mutex
	local   string
	etag    string
	driveID string
	itemID  string
}

var graphEntries sync.Map // share id -> *graphEntry

type driveItemMeta struct {
	ID              string `json:"id"`
	ETag            string `json:"eTag"`
	Size            int64  `json:"size"`
	ParentReference struct {
		DriveID string `json:"driveId"`
	} `json:"parentReference"`
}

func newGraph(cfg Config) (Source, error) {
	if !strings.HasPrefix(strings.ToLower(cfg.Path), "https://") {
		return nil, fmt.Errorf("graph source requires an https OneDrive or SharePoint file URL in path")
	}
	tokens, err := graphTokens(cfg)
	if err != nil {
		return nil, err
	}
	base := cfg.GraphBase
	if base == "" {
		base = "https://graph.microsoft.com"
	}
	share := "u!" + base64.RawURLEncoding.EncodeToString([]byte(cfg.Path))
	e, _ := graphEntries.LoadOrStore(share, &graphEntry{})
	return &graphSource{cfg: cfg, tokens: tokens, client: newGraphClient(), base: base, share: share, entry: e.(*graphEntry)}, nil
}

// newGraphClient never forwards the bearer token on a redirect: file downloads redirect to a
// pre-authenticated URL that must not receive it.
func newGraphClient() *http.Client {
	return &http.Client{CheckRedirect: func(req *http.Request, via []*http.Request) error {
		if len(via) >= 10 {
			return fmt.Errorf("stopped after 10 redirects")
		}
		req.Header.Del("Authorization")
		return nil
	}}
}

func (g *graphSource) ReadRows(ctx context.Context, start, end int) (*Batch, error) {
	if err := checkRange(g.cfg.HeaderRow, start, end); err != nil {
		return nil, err
	}
	g.entry.mu.Lock()
	defer g.entry.mu.Unlock()
	if err := g.refresh(ctx); err != nil {
		return nil, err
	}
	return g.localSource().ReadRows(ctx, start, end)
}

// Write edits the downloaded copy and uploads it. It fails with ErrConflict when the file in the
// cloud is no longer the version the rows were read from, so a stale row number can never be
// written into a file that has changed.
func (g *graphSource) Write(ctx context.Context, updates []CellUpdate) error {
	if len(updates) == 0 {
		return nil
	}
	e := g.entry
	e.mu.Lock()
	defer e.mu.Unlock()

	meta, err := g.meta(ctx)
	if err != nil {
		return err
	}
	if e.local == "" || e.etag == "" || e.etag != meta.ETag {
		return ErrConflict
	}
	if err := g.localSource().Write(ctx, updates); err != nil {
		return err
	}

	data, err := os.ReadFile(e.local)
	if err != nil {
		e.etag = ""
		return err
	}
	if len(data) > graphMaxUpload {
		e.etag = ""
		return fmt.Errorf("workbook is %d MB; Graph simple upload is limited to 250 MB", len(data)>>20)
	}

	newTag, err := g.upload(ctx, e, data)
	if err != nil {
		e.etag = "" // the local copy is ahead of the cloud; force a fresh download
		return err
	}
	e.etag = newTag
	return nil
}

func (g *graphSource) localSource() Source {
	return &xlsxSource{cfg: Config{Path: g.entry.local, Sheet: g.cfg.Sheet, HeaderRow: g.cfg.HeaderRow}}
}

// refresh makes sure the local copy matches the cloud version, downloading only when it differs.
func (g *graphSource) refresh(ctx context.Context) error {
	e := g.entry
	meta, err := g.meta(ctx)
	if err != nil {
		return err
	}
	if e.local != "" && e.etag == meta.ETag {
		if _, err := os.Stat(e.local); err == nil {
			return nil
		}
	}

	dir := g.cfg.WorkDir
	if dir == "" {
		dir = filepath.Join(os.TempDir(), "sheet-service")
	}
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return err
	}
	sum := sha256.Sum256([]byte(g.share))
	local := filepath.Join(dir, hex.EncodeToString(sum[:8])+".xlsx")
	if err := g.download(ctx, local); err != nil {
		return err
	}
	e.local, e.etag, e.driveID, e.itemID = local, meta.ETag, meta.ParentReference.DriveID, meta.ID
	return nil
}

func (g *graphSource) meta(ctx context.Context) (*driveItemMeta, error) {
	endpoint := fmt.Sprintf("%s/v1.0/shares/%s/driveItem?$select=id,eTag,size,parentReference", g.base, g.share)
	resp, err := g.do(ctx, http.MethodGet, endpoint, nil, nil)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var m driveItemMeta
	if err := json.NewDecoder(resp.Body).Decode(&m); err != nil {
		return nil, fmt.Errorf("graph file info: %w", err)
	}
	if m.ID == "" || m.ParentReference.DriveID == "" {
		return nil, fmt.Errorf("graph file info is missing the item or drive id; is the path a file?")
	}
	return &m, nil
}

func (g *graphSource) download(ctx context.Context, local string) error {
	endpoint := fmt.Sprintf("%s/v1.0/shares/%s/driveItem/content", g.base, g.share)
	resp, err := g.do(ctx, http.MethodGet, endpoint, nil, nil) // the client follows the 302 to the download URL
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	tmp := local + ".part"
	f, err := os.Create(tmp)
	if err != nil {
		return err
	}
	if _, err := io.Copy(f, resp.Body); err != nil {
		f.Close()
		os.Remove(tmp)
		return fmt.Errorf("download: %w", err)
	}
	if err := f.Close(); err != nil {
		return err
	}
	return os.Rename(tmp, local)
}

func (g *graphSource) upload(ctx context.Context, e *graphEntry, data []byte) (string, error) {
	endpoint := fmt.Sprintf("%s/v1.0/drives/%s/items/%s/content", g.base, url.PathEscape(e.driveID), url.PathEscape(e.itemID))
	headers := map[string]string{
		"Content-Type": "application/octet-stream",
		"If-Match":     e.etag,
	}
	resp, err := g.do(ctx, http.MethodPut, endpoint, data, headers)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	var item struct {
		ETag string `json:"eTag"`
	}
	_ = json.NewDecoder(resp.Body).Decode(&item)
	return item.ETag, nil
}

// do sends an authenticated request and returns the response for 2xx statuses.
// A 412 (precondition failed) is reported as ErrConflict.
func (g *graphSource) do(ctx context.Context, method, endpoint string, body []byte, headers map[string]string) (*http.Response, error) {
	token, err := g.tokens.Token(ctx)
	if err != nil {
		return nil, err
	}
	req, err := http.NewRequestWithContext(ctx, method, endpoint, bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Authorization", "Bearer "+token)
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	resp, err := g.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("graph api: %w", err)
	}
	if resp.StatusCode/100 == 2 {
		return resp, nil
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusPreconditionFailed {
		return nil, ErrConflict
	}
	raw, _ := io.ReadAll(io.LimitReader(resp.Body, 4096))
	return nil, fmt.Errorf("graph api %d: %s", resp.StatusCode, strings.TrimSpace(string(raw)))
}
