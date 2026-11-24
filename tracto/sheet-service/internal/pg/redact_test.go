package pg

import (
	"errors"
	"strings"
	"testing"
)

// An error that quotes the connection must never carry the password, in the form it has in the
// URL or the decoded form a driver may print.
func TestRedactURL_HidesThePasswordInBothForms(t *testing.T) {
	url := "postgres://tracto:p%40ss%2Fword@localhost:5432/tracto"
	for _, quoted := range []string{"p%40ss%2Fword", "p@ss/word"} {
		got := redactURL(errors.New("dial failed for user tracto password "+quoted+" on localhost"), url).Error()
		if strings.Contains(got, quoted) {
			t.Errorf("password %q still in: %s", quoted, got)
		}
		if !strings.Contains(got, "localhost") {
			t.Errorf("the rest of the message must survive: %s", got)
		}
	}
	if got := redactURL(nil, url); got != nil {
		t.Errorf("nil stays nil: %v", got)
	}
}
