package sheet

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/xuri/excelize/v2"
)

// OneDrive and SharePoint workbooks are read and written through the Excel range API of Microsoft
// Graph. Only the requested rows are fetched and only the cells sent are written; the file is never
// downloaded or uploaded as a whole. The range API accepts signed-in users only, so the token comes
// from the saved sign-in (see package msauth).
const (
	maxRowsPerPatch = 500 // rows written in one PATCH
	maxAttempts     = 4   // tries for a request that is throttled (429) or temporarily unavailable
)

type graphSource struct {
	cfg    Config
	tokens tokenSource
	client *http.Client
	base   string
	share  string // encoded sharing URL: "u!" + base64url(path)
}

type graphItem struct{ drive, item string }

var graphItems sync.Map // share id -> graphItem

// retryWait is replaced in tests.
var retryWait = func(ctx context.Context, d time.Duration) error {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-t.C:
		return nil
	}
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
	return &graphSource{cfg: cfg, tokens: tokens, client: http.DefaultClient, base: base, share: share}, nil
}

// worksheet resolves the file once per process and returns the URL of the worksheet resource.
func (g *graphSource) worksheet(ctx context.Context) (string, error) {
	it, ok := graphItems.Load(g.share)
	if !ok {
		raw, err := g.do(ctx, http.MethodGet, fmt.Sprintf("%s/v1.0/shares/%s/driveItem?$select=id,parentReference", g.base, g.share), nil, "")
		if err != nil {
			return "", err
		}
		var m struct {
			ID              string `json:"id"`
			ParentReference struct {
				DriveID string `json:"driveId"`
			} `json:"parentReference"`
		}
		if err := json.Unmarshal(raw, &m); err != nil || m.ID == "" || m.ParentReference.DriveID == "" {
			return "", fmt.Errorf("graph file info is missing the item or drive id; is the path a file?")
		}
		it = graphItem{drive: m.ParentReference.DriveID, item: m.ID}
		graphItems.Store(g.share, it)
	}
	i := it.(graphItem)
	return fmt.Sprintf("%s/v1.0/drives/%s/items/%s/workbook", g.base, url.PathEscape(i.drive), url.PathEscape(i.item)), nil
}

func (g *graphSource) sheetURL(workbook string) string {
	return workbook + "/worksheets/" + url.PathEscape(g.cfg.Sheet)
}

// bounds returns the last used column and row of the worksheet.
func (g *graphSource) bounds(ctx context.Context, ws string) (lastCol, lastRow int, err error) {
	raw, err := g.do(ctx, http.MethodGet, ws+"/usedRange(valuesOnly=true)?$select=address", nil, "")
	if err != nil {
		return 0, 0, err
	}
	var r struct {
		Address string `json:"address"`
	}
	if err := json.Unmarshal(raw, &r); err != nil {
		return 0, 0, fmt.Errorf("parse used range: %w", err)
	}
	addr := r.Address
	if i := strings.LastIndex(addr, "!"); i >= 0 {
		addr = addr[i+1:]
	}
	if i := strings.LastIndex(addr, ":"); i >= 0 {
		addr = addr[i+1:]
	}
	col, row, err := excelize.CellNameToCoordinates(strings.ReplaceAll(addr, "$", ""))
	if err != nil {
		return 0, 0, fmt.Errorf("unexpected used range address %q", r.Address)
	}
	return col, row, nil
}

// text reads a rectangle as the cells' displayed text.
func (g *graphSource) text(ctx context.Context, ws, address string) ([][]string, error) {
	raw, err := g.do(ctx, http.MethodGet, fmt.Sprintf("%s/range(address='%s')?$select=text", ws, address), nil, "")
	if err != nil {
		return nil, err
	}
	var r struct {
		Text [][]any `json:"text"`
	}
	if err := json.Unmarshal(raw, &r); err != nil {
		return nil, fmt.Errorf("parse range: %w", err)
	}
	out := make([][]string, len(r.Text))
	for i, cells := range r.Text {
		out[i] = make([]string, len(cells))
		for c, v := range cells {
			if v != nil {
				out[i][c] = fmt.Sprint(v)
			}
		}
	}
	return out, nil
}

func cellAddr(col, row int) string {
	name, _ := excelize.ColumnNumberToName(col)
	return name + strconv.Itoa(row)
}

func (g *graphSource) header(ctx context.Context, ws string, lastCol int) ([]string, error) {
	h := g.cfg.HeaderRow
	rows, err := g.text(ctx, ws, cellAddr(1, h)+":"+cellAddr(lastCol, h))
	if err != nil {
		return nil, err
	}
	if len(rows) == 0 || strings.Join(rows[0], "") == "" {
		return nil, fmt.Errorf("sheet %q has no header row %d", g.cfg.Sheet, h)
	}
	return rows[0], nil
}

func (g *graphSource) ReadRows(ctx context.Context, start, end int) (*Batch, error) {
	if err := checkRange(g.cfg.HeaderRow, start, end); err != nil {
		return nil, err
	}
	wb, err := g.worksheet(ctx)
	if err != nil {
		return nil, err
	}
	ws := g.sheetURL(wb)
	lastCol, lastRow, err := g.bounds(ctx, ws)
	if err != nil {
		return nil, err
	}
	header, err := g.header(ctx, ws, lastCol)
	if err != nil {
		return nil, err
	}
	if start > lastRow {
		return buildBatch(header, start, end, nil), nil
	}
	data, err := g.text(ctx, ws, cellAddr(1, start)+":"+cellAddr(lastCol, min(end, lastRow)))
	if err != nil {
		return nil, err
	}
	return buildBatch(header, start, end, data), nil
}

// Write sets the cells with as few PATCH calls as possible: neighbouring rows are sent as one
// rectangle, and cells that are not being changed are sent as null, which Graph ignores. All input
// is validated before anything is written.
func (g *graphSource) Write(ctx context.Context, updates []CellUpdate) error {
	if len(updates) == 0 {
		return nil
	}
	wb, err := g.worksheet(ctx)
	if err != nil {
		return err
	}
	ws := g.sheetURL(wb)
	lastCol, _, err := g.bounds(ctx, ws)
	if err != nil {
		return err
	}
	header, err := g.header(ctx, ws, lastCol)
	if err != nil {
		return err
	}
	cols := columnIndex(header)

	cells := map[int]map[int]string{} // row -> column -> value
	for _, u := range updates {
		c, ok := cols[u.Column]
		if !ok {
			return fmt.Errorf("column %q not found in header", u.Column)
		}
		if u.Row <= g.cfg.HeaderRow {
			return fmt.Errorf("row %d is not a data row", u.Row)
		}
		if cells[u.Row] == nil {
			cells[u.Row] = map[int]string{}
		}
		cells[u.Row][c] = u.Value
	}
	rows := make([]int, 0, len(cells))
	for r := range cells {
		rows = append(rows, r)
	}
	sort.Ints(rows)

	// A workbook session lets Graph apply the calls to one open copy of the file. If one cannot be
	// created the calls still work, only slower.
	session := g.createSession(ctx, wb)
	defer g.closeSession(wb, session)

	for i := 0; i < len(rows); {
		j := i + 1
		for j < len(rows) && rows[j] == rows[j-1]+1 && j-i < maxRowsPerPatch {
			j++
		}
		run := rows[i:j]
		minC, maxC := 1<<30, 0
		for _, r := range run {
			for c := range cells[r] {
				minC, maxC = min(minC, c), max(maxC, c)
			}
		}
		matrix := make([][]any, len(run))
		for k, r := range run {
			matrix[k] = make([]any, maxC-minC+1)
			for c, v := range cells[r] {
				matrix[k][c-minC] = v
			}
		}
		body, _ := json.Marshal(map[string]any{"values": matrix})
		addr := cellAddr(minC, run[0]) + ":" + cellAddr(maxC, run[len(run)-1])
		if _, err := g.do(ctx, http.MethodPatch, fmt.Sprintf("%s/range(address='%s')", ws, addr), body, session); err != nil {
			return err
		}
		i = j
	}
	return nil
}

func (g *graphSource) createSession(ctx context.Context, wb string) string {
	raw, err := g.do(ctx, http.MethodPost, wb+"/createSession", []byte(`{"persistChanges":true}`), "")
	if err != nil {
		return ""
	}
	var s struct {
		ID string `json:"id"`
	}
	_ = json.Unmarshal(raw, &s)
	return s.ID
}

func (g *graphSource) closeSession(wb, session string) {
	if session == "" {
		return
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	_, _ = g.do(ctx, http.MethodPost, wb+"/closeSession", []byte(`{}`), session)
}

// do sends an authenticated request. A throttled request (429) or a temporarily unavailable
// service (503, 504) is retried after the delay Graph asks for in Retry-After.
func (g *graphSource) do(ctx context.Context, method, endpoint string, body []byte, session string) ([]byte, error) {
	for attempt := 1; ; attempt++ {
		token, err := g.tokens.Token(ctx)
		if err != nil {
			return nil, err
		}
		req, err := http.NewRequestWithContext(ctx, method, endpoint, bytes.NewReader(body))
		if err != nil {
			return nil, err
		}
		req.Header.Set("Authorization", "Bearer "+token)
		if body != nil {
			req.Header.Set("Content-Type", "application/json")
		}
		if session != "" {
			req.Header.Set("Workbook-Session-Id", session)
		}
		resp, err := g.client.Do(req)
		if err != nil {
			return nil, fmt.Errorf("graph api: %w", err)
		}
		raw, err := io.ReadAll(resp.Body)
		resp.Body.Close()
		if err != nil {
			return nil, err
		}
		if resp.StatusCode/100 == 2 {
			return raw, nil
		}
		retryable := resp.StatusCode == http.StatusTooManyRequests || resp.StatusCode == http.StatusServiceUnavailable || resp.StatusCode == http.StatusGatewayTimeout
		if retryable && attempt < maxAttempts {
			wait := time.Duration(1<<attempt) * time.Second
			if s, err := strconv.Atoi(resp.Header.Get("Retry-After")); err == nil && s >= 0 {
				wait = time.Duration(s) * time.Second
			}
			if err := retryWait(ctx, min(wait, time.Minute)); err != nil {
				return nil, err
			}
			continue
		}
		return nil, fmt.Errorf("graph api %d: %s", resp.StatusCode, strings.TrimSpace(string(raw[:min(len(raw), 2048)])))
	}
}
