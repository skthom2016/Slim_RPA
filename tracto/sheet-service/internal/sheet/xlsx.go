package sheet

import (
	"context"
	"fmt"
	"strings"
	"sync"

	"github.com/xuri/excelize/v2"
)

// Writes to one file are serialized so concurrent requests never interleave a save.
var fileLocks sync.Map

type xlsxSource struct {
	cfg Config
}

func newXlsx(cfg Config) (Source, error) {
	if strings.TrimSpace(cfg.Path) == "" {
		return nil, fmt.Errorf("xlsx source requires path")
	}
	if strings.TrimSpace(cfg.Sheet) == "" {
		return nil, fmt.Errorf("xlsx source requires sheet")
	}
	if cfg.HeaderRow <= 0 {
		cfg.HeaderRow = 1
	}
	return &xlsxSource{cfg: cfg}, nil
}

func (x *xlsxSource) lock() *sync.Mutex {
	m, _ := fileLocks.LoadOrStore(strings.ToLower(x.cfg.Path), &sync.Mutex{})
	return m.(*sync.Mutex)
}

// ReadRows streams the sheet, so memory use depends on the batch size, not the sheet size.
func (x *xlsxSource) ReadRows(ctx context.Context, start, end int) (*Batch, error) {
	if err := checkRange(x.cfg.HeaderRow, start, end); err != nil {
		return nil, err
	}
	l := x.lock()
	l.Lock()
	defer l.Unlock()

	f, err := excelize.OpenFile(x.cfg.Path)
	if err != nil {
		return nil, fmt.Errorf("open %s: %w", x.cfg.Path, err)
	}
	defer f.Close()

	it, err := f.Rows(x.cfg.Sheet)
	if err != nil {
		return nil, fmt.Errorf("read sheet %q: %w", x.cfg.Sheet, err)
	}
	defer it.Close()

	var header []string
	var raw [][]string
	n := 0
	for it.Next() {
		n++
		if n > end {
			break
		}
		if n != x.cfg.HeaderRow && n < start {
			continue
		}
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		cells, err := it.Columns()
		if err != nil {
			return nil, fmt.Errorf("row %d: %w", n, err)
		}
		if n == x.cfg.HeaderRow {
			header = cells
			continue
		}
		raw = append(raw, cells)
	}
	if header == nil {
		return nil, fmt.Errorf("sheet %q has no header row %d", x.cfg.Sheet, x.cfg.HeaderRow)
	}
	return buildBatch(header, start, end, raw), nil
}

func (x *xlsxSource) Write(ctx context.Context, updates []CellUpdate) error {
	if len(updates) == 0 {
		return nil
	}
	l := x.lock()
	l.Lock()
	defer l.Unlock()

	f, err := excelize.OpenFile(x.cfg.Path)
	if err != nil {
		return fmt.Errorf("open %s: %w", x.cfg.Path, err)
	}
	defer f.Close()

	header, err := x.headerOf(f)
	if err != nil {
		return err
	}
	cols := columnIndex(header)

	for _, u := range updates {
		if err := ctx.Err(); err != nil {
			return err
		}
		c, ok := cols[u.Column]
		if !ok {
			return fmt.Errorf("column %q not found in header", u.Column)
		}
		if u.Row <= x.cfg.HeaderRow {
			return fmt.Errorf("row %d is not a data row", u.Row)
		}
		cell, err := excelize.CoordinatesToCellName(c, u.Row)
		if err != nil {
			return err
		}
		if err := f.SetCellStr(x.cfg.Sheet, cell, u.Value); err != nil {
			return fmt.Errorf("set %s: %w", cell, err)
		}
	}
	return f.Save()
}

func (x *xlsxSource) headerOf(f *excelize.File) ([]string, error) {
	it, err := f.Rows(x.cfg.Sheet)
	if err != nil {
		return nil, fmt.Errorf("read sheet %q: %w", x.cfg.Sheet, err)
	}
	defer it.Close()
	for n := 1; it.Next(); n++ {
		if n == x.cfg.HeaderRow {
			return it.Columns()
		}
	}
	return nil, fmt.Errorf("sheet %q has no header row %d", x.cfg.Sheet, x.cfg.HeaderRow)
}

// columnIndex maps header names to 1-based column numbers.
func columnIndex(header []string) map[string]int {
	m := make(map[string]int, len(header))
	for c, name := range header {
		if name != "" {
			m[name] = c + 1
		}
	}
	return m
}
