package db

import (
	"database/sql"
	"fmt"

	_ "modernc.org/sqlite"
)

// Open opens (or creates) the SQLite database at path and applies the full schema.
func Open(path string) (*sql.DB, error) {
	dsn := fmt.Sprintf("file:%s?_busy_timeout=5000&_journal_mode=WAL&_synchronous=NORMAL", path)
	db, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, fmt.Errorf("open sqlite: %w", err)
	}
	db.SetMaxOpenConns(1) // SQLite WAL: single writer is safest
	db.SetMaxIdleConns(1)

	if _, err = db.Exec(Schema); err != nil {
		db.Close()
		return nil, fmt.Errorf("apply schema: %w", err)
	}
	if err = addColumns(db); err != nil {
		db.Close()
		return nil, fmt.Errorf("migrate schema: %w", err)
	}
	return db, nil
}

// Columns added after a table's CREATE statement shipped. CREATE TABLE IF NOT EXISTS does
// not alter existing tables, so each is added here when missing.
var addedColumns = []struct{ Table, Column, Type string }{
	{"RunRecords", "ParamsJson", "TEXT"},
	{"RunRecords", "ParentRunId", "TEXT"},
	{"RunRecords", "SegmentIndex", "INTEGER"},
	{"ArtifactRecords", "ParentArtifactId", "TEXT"},
}

func addColumns(db *sql.DB) error {
	for _, c := range addedColumns {
		exists, err := hasColumn(db, c.Table, c.Column)
		if err != nil {
			return err
		}
		if exists {
			continue
		}
		if _, err := db.Exec(fmt.Sprintf("ALTER TABLE %s ADD COLUMN %s %s", c.Table, c.Column, c.Type)); err != nil {
			return fmt.Errorf("add %s.%s: %w", c.Table, c.Column, err)
		}
	}
	return nil
}

func hasColumn(db *sql.DB, table, column string) (bool, error) {
	rows, err := db.Query(fmt.Sprintf("PRAGMA table_info(%s)", table))
	if err != nil {
		return false, err
	}
	defer rows.Close()
	for rows.Next() {
		var cid int
		var name, ctype string
		var notnull, pk int
		var dflt sql.NullString
		if err := rows.Scan(&cid, &name, &ctype, &notnull, &dflt, &pk); err != nil {
			return false, err
		}
		if name == column {
			return true, nil
		}
	}
	return false, rows.Err()
}
