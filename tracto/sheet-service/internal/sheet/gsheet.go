package sheet

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"

	"github.com/xuri/excelize/v2"
)

type gsheetSource struct {
	id     string
	base   string
	cfg    Config
	tokens tokenSource
	client *http.Client
}

// Credentials come from the environment:
//
//	GOOGLE_APPLICATION_CREDENTIALS  path to a service-account JSON key, or
//	GOOGLE_ACCESS_TOKEN             a ready OAuth access token
func newGSheet(cfg Config) (Source, error) {
	id := spreadsheetID(cfg.Path)
	if id == "" {
		return nil, fmt.Errorf("gsheet source requires a Google Sheets URL or spreadsheet ID in path")
	}
	if strings.TrimSpace(cfg.Sheet) == "" {
		return nil, fmt.Errorf("gsheet source requires sheet")
	}
	if cfg.HeaderRow <= 0 {
		cfg.HeaderRow = 1
	}
	base := cfg.GoogleAPIBase
	if base == "" {
		base = "https://sheets.googleapis.com"
	}

	var tokens tokenSource
	switch {
	case cfg.getenv("GOOGLE_ACCESS_TOKEN") != "":
		tokens = staticToken(cfg.getenv("GOOGLE_ACCESS_TOKEN"))
	case cfg.getenv("GOOGLE_APPLICATION_CREDENTIALS") != "":
		t, err := newServiceAccountToken(cfg.getenv("GOOGLE_APPLICATION_CREDENTIALS"))
		if err != nil {
			return nil, err
		}
		tokens = t
	default:
		return nil, fmt.Errorf("google credentials missing: set GOOGLE_APPLICATION_CREDENTIALS or GOOGLE_ACCESS_TOKEN")
	}
	return &gsheetSource{cfg: cfg, id: id, base: base, tokens: tokens, client: http.DefaultClient}, nil
}

// spreadsheetID accepts a Google Sheets URL or a bare spreadsheet ID.
func spreadsheetID(path string) string {
	path = strings.TrimSpace(path)
	if m := googleSheetURL.FindStringSubmatch(path); m != nil {
		return m[1]
	}
	if path != "" && !strings.ContainsAny(path, `/\: `) {
		return path
	}
	return ""
}

// ReadRows asks the API for only the requested rows, so cost depends on the batch, not the sheet.
func (g *gsheetSource) ReadRows(ctx context.Context, start, end int) (*Batch, error) {
	if err := checkRange(g.cfg.HeaderRow, start, end); err != nil {
		return nil, err
	}
	headerRows, err := g.values(ctx, fmt.Sprintf("%d:%d", g.cfg.HeaderRow, g.cfg.HeaderRow))
	if err != nil {
		return nil, err
	}
	if len(headerRows) == 0 {
		return nil, fmt.Errorf("sheet %q has no header row %d", g.cfg.Sheet, g.cfg.HeaderRow)
	}
	data, err := g.values(ctx, fmt.Sprintf("%d:%d", start, end))
	if err != nil {
		return nil, err
	}
	return buildBatch(headerRows[0], start, end, data), nil
}

func (g *gsheetSource) Write(ctx context.Context, updates []CellUpdate) error {
	if len(updates) == 0 {
		return nil
	}
	headerRows, err := g.values(ctx, fmt.Sprintf("%d:%d", g.cfg.HeaderRow, g.cfg.HeaderRow))
	if err != nil {
		return err
	}
	if len(headerRows) == 0 {
		return fmt.Errorf("sheet %q has no header row %d", g.cfg.Sheet, g.cfg.HeaderRow)
	}
	cols := columnIndex(headerRows[0])

	type valueRange struct {
		Range  string     `json:"range"`
		Values [][]string `json:"values"`
	}
	body := struct {
		ValueInputOption string       `json:"valueInputOption"`
		Data             []valueRange `json:"data"`
	}{ValueInputOption: "RAW"}

	for _, u := range updates {
		c, ok := cols[u.Column]
		if !ok {
			return fmt.Errorf("column %q not found in header", u.Column)
		}
		if u.Row <= g.cfg.HeaderRow {
			return fmt.Errorf("row %d is not a data row", u.Row)
		}
		name, err := excelize.ColumnNumberToName(c)
		if err != nil {
			return err
		}
		body.Data = append(body.Data, valueRange{
			Range:  fmt.Sprintf("%s!%s%d", quoteSheet(g.cfg.Sheet), name, u.Row),
			Values: [][]string{{u.Value}},
		})
	}
	payload, _ := json.Marshal(body)
	endpoint := fmt.Sprintf("%s/v4/spreadsheets/%s/values:batchUpdate", g.base, url.PathEscape(g.id))
	_, err = g.do(ctx, http.MethodPost, endpoint, payload)
	return err
}

// values fetches whole rows (e.g. "5:104") and returns each row's cells as strings.
// The API omits trailing empty rows and cells, so rows can be shorter than requested.
func (g *gsheetSource) values(ctx context.Context, rowRange string) ([][]string, error) {
	rng := fmt.Sprintf("%s!%s", quoteSheet(g.cfg.Sheet), rowRange)
	endpoint := fmt.Sprintf("%s/v4/spreadsheets/%s/values/%s?majorDimension=ROWS",
		g.base, url.PathEscape(g.id), url.PathEscape(rng))
	raw, err := g.do(ctx, http.MethodGet, endpoint, nil)
	if err != nil {
		return nil, err
	}
	var out struct {
		Values [][]any `json:"values"`
	}
	if err := json.Unmarshal(raw, &out); err != nil {
		return nil, fmt.Errorf("parse values: %w", err)
	}
	rows := make([][]string, len(out.Values))
	for i, cells := range out.Values {
		rows[i] = make([]string, len(cells))
		for c, v := range cells {
			if v != nil {
				rows[i][c] = fmt.Sprint(v)
			}
		}
	}
	return rows, nil
}

func (g *gsheetSource) do(ctx context.Context, method, endpoint string, body []byte) ([]byte, error) {
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
	resp, err := g.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("sheets api: %w", err)
	}
	defer resp.Body.Close()
	raw, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	if resp.StatusCode/100 != 2 {
		return nil, fmt.Errorf("sheets api %d: %s", resp.StatusCode, strings.TrimSpace(string(raw)))
	}
	return raw, nil
}

// quoteSheet wraps a sheet name for A1 notation, escaping embedded quotes.
func quoteSheet(name string) string {
	return "'" + strings.ReplaceAll(name, "'", "''") + "'"
}
