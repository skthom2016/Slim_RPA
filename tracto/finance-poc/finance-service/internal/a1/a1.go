// Package a1 is process A1: approved staff claims become draft bills.
//
// It takes only the claim files in claims/approved/. For each one it checks the claim, creates
// the bill PDF and the bill JSON in bills/draft/, and moves the claim to claims/processed/.
// A claim that fails a check moves to exceptions/ with the reason. A claim is never billed
// twice: the bill id is derived from the claim id, and an existing bill stops a second one.
package a1

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"tracto/finance-service/internal/billpdf"
	"tracto/finance-service/internal/store"
)

// Item is the outcome for one claim.
type Item struct {
	ClaimID  string  `json:"claimId"`
	Employee string  `json:"employee,omitempty"`
	Status   string  `json:"status"` // Created, AlreadyBilled, Exception
	BillID   string  `json:"billId,omitempty"`
	Total    float64 `json:"total,omitempty"`
	Reason   string  `json:"reason,omitempty"`
}

// Result is what a run did.
type Result struct {
	State      string `json:"state"`
	Message    string `json:"message"`
	Claims     int    `json:"claims"`
	Created    int    `json:"billsCreated"`
	Exceptions int    `json:"exceptions"`
	Items      []Item `json:"items"`
}

// Exception is the file written to exceptions/ for a claim that could not be processed.
type Exception struct {
	Kind     string          `json:"kind"`
	ClaimID  string          `json:"claimId"`
	Reason   string          `json:"reason"`
	At       string          `json:"at"`
	Original string          `json:"originalFile"`
	Claim    json.RawMessage `json:"claim"`
}

// Options are the settings of a run.
type Options struct {
	Org string           // organisation name printed on the bill
	Now func() time.Time // for tests; nil means time.Now
}

// Run processes every claim in claims/approved/.
func Run(d store.Dirs, opt Options, log store.Logger) (Result, error) {
	now := time.Now
	if opt.Now != nil {
		now = opt.Now
	}
	if err := d.Ensure(); err != nil {
		return Result{}, err
	}
	ref, err := d.LoadReference()
	if err != nil {
		return Result{}, err
	}
	files, err := d.JSONFiles(store.ClaimsApproved)
	if err != nil {
		return Result{}, err
	}
	res := Result{State: "Completed", Items: []Item{}}
	if len(files) == 0 {
		res.Message = "Nothing to do: no approved claims waiting in claims/approved."
		log.Info(res.Message)
		return res, nil
	}
	log.Info(fmt.Sprintf("%d approved claim(s) to process.", len(files)))

	for _, name := range files {
		item, err := processOne(d, ref, opt, now(), name)
		if err != nil {
			// A file system problem, not a problem with the claim: stop and say so, the rest
			// stay where they are and a rerun continues with them.
			return res, fmt.Errorf("claim file %s: %w", name, err)
		}
		res.Items = append(res.Items, item)
		res.Claims++
		switch item.Status {
		case "Created":
			res.Created++
			log.Info(fmt.Sprintf("%s (%s): draft bill %s created, total %s.", item.ClaimID, item.Employee, item.BillID, store.Money(store.Cents(item.Total))))
		case "AlreadyBilled":
			log.Warn(fmt.Sprintf("%s: %s", item.ClaimID, item.Reason))
		default:
			res.Exceptions++
			log.Warn(fmt.Sprintf("%s: not processed, moved to exceptions: %s", item.ClaimID, item.Reason))
		}
	}
	res.Message = summary(res)
	log.Info(res.Message)
	return res, nil
}

func processOne(d store.Dirs, ref store.Reference, opt Options, now time.Time, name string) (Item, error) {
	path := d.Path(store.ClaimsApproved, name)
	raw, err := os.ReadFile(path)
	if err != nil {
		return Item{}, err
	}
	id := strings.TrimSuffix(name, filepath.Ext(name))

	var c store.Claim
	if err := json.Unmarshal(trimBOM(raw), &c); err != nil {
		return toException(d, name, id, "the claim file cannot be read: "+err.Error(), raw, now)
	}
	if c.ClaimID != "" {
		id = c.ClaimID
	}
	item := Item{ClaimID: id, Employee: c.EmployeeName}

	// A bill for this claim already exists: a run was interrupted after creating the bill and
	// before moving the claim, or the claim was approved twice. Finish the move only.
	billID := store.BillID(id)
	if folder := d.FindBill(billID); folder != "" && c.ClaimID != "" {
		if _, err := d.Move(store.ClaimsApproved, name, store.ClaimsProcessed); err != nil {
			return Item{}, err
		}
		item.Status, item.BillID = "AlreadyBilled", billID
		item.Reason = fmt.Sprintf("bill %s already exists in %s; no second bill created, claim moved to claims/processed.", billID, folder)
		return item, nil
	}

	bill, reason := buildBill(c, ref, now)
	if reason != "" {
		return toException(d, name, id, reason, raw, now)
	}

	pdfBytes, err := billpdf.Render(bill, opt.Org)
	if err != nil {
		return Item{}, fmt.Errorf("drawing the bill PDF: %w", err)
	}
	// PDF first, JSON second, claim move last: if anything stops in between, the next run
	// either redraws the PDF or finds the bill and only moves the claim.
	if err := store.WriteFile(d.Path(store.BillsDraft, bill.PDF), pdfBytes); err != nil {
		return Item{}, err
	}
	if err := store.WriteJSON(d.Path(store.BillsDraft, bill.BillID+".json"), bill); err != nil {
		return Item{}, err
	}
	if _, err := d.Move(store.ClaimsApproved, name, store.ClaimsProcessed); err != nil {
		return Item{}, err
	}
	item.Status, item.BillID, item.Total = "Created", bill.BillID, bill.Total
	return item, nil
}

// buildBill checks a claim and codes its lines. It returns the reason when the claim cannot
// become a bill; every problem found is listed, so the claim needs fixing only once.
func buildBill(c store.Claim, ref store.Reference, now time.Time) (store.Bill, string) {
	var problems []string
	if strings.TrimSpace(c.ClaimID) == "" {
		problems = append(problems, "claim id is missing")
	}
	payee, known := ref.Payees[strings.TrimSpace(c.EmployeeID)]
	switch {
	case strings.TrimSpace(c.EmployeeID) == "":
		problems = append(problems, "employee id is missing")
	case !known:
		problems = append(problems, fmt.Sprintf("employee %s has no contact in the accounting system", c.EmployeeID))
	}
	if len(c.Items) == 0 {
		problems = append(problems, "the claim has no items")
	}

	bill := store.Bill{
		BillID:    store.BillID(c.ClaimID),
		Reference: c.ClaimID,
		Status:    "draft",
		BillDate:  now.Format("2006-01-02"),
		CreatedAt: now.Format(time.RFC3339),
		Contact:   store.Contact{EmployeeID: c.EmployeeID, Name: payee.Name},
		PDF:       store.BillID(c.ClaimID) + ".pdf",
	}
	var total int64
	for i, it := range c.Items {
		n := i + 1
		if _, err := time.Parse("2006-01-02", strings.TrimSpace(it.Date)); err != nil {
			problems = append(problems, fmt.Sprintf("item %d: date is missing or not a date", n))
		}
		if strings.TrimSpace(it.Description) == "" {
			problems = append(problems, fmt.Sprintf("item %d: description is missing", n))
		}
		if store.Cents(it.Amount) <= 0 {
			problems = append(problems, fmt.Sprintf("item %d: amount must be more than zero", n))
		}
		cat, ok := ref.Categories[strings.ToLower(strings.TrimSpace(it.Category))]
		switch {
		case strings.TrimSpace(it.Category) == "":
			problems = append(problems, fmt.Sprintf("item %d: category is missing", n))
		case !ok:
			problems = append(problems, fmt.Sprintf("item %d: category '%s' is not a known expense category", n, it.Category))
		case strings.TrimSpace(cat.AccountCode) == "":
			problems = append(problems, fmt.Sprintf("item %d: category '%s' has no account code", n, it.Category))
		}
		total += store.Cents(it.Amount)
		bill.Lines = append(bill.Lines, store.BillLine{
			Date: it.Date, Description: strings.TrimSpace(it.Description), Category: cat.Name,
			AccountCode: cat.AccountCode, AccountName: cat.AccountName, Amount: store.Amount(store.Cents(it.Amount)),
		})
	}
	if len(problems) > 0 {
		return store.Bill{}, strings.Join(problems, "; ")
	}
	bill.Total = store.Amount(total)
	return bill, ""
}

func toException(d store.Dirs, name, id, reason string, raw []byte, now time.Time) (Item, error) {
	claim := json.RawMessage(trimBOM(raw))
	if !json.Valid(claim) {
		b, _ := json.Marshal(string(raw))
		claim = b
	}
	ex := Exception{Kind: "claim", ClaimID: id, Reason: reason, At: now.Format(time.RFC3339), Original: name, Claim: claim}
	target := store.UniqueName(d.Path(store.Exceptions), id+".json")
	if err := store.WriteJSON(d.Path(store.Exceptions, target), ex); err != nil {
		return Item{}, err
	}
	if err := os.Remove(d.Path(store.ClaimsApproved, name)); err != nil {
		return Item{}, err
	}
	var c store.Claim
	_ = json.Unmarshal(trimBOM(raw), &c)
	return Item{ClaimID: id, Employee: c.EmployeeName, Status: "Exception", Reason: reason}, nil
}

func summary(r Result) string {
	var b strings.Builder
	fmt.Fprintf(&b, "%d approved claim(s): %d draft bill(s) created", r.Claims, r.Created)
	var created, other []string
	for _, it := range r.Items {
		switch it.Status {
		case "Created":
			created = append(created, fmt.Sprintf("%s %s", it.BillID, store.Money(store.Cents(it.Total))))
		case "AlreadyBilled":
			other = append(other, fmt.Sprintf("%s already had bill %s", it.ClaimID, it.BillID))
		}
	}
	if len(created) > 0 {
		fmt.Fprintf(&b, " (%s)", strings.Join(created, ", "))
	}
	b.WriteString(".")
	if len(other) > 0 {
		fmt.Fprintf(&b, " %s.", strings.Join(other, "; "))
	}
	if r.Exceptions > 0 {
		fmt.Fprintf(&b, " %d need(s) attention:", r.Exceptions)
		first := true
		for _, it := range r.Items {
			if it.Status == "Exception" {
				if !first {
					b.WriteString(";")
				}
				fmt.Fprintf(&b, " %s, %s", it.ClaimID, it.Reason)
				first = false
			}
		}
		b.WriteString(".")
	}
	return b.String()
}

func trimBOM(b []byte) []byte {
	if len(b) >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF {
		return b[3:]
	}
	return b
}
