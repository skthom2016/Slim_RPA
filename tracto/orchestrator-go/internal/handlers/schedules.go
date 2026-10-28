package handlers

import (
	"database/sql"
	"net/http"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type ScheduleHandler struct{ db *sql.DB }

func NewScheduleHandler(db *sql.DB) *ScheduleHandler { return &ScheduleHandler{db: db} }

// POST /admin/schedules
func (h *ScheduleHandler) Create(w http.ResponseWriter, r *http.Request) {
	var req struct {
		CronExpression string `json:"cronExpression"`
		ArtifactId     string `json:"artifactId"`
		Version        string `json:"version"`
		Enabled        *bool  `json:"enabled"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.CronExpression == "" || req.ArtifactId == "" || req.Version == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "cronExpression, artifactId, version required")
		return
	}
	enabled := 1
	if req.Enabled != nil && !*req.Enabled {
		enabled = 0
	}
	now := time.Now().UTC().Format(time.RFC3339Nano)
	schedID := uuid.NewString()
	_, err := h.db.Exec(`
		INSERT INTO ScheduleRecords(ScheduleId,CronExpression,ArtifactId,Version,Enabled,CreatedAt)
		VALUES(?,?,?,?,?,?)`,
		schedID, req.CronExpression, req.ArtifactId, req.Version, enabled, now)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusCreated, map[string]any{
		"scheduleId": schedID, "cronExpression": req.CronExpression,
		"artifactId": req.ArtifactId, "version": req.Version, "enabled": enabled != 0,
	})
}

// GET /admin/schedules
func (h *ScheduleHandler) List(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT ScheduleId,CronExpression,ArtifactId,Version,Enabled,CreatedAt FROM ScheduleRecords`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	type item struct {
		ScheduleId     string `json:"scheduleId"`
		CronExpression string `json:"cronExpression"`
		ArtifactId     string `json:"artifactId"`
		Version        string `json:"version"`
		Enabled        bool   `json:"enabled"`
		CreatedAt      string `json:"createdAt"`
	}
	var items []item
	for rows.Next() {
		var it item
		var enabled int
		rows.Scan(&it.ScheduleId, &it.CronExpression, &it.ArtifactId, &it.Version, &enabled, &it.CreatedAt)
		it.Enabled = enabled != 0
		items = append(items, it)
	}
	if items == nil {
		items = []item{}
	}
	respond(w, http.StatusOK, items)
}

// PATCH /admin/schedules/{scheduleId}
func (h *ScheduleHandler) Update(w http.ResponseWriter, r *http.Request) {
	schedID := chi.URLParam(r, "scheduleId")
	var req struct {
		Enabled *bool `json:"enabled"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	now := time.Now().UTC().Format(time.RFC3339Nano)
	if req.Enabled != nil {
		enabled := 0
		if *req.Enabled {
			enabled = 1
		}
		res, _ := h.db.Exec(`UPDATE ScheduleRecords SET Enabled=?,UpdatedAt=? WHERE ScheduleId=?`, enabled, now, schedID)
		if n, _ := res.RowsAffected(); n == 0 {
			errResp(w, http.StatusNotFound, "NOT_FOUND", "schedule not found")
			return
		}
	}
	respond(w, http.StatusOK, map[string]string{"scheduleId": schedID})
}

// DELETE /admin/schedules/{scheduleId}
func (h *ScheduleHandler) Delete(w http.ResponseWriter, r *http.Request) {
	schedID := chi.URLParam(r, "scheduleId")
	res, _ := h.db.Exec(`DELETE FROM ScheduleRecords WHERE ScheduleId=?`, schedID)
	if n, _ := res.RowsAffected(); n == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "schedule not found")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
