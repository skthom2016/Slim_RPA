package handlers

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type JobHandler struct{ db *sql.DB }

func NewJobHandler(db *sql.DB) *JobHandler { return &JobHandler{db: db} }

// POST /jobs
func (h *JobHandler) CreateJob(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ScriptArtifactId       string   `json:"scriptArtifactId"`
		ScriptVersion          string   `json:"scriptVersion"`
		SchemaVersion          string   `json:"schemaVersion"`
		InputRefs              []string `json:"inputRefs"`
		SecretRefs             []string `json:"secretRefs"`
		Deadline               *string  `json:"deadline"`
		EventId                *string  `json:"eventId"`
		TriggerType            string   `json:"triggerType"`
		ContinuationScriptRef  *string  `json:"continuationScriptRef"`
		BusinessJobId          *string  `json:"businessJobId"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	// ContinuationScriptRef is forbidden at intake
	if req.ContinuationScriptRef != nil {
		errResp(w, http.StatusBadRequest, "CONTINUATION_SCRIPT_REF_FORBIDDEN",
			"continuationScriptRef must not be set at job creation")
		return
	}
	if req.SchemaVersion != "1" {
		errResp(w, http.StatusUnprocessableEntity, "UNSUPPORTED_SCHEMA",
			"only schemaVersion '1' is supported")
		return
	}

	// Check artifact exists and is not deleted
	var isDeleted int
	err := h.db.QueryRow(
		`SELECT IsDeleted FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
		req.ScriptArtifactId, req.ScriptVersion,
	).Scan(&isDeleted)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusUnprocessableEntity, "ARTIFACT_NOT_FOUND",
			"artifact "+req.ScriptArtifactId+"/"+req.ScriptVersion+" not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	if isDeleted != 0 {
		errResp(w, http.StatusUnprocessableEntity, "ARTIFACT_NOT_FOUND",
			"artifact is deleted")
		return
	}

	// Deadline validation
	if req.Deadline != nil {
		dl, err := time.Parse(time.RFC3339, *req.Deadline)
		if err != nil || dl.Before(time.Now().UTC()) {
			errResp(w, http.StatusUnprocessableEntity, "DEADLINE_IN_PAST",
				"deadline must be a future UTC timestamp")
			return
		}
	}

	// EventId dedup (5-minute window)
	if req.EventId != nil && *req.EventId != "" {
		window := time.Now().UTC().Add(-5 * time.Minute).Format(time.RFC3339Nano)
		var existingRunId string
		err := h.db.QueryRow(
			`SELECT Id FROM RunRecords WHERE EventId=? AND CreatedAt>=?`,
			*req.EventId, window,
		).Scan(&existingRunId)
		if err == nil {
			// Duplicate — return idempotent response
			var state string
			h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=?`, existingRunId).Scan(&state)
			respond(w, http.StatusOK, map[string]string{
				"runId": existingRunId,
				"state": strings.ToUpper(state),
			})
			return
		}
	}

	inputJSON, _ := json.Marshal(req.InputRefs)
	secretJSON, _ := json.Marshal(req.SecretRefs)

	triggerType := req.TriggerType
	if triggerType == "" {
		triggerType = "Manual"
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	runID := uuid.NewString()
	attemptID := uuid.NewString()
	correlationID := uuid.NewString()

	var businessJobId sql.NullString
	if req.BusinessJobId != nil {
		businessJobId = sql.NullString{String: *req.BusinessJobId, Valid: true}
	}
	var eventId sql.NullString
	if req.EventId != nil {
		eventId = sql.NullString{String: *req.EventId, Valid: true}
	}
	var deadline sql.NullString
	if req.Deadline != nil {
		deadline = sql.NullString{String: *req.Deadline, Valid: true}
	}

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	_, err = tx.Exec(`
		INSERT INTO RunRecords(Id,WorkflowId,State,AttemptId,CreatedAt,CorrelationId,
		    ScriptArtifactId,ScriptVersion,SchemaVersion,InputRefsJson,SecretRefsJson,
		    Deadline,EventId,TriggerType,BusinessJobId,NeedsAttention)
		VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,0)`,
		runID, req.ScriptArtifactId, "Created", attemptID, now, correlationID,
		req.ScriptArtifactId, req.ScriptVersion, req.SchemaVersion,
		string(inputJSON), string(secretJSON),
		deadline, eventId, triggerType, businessJobId,
	)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// Transition Created → Queued atomically
	_, err = tx.Exec(`UPDATE RunRecords SET State='Queued', UpdatedAt=? WHERE Id=?`, now, runID)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// Journal: RUN_CREATED + RUN_QUEUED
	for _, evType := range []string{"RUN_CREATED", "RUN_QUEUED"} {
		jID := uuid.NewString()
		p, _ := json.Marshal(map[string]string{"runId": runID, "triggerType": triggerType})
		tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			jID, runID, attemptID, evType, now, string(p))
	}

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusOK, map[string]string{"runId": runID, "state": "QUEUED"})
}

// POST /jobs/pull
func (h *JobHandler) PullJob(w http.ResponseWriter, r *http.Request) {
	var req struct {
		WorkerId            string   `json:"workerId"`
		RuntimeType         string   `json:"runtimeType"`
		CapabilityNames     []string `json:"capabilityNames"`
		InteractiveSessionId *string `json:"interactiveSessionId"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	// Worker must be AVAILABLE
	var wState string
	err := h.db.QueryRow(`SELECT State FROM WorkerRecords WHERE WorkerId=?`, req.WorkerId).Scan(&wState)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusBadRequest, "WORKER_NOT_FOUND", "worker not registered")
		return
	}
	if strings.EqualFold(wState, "Leased") || strings.EqualFold(wState, "Running") {
		respond(w, http.StatusOK, map[string]string{"result": "QueueFull"})
		return
	}

	// Auto-expire stale leases whose run has already reached a terminal state.
	// This handles the case where result submission timed out and the lease was never cleaned up.
	h.db.Exec(`
		UPDATE LeaseRecords SET State='Expired'
		WHERE WorkerId=? AND State<>'Expired'
		AND RunId IN (
			SELECT Id FROM RunRecords
			WHERE State IN ('Succeeded','Failed','Cancelled','TimedOut','Unknown','HandedOff')
		)`, req.WorkerId)

	// Worker must not hold an active lease
	var leaseCount int
	h.db.QueryRow(`SELECT COUNT(*) FROM LeaseRecords WHERE WorkerId=? AND State<>'Expired'`, req.WorkerId).Scan(&leaseCount)
	if leaseCount > 0 {
		respond(w, http.StatusOK, map[string]string{"result": "QueueFull"})
		return
	}

	// Find the oldest QUEUED run matching this worker's runtime
	query := `SELECT Id,AttemptId,ScriptArtifactId,ScriptVersion,InputRefsJson,SecretRefsJson,InteractiveSessionId
	          FROM RunRecords
	          WHERE State='Queued' AND (RuntimeType='' OR RuntimeType=?)
	          ORDER BY CreatedAt ASC LIMIT 1`
	var runID, attemptID, artifactID, artifactVersion string
	var inputJSON, secretJSON sql.NullString
	var interactiveSessionId sql.NullString

	err = h.db.QueryRow(query, req.RuntimeType).Scan(
		&runID, &attemptID, &artifactID, &artifactVersion,
		&inputJSON, &secretJSON, &interactiveSessionId,
	)
	if err == sql.ErrNoRows {
		respond(w, http.StatusOK, map[string]string{"result": "NoPendingJob"})
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// Headed job requires interactiveSessionId from worker
	if interactiveSessionId.Valid && interactiveSessionId.String != "" {
		if req.InteractiveSessionId == nil || *req.InteractiveSessionId == "" {
			respond(w, http.StatusOK, map[string]string{"result": "NoPendingJob"})
			return
		}
	}

	// Fetch artifact hash
	var artifactHash string
	h.db.QueryRow(`SELECT ContentHash FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
		artifactID, artifactVersion).Scan(&artifactHash)

	now := time.Now().UTC().Format(time.RFC3339Nano)
	expiresAt := time.Now().UTC().Add(30 * time.Second).Format(time.RFC3339Nano)
	leaseID := uuid.NewString()
	newAttemptID := uuid.NewString()

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	// Insert lease record (Offered)
	tx.Exec(`
		INSERT INTO LeaseRecords(LeaseId,RunId,AttemptId,WorkerId,ArtifactId,ArtifactVersion,ArtifactHash,
		    OfferedAt,ExpiresAt,State,InteractiveSessionId)
		VALUES(?,?,?,?,?,?,?,?,?,?,?)`,
		leaseID, runID, newAttemptID, req.WorkerId, artifactID, artifactVersion, artifactHash,
		now, expiresAt, "Offered", interactiveSessionId,
	)

	// Update run: adopt new AttemptId as fence, record worker
	tx.Exec(`UPDATE RunRecords SET AttemptId=?, WorkerId=?, UpdatedAt=? WHERE Id=?`,
		newAttemptID, req.WorkerId, now, runID)

	// Journal: LEASE_OFFERED
	jID := uuid.NewString()
	p, _ := json.Marshal(map[string]string{"leaseId": leaseID, "workerId": req.WorkerId})
	tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		jID, runID, newAttemptID, "LEASE_OFFERED", now, string(p))

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// Build inputRefs / secretRefs arrays
	var inputRefs, secretRefs []string
	if inputJSON.Valid {
		json.Unmarshal([]byte(inputJSON.String), &inputRefs)
	}
	if secretJSON.Valid {
		json.Unmarshal([]byte(secretJSON.String), &secretRefs)
	}

	respond(w, http.StatusOK, map[string]any{
		"result":          "JobOffered",
		"runId":           runID,
		"attemptId":       newAttemptID,
		"leaseId":         leaseID,
		"artifactId":      artifactID,
		"artifactVersion": artifactVersion,
		"artifactHash":    artifactHash,
		"inputRefs":       inputRefs,
		"secretRefs":      secretRefs,
	})
}

// GET /jobs/{runId}
func (h *JobHandler) GetJob(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var state string
	err := h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=?`, runID).Scan(&state)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusOK, map[string]string{
		"runId": runID,
		"state": strings.ToUpper(state),
	})
}

// GET /jobs?state=Queued&limit=50
// Lists runs, newest first. Optional query params:
//   state  — filter by state (case-insensitive); omit for all states
//   limit  — max rows returned (default 50, max 200)
func (h *JobHandler) ListJobs(w http.ResponseWriter, r *http.Request) {
	stateFilter := r.URL.Query().Get("state")
	limit := 50
	if l := r.URL.Query().Get("limit"); l != "" {
		fmt.Sscanf(l, "%d", &limit)
		if limit < 1 {
			limit = 1
		}
		if limit > 200 {
			limit = 200
		}
	}

	type jobRow struct {
		RunId            string  `json:"runId"`
		State            string  `json:"state"`
		ScriptArtifactId string  `json:"scriptArtifactId"`
		ScriptVersion    string  `json:"scriptVersion"`
		TriggerType      string  `json:"triggerType"`
		WorkerId         *string `json:"workerId,omitempty"`
		CreatedAt        string  `json:"createdAt"`
		UpdatedAt        *string `json:"updatedAt,omitempty"`
		NeedsAttention   bool    `json:"needsAttention"`
	}

	var query string
	var args []any

	if stateFilter != "" {
		query = `SELECT Id, State, ScriptArtifactId, ScriptVersion, TriggerType,
		                WorkerId, CreatedAt, UpdatedAt, NeedsAttention
		         FROM RunRecords
		         WHERE UPPER(State)=UPPER(?)
		         ORDER BY CreatedAt DESC LIMIT ?`
		args = []any{stateFilter, limit}
	} else {
		query = `SELECT Id, State, ScriptArtifactId, ScriptVersion, TriggerType,
		                WorkerId, CreatedAt, UpdatedAt, NeedsAttention
		         FROM RunRecords
		         ORDER BY CreatedAt DESC LIMIT ?`
		args = []any{limit}
	}

	rows, err := h.db.Query(query, args...)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	jobs := []jobRow{}
	for rows.Next() {
		var j jobRow
		var workerID sql.NullString
		var updatedAt sql.NullString
		var needsAttention int
		if err := rows.Scan(
			&j.RunId, &j.State, &j.ScriptArtifactId, &j.ScriptVersion,
			&j.TriggerType, &workerID, &j.CreatedAt, &updatedAt, &needsAttention,
		); err != nil {
			continue
		}
		j.State = strings.ToUpper(j.State)
		j.NeedsAttention = needsAttention != 0
		if workerID.Valid {
			j.WorkerId = &workerID.String
		}
		if updatedAt.Valid {
			j.UpdatedAt = &updatedAt.String
		}
		jobs = append(jobs, j)
	}

	respond(w, http.StatusOK, map[string]any{
		"jobs":  jobs,
		"count": len(jobs),
	})
}
