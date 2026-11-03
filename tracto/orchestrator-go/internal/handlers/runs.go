package handlers

import (
	"database/sql"
	"encoding/json"
	"time"

	"github.com/google/uuid"
)

// runSpec describes a run to queue. Shared by POST /jobs and flow advancement.
type runSpec struct {
	ArtifactId    string
	Version       string
	SchemaVersion string
	TriggerType   string
	InputRefs     []string
	SecretRefs    []string
	Params        map[string]string
	Deadline      *string
	EventId       *string
	BusinessJobId *string
	FlowId        *string
	FlowStepIndex *int
}

// insertRun creates a run in state Queued and journals RUN_CREATED and RUN_QUEUED.
func insertRun(db *sql.DB, s runSpec) (string, error) {
	inputJSON, _ := json.Marshal(nonNilStrings(s.InputRefs))
	secretJSON, _ := json.Marshal(nonNilStrings(s.SecretRefs))
	if s.Params == nil {
		s.Params = map[string]string{}
	}
	paramsJSON, _ := json.Marshal(s.Params)

	triggerType := s.TriggerType
	if triggerType == "" {
		triggerType = "Manual"
	}
	schemaVersion := s.SchemaVersion
	if schemaVersion == "" {
		schemaVersion = "1"
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	runID := uuid.NewString()
	attemptID := uuid.NewString()
	correlationID := uuid.NewString()

	var businessJobId, eventId, deadline, flowId sql.NullString
	if s.BusinessJobId != nil {
		businessJobId = sql.NullString{String: *s.BusinessJobId, Valid: true}
	}
	if s.EventId != nil {
		eventId = sql.NullString{String: *s.EventId, Valid: true}
	}
	if s.Deadline != nil {
		deadline = sql.NullString{String: *s.Deadline, Valid: true}
	}
	if s.FlowId != nil {
		flowId = sql.NullString{String: *s.FlowId, Valid: true}
	}
	var flowStep sql.NullInt64
	if s.FlowStepIndex != nil {
		flowStep = sql.NullInt64{Int64: int64(*s.FlowStepIndex), Valid: true}
	}

	// Read before the transaction opens: the database allows a single connection.
	runtimeType := scriptRuntimeType(db, s.ArtifactId, s.Version)

	tx, err := db.Begin()
	if err != nil {
		return "", err
	}
	defer tx.Rollback()

	_, err = tx.Exec(`
		INSERT INTO RunRecords(Id,WorkflowId,State,AttemptId,CreatedAt,CorrelationId,
		    ScriptArtifactId,ScriptVersion,SchemaVersion,InputRefsJson,SecretRefsJson,
		    Deadline,EventId,TriggerType,BusinessJobId,NeedsAttention,RuntimeType,
		    ParamsJson,FlowId,FlowStepIndex)
		VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,0,?,?,?,?)`,
		runID, s.ArtifactId, "Created", attemptID, now, correlationID,
		s.ArtifactId, s.Version, schemaVersion,
		string(inputJSON), string(secretJSON),
		deadline, eventId, triggerType, businessJobId,
		runtimeType, string(paramsJSON), flowId, flowStep,
	)
	if err != nil {
		return "", err
	}

	if _, err = tx.Exec(`UPDATE RunRecords SET State='Queued', UpdatedAt=? WHERE Id=?`, now, runID); err != nil {
		return "", err
	}

	for _, evType := range []string{"RUN_CREATED", "RUN_QUEUED"} {
		p, _ := json.Marshal(map[string]string{"runId": runID, "triggerType": triggerType})
		tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			uuid.NewString(), runID, attemptID, evType, now, string(p))
	}

	if err := tx.Commit(); err != nil {
		return "", err
	}
	return runID, nil
}

func nonNilStrings(v []string) []string {
	if v == nil {
		return []string{}
	}
	return v
}

// loadParams returns the params a run was queued with (never nil).
func loadParams(db *sql.DB, runID string) map[string]string {
	params := map[string]string{}
	var raw sql.NullString
	if err := db.QueryRow(`SELECT ParamsJson FROM RunRecords WHERE Id=?`, runID).Scan(&raw); err == nil && raw.Valid && raw.String != "" {
		json.Unmarshal([]byte(raw.String), &params)
	}
	return params
}

// loadOutputs returns the outputs a run reported (never nil).
func loadOutputs(db *sql.DB, runID string) map[string]string {
	outputs := map[string]string{}
	rows, err := db.Query(`SELECT Name,Value FROM RunOutputs WHERE RunId=? ORDER BY Name`, runID)
	if err != nil {
		return outputs
	}
	defer rows.Close()
	for rows.Next() {
		var name, value string
		if rows.Scan(&name, &value) == nil {
			outputs[name] = value
		}
	}
	return outputs
}
