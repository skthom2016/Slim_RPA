package handlers

import (
	"database/sql"
	"encoding/json"
	"net/http"
	"time"
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

// nullStr returns sql.NullString from a *string.
func nullStr(s *string) sql.NullString {
	if s == nil {
		return sql.NullString{}
	}
	return sql.NullString{String: *s, Valid: true}
}

func strPtr(s string) *string { return &s }
