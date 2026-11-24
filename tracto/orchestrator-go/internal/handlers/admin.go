package handlers

import (
	"database/sql"
	"net/http"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type AdminHandler struct{ db *sql.DB }

func NewAdminHandler(db *sql.DB) *AdminHandler { return &AdminHandler{db: db} }

// GET /admin/runs — list runs needing attention
func (h *AdminHandler) ListUnknownRuns(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(
		`SELECT Id,State,NeedsAttention,UnknownReason,CreatedAt,WorkerId
		 FROM RunRecords WHERE NeedsAttention ORDER BY CreatedAt DESC LIMIT 100`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	type item struct {
		RunId          string  `json:"runId"`
		State          string  `json:"state"`
		NeedsAttention bool    `json:"needsAttention"`
		UnknownReason  *string `json:"unknownReason"`
		CreatedAt      string  `json:"createdAt"`
		WorkerId       *string `json:"workerId"`
	}
	var items []item
	for rows.Next() {
		var it item
		var ur, wid sql.NullString
		if err := rows.Scan(&it.RunId, &it.State, &it.NeedsAttention, &ur, &it.CreatedAt, &wid); err != nil {
			dbErr(w, err)
			return
		}
		it.State = strings.ToUpper(it.State)
		if ur.Valid {
			it.UnknownReason = &ur.String
		}
		if wid.Valid {
			it.WorkerId = &wid.String
		}
		items = append(items, it)
	}
	if items == nil {
		items = []item{}
	}
	respond(w, http.StatusOK, items)
}

// POST /admin/runs/{runId}/resolve — admin resolve UNKNOWN run
func (h *AdminHandler) ResolveRun(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		Resolution  string  `json:"resolution"`
		ResolvedBy  *string `json:"resolvedBy"`
		Notes       *string `json:"notes"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}

	validResolutions := map[string]bool{"succeeded": true, "failed": true, "cancelled": true}
	if !validResolutions[strings.ToLower(req.Resolution)] {
		errResp(w, http.StatusBadRequest, "INVALID_RESOLUTION",
			"resolution must be Succeeded, Failed, or Cancelled")
		return
	}

	var runState string
	err := h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=$1`, runID).Scan(&runState)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "run not found")
		return
	}
	if !strings.EqualFold(runState, "Unknown") {
		errResp(w, http.StatusConflict, "INVALID_STATE",
			"AdminResolve requires run to be in Unknown state")
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	dbResolution := canonicalState(req.Resolution)

	tx, err := h.db.Begin()
	if err != nil {
		dbErr(w, err)
		return
	}
	defer tx.Rollback()
	x := newTx(tx)

	// Only a run that is still Unknown is resolved: two people resolving at once, or a result
	// arriving meanwhile, must not both write.
	if x.execRows(`UPDATE RunRecords SET State=$1,NeedsAttention=FALSE,UnknownReason=NULL,UpdatedAt=$2 WHERE Id=$3 AND State='Unknown'`,
		dbResolution, now, runID) == 0 && x.err == nil {
		errResp(w, http.StatusConflict, "INVALID_STATE", "AdminResolve requires run to be in Unknown state")
		return
	}

	resID := uuid.NewString()
	x.exec(`INSERT INTO ResolutionRecords(Id,RunId,Resolution,ResolvedBy,ResolvedAt,Notes) VALUES($1,$2,$3,$4,$5,$6)`,
		resID, runID, dbResolution, req.ResolvedBy, now, req.Notes)

	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]string{"runId": runID, "resolution": strings.ToUpper(dbResolution)})
}

// GET /admin/security-events
func (h *AdminHandler) SecurityEvents(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(
		`SELECT Id,EventType,SourceIp,OccurredAt,Details FROM SecurityEvents ORDER BY OccurredAt DESC LIMIT 500`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	type ev struct {
		Id        string  `json:"id"`
		EventType string  `json:"eventType"`
		SourceIp  *string `json:"sourceIp"`
		OccurredAt string `json:"occurredAt"`
		Details   string  `json:"details"`
	}
	var evs []ev
	for rows.Next() {
		var e ev
		var sip sql.NullString
		rows.Scan(&e.Id, &e.EventType, &sip, &e.OccurredAt, &e.Details)
		if sip.Valid {
			e.SourceIp = &sip.String
		}
		evs = append(evs, e)
	}
	if evs == nil {
		evs = []ev{}
	}
	respond(w, http.StatusOK, evs)
}
