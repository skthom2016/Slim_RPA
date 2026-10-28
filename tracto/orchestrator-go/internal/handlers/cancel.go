package handlers

import (
	"database/sql"
	"encoding/json"
	"net/http"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type CancelHandler struct{ db *sql.DB }

func NewCancelHandler(db *sql.DB) *CancelHandler { return &CancelHandler{db: db} }

// POST /jobs/{runId}/cancel
func (h *CancelHandler) Cancel(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		RequestedBy *string `json:"requestedBy"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.RequestedBy == nil || *req.RequestedBy == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "requestedBy is required")
		return
	}

	var runState, runAttemptID string
	err := h.db.QueryRow(`SELECT State,AttemptId FROM RunRecords WHERE Id=?`, runID).Scan(&runState, &runAttemptID)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}

	if strings.EqualFold(runState, "Cancelled") {
		respond(w, http.StatusOK, map[string]string{"runId": runID})
		return
	}

	if isTerminal(runState) {
		errResp(w, http.StatusConflict, "TERMINAL_STATE",
			"run '"+runID+"' is in terminal state "+runState+" and cannot be cancelled")
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	p, _ := json.Marshal(map[string]any{"requestedBy": *req.RequestedBy})

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	if strings.EqualFold(runState, "Running") {
		// Signal cancellation via lease
		tx.Exec(`UPDATE LeaseRecords SET CancellationRequested=1 WHERE RunId=? AND State='Accepted'`, runID)
		jID := uuid.NewString()
		tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			jID, runID, runAttemptID, "RUN_CANCEL_REQUESTED", now, string(p))
	} else {
		// QUEUED or LEASED → immediately cancel
		tx.Exec(`UPDATE RunRecords SET State='Cancelled',UpdatedAt=? WHERE Id=?`, now, runID)
		tx.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=? AND State<>'Expired'`, runID)
		jID := uuid.NewString()
		tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			jID, runID, runAttemptID, "RUN_CANCELLED", now, string(p))
		telID := uuid.NewString()
		tx.Exec(`INSERT INTO TelemetryEvents(Id,RunId,AttemptId,EventType,OccurredAt) VALUES(?,?,?,?,?)`,
			telID, runID, runAttemptID, "RUN_CANCELLED", now)
	}

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusOK, map[string]string{"runId": runID})
}
