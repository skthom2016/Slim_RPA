// Package api exposes the engine over HTTP. It is stateless: every request names its source,
// and row numbers are valid only for the read that produced them. A caller walks a large sheet
// by repeating /batch with the returned nextStartRow until endOfData is true.
package api

import (
	"encoding/json"
	"errors"
	"net/http"
	"sort"
	"strconv"
	"strings"

	"tracto/sheet-service/internal/engine"
	"tracto/sheet-service/internal/msauth"
	"tracto/sheet-service/internal/process"
	"tracto/sheet-service/internal/sheet"
)

const (
	defaultBatchSize = 1000
	maxBatchSize     = 5000
)

type BatchRequest struct {
	Source sheet.Config `json:"source"`
	// StartRow defaults to the row below the header. EndRow defaults to StartRow+999.
	StartRow int `json:"startRow"`
	EndRow   int `json:"endRow"`
	// Pending, when set, keeps only the rows that still need action.
	Pending *engine.Pending `json:"pending"`
}

type BatchResponse struct {
	Header       []string    `json:"header"`
	StartRow     int         `json:"startRow"`
	EndRow       int         `json:"endRow"`
	Scanned      int         `json:"scanned"` // non-empty rows read in the range
	Count        int         `json:"count"`   // rows returned after the pending filter
	Rows         []sheet.Row `json:"rows"`
	Text         string      `json:"text"` // rows as a tab-separated table, for display
	EndOfData    bool        `json:"endOfData"`
	NextStartRow int         `json:"nextStartRow"`
}

// CellRequest reads one cell: the value in Column on sheet row Row. Column is a header name.
// With Required set, an empty or missing cell is an error rather than an empty value, so a flow
// that must have the value stops instead of carrying on with nothing.
type CellRequest struct {
	Source   sheet.Config `json:"source"`
	Row      int          `json:"row"`
	Column   string       `json:"column"`
	Required bool         `json:"required"`
}

// CellResponse is flat on purpose: a script reads each field as ${name.value}, ${name.row} and
// so on, and only top-level scalars of a reply are reachable from a script.
type CellResponse struct {
	Row    int    `json:"row"`
	Column string `json:"column"`
	Value  string `json:"value"`
	Found  bool   `json:"found"` // false when the cell is empty or the row has no data
}

// RowUpdate sets several columns of one row.
type RowUpdate struct {
	Row    int               `json:"row"`
	Values map[string]string `json:"values"`
}

// UpdateRequest writes any number of rows, each with any number of columns, in one call.
// Rows and Updates may be combined; both are applied together.
type UpdateRequest struct {
	Source  sheet.Config       `json:"source"`
	Rows    []RowUpdate        `json:"rows"`
	Updates []sheet.CellUpdate `json:"updates"`
}

type UpdateResponse struct {
	Rows  int `json:"rows"`
	Cells int `json:"cells"`
}

// cells flattens a request into single-cell updates. Columns of a row are ordered by name so the
// result does not depend on map iteration order.
func (r UpdateRequest) cells() []sheet.CellUpdate {
	out := make([]sheet.CellUpdate, 0, len(r.Updates))
	for _, row := range r.Rows {
		cols := make([]string, 0, len(row.Values))
		for c := range row.Values {
			cols = append(cols, c)
		}
		sort.Strings(cols)
		for _, c := range cols {
			out = append(out, sheet.CellUpdate{Row: row.Row, Column: c, Value: row.Values[c]})
		}
	}
	return append(out, r.Updates...)
}

func (r UpdateRequest) rowCount(cells []sheet.CellUpdate) int {
	seen := map[int]bool{}
	for _, c := range cells {
		seen[c.Row] = true
	}
	return len(seen)
}

// ApplyRequest finds the first Limit pending rows at or after StartRow and sets the same Set
// values on each of them, in one call. It is the read-decide-write step of a typical flow.
type ApplyRequest struct {
	Source   sheet.Config      `json:"source"`
	StartRow int               `json:"startRow"`
	EndRow   int               `json:"endRow"`
	Pending  *engine.Pending   `json:"pending"`
	Limit    int               `json:"limit"`
	Set      map[string]string `json:"set"`
}

type ApplyResponse struct {
	Updated int         `json:"updated"`
	Rows    []sheet.Row `json:"rows"`
	Text    string      `json:"text"`
}

// text renders rows as tab-separated lines, headed by the sheet row number.
func text(header []string, rows []sheet.Row) string {
	var sb strings.Builder
	sb.WriteString("Row\t" + strings.Join(header, "\t") + "\r\n")
	for _, r := range rows {
		sb.WriteString(strconv.Itoa(r.Number))
		for _, h := range header {
			sb.WriteString("\t" + strings.NewReplacer("\t", " ", "\r", " ", "\n", " ").Replace(r.Values[h]))
		}
		sb.WriteString("\r\n")
	}
	return sb.String()
}

// New returns the stateless sheet routes only.
func New() http.Handler { return NewWithProcess(nil) }

// NewWithProcess adds the routes that run a child job for each row of a sheet. They are the one
// part of this service that keeps state, so they exist only when a Manager is supplied.
func NewWithProcess(pm *process.Manager) http.Handler {
	mux := http.NewServeMux()
	if pm != nil {
		registerProcess(mux, pm)
	}
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
	})
	mux.HandleFunc("POST /batch", batch)
	mux.HandleFunc("POST /update", update)
	mux.HandleFunc("POST /apply", apply)
	mux.HandleFunc("POST /cell", cell)
	return mux
}

func cell(w http.ResponseWriter, r *http.Request) {
	var req CellRequest
	if !decode(w, r, &req) {
		return
	}
	if req.Row <= 0 || strings.TrimSpace(req.Column) == "" {
		fail(w, http.StatusBadRequest, errors.New("cell needs a positive row and a column name"))
		return
	}
	src, err := sheet.Open(req.Source)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	b, err := src.ReadRows(r.Context(), req.Row, req.Row)
	if err != nil {
		fail(w, statusFor(err), err)
		return
	}
	known := false
	for _, h := range b.Header {
		if h == req.Column {
			known = true
			break
		}
	}
	if !known {
		fail(w, http.StatusBadRequest, errors.New("column \""+req.Column+"\" not found in header"))
		return
	}

	resp := CellResponse{Row: req.Row, Column: req.Column}
	for _, row := range b.Rows {
		if row.Number == req.Row {
			resp.Value = strings.TrimSpace(row.Values[req.Column])
			break
		}
	}
	resp.Found = resp.Value != ""
	if req.Required && !resp.Found {
		fail(w, http.StatusUnprocessableEntity,
			errors.New("row "+strconv.Itoa(req.Row)+" has no value in column \""+req.Column+"\""))
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func apply(w http.ResponseWriter, r *http.Request) {
	var req ApplyRequest
	if !decode(w, r, &req) {
		return
	}
	if req.Limit <= 0 || len(req.Set) == 0 {
		fail(w, http.StatusBadRequest, errors.New("apply needs a positive limit and a non-empty set"))
		return
	}
	headerRow := req.Source.HeaderRow
	if headerRow <= 0 {
		headerRow = 1
	}
	if req.StartRow <= 0 {
		req.StartRow = headerRow + 1
	}
	if req.EndRow <= 0 {
		req.EndRow = req.StartRow + defaultBatchSize - 1
	}
	if req.EndRow-req.StartRow+1 > maxBatchSize {
		req.EndRow = req.StartRow + maxBatchSize - 1
	}
	src, err := sheet.Open(req.Source)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	b, err := src.ReadRows(r.Context(), req.StartRow, req.EndRow)
	if err != nil {
		fail(w, statusFor(err), err)
		return
	}
	rows, err := engine.PendingRows(b, req.Pending)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	if len(rows) > req.Limit {
		rows = rows[:req.Limit]
	}
	var cells []sheet.CellUpdate
	for _, row := range rows {
		cells = append(cells, UpdateRequest{Rows: []RowUpdate{{Row: row.Number, Values: req.Set}}}.cells()...)
	}
	if len(cells) > 0 {
		if err := src.Write(r.Context(), cells); err != nil {
			fail(w, statusFor(err), err)
			return
		}
	}
	writeJSON(w, http.StatusOK, ApplyResponse{Updated: len(rows), Rows: rows, Text: text(b.Header, rows)})
}

func batch(w http.ResponseWriter, r *http.Request) {
	var req BatchRequest
	if !decode(w, r, &req) {
		return
	}
	headerRow := req.Source.HeaderRow
	if headerRow <= 0 {
		headerRow = 1
	}
	if req.StartRow <= 0 {
		req.StartRow = headerRow + 1
	}
	if req.EndRow <= 0 {
		req.EndRow = req.StartRow + defaultBatchSize - 1
	}
	if req.EndRow-req.StartRow+1 > maxBatchSize {
		req.EndRow = req.StartRow + maxBatchSize - 1
	}

	src, err := sheet.Open(req.Source)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	b, err := src.ReadRows(r.Context(), req.StartRow, req.EndRow)
	if err != nil {
		fail(w, statusFor(err), err)
		return
	}
	rows, err := engine.PendingRows(b, req.Pending)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	writeJSON(w, http.StatusOK, BatchResponse{
		Header: b.Header, StartRow: b.StartRow, EndRow: b.EndRow,
		Scanned: len(b.Rows), Count: len(rows), Rows: rows, Text: text(b.Header, rows),
		EndOfData: b.EndOfData, NextStartRow: b.EndRow + 1,
	})
}

func update(w http.ResponseWriter, r *http.Request) {
	var req UpdateRequest
	if !decode(w, r, &req) {
		return
	}
	src, err := sheet.Open(req.Source)
	if err != nil {
		fail(w, http.StatusBadRequest, err)
		return
	}
	cells := req.cells()
	if len(cells) == 0 {
		fail(w, http.StatusBadRequest, errors.New("nothing to update: send rows or updates"))
		return
	}
	if err := src.Write(r.Context(), cells); err != nil {
		fail(w, statusFor(err), err)
		return
	}
	writeJSON(w, http.StatusOK, UpdateResponse{Rows: req.rowCount(cells), Cells: len(cells)})
}

// statusFor maps a source error to an HTTP status: 401 when the Microsoft sign-in must be
// repeated, 409 when the file changed after it was read, 502 for anything else.
func statusFor(err error) int {
	switch {
	case errors.Is(err, msauth.ErrLoginRequired):
		return http.StatusUnauthorized
	case errors.Is(err, sheet.ErrConflict):
		return http.StatusConflict
	default:
		return http.StatusBadGateway
	}
}

func decode(w http.ResponseWriter, r *http.Request, v any) bool {
	if err := json.NewDecoder(r.Body).Decode(v); err != nil {
		fail(w, http.StatusBadRequest, err)
		return false
	}
	return true
}

func fail(w http.ResponseWriter, status int, err error) {
	writeJSON(w, status, map[string]string{"error": err.Error()})
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
