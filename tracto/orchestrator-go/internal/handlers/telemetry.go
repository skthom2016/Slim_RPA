package handlers

import (
	"database/sql"
	"net/http"

	"github.com/go-chi/chi/v5"
)

type TelemetryHandler struct{ db *sql.DB }

func NewTelemetryHandler(db *sql.DB) *TelemetryHandler { return &TelemetryHandler{db: db} }

// GET /admin/telemetry/{runId}
func (h *TelemetryHandler) GetByRun(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	rows, err := h.db.Query(
		`SELECT Id,RunId,WorkerId,AttemptId,EventType,OccurredAt,Metadata
		 FROM TelemetryEvents WHERE RunId=? ORDER BY OccurredAt ASC`, runID)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	type ev struct {
		Id         string  `json:"id"`
		RunId      string  `json:"runId"`
		WorkerId   *string `json:"workerId"`
		AttemptId  *string `json:"attemptId"`
		EventType  string  `json:"eventType"`
		OccurredAt string  `json:"occurredAt"`
		Metadata   *string `json:"metadata"`
	}
	var evs []ev
	for rows.Next() {
		var e ev
		var wid, aid, meta sql.NullString
		rows.Scan(&e.Id, &e.RunId, &wid, &aid, &e.EventType, &e.OccurredAt, &meta)
		if wid.Valid {
			e.WorkerId = &wid.String
		}
		if aid.Valid {
			e.AttemptId = &aid.String
		}
		if meta.Valid {
			e.Metadata = &meta.String
		}
		evs = append(evs, e)
	}
	if evs == nil {
		evs = []ev{}
	}
	respond(w, http.StatusOK, evs)
}

// GET /admin/telemetry
func (h *TelemetryHandler) List(w http.ResponseWriter, r *http.Request) {
	eventType := r.URL.Query().Get("eventType")
	var rows *sql.Rows
	var err error
	if eventType != "" {
		rows, err = h.db.Query(
			`SELECT Id,RunId,WorkerId,AttemptId,EventType,OccurredAt,Metadata
			 FROM TelemetryEvents WHERE EventType=? ORDER BY OccurredAt DESC LIMIT 200`, eventType)
	} else {
		rows, err = h.db.Query(
			`SELECT Id,RunId,WorkerId,AttemptId,EventType,OccurredAt,Metadata
			 FROM TelemetryEvents ORDER BY OccurredAt DESC LIMIT 200`)
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	type ev struct {
		Id         string  `json:"id"`
		RunId      string  `json:"runId"`
		WorkerId   *string `json:"workerId"`
		AttemptId  *string `json:"attemptId"`
		EventType  string  `json:"eventType"`
		OccurredAt string  `json:"occurredAt"`
		Metadata   *string `json:"metadata"`
	}
	var evs []ev
	for rows.Next() {
		var e ev
		var wid, aid, meta sql.NullString
		rows.Scan(&e.Id, &e.RunId, &wid, &aid, &e.EventType, &e.OccurredAt, &meta)
		if wid.Valid {
			e.WorkerId = &wid.String
		}
		if aid.Valid {
			e.AttemptId = &aid.String
		}
		if meta.Valid {
			e.Metadata = &meta.String
		}
		evs = append(evs, e)
	}
	if evs == nil {
		evs = []ev{}
	}
	respond(w, http.StatusOK, evs)
}
