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

type ReconcileHandler struct{ db *sql.DB }

func NewReconcileHandler(db *sql.DB) *ReconcileHandler { return &ReconcileHandler{db: db} }

// POST /jobs/{runId}/reconcile
// Accepts either {attemptId, journalEntries} (Go-native) or
// {leaseId, workerId, lastKnownStepId, lastKnownRunState} (Desktop Runtime format)
func (h *ReconcileHandler) Reconcile(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		// Desktop Runtime format
		LeaseId          string `json:"leaseId"`
		WorkerId         string `json:"workerId"`
		LastKnownStepId  string `json:"lastKnownStepId"`
		LastKnownRunState string `json:"lastKnownRunState"`
		// Go-native format
		AttemptId      string `json:"attemptId"`
		JournalEntries []struct {
			EventType  string `json:"eventType"`
			AttemptId  string `json:"attemptId"`
			OccurredAt string `json:"occurredAt"`
		} `json:"journalEntries"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	// If leaseId provided (Desktop Runtime), resolve attemptId from lease
	if req.LeaseId != "" && req.AttemptId == "" {
		err := h.db.QueryRow(`SELECT AttemptId FROM LeaseRecords WHERE LeaseId=?`, req.LeaseId).Scan(&req.AttemptId)
		if err == sql.ErrNoRows {
			errResp(w, http.StatusNotFound, "NOT_FOUND", "lease not found")
			return
		}
	}

	var runAttemptID, runState string
	err := h.db.QueryRow(`SELECT AttemptId,State FROM RunRecords WHERE Id=?`, runID).Scan(&runAttemptID, &runState)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if req.AttemptId != runAttemptID {
		errResp(w, http.StatusConflict, "WRONG_ATTEMPT", "AttemptId mismatch")
		return
	}
	if isTerminal(runState) {
		respond(w, http.StatusOK, map[string]any{
			"runId": runID, "attemptId": req.AttemptId,
			"reconciledState": strings.ToUpper(runState),
		})
		return
	}

	// Fetch workerID BEFORE opening the write transaction (avoids SQLite deadlock)
	var workerID sql.NullString
	h.db.QueryRow(`SELECT WorkerId FROM RunRecords WHERE Id=?`, runID).Scan(&workerID)

	// Check for RUN_SUCCEEDED with matching AttemptId in supplied journal
	isConfirmedSucceeded := false
	for _, e := range req.JournalEntries {
		if e.EventType == "RUN_SUCCEEDED" && e.AttemptId == req.AttemptId {
			isConfirmedSucceeded = true
			break
		}
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	var finalState string
	var evType string

	if isConfirmedSucceeded {
		finalState = "Succeeded"
		evType = "RUN_RECONCILED_SUCCEEDED"
		tx.Exec(`UPDATE RunRecords SET State='Succeeded',UpdatedAt=? WHERE Id=?`, now, runID)
	} else {
		finalState = "Unknown"
		evType = "RUN_RECONCILED_UNKNOWN"
		tx.Exec(`UPDATE RunRecords SET State='Unknown',NeedsAttention=1,UnknownReason=?,UpdatedAt=? WHERE Id=?`,
			"Reconciliation: uncertain journal", now, runID)
	}

	// Release worker slot + expire lease (same as CommitResult)
	if workerID.Valid && workerID.String != "" {
		tx.Exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=?`, workerID.String)
	}
	tx.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=? AND State<>'Expired'`, runID)

	// FinalResultRecord
	frID := uuid.NewString()
	unknownReason := sql.NullString{}
	if !isConfirmedSucceeded {
		unknownReason = sql.NullString{String: "Reconciliation: uncertain journal", Valid: true}
	}
	tx.Exec(`
		INSERT INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,UnknownReason,EvidenceRefsJson,CompletedAt)
		VALUES(?,?,?,?,?,?,'[]',?)`,
		frID, runID, req.AttemptId, finalState,
		func() int {
			if !isConfirmedSucceeded {
				return 1
			}
			return 0
		}(),
		unknownReason, now,
	)

	// Journal
	jID := uuid.NewString()
	p, _ := json.Marshal(map[string]any{
		"source":       "reconciliation",
		"outcome":      finalState,
		"journalCount": len(req.JournalEntries),
	})
	tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		jID, runID, req.AttemptId, evType, now, string(p))

	// Telemetry
	telID := uuid.NewString()
	tx.Exec(`INSERT INTO TelemetryEvents(Id,RunId,AttemptId,EventType,OccurredAt) VALUES(?,?,?,?,?)`,
		telID, runID, req.AttemptId, "RUN_"+strings.ToUpper(finalState), now)

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusOK, map[string]any{
		"runId": runID, "attemptId": req.AttemptId,
		"reconciledState": strings.ToUpper(finalState),
	})
}
