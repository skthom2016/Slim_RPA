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
		Params                 map[string]string `json:"params"`
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

	// A segmented script becomes a parent run plus one child run per segment.
	if seg := loadSegmentedScript(h.db, req.ScriptArtifactId, req.ScriptVersion); seg != nil {
		runID, err := startSegmentedRun(h.db, seg, runSpec{
			TriggerType:   req.TriggerType,
			InputRefs:     req.InputRefs,
			SecretRefs:    req.SecretRefs,
			Params:        req.Params,
			Deadline:      req.Deadline,
			EventId:       req.EventId,
			BusinessJobId: req.BusinessJobId,
		})
		if err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
		respond(w, http.StatusOK, map[string]any{"runId": runID, "state": "RUNNING", "segments": len(seg.Segments)})
		return
	}

	runID, err := insertRun(h.db, runSpec{
		ArtifactId:    req.ScriptArtifactId,
		Version:       req.ScriptVersion,
		SchemaVersion: req.SchemaVersion,
		TriggerType:   req.TriggerType,
		InputRefs:     req.InputRefs,
		SecretRefs:    req.SecretRefs,
		Params:        req.Params,
		Deadline:      req.Deadline,
		EventId:       req.EventId,
		BusinessJobId: req.BusinessJobId,
	})
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusOK, map[string]string{"runId": runID, "state": "QUEUED"})
}

// scriptRuntimeType returns the targetRuntime declared by a stored script ("desktop",
// "browser", "terminal"), lower-cased, or "" when it cannot be determined. A run with an
// empty RuntimeType can be pulled by any worker, so a declared runtime keeps browser jobs
// away from desktop workers and the reverse.
func scriptRuntimeType(db *sql.DB, artifactID, version string) string {
	var payload []byte
	if err := db.QueryRow(
		`SELECT Payload FROM ArtifactContents WHERE ArtifactId=? AND Version=?`,
		artifactID, version).Scan(&payload); err != nil {
		return ""
	}
	var doc struct {
		TargetRuntime string `json:"targetRuntime"`
		Script        struct {
			TargetRuntime string `json:"targetRuntime"`
		} `json:"script"`
	}
	if json.Unmarshal(payload, &doc) != nil {
		return ""
	}
	rt := doc.Script.TargetRuntime
	if rt == "" {
		rt = doc.TargetRuntime
	}
	return strings.ToLower(strings.TrimSpace(rt))
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

	params := loadParams(h.db, runID)

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
		"params":          params,
	})
}

// GET /jobs/{runId}
func (h *JobHandler) GetJob(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var state, runtimeType, artifactID, version string
	var parentID sql.NullString
	var segmentIndex sql.NullInt64
	err := h.db.QueryRow(`SELECT State,RuntimeType,ScriptArtifactId,ScriptVersion,ParentRunId,SegmentIndex
	    FROM RunRecords WHERE Id=?`, runID).
		Scan(&state, &runtimeType, &artifactID, &version, &parentID, &segmentIndex)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	resp := map[string]any{
		"runId":            runID,
		"state":            strings.ToUpper(state),
		"scriptArtifactId": artifactID,
		"scriptVersion":    version,
		"runtimeType":      runtimeType,
		"params":           loadParams(h.db, runID),
		"outputs":          loadOutputs(h.db, runID),
	}
	if runtimeType == segmentedRuntime {
		resp["segments"] = segmentStatuses(h.db, runID, artifactID, version)
	}
	if parentID.Valid && parentID.String != "" {
		resp["parentRunId"] = parentID.String
		resp["segmentIndex"] = segmentIndex.Int64
	}
	respond(w, http.StatusOK, resp)
}

// GET /jobs?state=Queued&limit=50&includeSegments=false
// Lists runs, newest first. Optional query params:
//   state            — filter by state (case-insensitive); omit for all states
//   limit            — max rows returned (default 50, max 200)
//   includeSegments  — also list the child runs of segmented scripts (default false)
func (h *JobHandler) ListJobs(w http.ResponseWriter, r *http.Request) {
	stateFilter := r.URL.Query().Get("state")
	includeSegments := r.URL.Query().Get("includeSegments") == "true"
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
		RuntimeType      string  `json:"runtimeType"`
		WorkerId         *string `json:"workerId,omitempty"`
		CreatedAt        string  `json:"createdAt"`
		UpdatedAt        *string `json:"updatedAt,omitempty"`
		NeedsAttention   bool    `json:"needsAttention"`
		ParentRunId      *string `json:"parentRunId,omitempty"`
	}

	query := `SELECT Id, State, ScriptArtifactId, ScriptVersion, TriggerType, RuntimeType,
	                 WorkerId, CreatedAt, UpdatedAt, NeedsAttention, ParentRunId
	          FROM RunRecords WHERE 1=1`
	var args []any
	if stateFilter != "" {
		query += ` AND UPPER(State)=UPPER(?)`
		args = append(args, stateFilter)
	}
	if !includeSegments {
		query += ` AND ParentRunId IS NULL`
	}
	query += ` ORDER BY CreatedAt DESC LIMIT ?`
	args = append(args, limit)

	rows, err := h.db.Query(query, args...)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	jobs := []jobRow{}
	for rows.Next() {
		var j jobRow
		var workerID, updatedAt, parentID sql.NullString
		var needsAttention int
		if err := rows.Scan(
			&j.RunId, &j.State, &j.ScriptArtifactId, &j.ScriptVersion,
			&j.TriggerType, &j.RuntimeType, &workerID, &j.CreatedAt, &updatedAt, &needsAttention, &parentID,
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
		if parentID.Valid && parentID.String != "" {
			j.ParentRunId = &parentID.String
		}
		jobs = append(jobs, j)
	}

	respond(w, http.StatusOK, map[string]any{
		"jobs":  jobs,
		"count": len(jobs),
	})
}
