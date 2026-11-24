package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"net/http"
	"os"
	"path/filepath"

	"tracto/sheet-service/internal/api"
	"tracto/sheet-service/internal/envfile"
	"tracto/sheet-service/internal/msauth"
	"tracto/sheet-service/internal/process"
	"tracto/sheet-service/internal/secretbox"
)

// loadEnv reads the .env file next to the executable, then the one in the working directory.
// SHEET_SERVICE_ENV_FILE names a different file. Real environment variables take precedence.
func loadEnv() {
	paths := []string{filepath.Join(filepath.Dir(os.Args[0]), ".env"), ".env"}
	if p := os.Getenv("SHEET_SERVICE_ENV_FILE"); p != "" {
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

// login signs in to Microsoft with the device-code flow and saves the connection, so that flows
// can read SharePoint files later without anyone being present.
//
//	sheet-service login [-name default]
func login(args []string) {
	fs := flag.NewFlagSet("login", flag.ExitOnError)
	name := fs.String("name", msauth.DefaultName, "name to save this sign-in under (a request picks it with \"connection\")")
	_ = fs.Parse(args)

	cfg, dbc, err := msauth.ConfigFromEnv(os.Getenv)
	if err != nil {
		log.Fatal(err)
	}
	store, err := msauth.OpenStore(context.Background(), dbc)
	if err != nil {
		log.Fatalf("opening the database: %v", err)
	}
	defer store.Close()
	if err := msauth.Login(context.Background(), store, cfg, *name, os.Stdout); err != nil {
		log.Fatal(err)
	}
	fmt.Printf("Signed in. Saved as %q in the database\n", *name)
}

func main() {
	loadEnv()
	if len(os.Args) > 1 && os.Args[1] == "login" {
		login(os.Args[2:])
		return
	}
	port := os.Getenv("SHEET_SERVICE_PORT")
	if port == "" {
		port = "5090"
	}

	// Everything the service keeps lives in PostgreSQL (DATABASE_URL), in the sheets schema, and
	// the stored Microsoft tokens are encrypted with TRACTO_SECRET_KEY. Refuse to start without
	// either, rather than fail on the first request.
	if _, err := secretbox.FromEnv(); err != nil {
		log.Fatal(err)
	}
	store, err := process.OpenStore(context.Background(), os.Getenv("DATABASE_URL"), os.Getenv("SHEET_SERVICE_SCHEMA"))
	if err != nil {
		log.Fatalf("opening the database: %v", err)
	}
	defer store.Close()
	if n, err := store.InterruptRunning(); err != nil {
		log.Fatalf("checking the database: %v", err)
	} else if n > 0 {
		log.Printf("%d batch(es) were running when the service last stopped; marked Interrupted", n)
	}
	orchURL := os.Getenv("SHEET_SERVICE_ORCHESTRATOR_URL")
	if orchURL == "" {
		orchURL = "http://127.0.0.1:5080"
	}
	pm := process.NewManager(store, process.NewOrchestrator(orchURL))

	log.Printf("sheet-service listening on :%s (orchestrator %s)", port, orchURL)
	log.Fatal(http.ListenAndServe(":"+port, api.NewWithProcess(pm)))
}
