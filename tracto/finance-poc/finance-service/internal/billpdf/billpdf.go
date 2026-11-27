// Package billpdf draws a draft bill as a one-page PDF, the document the finance officer opens
// to review the bill before approving it.
package billpdf

import (
	"bytes"
	"fmt"

	"github.com/go-pdf/fpdf"

	"tracto/finance-service/internal/store"
)

// Render returns the PDF for a bill. org is the organisation name printed at the top.
func Render(b store.Bill, org string) ([]byte, error) {
	pdf := fpdf.New("P", "mm", "A4", "")
	pdf.SetTitle("Draft bill "+b.BillID, true)
	pdf.SetMargins(18, 18, 18)
	pdf.SetAutoPageBreak(true, 18)
	pdf.AddPage()
	tr := pdf.UnicodeTranslatorFromDescriptor("") // cp1252, so names with accents print

	// Header
	pdf.SetFont("Helvetica", "B", 18)
	pdf.CellFormat(0, 9, tr(org), "", 1, "L", false, 0, "")
	pdf.SetFont("Helvetica", "", 10)
	pdf.SetTextColor(110, 110, 110)
	pdf.CellFormat(0, 5, "Staff expense claim", "", 1, "L", false, 0, "")
	pdf.Ln(6)

	pdf.SetTextColor(0, 0, 0)
	pdf.SetFont("Helvetica", "B", 15)
	pdf.CellFormat(120, 8, "DRAFT BILL", "", 0, "L", false, 0, "")
	pdf.SetFont("Helvetica", "B", 11)
	pdf.CellFormat(0, 8, b.BillID, "", 1, "R", false, 0, "")
	pdf.Ln(3)

	field := func(label, value string) {
		pdf.SetFont("Helvetica", "", 10)
		pdf.SetTextColor(110, 110, 110)
		pdf.CellFormat(38, 6, label, "", 0, "L", false, 0, "")
		pdf.SetTextColor(0, 0, 0)
		pdf.SetFont("Helvetica", "B", 10)
		pdf.CellFormat(0, 6, tr(value), "", 1, "L", false, 0, "")
	}
	field("Payable to", fmt.Sprintf("%s (%s)", b.Contact.Name, b.Contact.EmployeeID))
	field("Reference", b.Reference)
	field("Bill date", b.BillDate)
	field("Status", "Draft, awaiting approval")
	pdf.Ln(6)

	// Lines
	widths := []float64{24, 62, 56, 32}
	heads := []string{"Date", "Description", "Account", "Amount"}
	pdf.SetFillColor(235, 239, 245)
	pdf.SetFont("Helvetica", "B", 10)
	for i, h := range heads {
		align := "L"
		if i == 3 {
			align = "R"
		}
		pdf.CellFormat(widths[i], 8, h, "B", 0, align, true, 0, "")
	}
	pdf.Ln(-1)
	pdf.SetFont("Helvetica", "", 10)
	for _, l := range b.Lines {
		account := l.AccountCode
		if l.AccountName != "" {
			account += " " + l.AccountName
		}
		pdf.CellFormat(widths[0], 7, l.Date, "", 0, "L", false, 0, "")
		pdf.CellFormat(widths[1], 7, tr(clip(pdf, l.Description, widths[1]-2)), "", 0, "L", false, 0, "")
		pdf.CellFormat(widths[2], 7, tr(clip(pdf, account, widths[2]-2)), "", 0, "L", false, 0, "")
		pdf.CellFormat(widths[3], 7, store.Money(store.Cents(l.Amount)), "", 1, "R", false, 0, "")
	}
	pdf.SetFont("Helvetica", "B", 11)
	pdf.CellFormat(widths[0]+widths[1]+widths[2], 9, "Total", "T", 0, "R", false, 0, "")
	pdf.CellFormat(widths[3], 9, store.Money(store.Cents(b.Total)), "T", 1, "R", false, 0, "")

	pdf.Ln(10)
	pdf.SetFont("Helvetica", "I", 9)
	pdf.SetTextColor(110, 110, 110)
	pdf.MultiCell(0, 5, fmt.Sprintf("Created by NeeliRPA from approved claim %s on %s. "+
		"Review the lines and approve the bill to include it in a payment run.", b.Reference, b.BillDate), "", "L", false)

	var buf bytes.Buffer
	if err := pdf.Output(&buf); err != nil {
		return nil, err
	}
	return buf.Bytes(), nil
}

// clip shortens text so it fits in width millimetres.
func clip(pdf *fpdf.Fpdf, s string, width float64) string {
	if pdf.GetStringWidth(s) <= width {
		return s
	}
	r := []rune(s)
	for len(r) > 0 && pdf.GetStringWidth(string(r)+"...") > width {
		r = r[:len(r)-1]
	}
	return string(r) + "..."
}
