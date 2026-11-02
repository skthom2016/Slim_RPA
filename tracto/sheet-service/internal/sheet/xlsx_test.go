package sheet

import (
	"context"
	"fmt"
	"path/filepath"
	"testing"

	"github.com/xuri/excelize/v2"
)

// newWorkbook writes a sheet "Data" with a header and the given data rows starting at row 2.
// A nil row leaves that row out of the file entirely.
func newWorkbook(t *testing.T, rows [][]any) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "book with spaces.xlsx")
	f := excelize.NewFile()
	f.NewSheet("Data")
	all := append([][]any{{"Style_ID", "Approved_by_Finance", "Bot_Status"}}, rows...)
	for i, r := range all {
		if r == nil {
			continue
		}
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

func small(t *testing.T) string {
	return newWorkbook(t, [][]any{
		{"861211", "Approved", ""},
		{"7793035", "", ""},
		nil, // row 4 absent
		{"555", "Rejected", ""},
	})
}

func open(t *testing.T, path string) Source {
	t.Helper()
	src, err := Open(Config{Type: "xlsx", Path: path, Sheet: "Data"})
	if err != nil {
		t.Fatal(err)
	}
	return src
}

func TestXlsx_ReadRowsKeepsRowNumbersAcrossGaps(t *testing.T) {
	b, err := open(t, small(t)).ReadRows(context.Background(), 2, 100)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 3 || b.Rows[1].Number != 3 || b.Rows[2].Number != 5 {
		t.Fatalf("got %+v, want rows 2,3,5", b.Rows)
	}
	if b.LastRow != 5 || !b.EndOfData {
		t.Fatalf("lastRow=%d endOfData=%v, want 5 and true", b.LastRow, b.EndOfData)
	}
}

func TestXlsx_ReadRowsReturnsOnlyTheRequestedRange(t *testing.T) {
	b, err := open(t, small(t)).ReadRows(context.Background(), 3, 3)
	if err != nil {
		t.Fatal(err)
	}
	if len(b.Rows) != 1 || b.Rows[0].Number != 3 || b.Rows[0].Values["Style_ID"] != "7793035" {
		t.Fatalf("got %+v, want only row 3", b.Rows)
	}
	if b.EndOfData {
		t.Fatal("row 5 still has data after row 3, but the range ended exactly at row 3")
	}
}

func TestXlsx_WalksALargeSheetInBatches(t *testing.T) {
	const total = 2500
	rows := make([][]any, total)
	for i := range rows {
		rows[i] = []any{fmt.Sprint(1000 + i), "", ""}
	}
	src := open(t, newWorkbook(t, rows))

	seen, start := 0, 2
	for batches := 0; ; batches++ {
		if batches > 10 {
			t.Fatal("did not reach end of data")
		}
		b, err := src.ReadRows(context.Background(), start, start+999)
		if err != nil {
			t.Fatal(err)
		}
		seen += len(b.Rows)
		if b.EndOfData {
			break
		}
		start = b.EndRow + 1
	}
	if seen != total {
		t.Fatalf("saw %d rows, want %d", seen, total)
	}
}

func TestXlsx_ReadRowsRejectsBadRanges(t *testing.T) {
	src := open(t, small(t))
	if _, err := src.ReadRows(context.Background(), 1, 10); err == nil {
		t.Fatal("start on the header row must be rejected")
	}
	if _, err := src.ReadRows(context.Background(), 10, 5); err == nil {
		t.Fatal("end before start must be rejected")
	}
}

func TestXlsx_WriteUpdatesOnlyTheAddressedCells(t *testing.T) {
	src := open(t, small(t))
	err := src.Write(context.Background(), []CellUpdate{
		{Row: 3, Column: "Approved_by_Finance", Value: "Approved"},
		{Row: 3, Column: "Bot_Status", Value: "SUCCESS"},
	})
	if err != nil {
		t.Fatal(err)
	}
	b, _ := src.ReadRows(context.Background(), 2, 10)
	byRow := map[int]Row{}
	for _, r := range b.Rows {
		byRow[r.Number] = r
	}
	if byRow[3].Values["Approved_by_Finance"] != "Approved" || byRow[3].Values["Bot_Status"] != "SUCCESS" {
		t.Fatalf("row 3 not updated: %+v", byRow[3].Values)
	}
	if byRow[2].Values["Bot_Status"] != "" || byRow[5].Values["Approved_by_Finance"] != "Rejected" {
		t.Fatalf("other rows must be untouched: %+v %+v", byRow[2].Values, byRow[5].Values)
	}
}

func TestXlsx_WriteRejectsUnknownColumnAndHeaderRow(t *testing.T) {
	src := open(t, small(t))
	if err := src.Write(context.Background(), []CellUpdate{{Row: 2, Column: "Nope", Value: "x"}}); err == nil {
		t.Fatal("expected error for unknown column")
	}
	if err := src.Write(context.Background(), []CellUpdate{{Row: 1, Column: "Bot_Status", Value: "x"}}); err == nil {
		t.Fatal("expected error when writing the header row")
	}
}

func TestOpen_RejectsUnknownType(t *testing.T) {
	if _, err := Open(Config{Type: "ftp"}); err == nil {
		t.Fatal("expected error")
	}
}
