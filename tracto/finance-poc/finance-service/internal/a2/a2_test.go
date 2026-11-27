package a2

import (
	"errors"
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
	os.WriteFile(filepath.Join(d.Seed, "categories.json"), []byte(`[]`), 0o644)
	os.WriteFile(filepath.Join(d.Seed, "payees.json"), []byte(`[
		{"employeeId":"E1","name":"Priya Nair","bankCode":"7171","accountNumber":"0123456789"},
		{"employeeId":"E2","name":"Tom Reed","bankCode":"7339","accountNumber":"5566778899"},
		{"employeeId":"E3","name":"Sara Lim","bankCode":"7171","accountNumber":""}]`), 0o644)
	if err := d.Ensure(); err != nil {
		t.Fatal(err)
	}
	return d
}

func bill(t *testing.T, d store.Dirs, claim, emp, name string, total float64, date string) {
	t.Helper()
	b := store.Bill{BillID: store.BillID(claim), Reference: claim, Status: "approved", Contact: store.Contact{EmployeeID: emp, Name: name},
		Total: total, PaymentDate: date, PDF: store.BillID(claim) + ".pdf"}
	if err := store.WriteJSON(d.Path(store.BillsApproved, b.BillID+".json"), b); err != nil {
		t.Fatal(err)
	}
}

var fixed = func() time.Time { return time.Date(2026, 11, 27, 15, 0, 0, 0, time.UTC) }

func TestGroupsByPayeeAndLeavesOutIncompleteBankDetails(t *testing.T) {
	d := setup(t)
	bill(t, d, "CLM-1", "E1", "Priya Nair", 193.40, "2026-11-28")
	bill(t, d, "CLM-4", "E1", "Priya Nair", 41.00, "2026-11-28")
	bill(t, d, "CLM-6", "E2", "Tom Reed", 23.40, "2026-11-28")
	bill(t, d, "CLM-3", "E3", "Sara Lim", 268.20, "2026-11-28")
	lg := &memLog{}
	res, err := Run(d, Options{OrgAccount: "ORG-1", Now: fixed}, lg)
	if err != nil {
		t.Fatal(err)
	}
	if res.Files != 1 || res.Payments != 2 || res.Bills != 3 || res.Total != "257.80" || res.Excluded != 1 {
		t.Fatalf("got %+v", res)
	}
	csv, err := os.ReadFile(d.Path(store.Payments, "PAY_20261128_1.csv"))
	if err != nil {
		t.Fatal(err)
	}
	want := "H,ORG-1,20261128,PAY_20261128_1\r\n" +
		"D,Priya Nair,7171,0123456789,234.40,CLM-1 CLM-4\r\n" +
		"D,Tom Reed,7339,5566778899,23.40,CLM-6\r\n" +
		"T,2,257.80\r\n"
	if string(csv) != want {
		t.Fatalf("file:\n%s\nwant:\n%s", csv, want)
	}
	var sum Summary
	if err := store.ReadJSON(d.Path(store.Payments, "PAY_20261128_1.summary.json"), &sum); err != nil {
		t.Fatal(err)
	}
	if sum.Lines != 2 || len(sum.Excluded) != 1 || sum.Excluded[0].Reason != "no account number on record" {
		t.Fatalf("summary %+v", sum)
	}
	raw, _ := os.ReadFile(d.Path(store.Payments, "PAY_20261128_1.summary.json"))
	if strings.Contains(string(raw), "0123456789") {
		t.Fatal("summary carries an account number")
	}
	for _, id := range []string{"BILL-CLM-1", "BILL-CLM-4", "BILL-CLM-6"} {
		var b store.Bill
		if err := store.ReadJSON(d.Path(store.BillsPaid, id+".json"), &b); err != nil || b.Status != "paid" || b.PaymentFile != "PAY_20261128_1.csv" {
			t.Fatalf("%s not paid: %v %+v", id, err, b)
		}
	}
	if !store.Exists(d.Path(store.BillsApproved, "BILL-CLM-3.json")) {
		t.Fatal("left-out bill must stay in approved")
	}
	if !store.Exists(d.Path(store.Exceptions, "PAYEE-E3-20261128.json")) {
		t.Fatal("payee exception not written")
	}
	if len(lg.warn) != 1 {
		t.Fatalf("warnings %v", lg.warn)
	}
	for _, l := range append(lg.info, lg.warn...) {
		if strings.Contains(l, "0123456789") || strings.Contains(l, "5566778899") {
			t.Fatalf("log carries an account number: %s", l)
		}
	}

	// A second run pays nothing twice: only Sara's bill is left, and she still cannot be paid.
	res, err = Run(d, Options{OrgAccount: "ORG-1", Now: fixed}, &memLog{})
	if err != nil {
		t.Fatal(err)
	}
	if res.Files != 0 || res.Excluded != 1 || !strings.Contains(res.Message, "no payment file written") {
		t.Fatalf("second run %+v", res)
	}
	if store.Exists(d.Path(store.Payments, "PAY_20261128_2.csv")) {
		t.Fatal("second file written")
	}
}

func TestOneFilePerPaymentDateAndFilter(t *testing.T) {
	d := setup(t)
	bill(t, d, "CLM-1", "E1", "Priya Nair", 10, "2026-11-28")
	bill(t, d, "CLM-2", "E2", "Tom Reed", 20, "2026-12-05")
	res, err := Run(d, Options{PaymentDate: "2026-12-05", Now: fixed}, &memLog{})
	if err != nil {
		t.Fatal(err)
	}
	if res.Files != 1 || res.File != "payments/PAY_20261205_1.csv" || !store.Exists(d.Path(store.BillsApproved, "BILL-CLM-1.json")) {
		t.Fatalf("filter: %+v", res)
	}
	bill(t, d, "CLM-7", "E2", "Tom Reed", 5, "2026-12-05")
	res, err = Run(d, Options{Now: fixed}, &memLog{})
	if err != nil {
		t.Fatal(err)
	}
	if res.Files != 2 || !store.Exists(d.Path(store.Payments, "PAY_20261128_1.csv")) || !store.Exists(d.Path(store.Payments, "PAY_20261205_2.csv")) {
		t.Fatalf("per date: %+v", res)
	}
}

func TestNothingToDoAndBadDate(t *testing.T) {
	d := setup(t)
	res, err := Run(d, Options{Now: fixed}, &memLog{})
	if err != nil || !strings.HasPrefix(res.Message, "Nothing to do") {
		t.Fatalf("%v %+v", err, res)
	}
	if _, err := Run(d, Options{PaymentDate: "28/11/2026"}, &memLog{}); !errors.Is(err, ErrInvalid) {
		t.Fatalf("want ErrInvalid, got %v", err)
	}
}
