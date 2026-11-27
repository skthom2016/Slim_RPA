package a1

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"tracto/finance-service/internal/store"
)

type memLog struct{ info, warn []string }

func (m *memLog) Info(s string) { m.info = append(m.info, s) }
func (m *memLog) Warn(s string) { m.warn = append(m.warn, s) }

func setup(t *testing.T) store.Dirs {
	t.Helper()
	root := t.TempDir()
	d := store.Dirs{Data: filepath.Join(root, "data"), Seed: filepath.Join(root, "seed")}
	os.MkdirAll(d.Seed, 0o755)
	write(t, filepath.Join(d.Seed, "categories.json"), `[{"name":"Local travel","accountCode":"6410","accountName":"Local travel"},{"name":"Meals","accountCode":"6420"},{"name":"Taxi (overseas)","accountCode":""}]`)
	write(t, filepath.Join(d.Seed, "payees.json"), `[{"employeeId":"E1","name":"Priya Nair","bankCode":"7171","accountNumber":"0123456789"},{"employeeId":"E2","name":"Tom Reed","bankCode":"7339","accountNumber":"5566778899"}]`)
	if err := d.Ensure(); err != nil {
		t.Fatal(err)
	}
	return d
}

func write(t *testing.T, path, s string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(s), 0o644); err != nil {
		t.Fatal(err)
	}
}

var fixed = Options{Org: "Test Org", Now: func() time.Time { return time.Date(2026, 11, 27, 9, 0, 0, 0, time.UTC) }}

func TestCreatesBillAndMovesClaim(t *testing.T) {
	d := setup(t)
	write(t, d.Path(store.ClaimsApproved, "CLM-1.json"), `{"claimId":"CLM-1","employeeId":"E1","employeeName":"Priya Nair","items":[
		{"date":"2026-11-18","category":"Local travel","amount":42.5,"description":"Taxi"},
		{"date":"2026-11-19","category":"meals","amount":86.1,"description":"Lunch"}]}`)
	lg := &memLog{}
	res, err := Run(d, fixed, lg)
	if err != nil {
		t.Fatal(err)
	}
	if res.Created != 1 || res.Exceptions != 0 {
		t.Fatalf("got %+v", res)
	}
	var b store.Bill
	if err := store.ReadJSON(d.Path(store.BillsDraft, "BILL-CLM-1.json"), &b); err != nil {
		t.Fatal(err)
	}
	if b.Total != 128.6 || len(b.Lines) != 2 || b.Lines[1].AccountCode != "6420" || b.Contact.Name != "Priya Nair" || b.Reference != "CLM-1" {
		t.Fatalf("bill %+v", b)
	}
	pdf, err := os.ReadFile(d.Path(store.BillsDraft, "BILL-CLM-1.pdf"))
	if err != nil || !strings.HasPrefix(string(pdf), "%PDF") {
		t.Fatalf("pdf missing or not a PDF: %v", err)
	}
	if !store.Exists(d.Path(store.ClaimsProcessed, "CLM-1.json")) || store.Exists(d.Path(store.ClaimsApproved, "CLM-1.json")) {
		t.Fatal("claim was not moved to processed")
	}
	if len(lg.info) == 0 || len(lg.warn) != 0 {
		t.Fatalf("log %+v", lg)
	}
}

func TestExceptionListsEveryProblem(t *testing.T) {
	d := setup(t)
	write(t, d.Path(store.ClaimsApproved, "CLM-2.json"), `{"claimId":"CLM-2","employeeId":"E2","items":[
		{"date":"2026-11-21","category":"Local travel","amount":23.4,"description":"Train"},
		{"date":"2026-11-22","category":"Taxi (overseas)","amount":55,"description":"Airport taxi"},
		{"date":"","category":"Parking","amount":0,"description":""}]}`)
	lg := &memLog{}
	res, err := Run(d, fixed, lg)
	if err != nil {
		t.Fatal(err)
	}
	if res.Exceptions != 1 || res.Created != 0 {
		t.Fatalf("got %+v", res)
	}
	var ex Exception
	if err := store.ReadJSON(d.Path(store.Exceptions, "CLM-2.json"), &ex); err != nil {
		t.Fatal(err)
	}
	for _, want := range []string{"item 2: category 'Taxi (overseas)' has no account code", "item 3: date", "item 3: description", "item 3: amount", "item 3: category 'Parking'"} {
		if !strings.Contains(ex.Reason, want) {
			t.Errorf("reason %q lacks %q", ex.Reason, want)
		}
	}
	if store.Exists(d.Path(store.ClaimsApproved, "CLM-2.json")) || store.Exists(d.Path(store.BillsDraft, "BILL-CLM-2.json")) {
		t.Fatal("claim left in approved or bill created")
	}
	if len(lg.warn) != 1 {
		t.Fatalf("want one warning, got %v", lg.warn)
	}
}

func TestUnknownEmployeeAndBadJSON(t *testing.T) {
	d := setup(t)
	write(t, d.Path(store.ClaimsApproved, "CLM-3.json"), `{"claimId":"CLM-3","employeeId":"E9","items":[{"date":"2026-11-21","category":"Meals","amount":5,"description":"x"}]}`)
	write(t, d.Path(store.ClaimsApproved, "CLM-4.json"), `{not json`)
	res, err := Run(d, fixed, &memLog{})
	if err != nil {
		t.Fatal(err)
	}
	if res.Exceptions != 2 {
		t.Fatalf("got %+v", res)
	}
	if !strings.Contains(res.Items[0].Reason, "employee E9 has no contact") {
		t.Fatalf("reason %q", res.Items[0].Reason)
	}
}

func TestNeverBillsTwice(t *testing.T) {
	d := setup(t)
	claim := `{"claimId":"CLM-5","employeeId":"E1","items":[{"date":"2026-11-21","category":"Meals","amount":5,"description":"x"}]}`
	write(t, d.Path(store.ClaimsApproved, "CLM-5.json"), claim)
	if _, err := Run(d, fixed, &memLog{}); err != nil {
		t.Fatal(err)
	}
	// The bill moves on to approved, and the same claim turns up again.
	os.Rename(d.Path(store.BillsDraft, "BILL-CLM-5.json"), d.Path(store.BillsApproved, "BILL-CLM-5.json"))
	write(t, d.Path(store.ClaimsApproved, "CLM-5.json"), claim)
	lg := &memLog{}
	res, err := Run(d, fixed, lg)
	if err != nil {
		t.Fatal(err)
	}
	if res.Created != 0 || res.Items[0].Status != "AlreadyBilled" || store.Exists(d.Path(store.BillsDraft, "BILL-CLM-5.json")) {
		t.Fatalf("second bill created: %+v", res)
	}
	if !store.Exists(d.Path(store.ClaimsProcessed, "CLM-5-2.json")) {
		t.Fatal("repeat claim not moved to processed under a free name")
	}
	if len(lg.warn) != 1 || !strings.Contains(lg.warn[0], "already exists in bills/approved") {
		t.Fatalf("warnings %v", lg.warn)
	}
}

func TestNothingToDo(t *testing.T) {
	d := setup(t)
	res, err := Run(d, fixed, &memLog{})
	if err != nil {
		t.Fatal(err)
	}
	if res.Claims != 0 || !strings.HasPrefix(res.Message, "Nothing to do") {
		t.Fatalf("got %+v", res)
	}
}
