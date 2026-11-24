package pg_test

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"

	"tracto/sheet-service/internal/pg"
	"tracto/sheet-service/internal/pg/pgtest"
)

func TestMigrate_CreatesTablesAndIsRepeatable(t *testing.T) {
	db, _, _ := pgtest.Sheets(t)
	ctx := context.Background()
	for _, tbl := range []string{"ms_tokens", "process_batches", "process_items", "schema_migrations"} {
		var found bool
		if err := db.QueryRowContext(ctx,
			`SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = $1)`, tbl).Scan(&found); err != nil || !found {
			t.Errorf("table %s missing (%v)", tbl, err)
		}
	}
	// A second run applies nothing and does not fail.
	if err := pg.Migrate(ctx, db, pg.SheetsMigrations(), "migrations/sheets"); err != nil {
		t.Fatal(err)
	}
	var n int
	_ = db.QueryRowContext(ctx, `SELECT count(*) FROM schema_migrations`).Scan(&n)
	if n != 1 {
		t.Fatalf("expected one recorded migration, got %d", n)
	}
}

// Two processes starting together must not both apply the schema.
func TestMigrate_ConcurrentStartsApplyOnce(t *testing.T) {
	url := pgtest.URL(t)
	schema := fmt.Sprintf("t_conc_%d", time.Now().UnixNano())
	ctx := context.Background()
	first, err := pg.Open(ctx, url, schema)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		_, _ = first.Exec(`DROP SCHEMA IF EXISTS ` + schema + ` CASCADE`)
		first.Close()
	})
	var wg sync.WaitGroup
	errs := make(chan error, 8)
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			db, err := pg.Open(ctx, url, schema)
			if err != nil {
				errs <- err
				return
			}
			defer db.Close()
			errs <- pg.Migrate(ctx, db, pg.SheetsMigrations(), "migrations/sheets")
		}()
	}
	wg.Wait()
	close(errs)
	for err := range errs {
		if err != nil {
			t.Fatal(err)
		}
	}
	var n int
	_ = first.QueryRow(`SELECT count(*) FROM schema_migrations`).Scan(&n)
	if n != 1 {
		t.Fatalf("expected one recorded migration, got %d", n)
	}
}

func TestOpen_RejectsBadInput(t *testing.T) {
	if _, err := pg.Open(context.Background(), "", "sheets"); err == nil {
		t.Error("empty URL must fail")
	}
	if _, err := pg.Open(context.Background(), "postgres://u:p@localhost/d", "bad schema;"); err == nil {
		t.Error("an unsafe schema name must be refused")
	}
}

func TestOpen_ErrorDoesNotLeakPassword(t *testing.T) {
	_, err := pg.Open(context.Background(), "postgres://tracto:sup3rs3cret@127.0.0.1:1/tracto?sslmode=disable&connect_timeout=1", "sheets")
	if err == nil {
		t.Fatal("expected a connection error")
	}
	if got := err.Error(); contains(got, "sup3rs3cret") {
		t.Fatalf("error leaks the password: %s", got)
	}
}

func contains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
}
