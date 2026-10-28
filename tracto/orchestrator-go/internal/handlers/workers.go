package handlers

import (
	"crypto/sha256"
	"database/sql"
	"encoding/json"
	"fmt"
	"net/http"
	"sort"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

type WorkerHandler struct{ db *sql.DB }

func NewWorkerHandler(db *sql.DB) *WorkerHandler { return &WorkerHandler{db: db} }

// POST /workers/register
func (h *WorkerHandler) Register(w http.ResponseWriter, r *http.Request) {
	var req struct {
		WorkerID               string   `json:"workerId"`
		RuntimeType            string   `json:"runtimeType"`
		ActionNames            []string `json:"actionNames"`
		CapabilityDigest       *string  `json:"capabilityDigest"`
		AbsenceThresholdSeconds int     `json:"absenceThresholdSeconds"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.WorkerID == "" || req.RuntimeType == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "workerId and runtimeType are required")
		return
	}
	if req.AbsenceThresholdSeconds <= 0 {
		req.AbsenceThresholdSeconds = 60
	}

	// Validate capabilityDigest if provided
	if req.CapabilityDigest != nil {
		expected := computeCapabilityDigest(req.ActionNames, req.RuntimeType)
		if *req.CapabilityDigest != expected {
			// warning only — same as .NET
			fmt.Printf("[WARN] worker %s: capabilityDigest mismatch (expected %s, got %s)\n",
				req.WorkerID, expected, *req.CapabilityDigest)
		}
	}

	capsJSON := "null"
	if len(req.ActionNames) > 0 {
		b, _ := json.Marshal(map[string]any{
			"actionNames": req.ActionNames,
			"runtimeType": req.RuntimeType,
		})
		capsJSON = string(b)
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)

	var existing string
	err := h.db.QueryRow(`SELECT WorkerId FROM WorkerRecords WHERE WorkerId=?`, req.WorkerID).Scan(&existing)
	if err == sql.ErrNoRows {
		_, err = h.db.Exec(`
			INSERT INTO WorkerRecords(WorkerId,RuntimeType,State,RegisteredAt,LastHeartbeatAt,LastRegisteredAt,AbsenceThresholdSeconds,IsOnline,CapabilitiesJson)
			VALUES(?,?,?,?,?,?,?,1,?)`,
			req.WorkerID, req.RuntimeType, "Available", now, now, now, req.AbsenceThresholdSeconds, capsJSON)
	} else {
		_, err = h.db.Exec(`
			UPDATE WorkerRecords
			SET AbsenceThresholdSeconds=?, CapabilitiesJson=?, LastRegisteredAt=?, IsOnline=1,
			    State=CASE WHEN State='Offline' THEN 'Available' ELSE State END
			WHERE WorkerId=?`,
			req.AbsenceThresholdSeconds, capsJSON, now, req.WorkerID)
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	// Write WORKER_REGISTERED telemetry journal entry
	sentinelRunId := "00000000-0000-0000-0000-000000000001"
	jID := uuid.NewString()
	payload, _ := json.Marshal(map[string]any{
		"workerId":    req.WorkerID,
		"runtimeType": req.RuntimeType,
		"actionCount": len(req.ActionNames),
	})
	h.db.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
		jID, sentinelRunId, nil, "WORKER_REGISTERED", now, string(payload))

	respond(w, http.StatusOK, map[string]string{"workerId": req.WorkerID})
}

// POST /workers/{workerId}/offline
func (h *WorkerHandler) MarkOffline(w http.ResponseWriter, r *http.Request) {
	workerID := chi.URLParam(r, "workerId")
	if workerID == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "workerId required")
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	res, err := h.db.Exec(`UPDATE WorkerRecords SET State='Offline', IsOnline=0 WHERE WorkerId=?`, workerID)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "worker not found")
		return
	}
	_ = now
	respond(w, http.StatusOK, map[string]string{"workerId": workerID})
}

func computeCapabilityDigest(actionNames []string, runtimeType string) string {
	sorted := make([]string, len(actionNames))
	copy(sorted, actionNames)
	sort.Strings(sorted)
	b, _ := json.Marshal(map[string]any{
		"ActionNames": sorted,
		"RuntimeType": runtimeType,
	})
	sum := sha256.Sum256(b)
	return fmt.Sprintf("%x", sum)
}

// GET /workers (admin list, no auth needed in dev)
func (h *WorkerHandler) List(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT WorkerId,RuntimeType,State,IsOnline,LastHeartbeatAt,RegisteredAt FROM WorkerRecords`)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()
	type item struct {
		WorkerID       string `json:"workerId"`
		RuntimeType    string `json:"runtimeType"`
		State          string `json:"state"`
		IsOnline       bool   `json:"isOnline"`
		LastHeartbeat  string `json:"lastHeartbeatAt"`
		RegisteredAt   string `json:"registeredAt"`
	}
	var items []item
	for rows.Next() {
		var it item
		var online int
		rows.Scan(&it.WorkerID, &it.RuntimeType, &it.State, &online, &it.LastHeartbeat, &it.RegisteredAt)
		it.IsOnline = online != 0
		it.State = strings.ToUpper(it.State)
		items = append(items, it)
	}
	if items == nil {
		items = []item{}
	}
	respond(w, http.StatusOK, items)
}
