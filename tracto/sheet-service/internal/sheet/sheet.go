// Package sheet defines the source-independent view of a spreadsheet.
// Every connector (local xlsx, Google Sheets, OneDrive/SharePoint) implements Source, so the
// engine and API never depend on where the data lives. Sheets can hold lakhs of rows, so data is
// only ever read as a bounded range of row numbers.
//
// A request names the file by Path only. Credentials are never part of a request; each connector
// reads them from environment variables (see the README).
package sheet

import (
	"context"
	"errors"
	"fmt"
	"os"
	"regexp"
	"strings"
)

// ErrConflict means the file changed after it was read, so a write was refused.
var ErrConflict = errors.New("file changed since it was read; read it again before writing")

// Row is one data row. Number is the 1-based row number in the sheet.
type Row struct {
	Number int               `json:"row"`
	Values map[string]string `json:"values"`
}

// Batch is the result of reading the rows StartRow..EndRow (inclusive).
type Batch struct {
	Header   []string `json:"header"`
	StartRow int      `json:"startRow"`
	EndRow   int      `json:"endRow"`
	// Rows are the non-empty rows inside the range, in sheet order.
	Rows []Row `json:"rows"`
	// LastRow is the highest non-empty row number inside the range, or 0 when the range is empty.
	LastRow int `json:"lastRow"`
	// EndOfData is true when the range reached past the last non-empty row, meaning the next
	// batch would be empty. It assumes data has no blank gap as long as the rest of the range.
	EndOfData bool `json:"endOfData"`
}

// CellUpdate sets one cell, addressed by row number and header name.
type CellUpdate struct {
	Row    int    `json:"row"`
	Column string `json:"column"`
	Value  string `json:"value"`
}

// Source type names.
const (
	TypeXlsx   = "xlsx"   // file on a local or network path
	TypeGSheet = "gsheet" // Google Sheets
	TypeGraph  = "graph"  // OneDrive or SharePoint, through Microsoft Graph
)

// Config locates a sheet. Type is optional: it is worked out from Path when empty.
type Config struct {
	// Path is a file path, a Google Sheets URL or ID, or an https OneDrive/SharePoint file URL.
	Path      string `json:"path"`
	Type      string `json:"type,omitempty"`
	Sheet     string `json:"sheet"`
	HeaderRow int    `json:"headerRow,omitempty"` // default 1

	// The fields below are never read from a request.
	Env           func(string) string `json:"-"` // environment lookup; os.Getenv when nil
	GoogleAPIBase string              `json:"-"`
	GraphBase     string              `json:"-"`
	LoginBase     string              `json:"-"`
	WorkDir       string              `json:"-"` // where downloaded copies are kept
}

func (c Config) getenv(key string) string {
	if c.Env != nil {
		return c.Env(key)
	}
	return os.Getenv(key)
}

// Source reads a bounded range of a sheet and writes cells back to it.
type Source interface {
	// ReadRows reads the header plus rows start..end (1-based, inclusive, start > header row).
	ReadRows(ctx context.Context, start, end int) (*Batch, error)
	// Write sets the given cells. It is all-or-nothing for invalid input.
	Write(ctx context.Context, updates []CellUpdate) error
}

var googleSheetURL = regexp.MustCompile(`^https://docs\.google\.com/spreadsheets/d/([A-Za-z0-9_-]+)`)

// DetectType infers the source type from path.
func DetectType(path string) string {
	switch {
	case googleSheetURL.MatchString(path):
		return TypeGSheet
	case strings.HasPrefix(strings.ToLower(path), "https://"):
		return TypeGraph
	default:
		return TypeXlsx
	}
}

// Open returns the connector for cfg.
func Open(cfg Config) (Source, error) {
	if strings.TrimSpace(cfg.Path) == "" {
		return nil, fmt.Errorf("path is required")
	}
	if strings.TrimSpace(cfg.Sheet) == "" {
		return nil, fmt.Errorf("sheet is required")
	}
	if cfg.HeaderRow <= 0 {
		cfg.HeaderRow = 1
	}
	if cfg.Type == "" {
		cfg.Type = DetectType(cfg.Path)
	}
	switch cfg.Type {
	case TypeXlsx:
		return newXlsx(cfg)
	case TypeGSheet:
		return newGSheet(cfg)
	case TypeGraph:
		return newGraph(cfg)
	default:
		return nil, fmt.Errorf("unsupported source type %q", cfg.Type)
	}
}

func checkRange(headerRow, start, end int) error {
	if start <= headerRow {
		return fmt.Errorf("startRow %d must be below the header row %d", start, headerRow)
	}
	if end < start {
		return fmt.Errorf("endRow %d is before startRow %d", end, start)
	}
	return nil
}

// buildBatch assembles a Batch from raw cell rows, where raw[i] holds the cells of sheet row start+i.
func buildBatch(header []string, start, end int, raw [][]string) *Batch {
	b := &Batch{Header: header, StartRow: start, EndRow: end}
	for i, cells := range raw {
		n := start + i
		if n > end {
			break
		}
		vals := make(map[string]string, len(header))
		empty := true
		for c, name := range header {
			if name == "" {
				continue
			}
			v := ""
			if c < len(cells) {
				v = cells[c]
			}
			if v != "" {
				empty = false
			}
			vals[name] = v
		}
		if empty {
			continue
		}
		b.Rows = append(b.Rows, Row{Number: n, Values: vals})
		b.LastRow = n
	}
	b.EndOfData = b.LastRow < end
	return b
}
