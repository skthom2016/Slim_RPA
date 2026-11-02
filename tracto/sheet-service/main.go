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

	cfg, dbPath, err := msauth.ConfigFromEnv(os.Getenv)
	if err != nil {
		log.Fatal(err)
	}
	store, err := msauth.OpenStore(dbPath)
	if err != nil {
		log.Fatalf("opening %s: %v", dbPath, err)
	}
	defer store.Close()
	if err := msauth.Login(context.Background(), store, cfg, *name, os.Stdout); err != nil {
		log.Fatal(err)
	}
	fmt.Printf("Signed in. Saved as %q in %s\n", *name, dbPath)
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
	log.Printf("sheet-service listening on :%s", port)
	log.Fatal(http.ListenAndServe(":"+port, api.New()))
}
