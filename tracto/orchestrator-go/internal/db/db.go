// Package db opens the orchestrator's PostgreSQL database and applies its schema.
//
// All orchestrator tables live in one schema (orchestrator). A connection is bound to it through
// search_path, so queries name tables without a prefix. The schema is built from the numbered SQL
// files in migrations/, embedded in the binary and recorded in the schema's schema_migrations
// table, so a start-up applies only what is missing.
package db

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

//go:embed migrations/*.sql
var migrationsFS embed.FS

// Schema is the PostgreSQL schema that holds the orchestrator tables.
const Schema = "orchestrator"

var identRE = regexp.MustCompile(`^[a-z_][a-z0-9_]*$`)

// Migrations returns the embedded migration files; they are in the "migrations" directory.
func Migrations() fs.FS { return migrationsFS }

// Open connects to url, creates the schema when it is missing, applies the migrations and returns
// the pool. schema is Schema when empty.
func Open(ctx context.Context, url, schema string) (*sql.DB, error) {
	// The orchestrator keeps and reports every time in UTC (the API's timestamps end in Z). The
	// driver returns times in the process's local zone, so the local zone is UTC.
	time.Local = time.UTC

	if schema == "" {
		schema = Schema
	}
	db, err := OpenPool(ctx, url, schema)
	if err != nil {
		return nil, err
	}
	if err := Migrate(ctx, db, migrationsFS, "migrations"); err != nil {
		db.Close()
		return nil, err
	}
	return db, nil
}

// OpenPool connects with search_path set to schema (created when missing) and does not apply
// migrations.
func OpenPool(ctx context.Context, url, schema string) (*sql.DB, error) {
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
	db.SetMaxOpenConns(20)
	db.SetMaxIdleConns(10)
	db.SetConnMaxLifetime(30 * time.Minute)
	if err := db.PingContext(ctx); err != nil {
		db.Close()
		return nil, fmt.Errorf("connect to database: %w", redactURL(err, url))
	}
	return db, nil
}

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

// One migration at a time per schema, across processes. Released at commit or rollback.
const lockSQL = `SELECT pg_advisory_xact_lock(hashtext('tracto-migrate:' || current_schema()))`

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
	if pw := passwordOf(url); pw != "" {
		msg = strings.ReplaceAll(msg, pw, "****")
		// the password may appear decoded in an error, though it is percent-encoded in the URL
		if plain, uerr := neturl.PathUnescape(pw); uerr == nil && plain != pw {
			msg = strings.ReplaceAll(msg, plain, "****")
		}
	}
	return fmt.Errorf("%s", msg)
}

func passwordOf(url string) string {
	rest, ok := strings.CutPrefix(url, "postgres://")
	if !ok {
		rest, ok = strings.CutPrefix(url, "postgresql://")
	}
	if !ok {
		return ""
	}
	userinfo, _, ok := strings.Cut(rest, "@")
	if !ok {
		return ""
	}
	_, pw, ok := strings.Cut(userinfo, ":")
	if !ok {
		return ""
	}
	return pw
}
