package handlers

import (
	"database/sql"
	"encoding/json"
	"errors"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/jackc/pgx/v5/pgconn"
)

// respond writes a JSON response.
func respond(w http.ResponseWriter, code int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(code)
	if body != nil {
		json.NewEncoder(w).Encode(body)
	}
}

// errResp writes a standard JSON error.
func errResp(w http.ResponseWriter, code int, errCode, msg string) {
	respond(w, code, map[string]string{"error": errCode, "message": msg})
}

// dbErr writes a 500 DB_ERROR for err.
func dbErr(w http.ResponseWriter, err error) {
	errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
}

// decodeJSON decodes request body; returns false and writes 400 on failure.
func decodeJSON(w http.ResponseWriter, r *http.Request, dst any) bool {
	if err := json.NewDecoder(r.Body).Decode(dst); err != nil {
		errResp(w, http.StatusBadRequest, "INVALID_JSON", err.Error())
		return false
	}
	return true
}

// now returns the current UTC time formatted as RFC3339Nano.
func nowStr() string { return time.Now().UTC().Format(time.RFC3339Nano) }

// noNUL removes NUL characters. SQLite stored them; PostgreSQL text refuses them, which would fail the
// whole write (a log batch, or a result a worker would then retry for ever).
func noNUL(s string) string { return strings.ReplaceAll(s, "\x00", "") }

// terminalStatesSQL lists the states a run never leaves.
const terminalStatesSQL = "('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')"

// nullStr returns sql.NullString from a *string.
func nullStr(s *string) sql.NullString {
	if s == nil {
		return sql.NullString{}
	}
	return sql.NullString{String: noNUL(*s), Valid: true}
}

func strPtr(s string) *string { return &s }

// upsertRunOutput stores one output of a run, replacing an earlier value of the same name.
const upsertRunOutput = `INSERT INTO RunOutputs(RunId,Name,Value,CreatedAt) VALUES($1,$2,$3,$4)
	ON CONFLICT (RunId,Name) DO UPDATE SET Value=EXCLUDED.Value, CreatedAt=EXCLUDED.CreatedAt`

// argN is the placeholder for the next argument of a query that is built piece by piece: after
// two arguments have been appended it returns "$3".
func argN(args []any) string { return "$" + strconv.Itoa(len(args)+1) }

// isUniqueViolation reports whether err is PostgreSQL's unique-constraint error (23505).
func isUniqueViolation(err error) bool {
	var pgErr *pgconn.PgError
	return errors.As(err, &pgErr) && pgErr.Code == "23505"
}

// txRun runs the statements of one transaction and keeps the first error. PostgreSQL aborts a
// transaction at its first failed statement, so every later statement fails as well; keeping the
// first error lets a handler write its statements one after another, check err once, and roll
// back with the real cause instead of committing half a change.
type txRun struct {
	tx  *sql.Tx
	err error
}

func newTx(tx *sql.Tx) *txRun { return &txRun{tx: tx} }

// exec runs a statement unless an earlier one failed.
func (t *txRun) exec(query string, args ...any) {
	if t.err != nil {
		return
	}
	if _, err := t.tx.Exec(query, args...); err != nil {
		t.err = err
	}
}

// execRows is exec for a statement whose affected-row count matters.
func (t *txRun) execRows(query string, args ...any) int64 {
	if t.err != nil {
		return 0
	}
	res, err := t.tx.Exec(query, args...)
	if err != nil {
		t.err = err
		return 0
	}
	n, _ := res.RowsAffected()
	return n
}

// commit commits when every statement succeeded, and returns the first error otherwise. The
// caller's deferred Rollback cleans up.
func (t *txRun) commit() error {
	if t.err != nil {
		return t.err
	}
	return t.tx.Commit()
}
