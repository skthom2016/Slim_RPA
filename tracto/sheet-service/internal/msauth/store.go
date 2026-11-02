package msauth

import (
	"context"
	"database/sql"
	"errors"
	"sync"
	"time"

	_ "modernc.org/sqlite" // pure-Go SQLite driver
)

// Token is one saved Microsoft connection: the refresh token that keeps it signed in and the
// latest access token.
type Token struct {
	Name     string
	Tenant   string
	ClientID string
	Refresh  string
	Access   string
	Expires  time.Time // when Access stops being valid
	Updated  time.Time
}

// Store keeps tokens in a SQLite file.
type Store struct {
	db   *sql.DB
	path string
}

// Close releases the database file. The next OpenStore for the same path opens it again.
func (s *Store) Close() error {
	stores.Delete(s.path)
	return s.db.Close()
}

var stores sync.Map // path -> *Store

// OpenStore opens (creating when needed) the database at path. The same path always returns the
// same Store within a process.
func OpenStore(path string) (*Store, error) {
	if s, ok := stores.Load(path); ok {
		return s.(*Store), nil
	}
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}
	// One connection: calls in this process queue up, and BEGIN IMMEDIATE below makes other
	// processes wait for the file lock instead of failing.
	db.SetMaxOpenConns(1)
	for _, stmt := range []string{
		`PRAGMA busy_timeout = 30000`,
		`CREATE TABLE IF NOT EXISTS ms_tokens (
			name           TEXT PRIMARY KEY,
			tenant         TEXT    NOT NULL,
			client_id      TEXT    NOT NULL,
			refresh_token  TEXT    NOT NULL,
			access_token   TEXT    NOT NULL DEFAULT '',
			access_expires INTEGER NOT NULL DEFAULT 0,
			updated_at     INTEGER NOT NULL
		)`,
	} {
		if _, err := db.Exec(stmt); err != nil {
			db.Close()
			return nil, err
		}
	}
	actual, _ := stores.LoadOrStore(path, &Store{db: db, path: path})
	return actual.(*Store), nil
}

// Get returns the saved token for name, or nil when there is none.
func (s *Store) Get(ctx context.Context, name string) (*Token, error) {
	var t Token
	var exp, upd int64
	err := s.db.QueryRowContext(ctx,
		`SELECT name, tenant, client_id, refresh_token, access_token, access_expires, updated_at FROM ms_tokens WHERE name = ?`, name).
		Scan(&t.Name, &t.Tenant, &t.ClientID, &t.Refresh, &t.Access, &exp, &upd)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	t.Expires, t.Updated = time.Unix(exp, 0), time.Unix(upd, 0)
	return &t, nil
}

// Put saves t, replacing any token with the same name.
func (s *Store) Put(ctx context.Context, t Token) error {
	return s.Locked(ctx, t.Name, func(*Token) (*Token, error) { return &t, nil })
}

// Locked runs fn while holding the database write lock, so two processes never refresh the same
// connection at once. fn receives the current token (nil when none) and returns the token to save,
// or nil to leave it unchanged. Nothing is saved when fn fails.
func (s *Store) Locked(ctx context.Context, name string, fn func(cur *Token) (*Token, error)) error {
	conn, err := s.db.Conn(ctx)
	if err != nil {
		return err
	}
	defer conn.Close()
	if _, err := conn.ExecContext(ctx, `BEGIN IMMEDIATE`); err != nil {
		return err
	}
	done := false
	defer func() {
		if !done {
			_, _ = conn.ExecContext(context.Background(), `ROLLBACK`)
		}
	}()

	var cur *Token
	var t Token
	var exp, upd int64
	err = conn.QueryRowContext(ctx,
		`SELECT name, tenant, client_id, refresh_token, access_token, access_expires, updated_at FROM ms_tokens WHERE name = ?`, name).
		Scan(&t.Name, &t.Tenant, &t.ClientID, &t.Refresh, &t.Access, &exp, &upd)
	switch {
	case err == nil:
		t.Expires, t.Updated = time.Unix(exp, 0), time.Unix(upd, 0)
		cur = &t
	case !errors.Is(err, sql.ErrNoRows):
		return err
	}

	next, err := fn(cur)
	if err != nil {
		return err
	}
	if next != nil {
		_, err := conn.ExecContext(ctx, `INSERT INTO ms_tokens (name, tenant, client_id, refresh_token, access_token, access_expires, updated_at)
			VALUES (?, ?, ?, ?, ?, ?, ?)
			ON CONFLICT(name) DO UPDATE SET tenant = excluded.tenant, client_id = excluded.client_id,
				refresh_token = excluded.refresh_token, access_token = excluded.access_token,
				access_expires = excluded.access_expires, updated_at = excluded.updated_at`,
			next.Name, next.Tenant, next.ClientID, next.Refresh, next.Access, next.Expires.Unix(), time.Now().Unix())
		if err != nil {
			return err
		}
	}
	if _, err := conn.ExecContext(ctx, `COMMIT`); err != nil {
		return err
	}
	done = true
	return nil
}
