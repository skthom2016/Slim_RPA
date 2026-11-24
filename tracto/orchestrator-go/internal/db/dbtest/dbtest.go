// Package dbtest gives tests a PostgreSQL database built from the real migrations.
//
// Each test binary (one Go package) creates one schema of its own, the first time a test asks for
// the database, and every test then starts from empty tables: building the schema costs seconds,
// emptying it milliseconds. Schemas of different packages never meet, so packages can run at the
// same time. Tests of one package must not call t.Parallel, since they share the schema.
//
// A schema left behind by an interrupted run is dropped by the next run once it is an hour old.
//
// The server comes from TEST_DATABASE_URL, or DATABASE_URL, read from the environment or from a
// .env file in the working directory or one of its parents. Without one the test is skipped.
package dbtest

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/stdlib"

	"tracto/orchestrator/internal/db"
	"tracto/orchestrator/internal/envfile"
)

var (
	once   sync.Once
	pool   *sql.DB
	schema string
	setup  error
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

// Open returns a pool on this package's test schema with every table empty.
func Open(t testing.TB) *sql.DB {
	t.Helper()
	d, _ := OpenWithSchema(t)
	return d
}

// OpenWithSchema is Open that also returns the schema name, for code that opens its own pool.
func OpenWithSchema(t testing.TB) (*sql.DB, string) {
	t.Helper()
	url := URL(t)
	once.Do(func() {
		var b [4]byte
		_, _ = rand.Read(b[:])
		schema = fmt.Sprintf("t_%d_%s", time.Now().Unix(), hex.EncodeToString(b[:]))
		dropOld(url)
		pool, setup = db.Open(context.Background(), url, schema)
	})
	if setup != nil {
		t.Fatalf("open test schema: %v", setup)
	}
	if err := truncateAll(pool); err != nil {
		t.Fatalf("empty test schema: %v", err)
	}
	return pool, schema
}

// truncateAll empties every table of the schema except schema_migrations.
func truncateAll(d *sql.DB) error {
	rows, err := d.Query(`SELECT table_name FROM information_schema.tables
		WHERE table_schema = current_schema() AND table_type='BASE TABLE' AND table_name <> 'schema_migrations'`)
	if err != nil {
		return err
	}
	var names []string
	for rows.Next() {
		var n string
		if err := rows.Scan(&n); err != nil {
			rows.Close()
			return err
		}
		names = append(names, n)
	}
	rows.Close()
	if len(names) == 0 {
		return nil
	}
	// DELETE, not TRUNCATE: truncating rebuilds each table's files, which costs far more than
	// deleting the few rows a test leaves.
	tx, err := d.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	for _, n := range names {
		if _, err := tx.Exec(`DELETE FROM ` + n); err != nil {
			return err
		}
	}
	return tx.Commit()
}

// dropOld removes test schemas older than an hour.
func dropOld(url string) {
	cfg, err := pgx.ParseConfig(url)
	if err != nil {
		return
	}
	admin := stdlib.OpenDB(*cfg)
	defer admin.Close()
	rows, err := admin.Query(`SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 't\_%'`)
	if err != nil {
		return
	}
	var old []string
	for rows.Next() {
		var n string
		if rows.Scan(&n) != nil {
			continue
		}
		parts := strings.Split(n, "_")
		if len(parts) >= 3 {
			if ts, err := strconv.ParseInt(parts[1], 10, 64); err == nil && time.Since(time.Unix(ts, 0)) > time.Hour {
				old = append(old, n)
			}
		}
	}
	rows.Close()
	for _, n := range old {
		_, _ = admin.Exec(`DROP SCHEMA IF EXISTS ` + n + ` CASCADE`)
	}
}
