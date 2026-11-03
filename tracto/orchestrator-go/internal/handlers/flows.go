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

// FlowStep is one script in a flow. Scripts may target different runtimes.
type FlowStep struct {
	ScriptArtifactId string `json:"scriptArtifactId"`
	ScriptVersion    string `json:"scriptVersion"`
}

type FlowHandler struct{ db *sql.DB }

func NewFlowHandler(db *sql.DB) *FlowHandler { return &FlowHandler{db: db} }

// POST /flows — queue the first step; later steps are queued as each run succeeds.
func (h *FlowHandler) Create(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Name   string            `json:"name"`
		Steps  []FlowStep        `json:"steps"`
		Params map[string]string `json:"params"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if len(req.Steps) == 0 {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "steps must contain at least one script")
		return
	}
	for i, s := range req.Steps {
		if s.ScriptArtifactId == "" || s.ScriptVersion == "" {
			errResp(w, http.StatusBadRequest, "INVALID_REQUEST",
				"step "+itoa(i+1)+" needs scriptArtifactId and scriptVersion")
			return
		}
		var isDeleted int
		err := h.db.QueryRow(`SELECT IsDeleted FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
			s.ScriptArtifactId, s.ScriptVersion).Scan(&isDeleted)
		if err == sql.ErrNoRows || isDeleted != 0 {
			errResp(w, http.StatusUnprocessableEntity, "ARTIFACT_NOT_FOUND",
				"artifact "+s.ScriptArtifactId+"/"+s.ScriptVersion+" not found")
			return
		}
		if err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
	}
	if req.Params == nil {
		req.Params = map[string]string{}
	}

	flowID := uuid.NewString()
	now := time.Now().UTC().Format(time.RFC3339Nano)
	stepsJSON, _ := json.Marshal(req.Steps)
	paramsJSON, _ := json.Marshal(req.Params)

	if _, err := h.db.Exec(`
		INSERT INTO FlowRecords(FlowId,Name,StepsJson,ParamsJson,State,CurrentStep,CreatedAt,UpdatedAt)
		VALUES(?,?,?,?,'Running',0,?,?)`,
		flowID, req.Name, string(stepsJSON), string(paramsJSON), now, now); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	first := 0
	runID, err := insertRun(h.db, runSpec{
		ArtifactId:    req.Steps[0].ScriptArtifactId,
		Version:       req.Steps[0].ScriptVersion,
		TriggerType:   "Flow",
		Params:        req.Params,
		FlowId:        &flowID,
		FlowStepIndex: &first,
	})
	if err != nil {
		h.db.Exec(`UPDATE FlowRecords SET State='Failed', UpdatedAt=? WHERE FlowId=?`, now, flowID)
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusCreated, map[string]any{
		"flowId": flowID, "runId": runID, "state": "RUNNING", "steps": len(req.Steps),
	})
}

// GET /flows/{flowId}
func (h *FlowHandler) Get(w http.ResponseWriter, r *http.Request) {
	flowID := chi.URLParam(r, "flowId")
	var name, stepsJSON, paramsJSON, state, createdAt string
	var currentStep int
	err := h.db.QueryRow(`SELECT Name,StepsJson,ParamsJson,State,CurrentStep,CreatedAt FROM FlowRecords WHERE FlowId=?`, flowID).
		Scan(&name, &stepsJSON, &paramsJSON, &state, &currentStep, &createdAt)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "flow not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	var steps []FlowStep
	json.Unmarshal([]byte(stepsJSON), &steps)
	params := map[string]string{}
	json.Unmarshal([]byte(paramsJSON), &params)

	type runItem struct {
		RunId     string            `json:"runId"`
		StepIndex int               `json:"stepIndex"`
		Script    string            `json:"script"`
		State     string            `json:"state"`
		Outputs   map[string]string `json:"outputs"`
	}
	var runs []runItem
	rows, err := h.db.Query(`SELECT Id,FlowStepIndex,ScriptArtifactId,ScriptVersion,State FROM RunRecords WHERE FlowId=? ORDER BY FlowStepIndex`, flowID)
	if err == nil {
		for rows.Next() {
			var it runItem
			var idx sql.NullInt64
			var art, ver string
			if rows.Scan(&it.RunId, &idx, &art, &ver, &it.State) == nil {
				it.StepIndex = int(idx.Int64)
				it.Script = art + "/" + ver
				it.State = strings.ToUpper(it.State)
				runs = append(runs, it)
			}
		}
		rows.Close()
	}
	for i := range runs {
		runs[i].Outputs = loadOutputs(h.db, runs[i].RunId)
	}
	if runs == nil {
		runs = []runItem{}
	}

	respond(w, http.StatusOK, map[string]any{
		"flowId": flowID, "name": name, "state": strings.ToUpper(state),
		"currentStep": currentStep, "steps": steps, "params": params,
		"runs": runs, "createdAt": createdAt,
	})
}

// GET /flows
func (h *FlowHandler) List(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT FlowId,Name,State,CurrentStep,CreatedAt FROM FlowRecords ORDER BY CreatedAt DESC LIMIT 200`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	type item struct {
		FlowId      string `json:"flowId"`
		Name        string `json:"name"`
		State       string `json:"state"`
		CurrentStep int    `json:"currentStep"`
		CreatedAt   string `json:"createdAt"`
	}
	items := []item{}
	for rows.Next() {
		var it item
		if rows.Scan(&it.FlowId, &it.Name, &it.State, &it.CurrentStep, &it.CreatedAt) == nil {
			it.State = strings.ToUpper(it.State)
			items = append(items, it)
		}
	}
	respond(w, http.StatusOK, map[string]any{"flows": items, "count": len(items)})
}

// advanceFlow runs after a result is committed. When the run belongs to a flow it queues
// the next step with the flow params plus the outputs of every earlier run, or closes the
// flow when the run failed or was the last step.
func advanceFlow(db *sql.DB, runID, terminalState string) {
	var flowID sql.NullString
	var stepIdx sql.NullInt64
	if err := db.QueryRow(`SELECT FlowId,FlowStepIndex FROM RunRecords WHERE Id=?`, runID).Scan(&flowID, &stepIdx); err != nil {
		return
	}
	if !flowID.Valid || flowID.String == "" {
		return
	}

	var stepsJSON, paramsJSON, state string
	if err := db.QueryRow(`SELECT StepsJson,ParamsJson,State FROM FlowRecords WHERE FlowId=?`, flowID.String).
		Scan(&stepsJSON, &paramsJSON, &state); err != nil {
		return
	}
	if !strings.EqualFold(state, "Running") {
		return
	}
	now := time.Now().UTC().Format(time.RFC3339Nano)

	if !strings.EqualFold(terminalState, "Succeeded") {
		db.Exec(`UPDATE FlowRecords SET State='Failed', UpdatedAt=? WHERE FlowId=?`, now, flowID.String)
		return
	}

	var steps []FlowStep
	json.Unmarshal([]byte(stepsJSON), &steps)
	next := int(stepIdx.Int64) + 1
	if next >= len(steps) {
		db.Exec(`UPDATE FlowRecords SET State='Succeeded', UpdatedAt=? WHERE FlowId=?`, now, flowID.String)
		return
	}

	// Params for the next run: flow params, then outputs of earlier steps in order, so a
	// later step's output wins when names repeat.
	params := map[string]string{}
	json.Unmarshal([]byte(paramsJSON), &params)
	var earlierRuns []string
	rows, err := db.Query(`SELECT Id FROM RunRecords WHERE FlowId=? ORDER BY FlowStepIndex`, flowID.String)
	if err == nil {
		for rows.Next() {
			var id string
			if rows.Scan(&id) == nil {
				earlierRuns = append(earlierRuns, id)
			}
		}
		rows.Close()
	}
	for _, id := range earlierRuns {
		for k, v := range loadOutputs(db, id) {
			params[k] = v
		}
	}

	if _, err := insertRun(db, runSpec{
		ArtifactId:    steps[next].ScriptArtifactId,
		Version:       steps[next].ScriptVersion,
		TriggerType:   "Flow",
		Params:        params,
		FlowId:        &flowID.String,
		FlowStepIndex: &next,
	}); err != nil {
		db.Exec(`UPDATE FlowRecords SET State='Failed', UpdatedAt=? WHERE FlowId=?`, now, flowID.String)
		return
	}
	db.Exec(`UPDATE FlowRecords SET CurrentStep=?, UpdatedAt=? WHERE FlowId=?`, next, now, flowID.String)
}

func itoa(i int) string {
	b, _ := json.Marshal(i)
	return string(b)
}
