// Package server builds the orchestrator's HTTP router, so the binary and the tests serve the
// same API.
package server

import (
	"context"
	"database/sql"
	"log/slog"
	"net/http"

	"github.com/go-chi/chi/v5"
	chimw "github.com/go-chi/chi/v5/middleware"

	"tracto/orchestrator/internal/handlers"
	"tracto/orchestrator/internal/middleware"
	"tracto/orchestrator/internal/scheduler"
	"tracto/orchestrator/internal/services"
)

// Config is what the router needs besides the database.
type Config struct {
	JWTSecret string            // empty: parse-only JWT mode (development)
	AdminCIDR string            // empty: no admin address restriction
	HmacKeys  map[string]string // keyId -> secret for callbacks
}

// NewRouter returns the orchestrator API over database.
func NewRouter(database *sql.DB, logger *slog.Logger, cfg Config) http.Handler {
	r := chi.NewRouter()
	r.Use(chimw.Recoverer)
	r.Use(middleware.AuditLogging(database, logger))
	r.Use(middleware.JWTAuth(cfg.JWTSecret, database, logger))
	r.Use(middleware.AdminIPAllowlist(cfg.AdminCIDR, database, logger))

	// Health
	r.Get("/health", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusOK)
		w.Write([]byte(`{"status":"Healthy"}`))
	})

	// Workers
	wh := handlers.NewWorkerHandler(database)
	r.Post("/workers/register", wh.Register)
	r.Post("/workers/{workerId}/offline", wh.MarkOffline)

	// Jobs
	jh := handlers.NewJobHandler(database)
	r.Post("/jobs", jh.CreateJob)
	r.Get("/jobs", jh.ListJobs)
	r.Post("/jobs/pull", jh.PullJob)
	r.Get("/jobs/{runId}", jh.GetJob)

	// Lease & heartbeat
	lh := handlers.NewLeaseHandler(database)
	r.Post("/jobs/{runId}/lease/accept", lh.AcceptLease)
	r.Post("/jobs/{runId}/heartbeat", lh.Heartbeat)

	// Result
	rsh := handlers.NewResultHandler(database)
	r.Post("/jobs/{runId}/result", rsh.CommitResult)

	// Reconcile
	rch := handlers.NewReconcileHandler(database)
	r.Post("/jobs/{runId}/reconcile", rch.Reconcile)

	// Cancel
	ch := handlers.NewCancelHandler(database)
	r.Post("/jobs/{runId}/cancel", ch.Cancel)

	// Artifacts
	ah := handlers.NewArtifactHandler(database)
	r.Get("/artifacts", ah.List)
	r.Put("/artifacts/{id}/versions/{version}", ah.Publish)
	r.Get("/artifacts/{id}/versions/{version}", ah.Get)
	r.Delete("/artifacts/{id}/versions/{version}", ah.Delete)

	// Callbacks
	cbh := handlers.NewCallbackHandler(database, cfg.HmacKeys)
	r.Post("/callbacks", cbh.Receive)

	// Admin
	adh := handlers.NewAdminHandler(database)
	r.Get("/admin/runs", adh.ListUnknownRuns)
	r.Post("/admin/runs/{runId}/resolve", adh.ResolveRun)
	r.Get("/admin/security-events", adh.SecurityEvents)
	r.Get("/workers", wh.List)

	// Admin: telemetry
	th := handlers.NewTelemetryHandler(database)
	r.Get("/admin/telemetry", th.List)
	r.Get("/admin/telemetry/{runId}", th.GetByRun)

	// Run logs: what a runtime warned or failed about while it ran a job
	lgh := handlers.NewLogHandler(database)
	r.Post("/jobs/{runId}/logs", lgh.Append)
	r.Get("/admin/logs", lgh.List)
	r.Get("/admin/logs/{runId}", lgh.GetByRun)

	// Admin: schedules
	sh := handlers.NewScheduleHandler(database)
	r.Post("/admin/schedules", sh.Create)
	r.Get("/admin/schedules", sh.List)
	r.Get("/admin/schedules/completed", sh.Completed)
	r.Patch("/admin/schedules/{scheduleId}", sh.Update)
	r.Delete("/admin/schedules/{scheduleId}", sh.Delete)

	// Admin: continuation mappings
	cnh := handlers.NewContinuationHandler(database)
	r.Post("/admin/continuations", cnh.Create)
	r.Get("/admin/continuations", cnh.List)
	r.Delete("/admin/continuations/{mappingId}", cnh.Delete)

	return r
}

// StartBackground starts the monitors and the scheduler; they stop when ctx ends.
func StartBackground(ctx context.Context, database *sql.DB, logger *slog.Logger) {
	go services.LeaseExpiryMonitor(ctx, database, logger)
	go services.DeadlineMonitor(ctx, database, logger)
	go scheduler.New(database, logger).Run(ctx)
}
