package handlers

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"maps"
	"strings"

	"github.com/google/uuid"
)

// A segmented script is one artifact whose steps are grouped into segments, each with its
// own runtime:
//
//	{ "id": "...", "name": "...", "version": "1.0.0", "params": { "k": "v" },
//	  "segments": [ { "name": "...", "runtime": "desktop", "steps": [ ... ] },
//	                { "name": "...", "runtime": "browser", "steps": [ ... ] } ] }
//
// Publishing it also stores one flat script per segment under "<id>.seg<n>", so workers fetch
// and verify a segment like any other artifact. A job on the parent artifact is a parent run
// (RuntimeType "segmented", never offered to a worker) plus one child run per segment, queued
// in order; every child receives the parent params plus the outputs of the segments before it.

// Segment is one runtime-specific part of a segmented script. Mode is a browser-only setting:
// it overrides the browser host's own configuration for this segment.
type Segment struct {
	Name    string          `json:"name"`
	Runtime string          `json:"runtime"`
	Mode    string          `json:"mode,omitempty"`
	Steps   json.RawMessage `json:"steps"`
}

type segmentedScript struct {
	Id       string
	Name     string
	Version  string
	Params   map[string]string
	Segments []Segment
}

const segmentedRuntime = "segmented"

var segmentRuntimes = map[string]bool{"desktop": true, "browser": true, "terminal": true}
var segmentFields = map[string]bool{"name": true, "runtime": true, "mode": true, "steps": true}

// parseSegmentedScript returns the script when the payload declares segments, nil when it is
// a plain script, and an error when the segments are invalid. The payload may be bare or
// wrapped as {"schemaVersion":"1","script":{...}}.
func parseSegmentedScript(raw []byte) (*segmentedScript, error) {
	var root map[string]json.RawMessage
	if err := json.Unmarshal(raw, &root); err != nil {
		return nil, nil
	}
	if inner, ok := root["script"]; ok {
		var innerMap map[string]json.RawMessage
		if json.Unmarshal(inner, &innerMap) == nil && innerMap != nil {
			root = innerMap
		}
	}
	if _, ok := root["segments"]; !ok {
		return nil, nil
	}

	var doc struct {
		Id       string                       `json:"id"`
		Name     string                       `json:"name"`
		Version  string                       `json:"version"`
		Params   map[string]any               `json:"params"`
		Segments []map[string]json.RawMessage `json:"segments"`
	}
	rootJSON, _ := json.Marshal(root)
	if err := json.Unmarshal(rootJSON, &doc); err != nil {
		return nil, fmt.Errorf("segments: %v", err)
	}
	for i, raw := range doc.Segments {
		for k := range raw {
			if !segmentFields[k] {
				return nil, fmt.Errorf("segment %d: unknown field %q (allowed: name, runtime, mode, steps)", i+1, k)
			}
		}
	}
	segments := make([]Segment, len(doc.Segments))
	for i, raw := range doc.Segments {
		b, _ := json.Marshal(raw)
		if err := json.Unmarshal(b, &segments[i]); err != nil {
			return nil, fmt.Errorf("segment %d: %v", i+1, err)
		}
	}
	if doc.Id == "" || doc.Version == "" {
		return nil, errors.New("a segmented script needs id and version")
	}
	if len(segments) == 0 {
		return nil, errors.New("segments must contain at least one segment")
	}

	params := map[string]string{}
	for k, v := range doc.Params {
		switch t := v.(type) {
		case string:
			params[k] = t
		case float64, bool:
			params[k] = fmt.Sprint(t)
		case nil:
			params[k] = ""
		default:
			return nil, fmt.Errorf("param %q must be a string", k)
		}
	}

	for i := range segments {
		s := &segments[i]
		s.Runtime = strings.ToLower(strings.TrimSpace(s.Runtime))
		if !segmentRuntimes[s.Runtime] {
			return nil, fmt.Errorf("segment %d: runtime must be desktop, browser or terminal", i+1)
		}
		s.Mode = strings.ToLower(strings.TrimSpace(s.Mode))
		if s.Mode != "" {
			if s.Runtime != "browser" {
				return nil, fmt.Errorf("segment %d: mode applies to a browser segment only", i+1)
			}
			if s.Mode != "headed" && s.Mode != "headless" {
				return nil, fmt.Errorf("segment %d: mode must be headed or headless", i+1)
			}
		}
		var steps []json.RawMessage
		if json.Unmarshal(s.Steps, &steps) != nil || len(steps) == 0 {
			return nil, fmt.Errorf("segment %d: steps must be a non-empty array", i+1)
		}
		if s.Name == "" {
			s.Name = fmt.Sprintf("Segment %d", i+1)
		}
	}

	return &segmentedScript{
		Id: doc.Id, Name: doc.Name, Version: doc.Version, Params: params, Segments: segments,
	}, nil
}

// segmentArtifactID names the flat artifact stored for one segment of a parent artifact.
func segmentArtifactID(parentID string, index int) string {
	return fmt.Sprintf("%s.seg%d", parentID, index+1)
}

// segmentPayload is the flat script stored for one segment, in the envelope workers expect.
func segmentPayload(s *segmentedScript, index int) []byte {
	seg := s.Segments[index]
	name := s.Name
	if seg.Name != "" {
		name = strings.TrimSpace(s.Name + " - " + seg.Name)
	}
	type flat struct {
		Id            string          `json:"id"`
		Name          string          `json:"name"`
		Version       string          `json:"version"`
		TargetRuntime string          `json:"targetRuntime"`
		Mode          string          `json:"mode,omitempty"`
		Steps         json.RawMessage `json:"steps"`
	}
	env := struct {
		SchemaVersion string `json:"schemaVersion"`
		Script        flat   `json:"script"`
	}{"1", flat{
		segmentArtifactID(s.Id, index), name, s.Version, seg.Runtime, seg.Mode, seg.Steps,
	}}
	b, _ := json.Marshal(env)
	return b
}

// loadSegmentedScript returns the stored artifact's segments, or nil for a plain script.
func loadSegmentedScript(db *sql.DB, artifactID, version string) *segmentedScript {
	var payload []byte
	if err := db.QueryRow(`SELECT Payload FROM ArtifactContents WHERE ArtifactId=? AND Version=?`,
		artifactID, version).Scan(&payload); err != nil {
		return nil
	}
	s, err := parseSegmentedScript(payload)
	if err != nil {
		return nil
	}
	return s
}

// startSegmentedRun creates the parent run and queues the first segment. The request params
// override the script's default params; input and secret refs are handed to every segment.
func startSegmentedRun(db *sql.DB, s *segmentedScript, req runSpec) (string, error) {
	params := map[string]string{}
	maps.Copy(params, s.Params)
	maps.Copy(params, req.Params)
	paramsJSON, _ := json.Marshal(params)
	inputJSON, _ := json.Marshal(nonNilStrings(req.InputRefs))
	secretJSON, _ := json.Marshal(nonNilStrings(req.SecretRefs))
	triggerType := req.TriggerType
	if triggerType == "" {
		triggerType = "Manual"
	}

	now := nowStr()
	runID := uuid.NewString()
	attemptID := uuid.NewString()

	tx, err := db.Begin()
	if err != nil {
		return "", err
	}
	defer tx.Rollback()

	if _, err = tx.Exec(`
		INSERT INTO RunRecords(Id,WorkflowId,State,AttemptId,CreatedAt,UpdatedAt,CorrelationId,
		    ScriptArtifactId,ScriptVersion,SchemaVersion,InputRefsJson,SecretRefsJson,
		    Deadline,EventId,TriggerType,BusinessJobId,NeedsAttention,RuntimeType,ParamsJson)
		VALUES(?,?,'Running',?,?,?,?,?,?,'1',?,?,?,?,?,?,0,?,?)`,
		runID, s.Id, attemptID, now, now, uuid.NewString(),
		s.Id, s.Version, string(inputJSON), string(secretJSON),
		nullStr(req.Deadline), nullStr(req.EventId), triggerType, nullStr(req.BusinessJobId),
		segmentedRuntime, string(paramsJSON),
	); err != nil {
		return "", err
	}
	for _, ev := range []string{"RUN_CREATED", "RUN_RUNNING"} {
		p, _ := json.Marshal(map[string]any{"runId": runID, "triggerType": triggerType, "segments": len(s.Segments)})
		tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			uuid.NewString(), runID, attemptID, ev, now, string(p))
	}
	if err := tx.Commit(); err != nil {
		return "", err
	}

	if err := queueSegment(db, runID, s, 0, params); err != nil {
		finishParent(db, runID, "Failed", "segment 1 could not be queued: "+err.Error())
		return "", err
	}
	return runID, nil
}

// queueSegment queues the child run for one segment. It does nothing when a child for that
// segment already exists (a duplicate result post), and it withdraws the child again when
// the parent stopped running in the meantime (a cancel or timeout that raced this call).
func queueSegment(db *sql.DB, parentID string, s *segmentedScript, index int, params map[string]string) error {
	var existing int
	db.QueryRow(`SELECT COUNT(*) FROM RunRecords WHERE ParentRunId=? AND SegmentIndex=?`, parentID, index).Scan(&existing)
	if existing > 0 {
		return nil
	}

	var inputJSON, secretJSON sql.NullString
	db.QueryRow(`SELECT InputRefsJson,SecretRefsJson FROM RunRecords WHERE Id=?`, parentID).Scan(&inputJSON, &secretJSON)
	var inputRefs, secretRefs []string
	if inputJSON.Valid {
		json.Unmarshal([]byte(inputJSON.String), &inputRefs)
	}
	if secretJSON.Valid {
		json.Unmarshal([]byte(secretJSON.String), &secretRefs)
	}

	childID, err := insertRun(db, runSpec{
		ArtifactId:   segmentArtifactID(s.Id, index),
		Version:      s.Version,
		TriggerType:  "Segment",
		InputRefs:    inputRefs,
		SecretRefs:   secretRefs,
		Params:       params,
		ParentRunId:  &parentID,
		SegmentIndex: &index,
	})
	if err != nil {
		return err
	}
	journalParent(db, parentID, "SEGMENT_QUEUED", map[string]any{
		"segment": index + 1, "name": s.Segments[index].Name,
		"runtime": s.Segments[index].Runtime, "runId": childID,
	})

	var parentState string
	db.QueryRow(`SELECT State FROM RunRecords WHERE Id=?`, parentID).Scan(&parentState)
	if !strings.EqualFold(parentState, "Running") {
		now := nowStr()
		db.Exec(`UPDATE RunRecords SET State='Cancelled',UpdatedAt=? WHERE Id=? AND State='Queued'`, now, childID)
		db.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
			uuid.NewString(), childID, "", "RUN_CANCELLED", now, `{"reason":"parent run is not running"}`)
	}
	return nil
}

// cancelRequested reports whether a cancel was asked for on the parent run while a segment
// was still running; the parent closes as Cancelled when that segment's result arrives.
func cancelRequested(db *sql.DB, parentID string) bool {
	var n int
	db.QueryRow(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=? AND EventType='RUN_CANCEL_REQUESTED'`, parentID).Scan(&n)
	return n > 0
}

// advanceSegments runs after a child run reaches a terminal state: it copies the child's
// outputs to the parent and queues the next segment, or closes the parent.
func advanceSegments(db *sql.DB, runID, terminalState string) {
	var parentID sql.NullString
	var index sql.NullInt64
	if err := db.QueryRow(`SELECT ParentRunId,SegmentIndex FROM RunRecords WHERE Id=?`, runID).
		Scan(&parentID, &index); err != nil || !parentID.Valid || parentID.String == "" {
		return
	}

	var parentState, artifactID, version string
	var paramsJSON sql.NullString
	if err := db.QueryRow(`SELECT State,ScriptArtifactId,ScriptVersion,ParamsJson FROM RunRecords WHERE Id=?`,
		parentID.String).Scan(&parentState, &artifactID, &version, &paramsJSON); err != nil {
		return
	}
	if !strings.EqualFold(parentState, "Running") {
		return
	}
	s := loadSegmentedScript(db, artifactID, version)
	if s == nil {
		finishParent(db, parentID.String, "Failed", "the segmented script could not be read")
		return
	}
	idx := int(index.Int64)
	if idx < 0 || idx >= len(s.Segments) {
		finishParent(db, parentID.String, "Failed", fmt.Sprintf("run %s refers to segment %d, which the script does not have", runID, idx+1))
		return
	}
	label := fmt.Sprintf("segment %d (%s)", idx+1, s.Segments[idx].Name)

	if cancelRequested(db, parentID.String) {
		journalParent(db, parentID.String, "SEGMENT_"+strings.ToUpper(terminalState),
			map[string]any{"segment": idx + 1, "name": s.Segments[idx].Name, "runId": runID})
		finishParent(db, parentID.String, "Cancelled", "cancelled while "+label+" was running")
		return
	}

	if !strings.EqualFold(terminalState, "Succeeded") {
		journalParent(db, parentID.String, "SEGMENT_"+strings.ToUpper(terminalState),
			map[string]any{"segment": idx + 1, "name": s.Segments[idx].Name, "runId": runID})
		finishParent(db, parentID.String, canonicalState(terminalState), label+" ended "+terminalState)
		return
	}

	now := nowStr()
	for name, value := range loadOutputs(db, runID) {
		db.Exec(`INSERT OR REPLACE INTO RunOutputs(RunId,Name,Value,CreatedAt) VALUES(?,?,?,?)`,
			parentID.String, name, value, now)
	}
	journalParent(db, parentID.String, "SEGMENT_SUCCEEDED",
		map[string]any{"segment": idx + 1, "name": s.Segments[idx].Name, "runId": runID})

	next := idx + 1
	if next >= len(s.Segments) {
		finishParent(db, parentID.String, "Succeeded", "")
		return
	}

	// The next segment sees the parent params plus every output collected so far; a later
	// segment's output wins when names repeat.
	params := map[string]string{}
	if paramsJSON.Valid {
		json.Unmarshal([]byte(paramsJSON.String), &params)
	}
	maps.Copy(params, loadOutputs(db, parentID.String))
	if err := queueSegment(db, parentID.String, s, next, params); err != nil {
		finishParent(db, parentID.String, "Failed", fmt.Sprintf("segment %d could not be queued: %v", next+1, err))
	}
}

// finishParent moves a parent run to a terminal state and records the result.
func finishParent(db *sql.DB, parentID, state, message string) {
	now := nowStr()
	needsAttention := 0
	if strings.EqualFold(state, "Unknown") {
		needsAttention = 1
	}
	var attemptID string
	db.QueryRow(`SELECT AttemptId FROM RunRecords WHERE Id=?`, parentID).Scan(&attemptID)

	tx, err := db.Begin()
	if err != nil {
		return
	}
	defer tx.Rollback()
	tx.Exec(`UPDATE RunRecords SET State=?,NeedsAttention=?,UpdatedAt=? WHERE Id=?`, state, needsAttention, now, parentID)
	var errCategory, errMessage sql.NullString
	if !strings.EqualFold(state, "Succeeded") {
		errCategory = sql.NullString{String: "SEGMENT_" + strings.ToUpper(state), Valid: true}
		errMessage = sql.NullString{String: message, Valid: true}
	}
	tx.Exec(`INSERT OR REPLACE INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,
	            ErrorCategory,ErrorMessage,EvidenceRefsJson,CompletedAt)
	         VALUES(?,?,?,?,?,?,?,'[]',?)`,
		uuid.NewString(), parentID, attemptID, state, needsAttention, errCategory, errMessage, now)
	p, _ := json.Marshal(map[string]any{"terminalState": state, "message": message})
	tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		uuid.NewString(), parentID, attemptID, "RUN_"+strings.ToUpper(state), now, string(p))
	tx.Commit()
}

func journalParent(db *sql.DB, parentID, eventType string, payload map[string]any) {
	var attemptID string
	db.QueryRow(`SELECT AttemptId FROM RunRecords WHERE Id=?`, parentID).Scan(&attemptID)
	p, _ := json.Marshal(payload)
	db.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		uuid.NewString(), parentID, attemptID, eventType, nowStr(), string(p))
}

// segmentStatus describes one segment of a parent run for GET /jobs/{runId}.
type segmentStatus struct {
	Index   int               `json:"index"`
	Name    string            `json:"name"`
	Runtime string            `json:"runtime"`
	RunId   string            `json:"runId,omitempty"`
	State   string            `json:"state"`
	Outputs map[string]string `json:"outputs"`
}

// segmentStatuses lists every segment of a parent run with the state of its child run.
func segmentStatuses(db *sql.DB, parentID, artifactID, version string) []segmentStatus {
	s := loadSegmentedScript(db, artifactID, version)
	if s == nil {
		return nil
	}
	list := make([]segmentStatus, len(s.Segments))
	for i, seg := range s.Segments {
		list[i] = segmentStatus{Index: i, Name: seg.Name, Runtime: seg.Runtime, State: "PENDING", Outputs: map[string]string{}}
	}
	rows, err := db.Query(`SELECT Id,SegmentIndex,State FROM RunRecords WHERE ParentRunId=? ORDER BY CreatedAt`, parentID)
	if err != nil {
		return list
	}
	type child struct {
		id, state string
		idx       int
	}
	var children []child
	for rows.Next() {
		var c child
		var idx sql.NullInt64
		if rows.Scan(&c.id, &idx, &c.state) == nil {
			c.idx = int(idx.Int64)
			children = append(children, c)
		}
	}
	rows.Close()
	for _, c := range children {
		if c.idx < 0 || c.idx >= len(list) {
			continue
		}
		list[c.idx].RunId = c.id
		list[c.idx].State = strings.ToUpper(c.state)
		list[c.idx].Outputs = loadOutputs(db, c.id)
	}
	return list
}

// activeSegmentRun returns the parent's non-terminal child run, if any.
func activeSegmentRun(db *sql.DB, parentID string) (runID, state, attemptID string, ok bool) {
	err := db.QueryRow(`SELECT Id,State,AttemptId FROM RunRecords WHERE ParentRunId=?
	    AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')
	    ORDER BY CreatedAt DESC LIMIT 1`, parentID).Scan(&runID, &state, &attemptID)
	return runID, state, attemptID, err == nil
}
