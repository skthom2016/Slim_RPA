package process

import (
	"testing"

	"tracto/sheet-service/internal/pg/pgtest"
)

// PostgreSQL text refuses NUL characters; a message from a child job must not make the update fail
// and leave the item looking Running.
func TestStore_MessagesWithNulCharactersAreStoredWithoutThem(t *testing.T) {
	db, _, _ := pgtest.Sheets(t)
	store := NewStore(db)
	if err := store.CreateBatch(BatchRecord{ID: "b1", SourcePath: "x", Sheet: "s", Child: "c@1"}, []ItemRecord{{Row: 2}}); err != nil {
		t.Fatal(err)
	}
	if err := store.SetItem("b1", 2, ItemFailed, "run-1", "bad\x00message"); err != nil {
		t.Fatalf("SetItem: %v", err)
	}
	if err := store.SetBatchState("b1", BatchStopped, "stopped\x00here"); err != nil {
		t.Fatalf("SetBatchState: %v", err)
	}
	b, err := store.Get("b1")
	if err != nil {
		t.Fatal(err)
	}
	if b.Items[0].Message != "badmessage" || b.Items[0].State != ItemFailed || b.Message != "stoppedhere" {
		t.Fatalf("got item %+v, batch message %q", b.Items[0], b.Message)
	}
}
