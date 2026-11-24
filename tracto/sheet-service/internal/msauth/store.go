package msauth

import (
	"context"
	"database/sql"
	"errors"
	"sync"
	"time"

	"tracto/sheet-service/internal/pg"
	"tracto/sheet-service/internal/secretbox"
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

// DBConfig says which PostgreSQL schema holds the saved sign-ins.
type DBConfig struct {
	URL    string // DATABASE_URL
	Schema string // SHEET_SERVICE_SCHEMA; pg.Schema when empty
}

func (c DBConfig) key() string { return c.URL + "\x00" + c.Schema }

// Store keeps tokens in the ms_tokens table. The refresh and access tokens are credentials and
// are encrypted before they are written.
type Store struct {
	db  *sql.DB
	box *secretbox.Box
	key string // cache key, empty when the Store was built with NewStore
}

// NewStore wraps an open pool. The caller owns the pool.
func NewStore(db *sql.DB, box *secretbox.Box) *Store { return &Store{db: db, box: box} }

// Close releases the pool of a Store made by OpenStore. The next OpenStore for the same database
// opens it again. A Store made by NewStore leaves its pool to the caller.
func (s *Store) Close() error {
	if s.key == "" {
		return nil
	}
	stores.Delete(s.key)
	return s.db.Close()
}

var stores sync.Map // DBConfig.key() -> *Store

// OpenStore connects to the database (creating the schema and tables when needed) and returns a
// Store. The same database always returns the same Store within a process.
func OpenStore(ctx context.Context, c DBConfig) (*Store, error) {
	if s, ok := stores.Load(c.key()); ok {
		return s.(*Store), nil
	}
	box, err := secretbox.FromEnv()
	if err != nil {
		return nil, err
	}
	db, err := pg.OpenSheets(ctx, c.URL, c.Schema)
	if err != nil {
		return nil, err
	}
	s := &Store{db: db, box: box, key: c.key()}
	if actual, loaded := stores.LoadOrStore(c.key(), s); loaded {
		db.Close()
		return actual.(*Store), nil
	}
	return s, nil
}

const selectToken = `SELECT name, tenant, client_id, refresh_token, access_token, access_expires, updated_at
	FROM ms_tokens WHERE name = $1`

type queryer interface {
	QueryRowContext(ctx context.Context, query string, args ...any) *sql.Row
}

// load reads and decrypts the token called name; nil when there is none.
func (s *Store) load(ctx context.Context, q queryer, name string) (*Token, error) {
	var t Token
	var refresh, access string
	err := q.QueryRowContext(ctx, selectToken, name).
		Scan(&t.Name, &t.Tenant, &t.ClientID, &refresh, &access, &t.Expires, &t.Updated)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if t.Refresh, err = s.box.Open(refresh); err != nil {
		return nil, err
	}
	if t.Access, err = s.box.Open(access); err != nil {
		return nil, err
	}
	return &t, nil
}

// Get returns the saved token for name, or nil when there is none.
func (s *Store) Get(ctx context.Context, name string) (*Token, error) {
	return s.load(ctx, s.db, name)
}

// Put saves t, replacing any token with the same name.
func (s *Store) Put(ctx context.Context, t Token) error {
	return s.Locked(ctx, t.Name, func(*Token) (*Token, error) { return &t, nil })
}

// Locked runs fn while holding a lock on the sign-in called name, so two processes never refresh
// the same connection at once (a refresh token is single use). fn receives the current token (nil
// when none) and returns the token to save, or nil to leave it unchanged. Nothing is saved when
// fn fails. The lock is a transaction-level advisory lock: it is released at commit or rollback,
// including when the process dies.
func (s *Store) Locked(ctx context.Context, name string, fn func(cur *Token) (*Token, error)) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err := tx.ExecContext(ctx, `SELECT pg_advisory_xact_lock(hashtext('ms_tokens:' || $1))`, name); err != nil {
		return err
	}

	cur, err := s.load(ctx, tx, name)
	if err != nil {
		return err
	}
	next, err := fn(cur)
	if err != nil {
		return err
	}
	if next != nil {
		refresh, err := s.box.Seal(next.Refresh)
		if err != nil {
			return err
		}
		access, err := s.box.Seal(next.Access)
		if err != nil {
			return err
		}
		_, err = tx.ExecContext(ctx, `INSERT INTO ms_tokens (name, tenant, client_id, refresh_token, access_token, access_expires, updated_at)
			VALUES ($1, $2, $3, $4, $5, $6, $7)
			ON CONFLICT (name) DO UPDATE SET tenant = excluded.tenant, client_id = excluded.client_id,
				refresh_token = excluded.refresh_token, access_token = excluded.access_token,
				access_expires = excluded.access_expires, updated_at = excluded.updated_at`,
			next.Name, next.Tenant, next.ClientID, refresh, access, next.Expires.UTC(), time.Now().UTC())
		if err != nil {
			return err
		}
	}
	return tx.Commit()
}
