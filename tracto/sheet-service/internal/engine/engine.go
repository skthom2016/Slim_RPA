// Package engine holds the source-independent rules: which rows of a batch still need action.
// Flow-specific behaviour is expressed in the request, never hard-coded here.
package engine

import (
	"fmt"
	"strings"

	"tracto/sheet-service/internal/sheet"
)

// Pending describes when a row still needs action: its Column holds none of the DoneValues
// (compared case-insensitively, ignoring surrounding spaces). An empty cell is always pending.
type Pending struct {
	Column     string   `json:"column"`
	DoneValues []string `json:"doneValues"`
}

// PendingRows returns the rows of b that still need action, in sheet order.
// A nil p means every row of the batch is wanted.
func PendingRows(b *sheet.Batch, p *Pending) ([]sheet.Row, error) {
	if p == nil {
		return b.Rows, nil
	}
	if !hasColumn(b.Header, p.Column) {
		return nil, fmt.Errorf("column %q not found in header", p.Column)
	}
	done := make(map[string]bool, len(p.DoneValues))
	for _, v := range p.DoneValues {
		done[norm(v)] = true
	}

	var out []sheet.Row
	for _, r := range b.Rows {
		if !done[norm(r.Values[p.Column])] {
			out = append(out, r)
		}
	}
	return out, nil
}

func norm(s string) string { return strings.ToLower(strings.TrimSpace(s)) }

func hasColumn(header []string, name string) bool {
	for _, h := range header {
		if h == name {
			return true
		}
	}
	return false
}
