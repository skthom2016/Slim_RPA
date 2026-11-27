// Package api is the finance service's HTTP interface.
//
//	GET  /health              is the service up
//	POST /claims/run/start    A1: approved claims to draft bills
//	POST /payrun/start        A2: approved bills to a bank payment file
//
// Both runs do their work inside the call (seconds at this size) and reply with a message that
// says what was done or why nothing was done. Each item is logged to the calling job's run log.
package api

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"strings"
	"sync"
	"time"

	"tracto/finance-service/internal/a1"
	"tracto/finance-service/internal/a2"
	"tracto/finance-service/internal/orch"
	"tracto/finance-service/internal/store"
)

// Config is what the handlers need.
type Config struct {
	Dirs       store.Dirs
	Orch       *orch.Client // nil: log to the console only
	A1Artifact string       // script id of the A1 main job, to find its run
	A2Artifact string       // script id of the A2 main job
	Org        string       // organisation name on the bills
	OrgAccount string       // organisation account in the payment file header
}

// Handler returns the service's routes.
func Handler(cfg Config) http.Handler {
	mux := http.NewServeMux()
	var a1Lock, a2Lock sync.Mutex

	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, http.StatusOK, map[string]string{"status": "ok", "dataDir": cfg.Dirs.Data})
	})

	mux.HandleFunc("POST /claims/run/start", func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			RunID string `json:"runId"`
		}
		if !decode(w, r, &req) {
			return
		}
		if !a1Lock.TryLock() {
			writeJSON(w, http.StatusConflict, map[string]string{"error": store.ErrBusy.Error()})
			return
		}
		defer a1Lock.Unlock()
		lg := runLog(r.Context(), cfg, req.RunID, cfg.A1Artifact, "[A1]")
		res, err := a1.Run(cfg.Dirs, a1.Options{Org: cfg.Org}, lg)
		if err != nil {
			lg.Warn("A1 stopped: " + err.Error())
			writeJSON(w, http.StatusInternalServerError, map[string]any{"error": err.Error(), "partial": res})
			return
		}
		writeJSON(w, http.StatusOK, res)
	})

	mux.HandleFunc("POST /payrun/start", func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			RunID       string `json:"runId"`
			PaymentDate string `json:"paymentDate"`
		}
		if !decode(w, r, &req) {
			return
		}
		if !a2Lock.TryLock() {
			writeJSON(w, http.StatusConflict, map[string]string{"error": store.ErrBusy.Error()})
			return
		}
		defer a2Lock.Unlock()
		lg := runLog(r.Context(), cfg, req.RunID, cfg.A2Artifact, "[A2]")
		res, err := a2.Run(cfg.Dirs, a2.Options{PaymentDate: strings.TrimSpace(req.PaymentDate), OrgAccount: cfg.OrgAccount}, lg)
		if err != nil {
			if errors.Is(err, a2.ErrInvalid) {
				lg.Warn("A2 not started: " + err.Error())
				writeJSON(w, http.StatusBadRequest, map[string]any{"error": err.Error()})
				return
			}
			lg.Warn("A2 stopped: " + err.Error())
			writeJSON(w, http.StatusInternalServerError, map[string]any{"error": err.Error(), "partial": res})
			return
		}
		writeJSON(w, http.StatusOK, res)
	})
	return mux
}

// runLog finds the run to log to: the one named in the request, or else the running main job
// of the process's script. Called from outside a job, there is none and the console is used.
func runLog(ctx context.Context, cfg Config, runID, artifact, prefix string) orch.RunLog {
	lg := orch.RunLog{Client: cfg.Orch, RunID: strings.TrimSpace(runID), Prefix: prefix}
	if cfg.Orch == nil || lg.RunID != "" || artifact == "" {
		return lg
	}
	c, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	id, err := cfg.Orch.FindRunning(c, artifact)
	switch {
	case err != nil:
		log.Printf("%s could not ask the orchestrator for the running %s job: %v", prefix, artifact, err)
	case id == "":
		log.Printf("%s no running %s job found; logging to this console only", prefix, artifact)
	default:
		lg.RunID = id
		log.Printf("%s logging to run %s", prefix, id)
	}
	return lg
}

// decode reads an optional JSON body. An empty body is the same as {}.
func decode(w http.ResponseWriter, r *http.Request, v any) bool {
	b, err := io.ReadAll(io.LimitReader(r.Body, 1<<16))
	if err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
		return false
	}
	if strings.TrimSpace(string(b)) == "" {
		return true
	}
	if err := json.Unmarshal(b, v); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "the request body is not valid JSON: " + err.Error()})
		return false
	}
	return true
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
