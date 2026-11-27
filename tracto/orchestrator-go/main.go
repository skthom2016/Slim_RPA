package main

import (
	"context"
	"fmt"
	"log/slog"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/db"
	"tracto/orchestrator/internal/envfile"
	"tracto/orchestrator/internal/handlers"
	"tracto/orchestrator/internal/server"
	"tracto/orchestrator/internal/services"
	"tracto/orchestrator/internal/vault"
)

// loadEnv reads the .env file next to the executable, then the one in the working directory.
// ORCHESTRATOR_ENV_FILE names a different file. Real environment variables take precedence.
func loadEnv(logger *slog.Logger) {
	paths := []string{filepath.Join(filepath.Dir(os.Args[0]), ".env"), ".env"}
	if p := os.Getenv("ORCHESTRATOR_ENV_FILE"); p != "" {
		paths = []string{p}
	}
	for _, p := range paths {
		found, err := envfile.Load(p)
		if err != nil {
			logger.Error("could not read settings file", "path", p, "err", err)
			os.Exit(1)
		}
		if found {
			logger.Info("settings loaded", "path", p)
		}
	}
}

func main() {
	// The orchestrator keeps and reports every time in UTC; this also makes its log lines UTC.
	time.Local = time.UTC

	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))
	loadEnv(logger)

	// ── Config from environment ───────────────────────────────────────────────
	databaseURL := os.Getenv("DATABASE_URL")
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
	if databaseURL == "" {
		logger.Error("DATABASE_URL is not set: it names the PostgreSQL database, for example postgres://tracto:<password>@localhost:5432/tracto")
		os.Exit(1)
	}
	database, err := db.Open(context.Background(), databaseURL, os.Getenv("ORCHESTRATOR_SCHEMA"))
	if err != nil {
		logger.Error("failed to open database", "err", err)
		os.Exit(1)
	}
	defer database.Close()
	logger.Info("database ready", "target", describe(databaseURL))

	// ── Credential store: master key, first administrator ─────────────────────
	credVault, err := vault.New(os.Getenv(vault.EnvKey), os.Getenv(vault.EnvKeyID),
		os.Getenv(vault.EnvOldKey), os.Getenv(vault.EnvOldKeyID))
	if err != nil {
		logger.Error("master key is not usable", "err", err)
		os.Exit(1)
	}
	if len(os.Args) > 1 && os.Args[1] == "rekey" {
		n, err := handlers.Rekey(database, credVault)
		if err != nil {
			logger.Error("rekey failed; nothing was changed", "err", err)
			os.Exit(1)
		}
		logger.Info("rekey done", "secretsResealed", n, "keyId", credVault.KeyID())
		return
	}
	if credVault.IsLocked() {
		logger.Warn(vault.EnvKey + " is not set: the secret store is locked (jobs still run)")
	} else if err := handlers.VerifyAll(database, credVault); err != nil {
		logger.Error("the master key does not open the stored secrets", "err", err)
		os.Exit(1)
	}
	if err := auth.EnsureFirstAdmin(database, logger); err != nil {
		logger.Error("could not create the first administrator", "err", err)
		os.Exit(1)
	}

	// ── Startup: restart recovery ─────────────────────────────────────────────
	services.RestartRecovery(database, logger)

	// ── Router and background services ────────────────────────────────────────
	router := server.NewRouter(database, logger, server.Config{
		JWTSecret: jwtSecret, AdminCIDR: adminCIDR, HmacKeys: hmacKeys, Vault: credVault,
	})
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	server.StartBackground(ctx, database, logger)

	// ── Server ────────────────────────────────────────────────────────────────
	srv := &http.Server{
		Addr:         addr,
		Handler:      router,
		ReadTimeout:  30 * time.Second,
		WriteTimeout: 60 * time.Second,
		IdleTimeout:  120 * time.Second,
	}

	go func() {
		logger.Info("Tracto Orchestrator (Go) starting", "addr", addr, "mode", tractoMode)
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
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
	srv.Shutdown(shutCtx)
	logger.Info("shutdown complete")
}

// describe names a database URL without its credentials, for the log.
func describe(raw string) string {
	u, err := url.Parse(raw)
	if err != nil {
		return "(unreadable DATABASE_URL)"
	}
	return u.Host + u.Path
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
