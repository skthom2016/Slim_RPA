// Package store is the file layout both processes work on. Every hand-over between steps is a
// file in a folder under the data directory, so the folders are the state: a claim or bill is
// in exactly one folder at a time, and moving it is how a step says it is done.
package store

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// Folders under the data directory, relative paths.
const (
	ClaimsSubmitted = "claims/submitted"
	ClaimsApproved  = "claims/approved"
	ClaimsProcessed = "claims/processed"
	BillsDraft      = "bills/draft"
	BillsApproved   = "bills/approved"
	BillsPaid       = "bills/paid"
	Payments        = "payments"
	Exceptions      = "exceptions"
)

// AllFolders lists every folder the processes use, in the order they are created.
var AllFolders = []string{ClaimsSubmitted, ClaimsApproved, ClaimsProcessed, BillsDraft, BillsApproved, BillsPaid, Payments, Exceptions}

// Dirs locates the data directory and the seed (reference data) directory.
type Dirs struct {
	Data string
	Seed string
}

// Path joins the data directory, a folder and an optional file name.
func (d Dirs) Path(folder string, name ...string) string {
	return filepath.Join(append([]string{d.Data, filepath.FromSlash(folder)}, name...)...)
}

// Ensure creates every folder that is missing.
func (d Dirs) Ensure() error {
	for _, f := range AllFolders {
		if err := os.MkdirAll(d.Path(f), 0o755); err != nil {
			return err
		}
	}
	return nil
}

// JSONFiles lists the .json files in a folder, sorted by name.
func (d Dirs) JSONFiles(folder string) ([]string, error) {
	entries, err := os.ReadDir(d.Path(folder))
	if err != nil {
		return nil, err
	}
	var names []string
	for _, e := range entries {
		if !e.IsDir() && strings.EqualFold(filepath.Ext(e.Name()), ".json") {
			names = append(names, e.Name())
		}
	}
	sort.Strings(names)
	return names, nil
}

// Exists reports whether a file is present.
func Exists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

// ReadJSON decodes a file into v.
func ReadJSON(path string, v any) error {
	b, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	b = bytes.TrimPrefix(b, []byte{0xEF, 0xBB, 0xBF}) // a byte order mark some editors add
	return json.Unmarshal(b, v)
}

// WriteJSON writes v as indented JSON. It writes a temporary file first and renames it, so a
// reader never sees half a file.
func WriteJSON(path string, v any) error {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return err
	}
	return WriteFile(path, append(b, '\n'))
}

// WriteFile writes data through a temporary file and a rename.
func WriteFile(path string, data []byte) error {
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		os.Remove(tmp)
		return err
	}
	return nil
}

// UniqueName returns name if it is free in dir, otherwise name with -2, -3, ... before the extension.
func UniqueName(dir, name string) string {
	if !Exists(filepath.Join(dir, name)) {
		return name
	}
	ext := filepath.Ext(name)
	base := strings.TrimSuffix(name, ext)
	for i := 2; ; i++ {
		n := fmt.Sprintf("%s-%d%s", base, i, ext)
		if !Exists(filepath.Join(dir, n)) {
			return n
		}
	}
}

// Move moves a file into another folder under a name that is free there, and returns that name.
func (d Dirs) Move(fromFolder, name, toFolder string) (string, error) {
	target := UniqueName(d.Path(toFolder), name)
	if err := os.Rename(d.Path(fromFolder, name), d.Path(toFolder, target)); err != nil {
		return "", err
	}
	return target, nil
}

// Cents converts an amount to whole cents. Amounts are handled in cents so that totals add up exactly.
func Cents(amount float64) int64 { return int64(math.Round(amount * 100)) }

// Amount converts cents back to a JSON number.
func Amount(cents int64) float64 { return float64(cents) / 100 }

// Money formats cents as 1234.50.
func Money(cents int64) string {
	sign := ""
	if cents < 0 {
		sign, cents = "-", -cents
	}
	return fmt.Sprintf("%s%d.%02d", sign, cents/100, cents%100)
}

// Reference data, from the seed directory. In a real deployment it comes from the accounting
// system (contacts, bank details) and the configuration workbook (expense coding).

// Category is an expense category and the account it is coded to.
type Category struct {
	Name        string `json:"name"`
	AccountCode string `json:"accountCode"`
	AccountName string `json:"accountName"`
}

// Payee is a staff member as a contact in the accounting system, with bank details.
type Payee struct {
	EmployeeID    string `json:"employeeId"`
	Name          string `json:"name"`
	BankCode      string `json:"bankCode"`
	AccountNumber string `json:"accountNumber"`
}

// Reference holds the categories and payees, keyed for lookup.
type Reference struct {
	Categories map[string]Category // by lower-case name
	Payees     map[string]Payee    // by employee id
}

// LoadReference reads categories.json and payees.json from the seed directory.
func (d Dirs) LoadReference() (Reference, error) {
	var cats []Category
	var pays []Payee
	if err := ReadJSON(filepath.Join(d.Seed, "categories.json"), &cats); err != nil {
		return Reference{}, fmt.Errorf("reading the expense categories: %w", err)
	}
	if err := ReadJSON(filepath.Join(d.Seed, "payees.json"), &pays); err != nil {
		return Reference{}, fmt.Errorf("reading the payees: %w", err)
	}
	ref := Reference{Categories: map[string]Category{}, Payees: map[string]Payee{}}
	for _, c := range cats {
		ref.Categories[strings.ToLower(strings.TrimSpace(c.Name))] = c
	}
	for _, p := range pays {
		ref.Payees[strings.TrimSpace(p.EmployeeID)] = p
	}
	return ref, nil
}

// Claim is one expense claim as the claims platform hands it over.
type Claim struct {
	ClaimID      string      `json:"claimId"`
	EmployeeID   string      `json:"employeeId"`
	EmployeeName string      `json:"employeeName"`
	SubmittedAt  string      `json:"submittedAt,omitempty"`
	ApprovedAt   string      `json:"approvedAt,omitempty"`
	ApprovedBy   string      `json:"approvedBy,omitempty"`
	Items        []ClaimItem `json:"items"`
}

// ClaimItem is one expense on a claim.
type ClaimItem struct {
	Date        string  `json:"date"`
	Category    string  `json:"category"`
	Amount      float64 `json:"amount"`
	Description string  `json:"description"`
}

// Bill is a bill as the accounting system would hold it.
type Bill struct {
	BillID      string     `json:"billId"`
	Reference   string     `json:"reference"`
	Status      string     `json:"status"`
	BillDate    string     `json:"billDate"`
	CreatedAt   string     `json:"createdAt"`
	Contact     Contact    `json:"contact"`
	Lines       []BillLine `json:"lines"`
	Total       float64    `json:"total"`
	PDF         string     `json:"pdf"`
	ApprovedAt  string     `json:"approvedAt,omitempty"`
	PaymentDate string     `json:"paymentDate,omitempty"`
	PaidAt      string     `json:"paidAt,omitempty"`
	PaymentFile string     `json:"paymentFile,omitempty"`
}

// Contact is who a bill is payable to.
type Contact struct {
	EmployeeID string `json:"employeeId"`
	Name       string `json:"name"`
}

// BillLine is one coded line of a bill.
type BillLine struct {
	Date        string  `json:"date"`
	Description string  `json:"description"`
	Category    string  `json:"category"`
	AccountCode string  `json:"accountCode"`
	AccountName string  `json:"accountName,omitempty"`
	Amount      float64 `json:"amount"`
}

// BillID is the bill id for a claim. One claim has at most one bill, so the id is the guard
// against billing a claim twice.
func BillID(claimID string) string { return "BILL-" + claimID }

// FindBill reports the folder that already holds a bill with this id, or "" if none does.
func (d Dirs) FindBill(billID string) string {
	for _, f := range []string{BillsDraft, BillsApproved, BillsPaid} {
		if Exists(d.Path(f, billID+".json")) {
			return f
		}
	}
	return ""
}

// ErrBusy is returned when a run of the same process is already going.
var ErrBusy = errors.New("a run is already in progress; try again when it has finished")

// Logger receives one line per item a run handles: Info for each item done, Warn for each exception.
type Logger interface {
	Info(msg string)
	Warn(msg string)
}
