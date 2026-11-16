package handlers

import (
	"database/sql"
	"encoding/json"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"

	"tracto/orchestrator/internal/cronspec"
)

type ScheduleHandler struct{ db *sql.DB }

func NewScheduleHandler(db *sql.DB) *ScheduleHandler { return &ScheduleHandler{db: db} }

// scheduleView is a schedule as the API returns it.
type scheduleView struct {
	ScheduleId     string            `json:"scheduleId"`
	CronExpression string            `json:"cronExpression"`
	TimeZone       string            `json:"timeZone"`
	ArtifactId     string            `json:"artifactId"`
	Version        string            `json:"version"`
	Params         map[string]string `json:"params"`
	Enabled        bool              `json:"enabled"`
	OverlapPolicy  string            `json:"overlapPolicy"`
	GraceSeconds   int               `json:"graceSeconds"`
	NextRunAt      *string           `json:"nextRunAt"`
	NextRunLocal   *string           `json:"nextRunLocal"`
	LastFireAt     *string           `json:"lastFireAt"`
	LastRunId      *string           `json:"lastRunId"`
	LastStatus     *string           `json:"lastStatus"`
	LastError      *string           `json:"lastError"`
	// ActiveRunState is the state of the schedule's last run while that run has not finished
	// (QUEUED or RUNNING), and null otherwise.
	ActiveRunState *string `json:"activeRunState"`
	CreatedAt      string  `json:"createdAt"`
}

const scheduleColumns = `ScheduleId,CronExpression,COALESCE(TimeZone,'UTC'),ArtifactId,Version,
	COALESCE(ParamsJson,'{}'),Enabled,COALESCE(OverlapPolicy,'skip'),COALESCE(GraceSeconds,900),
	NextRunAt,LastFireAt,LastRunId,LastStatus,LastError,CreatedAt`

type rowScanner interface{ Scan(dest ...any) error }

func scanSchedule(r rowScanner) (scheduleView, error) {
	var v scheduleView
	var paramsJSON string
	var enabled int
	var next, lastFire, lastRun, lastStatus, lastErr sql.NullString
	if err := r.Scan(&v.ScheduleId, &v.CronExpression, &v.TimeZone, &v.ArtifactId, &v.Version,
		&paramsJSON, &enabled, &v.OverlapPolicy, &v.GraceSeconds,
		&next, &lastFire, &lastRun, &lastStatus, &lastErr, &v.CreatedAt); err != nil {
		return v, err
	}
	v.Enabled = enabled != 0
	v.Params = map[string]string{}
	_ = json.Unmarshal([]byte(paramsJSON), &v.Params)
	str := func(n sql.NullString) *string {
		if !n.Valid {
			return nil
		}
		s := n.String
		return &s
	}
	v.NextRunAt, v.LastFireAt, v.LastRunId, v.LastStatus, v.LastError =
		str(next), str(lastFire), str(lastRun), str(lastStatus), str(lastErr)
	if next.Valid {
		if t, err := cronspec.ParseTime(next.String); err == nil {
			if loc, err := time.LoadLocation(v.TimeZone); err == nil {
				l := t.In(loc).Format("2006-01-02 15:04 MST")
				v.NextRunLocal = &l
			}
		}
	}
	return v, nil
}

func (h *ScheduleHandler) get(id string) (scheduleView, error) {
	return scanSchedule(h.db.QueryRow(`SELECT `+scheduleColumns+` FROM ScheduleRecords WHERE ScheduleId=?`, id))
}

// validateSchedule checks the fields a schedule needs to fire and returns its next fire time
// after now. The message is meant for the caller.
func validateSchedule(expr, zone, overlap string, grace int, now time.Time) (time.Time, string) {
	next, err := cronspec.Next(expr, zone, now)
	if err != nil {
		return time.Time{}, err.Error()
	}
	if overlap != cronspec.OverlapSkip && overlap != cronspec.OverlapQueue {
		return time.Time{}, "overlapPolicy must be \"skip\" or \"queue\""
	}
	if grace < cronspec.MinGraceSeconds || grace > cronspec.MaxGraceSeconds {
		return time.Time{}, "graceSeconds must be between 60 and 86400"
	}
	return next, ""
}

// POST /admin/schedules
func (h *ScheduleHandler) Create(w http.ResponseWriter, r *http.Request) {
	var req struct {
		CronExpression string            `json:"cronExpression"`
		TimeZone       string            `json:"timeZone"`
		ArtifactId     string            `json:"artifactId"`
		Version        string            `json:"version"`
		Params         map[string]string `json:"params"`
		Enabled        *bool             `json:"enabled"`
		OverlapPolicy  string            `json:"overlapPolicy"`
		GraceSeconds   *int              `json:"graceSeconds"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.CronExpression == "" || req.ArtifactId == "" || req.Version == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "cronExpression, artifactId, version required")
		return
	}
	if req.OverlapPolicy == "" {
		req.OverlapPolicy = cronspec.OverlapSkip
	}
	grace := cronspec.DefaultGraceSeconds
	if req.GraceSeconds != nil {
		grace = *req.GraceSeconds
	}
	now := time.Now().UTC()
	next, msg := validateSchedule(req.CronExpression, req.TimeZone, req.OverlapPolicy, grace, now)
	if msg != "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", msg)
		return
	}
	ok, err := ArtifactPublished(h.db, req.ArtifactId, req.Version)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	if !ok {
		errResp(w, http.StatusUnprocessableEntity, "ARTIFACT_NOT_FOUND",
			"artifact "+req.ArtifactId+"/"+req.Version+" is not published")
		return
	}
	enabled := 1
	if req.Enabled != nil && !*req.Enabled {
		enabled = 0
	}
	var nextRun any
	if enabled == 1 {
		nextRun = cronspec.Format(next)
	}
	params := req.Params
	if params == nil {
		params = map[string]string{}
	}
	paramsJSON, _ := json.Marshal(params)
	id := uuid.NewString()
	_, err = h.db.Exec(`
		INSERT INTO ScheduleRecords(ScheduleId,CronExpression,TimeZone,ArtifactId,Version,ParamsJson,
			Enabled,OverlapPolicy,GraceSeconds,NextRunAt,CreatedAt)
		VALUES(?,?,?,?,?,?,?,?,?,?,?)`,
		id, req.CronExpression, req.TimeZone, req.ArtifactId, req.Version, string(paramsJSON),
		enabled, req.OverlapPolicy, grace, nextRun, now.Format(time.RFC3339Nano))
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	v, err := h.get(id)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusCreated, v)
}

// GET /admin/schedules
//
// With no view, every schedule in the order it was created. With view=upcoming, the order a
// person wants to read them in: the schedule that fires soonest first, and a schedule that is
// switched off (so has no next run) after all the others.
func (h *ScheduleHandler) List(w http.ResponseWriter, r *http.Request) {
	order := `ORDER BY CreatedAt`
	switch view := r.URL.Query().Get("view"); view {
	case "":
	case "upcoming":
		order = `ORDER BY Enabled DESC, (NextRunAt IS NULL), NextRunAt ASC, CreatedAt ASC`
	default:
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST",
			"view must be \"upcoming\"; finished runs are at /admin/schedules/completed")
		return
	}
	rows, err := h.db.Query(`SELECT ` + scheduleColumns + ` FROM ScheduleRecords ` + order)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	items := []scheduleView{}
	for rows.Next() {
		v, err := scanSchedule(rows)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
		items = append(items, v)
	}
	if err := rows.Err(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	// The database has one connection, so the rows must be closed before the next query.
	rows.Close()
	h.fillActiveRuns(items)
	respond(w, http.StatusOK, items)
}

// fillActiveRuns marks each schedule whose last run has not finished yet.
func (h *ScheduleHandler) fillActiveRuns(items []scheduleView) {
	for i := range items {
		if items[i].LastRunId == nil {
			continue
		}
		var state string
		if err := h.db.QueryRow(`SELECT State FROM RunRecords WHERE Id=?`, *items[i].LastRunId).Scan(&state); err != nil {
			continue
		}
		if !isTerminalState(state) {
			up := strings.ToUpper(state)
			items[i].ActiveRunState = &up
		}
	}
}

// completedRun is one finished run that a schedule started.
type completedRun struct {
	RunId          string  `json:"runId"`
	ScheduleId     string  `json:"scheduleId"`
	ScheduleExists bool    `json:"scheduleExists"`
	ArtifactId     string  `json:"artifactId"`
	Version        string  `json:"version"`
	CronExpression *string `json:"cronExpression"`
	TimeZone       *string `json:"timeZone"`
	ScheduledFor   *string `json:"scheduledFor"`
	QueuedAt       string  `json:"queuedAt"`
	FinishedAt     string  `json:"finishedAt"`
	State          string  `json:"state"`
	WorkerId       *string `json:"workerId"`
}

// GET /admin/schedules/completed?limit=50&offset=0&scheduleId=
//
// The runs a schedule started that have finished, most recently finished first. Each run keeps
// the schedule's id and the fire time in its eventId, which is where scheduleId and scheduledFor
// come from; a run whose schedule has since been deleted is still listed.
func (h *ScheduleHandler) Completed(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	limit, offset := 50, 0
	if v := q.Get("limit"); v != "" {
		n, err := strconv.Atoi(v)
		if err != nil || n < 1 || n > 200 {
			errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "limit must be between 1 and 200")
			return
		}
		limit = n
	}
	if v := q.Get("offset"); v != "" {
		n, err := strconv.Atoi(v)
		if err != nil || n < 0 {
			errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "offset must be 0 or more")
			return
		}
		offset = n
	}

	const scheduleIDExpr = `substr(r.EventId, 1, instr(r.EventId, ':') - 1)`
	where := `r.TriggerType='Schedule' AND (r.ParentRunId IS NULL OR r.ParentRunId='')
		AND lower(r.State) IN ('succeeded','failed','handedoff','cancelled','timedout','unknown')`
	args := []any{}
	if id := q.Get("scheduleId"); id != "" {
		where += ` AND ` + scheduleIDExpr + `=?`
		args = append(args, id)
	}

	var total int
	if err := h.db.QueryRow(`SELECT COUNT(*) FROM RunRecords r WHERE `+where, args...).Scan(&total); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	rows, err := h.db.Query(`
		SELECT r.Id, COALESCE(r.EventId,''), COALESCE(r.ScriptArtifactId,''), COALESCE(r.ScriptVersion,''),
		       r.CreatedAt, COALESCE(r.UpdatedAt, r.CreatedAt), r.State, r.WorkerId,
		       s.ScheduleId, s.CronExpression, COALESCE(s.TimeZone,'UTC')
		FROM RunRecords r
		LEFT JOIN ScheduleRecords s ON s.ScheduleId = `+scheduleIDExpr+`
		WHERE `+where+`
		ORDER BY COALESCE(r.UpdatedAt, r.CreatedAt) DESC, r.CreatedAt DESC
		LIMIT ? OFFSET ?`, append(args, limit, offset)...)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	items := []completedRun{}
	for rows.Next() {
		var c completedRun
		var eventID string
		var worker, schedID, cron, zone sql.NullString
		if err := rows.Scan(&c.RunId, &eventID, &c.ArtifactId, &c.Version, &c.QueuedAt, &c.FinishedAt,
			&c.State, &worker, &schedID, &cron, &zone); err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
		c.State = strings.ToUpper(c.State)
		if i := strings.Index(eventID, ":"); i >= 0 {
			c.ScheduleId = eventID[:i]
			if fire := eventID[i+1:]; fire != "" {
				c.ScheduledFor = &fire
			}
		}
		if worker.Valid {
			c.WorkerId = &worker.String
		}
		if schedID.Valid {
			c.ScheduleExists = true
			c.CronExpression = &cron.String
			c.TimeZone = &zone.String
		}
		items = append(items, c)
	}
	if err := rows.Err(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusOK, map[string]any{"total": total, "limit": limit, "offset": offset, "items": items})
}

// PATCH /admin/schedules/{scheduleId}
//
// Any field may be sent. Enabling a schedule, or changing its expression or time zone, works out
// the next fire time from now, so a schedule that was off owes nothing for the time it was off.
func (h *ScheduleHandler) Update(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "scheduleId")
	var req struct {
		CronExpression *string            `json:"cronExpression"`
		TimeZone       *string            `json:"timeZone"`
		Version        *string            `json:"version"`
		Params         *map[string]string `json:"params"`
		Enabled        *bool              `json:"enabled"`
		OverlapPolicy  *string            `json:"overlapPolicy"`
		GraceSeconds   *int               `json:"graceSeconds"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	cur, err := h.get(id)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "schedule not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	restart := false
	if req.CronExpression != nil {
		cur.CronExpression, restart = *req.CronExpression, true
	}
	if req.TimeZone != nil {
		cur.TimeZone, restart = *req.TimeZone, true
	}
	if req.Version != nil {
		cur.Version = *req.Version
	}
	if req.Params != nil {
		cur.Params = *req.Params
		if cur.Params == nil {
			cur.Params = map[string]string{}
		}
	}
	if req.OverlapPolicy != nil {
		cur.OverlapPolicy = *req.OverlapPolicy
	}
	if req.GraceSeconds != nil {
		cur.GraceSeconds = *req.GraceSeconds
	}
	if req.Enabled != nil {
		if *req.Enabled && !cur.Enabled {
			restart = true
		}
		cur.Enabled = *req.Enabled
	}

	now := time.Now().UTC()
	// Switching a schedule off needs no valid expression, so a broken one can still be disabled.
	next, msg := validateSchedule(cur.CronExpression, cur.TimeZone, cur.OverlapPolicy, cur.GraceSeconds, now)
	if msg != "" && (cur.Enabled || req.CronExpression != nil || req.TimeZone != nil) {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", msg)
		return
	}
	if req.Version != nil {
		ok, err := ArtifactPublished(h.db, cur.ArtifactId, cur.Version)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
			return
		}
		if !ok {
			errResp(w, http.StatusUnprocessableEntity, "ARTIFACT_NOT_FOUND",
				"artifact "+cur.ArtifactId+"/"+cur.Version+" is not published")
			return
		}
	}

	paramsJSON, _ := json.Marshal(cur.Params)
	var res sql.Result
	if !cur.Enabled {
		res, err = h.db.Exec(`UPDATE ScheduleRecords SET CronExpression=?,TimeZone=?,Version=?,ParamsJson=?,
			Enabled=0,OverlapPolicy=?,GraceSeconds=?,NextRunAt=NULL,UpdatedAt=? WHERE ScheduleId=?`,
			cur.CronExpression, cur.TimeZone, cur.Version, string(paramsJSON),
			cur.OverlapPolicy, cur.GraceSeconds, now.Format(time.RFC3339Nano), id)
	} else if restart {
		res, err = h.db.Exec(`UPDATE ScheduleRecords SET CronExpression=?,TimeZone=?,Version=?,ParamsJson=?,
			Enabled=1,OverlapPolicy=?,GraceSeconds=?,NextRunAt=?,LastStatus=NULL,LastError=NULL,UpdatedAt=?
			WHERE ScheduleId=?`,
			cur.CronExpression, cur.TimeZone, cur.Version, string(paramsJSON),
			cur.OverlapPolicy, cur.GraceSeconds, cronspec.Format(next), now.Format(time.RFC3339Nano), id)
	} else {
		res, err = h.db.Exec(`UPDATE ScheduleRecords SET Version=?,ParamsJson=?,OverlapPolicy=?,GraceSeconds=?,
			UpdatedAt=? WHERE ScheduleId=?`,
			cur.Version, string(paramsJSON), cur.OverlapPolicy, cur.GraceSeconds,
			now.Format(time.RFC3339Nano), id)
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "schedule not found")
		return
	}
	v, err := h.get(id)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	respond(w, http.StatusOK, v)
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
