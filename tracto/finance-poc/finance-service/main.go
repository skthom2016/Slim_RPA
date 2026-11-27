// Command finance-service runs the finance automation proof of concept: process A1 (approved
// claims to draft bills) and process A2 (approved bills to a bank payment file). One-step main
// jobs on the Orchestrator call it with http.post; it works on the folders under the data
// directory and logs each item to the calling job's run log.
package main

import (
	"log"
	"net/http"
	"os"
	"path/filepath"

	"tracto/finance-service/internal/api"
	"tracto/finance-service/internal/envfile"
	"tracto/finance-service/internal/orch"
	"tracto/finance-service/internal/store"
)

// loadEnv reads the .env file next to the executable, then the one in the working directory.
// FINANCE_SERVICE_ENV_FILE names a different file. Real environment variables take precedence.
func loadEnv() {
	paths := []string{filepath.Join(filepath.Dir(os.Args[0]), ".env"), ".env"}
	if p := os.Getenv("FINANCE_SERVICE_ENV_FILE"); p != "" {
		paths = []string{p}
	}
	for _, p := range paths {
		found, err := envfile.Load(p)
		if err != nil {
			log.Fatalf("reading %s: %v", p, err)
		}
		if found {
			log.Printf("settings loaded from %s", p)
		}
	}
}

func env(name, def string) string {
	if v := os.Getenv(name); v != "" {
		return v
	}
	return def
}

func absPath(p string) string {
	a, err := filepath.Abs(p)
	if err != nil {
		log.Fatalf("path %s: %v", p, err)
	}
	return a
}

func main() {
	loadEnv()
	port := env("FINANCE_SERVICE_PORT", "5095")
	dirs := store.Dirs{
		Data: absPath(env("FINANCE_DATA_DIR", "../poc-data")),
		Seed: absPath(env("FINANCE_SEED_DIR", "../seed")),
	}
	if err := dirs.Ensure(); err != nil {
		log.Fatalf("creating the data folders under %s: %v", dirs.Data, err)
	}
	if _, err := dirs.LoadReference(); err != nil {
		log.Fatalf("%v (seed folder %s)", err, dirs.Seed)
	}

	var oc *orch.Client
	if base := env("FINANCE_ORCHESTRATOR_URL", "http://127.0.0.1:5080"); base != "off" {
		oc = orch.New(base)
	}
	cfg := api.Config{
		Dirs:       dirs,
		Orch:       oc,
		A1Artifact: env("FINANCE_A1_SCRIPT", "finance-a1-claims-to-bills"),
		A2Artifact: env("FINANCE_A2_SCRIPT", "finance-a2-payment-file"),
		Org:        env("FINANCE_ORG_NAME", "Demo Organisation"),
		OrgAccount: env("FINANCE_ORG_ACCOUNT", "DEMO-ORG-0001"),
	}
	log.Printf("finance service on http://127.0.0.1:%s, data in %s, reference data in %s", port, dirs.Data, dirs.Seed)
	srv := &http.Server{Addr: "127.0.0.1:" + port, Handler: api.Handler(cfg)}
	log.Fatal(srv.ListenAndServe())
}
