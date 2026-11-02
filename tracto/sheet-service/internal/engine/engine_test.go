package engine

import (
	"testing"

	"tracto/sheet-service/internal/sheet"
)

func batch() *sheet.Batch {
	return &sheet.Batch{
		Header: []string{"Style_ID", "Status"},
		Rows: []sheet.Row{
			{Number: 2, Values: map[string]string{"Style_ID": "1", "Status": "Approved"}},
			{Number: 3, Values: map[string]string{"Style_ID": "2", "Status": ""}},
			{Number: 4, Values: map[string]string{"Style_ID": "3", "Status": " rejected "}},
			{Number: 5, Values: map[string]string{"Style_ID": "4", "Status": "Hold"}},
		},
	}
}

func TestPendingRows_EmptyAndUnknownValuesArePending(t *testing.T) {
	rows, err := PendingRows(batch(), &Pending{Column: "Status", DoneValues: []string{"Approved", "Rejected"}})
	if err != nil {
		t.Fatal(err)
	}
	if len(rows) != 2 || rows[0].Number != 3 || rows[1].Number != 5 {
		t.Fatalf("got %+v, want rows 3 and 5", rows)
	}
}

func TestPendingRows_NilRuleReturnsEveryRow(t *testing.T) {
	rows, err := PendingRows(batch(), nil)
	if err != nil || len(rows) != 4 {
		t.Fatalf("got %d rows, err %v, want 4", len(rows), err)
	}
}

func TestPendingRows_UnknownColumnIsAnError(t *testing.T) {
	if _, err := PendingRows(batch(), &Pending{Column: "Nope"}); err == nil {
		t.Fatal("expected error for missing column")
	}
}
