package main

import (
	"context"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/go-chi/chi/v5"
	chimw "github.com/go-chi/chi/v5/middleware"

	"tracto/orchestrator/internal/db"
	"tracto/orchestrator/internal/handlers"
	"tracto/orchestrator/internal/middleware"
	"tracto/orchestrator/internal/services"
)

func main() {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))

	// ── Config from environment ───────────────────────────────────────────────
	dbPath := envOr("ORCHESTRATOR_DB_PATH", "orchestrator.db")
	port := envOr("ORCHESTRATOR_PORT", "5080")
	tractoMode := strings.ToLower(envOr("TRACTO_MODE", "isolated"))
	jwtSecret := os.Getenv("ORCHESTRATOR_JWT_SECRET")
	adminCIDR := os.Getenv("ORCHESTRATOR_ADMIN_CIDR")

	// HMAC keys: ORCHESTRATOR_HMAC_KEYS=keyId1:secret1,keyId2:secret2
	hmacKeys := parseHmacKeys(os.Getenv("ORCHESTRATOR_HMAC_KEYS"))

	// Bind address
	bindAddr := "127.0.0.1"
	if tractoMode == "connected" {
		bindAddr = "0.0.0.0"
	}
	addr := fmt.Sprintf("%s:%s", bindAddr, port)

	if jwtSecret == "" {
		logger.Warn("ORCHESTRATOR_JWT_SECRET not set — running in parse-only JWT mode (development)")
	}

	// ── Database ──────────────────────────────────────────────────────────────
	database, err := db.Open(dbPath)
	if err != nil {
		logger.Error("failed to open database", "err", err)
		os.Exit(1)
	}
	defer database.Close()
	logger.Info("database ready", "path", dbPath)

	// ── Startup: restart recovery ─────────────────────────────────────────────
	services.RestartRecovery(database, logger)

	// ── Router ────────────────────────────────────────────────────────────────
	r := chi.NewRouter()
	r.Use(chimw.Recoverer)
	r.Use(middleware.AuditLogging(database, logger))
	r.Use(middleware.JWTAuth(jwtSecret, database, logger))
	r.Use(middleware.AdminIPAllowlist(adminCIDR, database, logger))

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
	cbh := handlers.NewCallbackHandler(database, hmacKeys)
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

	// Admin: schedules
	sh := handlers.NewScheduleHandler(database)
	r.Post("/admin/schedules", sh.Create)
	r.Get("/admin/schedules", sh.List)
	r.Patch("/admin/schedules/{scheduleId}", sh.Update)
	r.Delete("/admin/schedules/{scheduleId}", sh.Delete)

	// Admin: continuation mappings
	cnh := handlers.NewContinuationHandler(database)
	r.Post("/admin/continuations", cnh.Create)
	r.Get("/admin/continuations", cnh.List)
	r.Delete("/admin/continuations/{mappingId}", cnh.Delete)

	// ── Background services ───────────────────────────────────────────────────
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	go services.LeaseExpiryMonitor(ctx, database, logger)
	go services.DeadlineMonitor(ctx, database, logger)

	// ── Server ────────────────────────────────────────────────────────────────
	server := &http.Server{
		Addr:         addr,
		Handler:      r,
		ReadTimeout:  30 * time.Second,
		WriteTimeout: 60 * time.Second,
		IdleTimeout:  120 * time.Second,
	}

	go func() {
		logger.Info("Tracto Orchestrator (Go) starting", "addr", addr, "mode", tractoMode)
		if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			logger.Error("server error", "err", err)
			os.Exit(1)
		}
	}()

	// Graceful shutdown
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	<-sig
	logger.Info("shutting down")
	cancel()
	shutCtx, shutCancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer shutCancel()
	server.Shutdown(shutCtx)
	logger.Info("shutdown complete")
}

func envOr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

func parseHmacKeys(s string) map[string]string {
	m := make(map[string]string)
	if s == "" {
		return m
	}
	for _, pair := range strings.Split(s, ",") {
		parts := strings.SplitN(pair, ":", 2)
		if len(parts) == 2 {
			m[parts[0]] = parts[1]
		}
	}
	return m
}
