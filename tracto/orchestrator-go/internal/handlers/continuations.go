package handlers

import (
	"database/sql"
	"net/http"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type ContinuationHandler struct{ db *sql.DB }

func NewContinuationHandler(db *sql.DB) *ContinuationHandler { return &ContinuationHandler{db: db} }

// POST /admin/continuations
func (h *ContinuationHandler) Create(w http.ResponseWriter, r *http.Request) {
	var req struct {
		TriggerRunId          *string `json:"triggerRunId"`
		TriggerState          string  `json:"triggerState"`
		TargetArtifactId      string  `json:"targetArtifactId"`
		TargetArtifactVersion string  `json:"targetArtifactVersion"`
		InputTemplate         *string `json:"inputTemplate"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.TriggerState == "" || req.TargetArtifactId == "" || req.TargetArtifactVersion == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "triggerState, targetArtifactId, targetArtifactVersion required")
		return
	}

	validStates := map[string]bool{
		"succeeded": true, "failed": true, "handedoff": true,
		"cancelled": true, "timedout": true, "unknown": true,
	}
	if !validStates[strings.ToLower(req.TriggerState)] {
		errResp(w, http.StatusBadRequest, "INVALID_STATE", "triggerState must be a valid terminal run state")
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	mappingID := uuid.NewString()
	_, err := h.db.Exec(`
		INSERT INTO ContinuationMappings(MappingId,TriggerRunId,TriggerState,TargetArtifactId,TargetArtifactVersion,InputTemplate,CreatedAt,Version)
		VALUES(?,?,?,?,?,?,?,1)`,
		mappingID, req.TriggerRunId, req.TriggerState, req.TargetArtifactId, req.TargetArtifactVersion, req.InputTemplate, now)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusCreated, map[string]any{
		"mappingId": mappingID, "triggerState": req.TriggerState,
		"targetArtifactId": req.TargetArtifactId, "targetArtifactVersion": req.TargetArtifactVersion,
	})
}

// GET /admin/continuations
func (h *ContinuationHandler) List(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT MappingId,TriggerRunId,TriggerState,TargetArtifactId,TargetArtifactVersion,CreatedAt,Version FROM ContinuationMappings`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	type item struct {
		MappingId             string  `json:"mappingId"`
		TriggerRunId          *string `json:"triggerRunId"`
		TriggerState          string  `json:"triggerState"`
		TargetArtifactId      string  `json:"targetArtifactId"`
		TargetArtifactVersion string  `json:"targetArtifactVersion"`
		CreatedAt             string  `json:"createdAt"`
		Version               int     `json:"version"`
	}
	var items []item
	for rows.Next() {
		var it item
		var trid sql.NullString
		rows.Scan(&it.MappingId, &trid, &it.TriggerState, &it.TargetArtifactId, &it.TargetArtifactVersion, &it.CreatedAt, &it.Version)
		if trid.Valid {
			it.TriggerRunId = &trid.String
		}
		items = append(items, it)
	}
	if items == nil {
		items = []item{}
	}
	respond(w, http.StatusOK, items)
}

// DELETE /admin/continuations/{mappingId}
func (h *ContinuationHandler) Delete(w http.ResponseWriter, r *http.Request) {
	mappingID := chi.URLParam(r, "mappingId")
	res, _ := h.db.Exec(`DELETE FROM ContinuationMappings WHERE MappingId=?`, mappingID)
	if n, _ := res.RowsAffected(); n == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "mapping not found")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
