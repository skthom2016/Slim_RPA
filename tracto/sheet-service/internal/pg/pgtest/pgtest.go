// Package pgtest gives tests their own PostgreSQL schema, created from the real migrations and
// dropped when the test ends, so tests never touch each other's rows or the service's own data.
//
// The server comes from TEST_DATABASE_URL, or DATABASE_URL, read from the environment or from a
// .env file in the working directory or one of its parents. Without one the test is skipped.
package pgtest

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"io/fs"
	"os"
	"path/filepath"
	"testing"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/stdlib"

	"tracto/sheet-service/internal/envfile"
	"tracto/sheet-service/internal/pg"
)

// URL returns the test server's connection string, or skips the test.
func URL(t testing.TB) string {
	t.Helper()
	dir, _ := os.Getwd()
	for i := 0; i < 6 && dir != ""; i++ {
		_, _ = envfile.Load(filepath.Join(dir, ".env"))
		parent := filepath.Dir(dir)
		if parent == dir {
			break
		}
		dir = parent
	}
	for _, k := range []string{"TEST_DATABASE_URL", "DATABASE_URL"} {
		if v := os.Getenv(k); v != "" {
			return v
		}
	}
	t.Skip("no PostgreSQL: set TEST_DATABASE_URL (or DATABASE_URL) to run the database tests")
	return ""
}

// Schema creates a schema with a random name, applies the migrations in dir of fsys into it and
// returns a pool bound to it together with the connection string and the schema name.
func Schema(t testing.TB, fsys fs.FS, dir string) (db *sql.DB, url, schema string) {
	t.Helper()
	url = URL(t)
	var b [6]byte
	_, _ = rand.Read(b[:])
	schema = "t_" + hex.EncodeToString(b[:])
	ctx := context.Background()

	db, err := pg.Open(ctx, url, schema)
	if err != nil {
		t.Fatalf("open test schema: %v", err)
	}
	if err := pg.Migrate(ctx, db, fsys, dir); err != nil {
		db.Close()
		t.Fatalf("migrate test schema: %v", err)
	}
	t.Cleanup(func() {
		db.Close()
		cfg, err := pgx.ParseConfig(url)
		if err != nil {
			return
		}
		admin := stdlib.OpenDB(*cfg)
		defer admin.Close()
		_, _ = admin.Exec(`DROP SCHEMA IF EXISTS ` + schema + ` CASCADE`)
	})
	return db, url, schema
}

// Sheets is Schema for the sheet-service tables.
func Sheets(t testing.TB) (db *sql.DB, url, schema string) {
	t.Helper()
	return Schema(t, pg.SheetsMigrations(), "migrations/sheets")
}
