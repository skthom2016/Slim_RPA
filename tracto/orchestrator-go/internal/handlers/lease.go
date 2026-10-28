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

type LeaseHandler struct{ db *sql.DB }

func NewLeaseHandler(db *sql.DB) *LeaseHandler { return &LeaseHandler{db: db} }

// POST /jobs/{runId}/lease/accept
func (h *LeaseHandler) AcceptLease(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		AttemptId string `json:"attemptId"`
		WorkerId  string `json:"workerId"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	// Load run
	var runState, runAttemptID, scriptArtifactId, scriptVersion string
	var inputJSON, secretJSON sql.NullString
	err := h.db.QueryRow(
		`SELECT State,AttemptId,ScriptArtifactId,ScriptVersion,InputRefsJson,SecretRefsJson FROM RunRecords WHERE Id=?`,
		runID,
	).Scan(&runState, &runAttemptID, &scriptArtifactId, &scriptVersion, &inputJSON, &secretJSON)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// AttemptId fence
	if req.AttemptId != runAttemptID {
		errResp(w, http.StatusConflict, "WRONG_ATTEMPT", "AttemptId mismatch")
		return
	}

	// Load lease
	var leaseState, expiresAt, artifactHash, artifactID, artifactVersion, leaseWorkerID string
	err = h.db.QueryRow(
		`SELECT State,ExpiresAt,ArtifactHash,ArtifactId,ArtifactVersion,WorkerId FROM LeaseRecords WHERE RunId=? AND AttemptId=? AND State<>'Expired' LIMIT 1`,
		runID, req.AttemptId,
	).Scan(&leaseState, &expiresAt, &artifactHash, &artifactID, &artifactVersion, &leaseWorkerID)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusConflict, "LEASE_EXPIRED", "no active lease found")
		return
	}

	// WorkerId fence
	if leaseWorkerID != req.WorkerId {
		errResp(w, http.StatusConflict, "WRONG_WORKER", "workerId mismatch")
		return
	}

	// Idempotent: already accepted
	if strings.EqualFold(leaseState, "Accepted") {
		var inputRefs, secretRefs []string
		if inputJSON.Valid {
			json.Unmarshal([]byte(inputJSON.String), &inputRefs)
		}
		if secretJSON.Valid {
			json.Unmarshal([]byte(secretJSON.String), &secretRefs)
		}
		respond(w, http.StatusOK, map[string]any{
			"runId": runID, "attemptId": req.AttemptId,
			"artifactId": artifactID, "artifactVersion": artifactVersion,
			"artifactHash": artifactHash,
			"inputRefs": inputRefs, "secretRefs": secretRefs,
		})
		return
	}

	// Check expiry
	exp, _ := time.Parse(time.RFC3339Nano, expiresAt)
	now := time.Now().UTC()
	if now.After(exp) {
		// Expired — check if run was ever RUNNING
		var runningCount int
		h.db.QueryRow(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=? AND EventType='RUN_RUNNING'`, runID).Scan(&runningCount)
		newAttemptID := uuid.NewString()
		nowStr := now.Format(time.RFC3339Nano)
		if runningCount > 0 {
			// Requeue with new fence
			h.db.Exec(`UPDATE RunRecords SET State='Queued',AttemptId=?,WorkerId=NULL,UpdatedAt=? WHERE Id=?`,
				newAttemptID, nowStr, runID)
		} else {
			// Mark Unknown
			h.db.Exec(`UPDATE RunRecords SET State='Unknown',NeedsAttention=1,UnknownReason=?,UpdatedAt=? WHERE Id=?`,
				"Lease expired before accept; run state unknown", nowStr, runID)
		}
		h.db.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=? AND AttemptId=?`, runID, req.AttemptId)
		errResp(w, http.StatusConflict, "LEASE_EXPIRED", "lease expired")
		return
	}

	nowStr := now.Format(time.RFC3339Nano)
	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	// Lease: Offered → Accepted
	tx.Exec(`UPDATE LeaseRecords SET State='Accepted',AcceptedAt=? WHERE RunId=? AND AttemptId=?`,
		nowStr, runID, req.AttemptId)

	// Run: Queued → Leased → Running (both transitions in one shot)
	tx.Exec(`UPDATE RunRecords SET State='Running',UpdatedAt=? WHERE Id=?`, nowStr, runID)

	// Worker: Available → Running
	tx.Exec(`UPDATE WorkerRecords SET State='Running' WHERE WorkerId=?`, req.WorkerId)

	// Journal: RUN_RUNNING
	jID := uuid.NewString()
	p, _ := json.Marshal(map[string]string{"workerId": req.WorkerId, "leaseId": "accepted"})
	tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		jID, runID, req.AttemptId, "RUN_RUNNING", nowStr, string(p))

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	var inputRefs, secretRefs []string
	if inputJSON.Valid {
		json.Unmarshal([]byte(inputJSON.String), &inputRefs)
	}
	if secretJSON.Valid {
		json.Unmarshal([]byte(secretJSON.String), &secretRefs)
	}

	respond(w, http.StatusOK, map[string]any{
		"runId": runID, "attemptId": req.AttemptId,
		"artifactId": artifactID, "artifactVersion": artifactVersion,
		"artifactHash": artifactHash,
		"inputRefs": inputRefs, "secretRefs": secretRefs,
	})
}

// POST /jobs/{runId}/heartbeat
func (h *LeaseHandler) Heartbeat(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		AttemptId string `json:"attemptId"`
		WorkerId  string `json:"workerId"`
		SeqNo     int64  `json:"seqNo"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	// Load run
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

	// Load active lease
	var leaseState, leaseWorkerID string
	var lastSeqNo int64
	var cancellationRequested int
	err = h.db.QueryRow(
		`SELECT State,WorkerId,LastSeqNo,CancellationRequested FROM LeaseRecords WHERE RunId=? AND AttemptId=? AND State<>'Expired' LIMIT 1`,
		runID, req.AttemptId,
	).Scan(&leaseState, &leaseWorkerID, &lastSeqNo, &cancellationRequested)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusConflict, "LEASE_EXPIRED", "no active lease")
		return
	}
	if leaseWorkerID != req.WorkerId {
		errResp(w, http.StatusConflict, "WRONG_WORKER", "workerId mismatch")
		return
	}
	if !strings.EqualFold(leaseState, "Accepted") {
		errResp(w, http.StatusConflict, "WRONG_STATE", "lease not in Accepted state")
		return
	}

	// SeqNo check
	if req.SeqNo < lastSeqNo {
		errResp(w, http.StatusConflict, "STALE_SEQ_NO", "sequence number is stale")
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	h.db.Exec(`UPDATE LeaseRecords SET LastHeartbeatAt=?,LastSeqNo=? WHERE RunId=? AND AttemptId=?`,
		now, req.SeqNo, runID, req.AttemptId)

	respond(w, http.StatusOK, map[string]bool{
		"cancellationRequested": cancellationRequested != 0,
	})
}
