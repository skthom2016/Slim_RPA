// Package a2 is process A2: the approved bills become one bank payment file per payment date.
//
// It takes only the bills in bills/approved/, groups them by payment date and then by payee,
// and writes one CSV line per payee in a simple bank template, with a header and a trailer that
// carries the control count and total. A payee without complete bank details is left out, with
// the reason, and their bills stay in bills/approved/ for the next run. The bills that went into
// a file move to bills/paid/, so no later run can pay them again.
package a2

import (
	"bufio"
	"bytes"
	"fmt"
	"os"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"

	"tracto/finance-service/internal/store"
)

// Options are the settings of a run.
type Options struct {
	PaymentDate string           // only bills with this payment date; "" means every approved bill
	OrgAccount  string           // the organisation's account, printed in the file header
	Now         func() time.Time // for tests; nil means time.Now
}

// BillRef is a bill as it appears in a summary.
type BillRef struct {
	BillID    string  `json:"billId"`
	Reference string  `json:"reference"`
	Amount    float64 `json:"amount"`
}

// Payment is one line of the payment file.
type Payment struct {
	EmployeeID string    `json:"employeeId"`
	Payee      string    `json:"payee"`
	Amount     float64   `json:"amount"`
	Bills      []BillRef `json:"bills"`
}

// Excluded is a payee left out of the file, with the reason.
type Excluded struct {
	EmployeeID string    `json:"employeeId"`
	Payee      string    `json:"payee"`
	Amount     float64   `json:"amount"`
	Reason     string    `json:"reason"`
	Bills      []BillRef `json:"bills"`
}

// Summary is written next to each payment file. It carries no account numbers.
type Summary struct {
	File        string     `json:"file"`
	PaymentDate string     `json:"paymentDate"`
	CreatedAt   string     `json:"createdAt"`
	Lines       int        `json:"lines"`
	Total       float64    `json:"total"`
	BillCount   int        `json:"billCount"`
	Payments    []Payment  `json:"payments"`
	Excluded    []Excluded `json:"excluded"`
	Message     string     `json:"message"`
}

// Result is what a run did.
type Result struct {
	State    string     `json:"state"`
	Message  string     `json:"message"`
	Files    int        `json:"files"`
	Payments int        `json:"payments"`
	Bills    int        `json:"bills"`
	Total    string     `json:"total"`
	Excluded int        `json:"excludedPayees"`
	File     string     `json:"file,omitempty"` // the first file, for the job's outputs
	Runs     []Summary  `json:"summaries"`
	Skipped  []Excluded `json:"skippedBills,omitempty"` // bills that could not be read or have no payment date
}

// PayeeException is the file written to exceptions/ for a payee left out of a payment file.
type PayeeException struct {
	Kind        string    `json:"kind"`
	EmployeeID  string    `json:"employeeId"`
	Payee       string    `json:"payee"`
	PaymentDate string    `json:"paymentDate"`
	Reason      string    `json:"reason"`
	At          string    `json:"at"`
	Amount      float64   `json:"amount"`
	Bills       []BillRef `json:"bills"`
	Note        string    `json:"note"`
}

type loaded struct {
	file string
	bill store.Bill
}

var (
	datePattern    = regexp.MustCompile(`^\d{4}-\d{2}-\d{2}$`)
	accountPattern = regexp.MustCompile(`^\d{6,12}$`)
)

// Run builds the payment files.
func Run(d store.Dirs, opt Options, log store.Logger) (Result, error) {
	now := time.Now
	if opt.Now != nil {
		now = opt.Now
	}
	if opt.PaymentDate != "" {
		if _, err := time.Parse("2006-01-02", opt.PaymentDate); err != nil {
			return Result{}, fmt.Errorf("%w: paymentDate %q is not a date (YYYY-MM-DD)", ErrInvalid, opt.PaymentDate)
		}
	}
	if err := d.Ensure(); err != nil {
		return Result{}, err
	}
	ref, err := d.LoadReference()
	if err != nil {
		return Result{}, err
	}
	files, err := d.JSONFiles(store.BillsApproved)
	if err != nil {
		return Result{}, err
	}
	res := Result{State: "Completed", Runs: []Summary{}}

	// Read the selection.
	byDate := map[string][]loaded{}
	for _, f := range files {
		var b store.Bill
		if err := store.ReadJSON(d.Path(store.BillsApproved, f), &b); err != nil {
			res.Skipped = append(res.Skipped, Excluded{Reason: fmt.Sprintf("bill file %s cannot be read: %v", f, err)})
			log.Warn(fmt.Sprintf("Bill file %s cannot be read and was left in bills/approved: %v", f, err))
			continue
		}
		if !datePattern.MatchString(b.PaymentDate) {
			res.Skipped = append(res.Skipped, Excluded{EmployeeID: b.Contact.EmployeeID, Payee: b.Contact.Name, Amount: b.Total,
				Reason: "bill " + b.BillID + " has no payment date", Bills: []BillRef{refOf(b)}})
			log.Warn(fmt.Sprintf("%s: has no payment date; left in bills/approved.", b.BillID))
			continue
		}
		if opt.PaymentDate != "" && b.PaymentDate != opt.PaymentDate {
			continue
		}
		byDate[b.PaymentDate] = append(byDate[b.PaymentDate], loaded{file: f, bill: b})
	}
	if len(byDate) == 0 {
		res.Message = "Nothing to do: no approved bills waiting in bills/approved"
		if opt.PaymentDate != "" {
			res.Message += " for payment date " + opt.PaymentDate
		}
		res.Message += "."
		res.Total = "0.00"
		log.Info(res.Message)
		return res, nil
	}

	dates := make([]string, 0, len(byDate))
	for k := range byDate {
		dates = append(dates, k)
	}
	sort.Strings(dates)

	var grand int64
	var messages []string
	for _, date := range dates {
		sum, err := payDate(d, ref, opt, now(), date, byDate[date], log)
		if err != nil {
			return res, err
		}
		res.Runs = append(res.Runs, sum)
		messages = append(messages, sum.Message)
		res.Excluded += len(sum.Excluded)
		if sum.File != "" {
			res.Files++
			res.Payments += sum.Lines
			res.Bills += sum.BillCount
			grand += store.Cents(sum.Total)
			if res.File == "" {
				res.File = store.Payments + "/" + sum.File
			}
		}
	}
	res.Total = store.Money(grand)
	res.Message = strings.Join(messages, " ")
	return res, nil
}

// payDate handles the bills of one payment date: one file, unless no payee can be paid.
func payDate(d store.Dirs, ref store.Reference, opt Options, now time.Time, date string, bills []loaded, log store.Logger) (Summary, error) {
	// Group by payee, in a stable order.
	groups := map[string][]loaded{}
	var order []string
	for _, l := range bills {
		k := l.bill.Contact.EmployeeID
		if _, ok := groups[k]; !ok {
			order = append(order, k)
		}
		groups[k] = append(groups[k], l)
	}
	sort.Slice(order, func(i, j int) bool {
		return strings.ToLower(groups[order[i]][0].bill.Contact.Name) < strings.ToLower(groups[order[j]][0].bill.Contact.Name)
	})

	sum := Summary{PaymentDate: date, CreatedAt: now.Format(time.RFC3339), Payments: []Payment{}, Excluded: []Excluded{}}
	type line struct {
		payee store.Payee
		pay   Payment
		cents int64
		files []loaded
	}
	var lines []line
	for _, id := range order {
		g := groups[id]
		var cents int64
		var refs []BillRef
		for _, l := range g {
			cents += store.Cents(l.bill.Total)
			refs = append(refs, refOf(l.bill))
		}
		name := g[0].bill.Contact.Name
		payee, known := ref.Payees[id]
		if known && payee.Name != "" {
			name = payee.Name
		}
		reason := checkPayee(payee, known, cents)
		if reason != "" {
			ex := Excluded{EmployeeID: id, Payee: name, Amount: store.Amount(cents), Reason: reason, Bills: refs}
			sum.Excluded = append(sum.Excluded, ex)
			continue
		}
		lines = append(lines, line{payee: payee, cents: cents, files: g,
			pay: Payment{EmployeeID: id, Payee: name, Amount: store.Amount(cents), Bills: refs}})
	}

	// Write the exceptions for the payees left out; their bills stay in bills/approved.
	for _, ex := range sum.Excluded {
		pe := PayeeException{Kind: "payee", EmployeeID: ex.EmployeeID, Payee: ex.Payee, PaymentDate: date, Reason: ex.Reason,
			At: now.Format(time.RFC3339), Amount: ex.Amount, Bills: ex.Bills,
			Note: "The bills stay in bills/approved and go into the next payment run once the payee's bank details are complete."}
		name := store.UniqueName(d.Path(store.Exceptions), fmt.Sprintf("PAYEE-%s-%s.json", ex.EmployeeID, strings.ReplaceAll(date, "-", "")))
		if err := store.WriteJSON(d.Path(store.Exceptions, name), pe); err != nil {
			return sum, err
		}
		log.Warn(fmt.Sprintf("%s (%s): left out of the payment file for %s: %s. %d bill(s), %s, stay in bills/approved.",
			ex.Payee, ex.EmployeeID, date, ex.Reason, len(ex.Bills), store.Money(store.Cents(ex.Amount))))
	}

	if len(lines) == 0 {
		sum.Message = fmt.Sprintf("Payment date %s: no payment file written; every payee was left out (%s).", date, excludedText(sum.Excluded))
		log.Info(sum.Message)
		return sum, nil
	}

	// Build the file.
	stamp := strings.ReplaceAll(date, "-", "")
	fileName := nextFileName(d, stamp)
	runRef := strings.TrimSuffix(fileName, ".csv")
	var buf bytes.Buffer
	fmt.Fprintf(&buf, "H,%s,%s,%s\r\n", field(opt.OrgAccount), stamp, runRef)
	var total int64
	for _, l := range lines {
		refs := make([]string, 0, len(l.pay.Bills))
		for _, b := range l.pay.Bills {
			refs = append(refs, b.Reference)
		}
		fmt.Fprintf(&buf, "D,%s,%s,%s,%s,%s\r\n", field(l.pay.Payee), field(l.payee.BankCode), field(l.payee.AccountNumber),
			store.Money(l.cents), field(cut(strings.Join(refs, " "), 70)))
		total += l.cents
	}
	fmt.Fprintf(&buf, "T,%d,%s\r\n", len(lines), store.Money(total))

	path := d.Path(store.Payments, fileName)
	if err := store.WriteFile(path, buf.Bytes()); err != nil {
		return sum, err
	}
	// Read the written file back and check it against what was meant to be in it, so the file
	// the officer uploads is the one that was checked.
	if err := verify(path, len(lines), total); err != nil {
		os.Remove(path)
		return sum, fmt.Errorf("the payment file %s failed its check and was removed: %w", fileName, err)
	}

	// Move the paid bills.
	paidAt := now.Format(time.RFC3339)
	billCount := 0
	for _, l := range lines {
		for _, f := range l.files {
			b := f.bill
			b.Status, b.PaidAt, b.PaymentFile = "paid", paidAt, fileName
			target := store.UniqueName(d.Path(store.BillsPaid), f.file)
			if err := store.WriteJSON(d.Path(store.BillsPaid, target), b); err != nil {
				return sum, err
			}
			if err := os.Remove(d.Path(store.BillsApproved, f.file)); err != nil {
				return sum, err
			}
			billCount++
		}
		log.Info(fmt.Sprintf("%s (%s): %s from %d bill(s) (%s) in %s.", l.pay.Payee, l.pay.EmployeeID, store.Money(l.cents),
			len(l.pay.Bills), billList(l.pay.Bills), fileName))
		sum.Payments = append(sum.Payments, l.pay)
	}

	sum.File = fileName
	sum.Lines = len(lines)
	sum.Total = store.Amount(total)
	sum.BillCount = billCount
	sum.Message = fmt.Sprintf("Payment file %s for %s: %d payment(s), total %s, from %d bill(s).", runRef, date, len(lines), store.Money(total), billCount)
	if len(sum.Excluded) > 0 {
		sum.Message += fmt.Sprintf(" %d payee(s) left out: %s.", len(sum.Excluded), excludedText(sum.Excluded))
	}
	if err := store.WriteJSON(d.Path(store.Payments, runRef+".summary.json"), sum); err != nil {
		return sum, err
	}
	log.Info(sum.Message)
	return sum, nil
}

// checkPayee returns why a payee cannot be paid, or "".
func checkPayee(p store.Payee, known bool, cents int64) string {
	switch {
	case !known:
		return "no bank details on record"
	case strings.TrimSpace(p.AccountNumber) == "":
		return "no account number on record"
	case strings.TrimSpace(p.BankCode) == "":
		return "no bank code on record"
	case !accountPattern.MatchString(strings.TrimSpace(p.AccountNumber)):
		return "account number is not 6 to 12 digits"
	case cents <= 0:
		return "amount to pay is not more than zero"
	}
	return ""
}

func verify(path string, wantLines int, wantTotal int64) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	var lines int
	var total int64
	var trailer []string
	for sc.Scan() {
		cols := strings.Split(strings.TrimRight(sc.Text(), "\r"), ",")
		switch cols[0] {
		case "D":
			if len(cols) != 6 {
				return fmt.Errorf("a payment line has %d columns, expected 6", len(cols))
			}
			v, err := strconv.ParseFloat(cols[4], 64)
			if err != nil {
				return fmt.Errorf("a payment amount %q is not a number", cols[4])
			}
			lines++
			total += store.Cents(v)
		case "T":
			trailer = cols
		}
	}
	if err := sc.Err(); err != nil {
		return err
	}
	if len(trailer) != 3 {
		return fmt.Errorf("the control line is missing")
	}
	n, _ := strconv.Atoi(trailer[1])
	t, _ := strconv.ParseFloat(trailer[2], 64)
	if lines != wantLines || n != wantLines || total != wantTotal || store.Cents(t) != wantTotal {
		return fmt.Errorf("lines %d (control %d, expected %d), total %s (control %s, expected %s)",
			lines, n, wantLines, store.Money(total), trailer[2], store.Money(wantTotal))
	}
	return nil
}

// nextFileName returns PAY_<date>_<n>.csv with the first n not yet used.
func nextFileName(d store.Dirs, stamp string) string {
	for n := 1; ; n++ {
		name := fmt.Sprintf("PAY_%s_%d.csv", stamp, n)
		if !store.Exists(d.Path(store.Payments, name)) {
			return name
		}
	}
}

func refOf(b store.Bill) BillRef {
	return BillRef{BillID: b.BillID, Reference: b.Reference, Amount: b.Total}
}

func billList(refs []BillRef) string {
	s := make([]string, 0, len(refs))
	for _, r := range refs {
		s = append(s, r.BillID)
	}
	return strings.Join(s, ", ")
}

func excludedText(ex []Excluded) string {
	s := make([]string, 0, len(ex))
	for _, e := range ex {
		s = append(s, fmt.Sprintf("%s (%s, %s), %s", e.Payee, billList(e.Bills), store.Money(store.Cents(e.Amount)), e.Reason))
	}
	return strings.Join(s, "; ")
}

// field makes a value safe for a CSV column: no commas, quotes or line breaks.
func field(s string) string {
	return strings.NewReplacer(",", " ", "\"", "", "\r", " ", "\n", " ").Replace(strings.TrimSpace(s))
}

func cut(s string, n int) string {
	if len([]rune(s)) <= n {
		return s
	}
	return string([]rune(s)[:n])
}

// ErrInvalid marks a request that cannot be run as asked.
var ErrInvalid = fmt.Errorf("invalid request")
