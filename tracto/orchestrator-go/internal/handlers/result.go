package handlers

import (
	"database/sql"
	"encoding/json"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type ResultHandler struct{ db *sql.DB }

func NewResultHandler(db *sql.DB) *ResultHandler { return &ResultHandler{db: db} }

var secretPatterns = []*regexp.Regexp{
	regexp.MustCompile(`(?i)(password|passwd|pwd)["'\s]*[:=]["'\s]*\S+`),
	regexp.MustCompile(`(?i)(secret|api[_-]?key|apikey|token)["'\s]*[:=]["'\s]*\S+`),
	regexp.MustCompile(`(?i)(auth[_-]?token|bearer)\s+[A-Za-z0-9\-._~+/]+=*`),
	regexp.MustCompile(`(?i)(private[_-]?key|-----BEGIN)`),
	regexp.MustCompile(`(?i)aws[_-]?(secret[_-]?access[_-]?key|secret|access[_-]?key)`),
}

func containsSecret(s string) bool {
	for _, p := range secretPatterns {
		if p.MatchString(s) {
			return true
		}
	}
	return false
}

var terminalStates = map[string]bool{
	"succeeded": true, "failed": true, "handedoff": true,
	"cancelled": true, "timedout": true, "unknown": true,
}

// POST /jobs/{runId}/result
func (h *ResultHandler) CommitResult(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		AttemptId       string            `json:"attemptId"`
		TerminalState   string            `json:"terminalState"`
		ErrorCategory   *string           `json:"errorCategory"`
		BusinessOutcome *string           `json:"businessOutcome"`
		EvidenceRefs    []string          `json:"evidenceRefs"`
		BusinessJobId   *string           `json:"businessJobId"`
		UnknownReason   *string           `json:"unknownReason"`
		Payload         *string           `json:"payload"`
		Outputs         map[string]string `json:"outputs"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	stateKey := strings.ToLower(req.TerminalState)
	if !terminalStates[stateKey] {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_TERMINAL_STATE",
			"'"+req.TerminalState+"' is not a valid terminal state")
		return
	}
	if stateKey == "unknown" && (req.UnknownReason == nil || *req.UnknownReason == "") {
		errResp(w, http.StatusUnprocessableEntity, "UNKNOWN_REASON_REQUIRED",
			"unknownReason is required when terminalState is Unknown")
		return
	}
	if req.Payload != nil && containsSecret(*req.Payload) {
		errResp(w, http.StatusUnprocessableEntity, "SENSITIVE_PAYLOAD",
			"payload contains secret material and was rejected")
		return
	}
	for name, value := range req.Outputs {
		if containsSecret(name) || containsSecret(value) {
			errResp(w, http.StatusUnprocessableEntity, "SENSITIVE_PAYLOAD",
				"output '"+name+"' contains secret material and was rejected")
			return
		}
	}

	var runAttemptID, runState string
	var workerId sql.NullString
	err := h.db.QueryRow(`SELECT AttemptId,State,WorkerId FROM RunRecords WHERE Id=$1`, runID).
		Scan(&runAttemptID, &runState, &workerId)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if req.AttemptId != runAttemptID {
		errResp(w, http.StatusConflict, "WRONG_ATTEMPT", "AttemptId mismatch")
		return
	}

	// Idempotent: already terminal
	if isTerminal(runState) {
		respond(w, http.StatusOK, map[string]any{
			"runId": runID, "attemptId": req.AttemptId,
			"terminalState": strings.ToUpper(runState),
			"slotReleased":  false,
		})
		return
	}

	if req.EvidenceRefs == nil {
		req.EvidenceRefs = []string{}
	}
	evidenceJSON, _ := json.Marshal(req.EvidenceRefs)
	now := time.Now().UTC().Format(time.RFC3339Nano)

	// Map Go terminalState to canonical DB value
	dbState := canonicalState(req.TerminalState)
	needsAttention := stateKey == "unknown"

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()
	x := newTx(tx)

	// Update run state. The run must still be live: a second result, a cancel or a timeout that
	// got here first has already closed it, and the answer is then the idempotent one.
	if x.execRows(`UPDATE RunRecords SET State=$1,NeedsAttention=$2,UnknownReason=$3,UpdatedAt=$4 WHERE Id=$5 AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
		dbState, needsAttention, req.UnknownReason, now, runID) == 0 && x.err == nil {
		var cur string
		h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=$1`, runID).Scan(&cur)
		respond(w, http.StatusOK, map[string]any{
			"runId": runID, "attemptId": req.AttemptId,
			"terminalState": strings.ToUpper(cur),
			"slotReleased":  false,
		})
		return
	}

	// Insert FinalResultRecord
	frID := uuid.NewString()
	x.exec(`
		INSERT INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,UnknownReason,
		    ErrorCategory,BusinessOutcome,EvidenceRefsJson,BusinessJobId,CompletedAt)
		VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)`,
		frID, runID, req.AttemptId, dbState, needsAttention,
		req.UnknownReason, req.ErrorCategory, req.BusinessOutcome,
		string(evidenceJSON), req.BusinessJobId, now,
	)

	// Store the run's outputs for later segments and for GET /jobs/{runId}
	for name, value := range req.Outputs {
		x.exec(upsertRunOutput, runID, name, value, now)
	}

	// Release worker slot
	slotReleased := false
	if workerId.Valid && workerId.String != "" {
		if x.execRows(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=$1`, workerId.String) > 0 {
			slotReleased = true
		}
	}

	// Expire active lease
	x.exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=$1 AND State<>'Expired'`, runID)

	// Journal entry
	jID := uuid.NewString()
	evType := "RUN_" + strings.ToUpper(dbState)
	p, _ := json.Marshal(map[string]any{
		"terminalState": dbState, "slotReleased": slotReleased,
		"errorCategory": req.ErrorCategory, "businessOutcome": req.BusinessOutcome,
	})
	x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
		jID, runID, req.AttemptId, evType, now, string(p))

	// Telemetry
	telID := uuid.NewString()
	telMeta, _ := json.Marshal(map[string]any{"terminalState": dbState})
	x.exec(`INSERT INTO TelemetryEvents(Id,RunId,WorkerId,AttemptId,EventType,OccurredAt,Metadata) VALUES($1,$2,$3,$4,$5,$6,$7)`,
		telID, runID, workerId, req.AttemptId, "RUN_"+strings.ToUpper(dbState), now, string(telMeta))

	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}

	// Segment of a segmented script: queue the next segment, or close the parent run
	advanceSegments(h.db, runID, dbState)

	// Trigger continuation (best-effort, non-blocking)
	go triggerContinuation(h.db, runID, dbState, req.BusinessJobId)

	respond(w, http.StatusOK, map[string]any{
		"runId": runID, "attemptId": req.AttemptId,
		"terminalState": strings.ToUpper(dbState),
		"slotReleased":  slotReleased,
	})
}

func isTerminal(state string) bool {
	s := strings.ToLower(state)
	return terminalStates[s]
}

func canonicalState(s string) string {
	switch strings.ToLower(s) {
	case "succeeded":
		return "Succeeded"
	case "failed":
		return "Failed"
	case "handedoff":
		return "HandedOff"
	case "cancelled":
		return "Cancelled"
	case "timedout":
		return "TimedOut"
	case "unknown":
		return "Unknown"
	default:
		return s
	}
}

func triggerContinuation(db *sql.DB, sourceRunID, terminalState string, businessJobId *string) {
	// Find matching continuation mapping: run-specific first, then global
	var mappingID, targetArtifactID, targetVersion string
	var mappingVersion int
	err := db.QueryRow(
		`SELECT MappingId,TargetArtifactId,TargetArtifactVersion,Version
		 FROM ContinuationMappings WHERE TriggerRunId=$1 AND TriggerState=$2`,
		sourceRunID, terminalState,
	).Scan(&mappingID, &targetArtifactID, &targetVersion, &mappingVersion)
	if err == sql.ErrNoRows {
		// Global fallback
		err = db.QueryRow(
			`SELECT MappingId,TargetArtifactId,TargetArtifactVersion,Version
			 FROM ContinuationMappings WHERE TriggerRunId IS NULL AND TriggerState=$1 LIMIT 1`,
			terminalState,
		).Scan(&mappingID, &targetArtifactID, &targetVersion, &mappingVersion)
	}
	if err != nil {
		return
	}

	// Dedup guard: already triggered for this (source, state) pair
	var existing string
	err = db.QueryRow(
		`SELECT Id FROM ContinuationRunRecords WHERE SourceRunId=$1 AND TriggerState=$2`,
		sourceRunID, terminalState,
	).Scan(&existing)
	if err == nil {
		return // already fired
	}

	// Create the continuation run
	runB := uuid.NewString()
	attemptB := uuid.NewString()
	corrB := uuid.NewString()
	now := time.Now().UTC().Format(time.RFC3339Nano)

	tx, err := db.Begin()
	if err != nil {
		return
	}
	defer tx.Rollback()
	x := newTx(tx)

	x.exec(`
		INSERT INTO RunRecords(Id,WorkflowId,State,AttemptId,CreatedAt,CorrelationId,
		    ScriptArtifactId,ScriptVersion,SchemaVersion,InputRefsJson,SecretRefsJson,TriggerType,NeedsAttention)
		VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,FALSE)`,
		runB, targetArtifactID, "Queued", attemptB, now, corrB,
		targetArtifactID, targetVersion, "1", "[]", "[]", "Continuation",
	)

	linkID := uuid.NewString()
	x.exec(`
		INSERT INTO ContinuationRunRecords(Id,SourceRunId,TriggerState,RunBId,MappingId,MappingVersionSnapshot,CreatedAt)
		VALUES($1,$2,$3,$4,$5,$6,$7)`,
		linkID, sourceRunID, terminalState, runB, mappingID, mappingVersion, now,
	)

	jID := uuid.NewString()
	p, _ := json.Marshal(map[string]string{"sourceRunId": sourceRunID, "runBId": runB, "mappingId": mappingID})
	x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
		jID, runB, attemptB, "RUN_QUEUED", now, string(p))

	_ = x.commit() // best effort; a continuation that could not be written is simply not queued
}
