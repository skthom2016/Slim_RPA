package handlers

import (
	"database/sql"
	"net/http"
	"strconv"
	"strings"
	"unicode/utf8"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type LogHandler struct{ db *sql.DB }

func NewLogHandler(db *sql.DB) *LogHandler { return &LogHandler{db: db} }

// How many entries one request may carry, and how long a single message may be. A runtime
// that loops on an error should not be able to fill the database with one run's noise.
const (
	maxLogEntriesPerRequest = 500
	maxLogMessageChars      = 4000
)

// storedLevels are the only levels kept. A worker's Info output is its own console business:
// keeping it would bury the two lines that explain a failure, which is what these are for.
var storedLevels = map[string]string{
	"warn": "Warning", "warning": "Warning",
	"error": "Error", "err": "Error",
	"critical": "Error", "fatal": "Error",
}

// redactSecrets blanks anything that looks like a credential before it is stored. A log line
// is written for a person to read later, so unlike a result payload — which is rejected
// outright — it is kept with the secret removed rather than thrown away.
func redactSecrets(s string) string {
	for _, p := range secretPatterns {
		s = p.ReplaceAllString(s, "[redacted]")
	}
	return s
}

// POST /jobs/{runId}/logs
// A runtime posts what it logged while running a job. Entries at Info or below are dropped
// rather than refused, so a worker can send its whole buffer without filtering first.
func (h *LogHandler) Append(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")

	var req struct {
		WorkerId  *string `json:"workerId"`
		Runtime   *string `json:"runtime"`
		AttemptId *string `json:"attemptId"`
		Entries   []struct {
			Level      string  `json:"level"`
			StepId     *string `json:"stepId"`
			Message    string  `json:"message"`
			OccurredAt *string `json:"occurredAt"`
		} `json:"entries"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if len(req.Entries) > maxLogEntriesPerRequest {
		errResp(w, http.StatusRequestEntityTooLarge, "TOO_MANY_ENTRIES",
			"a request carries at most "+strconv.Itoa(maxLogEntriesPerRequest)+" entries")
		return
	}

	var exists int
	h.db.QueryRow(`SELECT COUNT(*) FROM RunRecords WHERE Id=?`, runID).Scan(&exists)
	if exists == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}

	now := nowStr()
	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	stored := 0
	for _, e := range req.Entries {
		level, keep := storedLevels[strings.ToLower(strings.TrimSpace(e.Level))]
		if !keep {
			continue
		}
		msg := redactSecrets(strings.TrimSpace(e.Message))
		if msg == "" {
			continue
		}
		// Cut on characters, not bytes: slicing a Go string by byte splits a multi-byte
		// character in half and stores a replacement character in its place.
		if utf8.RuneCountInString(msg) > maxLogMessageChars {
			msg = string([]rune(msg)[:maxLogMessageChars]) + "… (truncated)"
		}
		occurred := now
		if e.OccurredAt != nil && *e.OccurredAt != "" {
			occurred = *e.OccurredAt
		}
		_, err := tx.Exec(`
			INSERT INTO RunLogs(Id,RunId,AttemptId,WorkerId,Runtime,Level,StepId,Message,OccurredAt,ReceivedAt)
			VALUES(?,?,?,?,?,?,?,?,?,?)`,
			uuid.NewString(), runID, nullStr(req.AttemptId), nullStr(req.WorkerId),
			nullStr(req.Runtime), level, nullStr(e.StepId), msg, occurred, now)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
		stored++
	}

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusAccepted, map[string]any{
		"runId": runID, "received": len(req.Entries), "stored": stored,
	})
}

type logRow struct {
	Id         string  `json:"id"`
	RunId      string  `json:"runId"`
	AttemptId  *string `json:"attemptId"`
	WorkerId   *string `json:"workerId"`
	Runtime    *string `json:"runtime"`
	Level      string  `json:"level"`
	StepId     *string `json:"stepId"`
	Message    string  `json:"message"`
	OccurredAt string  `json:"occurredAt"`
	SegmentRun *string `json:"segmentRunId"` // set when the entry came from a segment of this run
}

func scanLogs(rows *sql.Rows) []logRow {
	out := []logRow{}
	for rows.Next() {
		var l logRow
		var attempt, worker, runtime, stepID sql.NullString
		if err := rows.Scan(&l.Id, &l.RunId, &attempt, &worker, &runtime, &l.Level,
			&stepID, &l.Message, &l.OccurredAt); err != nil {
			continue
		}
		if attempt.Valid {
			l.AttemptId = &attempt.String
		}
		if worker.Valid {
			l.WorkerId = &worker.String
		}
		if runtime.Valid {
			l.Runtime = &runtime.String
		}
		if stepID.Valid {
			l.StepId = &stepID.String
		}
		out = append(out, l)
	}
	return out
}

// GET /admin/logs/{runId}?level=Error
// Returns the run's own entries and those of its segment runs together, oldest first, so a
// segmented script reads as one story rather than one list per segment.
func (h *LogHandler) GetByRun(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")

	// The OR group is bracketed: without it AND binds tighter, and a level filter would apply
	// only to the segment rows while the parent's own rows came back unfiltered.
	query := `
		SELECT Id,RunId,AttemptId,WorkerId,Runtime,Level,StepId,Message,OccurredAt
		FROM RunLogs
		WHERE (RunId=? OR RunId IN (SELECT Id FROM RunRecords WHERE ParentRunId=?))`
	args := []any{runID, runID}

	if lvl := r.URL.Query().Get("level"); lvl != "" {
		query += ` AND Level=?`
		args = append(args, lvl)
	}
	query += ` ORDER BY OccurredAt ASC`

	rows, err := h.db.Query(query, args...)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	logs := scanLogs(rows)
	for i := range logs {
		if logs[i].RunId != runID {
			id := logs[i].RunId
			logs[i].SegmentRun = &id
		}
	}
	respond(w, http.StatusOK, map[string]any{"runId": runID, "count": len(logs), "logs": logs})
}

// GET /admin/logs?level=Error&limit=200
// The most recent entries across every run, for when there is no run id to hand.
func (h *LogHandler) List(w http.ResponseWriter, r *http.Request) {
	limit := 200
	if v := r.URL.Query().Get("limit"); v != "" {
		if n, err := strconv.Atoi(v); err == nil && n > 0 && n <= 1000 {
			limit = n
		}
	}

	query := `
		SELECT Id,RunId,AttemptId,WorkerId,Runtime,Level,StepId,Message,OccurredAt
		FROM RunLogs`
	args := []any{}
	if lvl := r.URL.Query().Get("level"); lvl != "" {
		query += ` WHERE Level=?`
		args = append(args, lvl)
	}
	query += ` ORDER BY OccurredAt DESC LIMIT ?`
	args = append(args, limit)

	rows, err := h.db.Query(query, args...)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	logs := scanLogs(rows)
	respond(w, http.StatusOK, map[string]any{"count": len(logs), "logs": logs})
}
