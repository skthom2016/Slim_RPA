// Package pg opens the PostgreSQL pool the sheet-service stores use and applies their schema.
//
// All of the service's tables live in one schema (sheets). A connection is bound to it through
// search_path, so queries name tables without a prefix. The schema is created from numbered SQL
// files embedded in the binary and recorded in the schema's schema_migrations table, so a start-up
// applies only what is missing.
package pg

import (
	"context"
	"database/sql"
	"embed"
	"fmt"
	"io/fs"
	neturl "net/url"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/stdlib"
)

//go:embed migrations/sheets/*.sql
var sheetsFS embed.FS

// Schema is the PostgreSQL schema that holds the sheet-service tables.
const Schema = "sheets"

var identRE = regexp.MustCompile(`^[a-z_][a-z0-9_]*$`)

// Open connects to url with search_path set to schema, creating the schema when it is missing.
// The pool is sized for a service that handles requests concurrently.
func Open(ctx context.Context, url, schema string) (*sql.DB, error) {
	if url == "" {
		return nil, fmt.Errorf("DATABASE_URL is not set")
	}
	if !identRE.MatchString(schema) {
		return nil, fmt.Errorf("invalid schema name %q", schema)
	}
	cfg, err := pgx.ParseConfig(url)
	if err != nil {
		return nil, fmt.Errorf("DATABASE_URL: %w", redactURL(err, url))
	}

	// The schema has to exist before a connection can create tables in it.
	boot := stdlib.OpenDB(*cfg)
	_, err = boot.ExecContext(ctx, `CREATE SCHEMA IF NOT EXISTS `+schema)
	boot.Close()
	if err != nil {
		return nil, fmt.Errorf("create schema %s: %w", schema, redactURL(err, url))
	}

	if cfg.RuntimeParams == nil {
		cfg.RuntimeParams = map[string]string{}
	}
	cfg.RuntimeParams["search_path"] = schema
	db := stdlib.OpenDB(*cfg)
	db.SetMaxOpenConns(10)
	db.SetMaxIdleConns(5)
	db.SetConnMaxLifetime(30 * time.Minute)
	if err := db.PingContext(ctx); err != nil {
		db.Close()
		return nil, fmt.Errorf("connect to database: %w", redactURL(err, url))
	}
	return db, nil
}

// OpenSheets opens the sheet-service schema (Schema when schema is empty) and applies its
// migrations.
func OpenSheets(ctx context.Context, url, schema string) (*sql.DB, error) {
	if schema == "" {
		schema = Schema
	}
	db, err := Open(ctx, url, schema)
	if err != nil {
		return nil, err
	}
	if err := Migrate(ctx, db, SheetsMigrations(), "migrations/sheets"); err != nil {
		db.Close()
		return nil, err
	}
	return db, nil
}

// SheetsMigrations returns the embedded migration files of the sheets schema.
func SheetsMigrations() fs.FS { return sheetsFS }

// Migrate applies the files in dir of fsys (named NNN_name.sql) that are not yet recorded in the
// current schema's schema_migrations table, each in its own transaction. A lock keeps two
// processes from applying the same migration at once.
func Migrate(ctx context.Context, db *sql.DB, fsys fs.FS, dir string) error {
	if err := ensureMigrationsTable(ctx, db); err != nil {
		return fmt.Errorf("create schema_migrations: %w", err)
	}
	entries, err := fs.ReadDir(fsys, dir)
	if err != nil {
		return err
	}
	type mig struct {
		version int
		name    string
	}
	var migs []mig
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(e.Name(), ".sql") {
			continue
		}
		num, _, _ := strings.Cut(e.Name(), "_")
		v, err := strconv.Atoi(num)
		if err != nil {
			return fmt.Errorf("migration %s: name must start with a number", e.Name())
		}
		migs = append(migs, mig{v, e.Name()})
	}
	sort.Slice(migs, func(i, j int) bool { return migs[i].version < migs[j].version })

	for _, m := range migs {
		body, err := fs.ReadFile(fsys, dir+"/"+m.name)
		if err != nil {
			return err
		}
		if err := applyOne(ctx, db, m.version, string(body)); err != nil {
			return fmt.Errorf("migration %s: %w", m.name, err)
		}
	}
	return nil
}

// ensureMigrationsTable creates schema_migrations under the migration lock: CREATE TABLE IF NOT
// EXISTS is not safe when two processes run it at the same moment.
func ensureMigrationsTable(ctx context.Context, db *sql.DB) error {
	tx, err := db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err := tx.ExecContext(ctx, lockSQL); err != nil {
		return err
	}
	if _, err := tx.ExecContext(ctx, `CREATE TABLE IF NOT EXISTS schema_migrations (
		version    INTEGER     PRIMARY KEY,
		applied_at TIMESTAMPTZ NOT NULL DEFAULT now())`); err != nil {
		return err
	}
	return tx.Commit()
}

// One migration at a time per schema, across processes. Released at commit or rollback.
const lockSQL = `SELECT pg_advisory_xact_lock(hashtext('tracto-migrate:' || current_schema()))`

func applyOne(ctx context.Context, db *sql.DB, version int, body string) error {
	tx, err := db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err := tx.ExecContext(ctx, lockSQL); err != nil {
		return err
	}
	var done bool
	if err := tx.QueryRowContext(ctx, `SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = $1)`, version).Scan(&done); err != nil {
		return err
	}
	if done {
		return nil
	}
	if _, err := tx.ExecContext(ctx, body); err != nil {
		return err
	}
	if _, err := tx.ExecContext(ctx, `INSERT INTO schema_migrations (version) VALUES ($1)`, version); err != nil {
		return err
	}
	return tx.Commit()
}

// redactURL removes the password from an error that quotes the connection string.
func redactURL(err error, url string) error {
	if err == nil {
		return nil
	}
	msg := err.Error()
	if u, perr := parsePassword(url); perr == nil && u != "" {
		msg = strings.ReplaceAll(msg, u, "****")
		// the password may appear decoded in an error, though it is percent-encoded in the URL
		if plain, uerr := neturl.PathUnescape(u); uerr == nil && plain != u {
			msg = strings.ReplaceAll(msg, plain, "****")
		}
	}
	return fmt.Errorf("%s", msg)
}

func parsePassword(url string) (string, error) {
	rest, ok := strings.CutPrefix(url, "postgres://")
	if !ok {
		rest, ok = strings.CutPrefix(url, "postgresql://")
	}
	if !ok {
		return "", fmt.Errorf("not a url")
	}
	userinfo, _, ok := strings.Cut(rest, "@")
	if !ok {
		return "", fmt.Errorf("no userinfo")
	}
	_, pw, ok := strings.Cut(userinfo, ":")
	if !ok {
		return "", fmt.Errorf("no password")
	}
	return pw, nil
}
