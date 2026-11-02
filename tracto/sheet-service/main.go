package main

import (
	"log"
	"net/http"
	"os"
	"path/filepath"

	"tracto/sheet-service/internal/api"
	"tracto/sheet-service/internal/envfile"
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

func main() {
	loadEnv()
	port := os.Getenv("SHEET_SERVICE_PORT")
	if port == "" {
		port = "5090"
	}
	log.Printf("sheet-service listening on :%s", port)
	log.Fatal(http.ListenAndServe(":"+port, api.New()))
}
