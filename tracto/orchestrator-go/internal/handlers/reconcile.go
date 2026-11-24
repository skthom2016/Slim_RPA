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
		LeaseId           string `json:"leaseId"`
		WorkerId          string `json:"workerId"`
		LastKnownStepId   string `json:"lastKnownStepId"`
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
		err := h.db.QueryRow(`SELECT AttemptId FROM LeaseRecords WHERE LeaseId=$1`, req.LeaseId).Scan(&req.AttemptId)
		if err == sql.ErrNoRows {
			errResp(w, http.StatusNotFound, "NOT_FOUND", "lease not found")
			return
		}
	}

	var runAttemptID, runState string
	err := h.db.QueryRow(`SELECT AttemptId,State FROM RunRecords WHERE Id=$1`, runID).Scan(&runAttemptID, &runState)
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

	// The worker the run was on
	var workerID sql.NullString
	h.db.QueryRow(`SELECT WorkerId FROM RunRecords WHERE Id=$1`, runID).Scan(&workerID)

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
	x := newTx(tx)

	var finalState string
	var evType string
	var closed int64

	// The run must still be live and still on this attempt: a result, a cancel or a timeout that got
	// here first has closed it, and a requeue since the check above has given it a new attempt.
	if isConfirmedSucceeded {
		finalState = "Succeeded"
		evType = "RUN_RECONCILED_SUCCEEDED"
		closed = x.execRows(`UPDATE RunRecords SET State='Succeeded',UpdatedAt=$1 WHERE Id=$2 AND AttemptId=$3 AND State NOT IN `+terminalStatesSQL,
			now, runID, req.AttemptId)
	} else {
		finalState = "Unknown"
		evType = "RUN_RECONCILED_UNKNOWN"
		closed = x.execRows(`UPDATE RunRecords SET State='Unknown',NeedsAttention=TRUE,UnknownReason=$1,UpdatedAt=$2 WHERE Id=$3 AND AttemptId=$4 AND State NOT IN `+terminalStatesSQL,
			"Reconciliation: uncertain journal", now, runID, req.AttemptId)
	}
	if closed == 0 && x.err == nil {
		var cur string
		h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=$1`, runID).Scan(&cur)
		if !isTerminal(cur) {
			errResp(w, http.StatusConflict, "WRONG_ATTEMPT", "AttemptId mismatch")
			return
		}
		respond(w, http.StatusOK, map[string]any{
			"runId": runID, "attemptId": req.AttemptId,
			"reconciledState": strings.ToUpper(cur),
		})
		return
	}

	// Release worker slot + expire lease (same as CommitResult)
	if workerID.Valid && workerID.String != "" {
		x.exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=$1`, workerID.String)
	}
	x.exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=$1 AND State<>'Expired'`, runID)

	// FinalResultRecord
	frID := uuid.NewString()
	unknownReason := sql.NullString{}
	if !isConfirmedSucceeded {
		unknownReason = sql.NullString{String: "Reconciliation: uncertain journal", Valid: true}
	}
	x.exec(`
		INSERT INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,UnknownReason,EvidenceRefsJson,CompletedAt)
		VALUES($1,$2,$3,$4,$5,$6,'[]',$7)`,
		frID, runID, req.AttemptId, finalState,
		!isConfirmedSucceeded,
		unknownReason, now,
	)

	// Journal
	jID := uuid.NewString()
	p, _ := json.Marshal(map[string]any{
		"source":       "reconciliation",
		"outcome":      finalState,
		"journalCount": len(req.JournalEntries),
	})
	x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
		jID, runID, req.AttemptId, evType, now, string(p))

	// Telemetry
	telID := uuid.NewString()
	x.exec(`INSERT INTO TelemetryEvents(Id,RunId,AttemptId,EventType,OccurredAt) VALUES($1,$2,$3,$4,$5)`,
		telID, runID, req.AttemptId, "RUN_"+strings.ToUpper(finalState), now)

	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}

	respond(w, http.StatusOK, map[string]any{
		"runId": runID, "attemptId": req.AttemptId,
		"reconciledState": strings.ToUpper(finalState),
	})
}
