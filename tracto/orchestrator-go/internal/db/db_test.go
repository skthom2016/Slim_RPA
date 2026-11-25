package db_test

import (
	"context"
	"fmt"
	"io/fs"
	"os"
	"strings"
	"sync"
	"testing"
	"time"

	"tracto/orchestrator/internal/db"
	"tracto/orchestrator/internal/db/dbtest"
)

func TestMigrate_BuildsEveryTable(t *testing.T) {
	d := dbtest.Open(t)
	want := []string{"runrecords", "workerrecords", "journalentries", "leaserecords", "artifactrecords", "artifactcontents",
		"schedulerecords", "finalresultrecords", "runoutputs", "callbackrecords", "continuationmappings",
		"continuationrunrecords", "telemetryevents", "runlogs", "securityevents", "resolutionrecords", "artifactaliases",
		"schema_migrations"}
	for _, tbl := range want {
		var found bool
		if err := d.QueryRow(`SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = $1)`, tbl).Scan(&found); err != nil || !found {
			t.Errorf("table %s missing (%v)", tbl, err)
		}
	}
	// The queue claim, the lease check and the schedule due-scan each rely on an index.
	for _, idx := range []string{"ix_runrecords_queue", "ix_schedulerecords_next", "ix_finalresultrecords_runid"} {
		var found bool
		if err := d.QueryRow(`SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = current_schema() AND indexname = $1)`, idx).Scan(&found); err != nil || !found {
			t.Errorf("index %s missing (%v)", idx, err)
		}
	}
}

// migrationFiles is how many migration files the binary carries.
func migrationFiles(t *testing.T) int {
	t.Helper()
	entries, err := fs.ReadDir(db.Migrations(), "migrations")
	if err != nil {
		t.Fatal(err)
	}
	n := 0
	for _, e := range entries {
		if !e.IsDir() && strings.HasSuffix(e.Name(), ".sql") {
			n++
		}
	}
	if n == 0 {
		t.Fatal("no migration files found")
	}
	return n
}

func TestMigrate_IsRepeatable(t *testing.T) {
	d := dbtest.Open(t)
	if err := db.Migrate(context.Background(), d, db.Migrations(), "migrations"); err != nil {
		t.Fatal(err)
	}
	var n int
	_ = d.QueryRow(`SELECT count(*) FROM schema_migrations`).Scan(&n)
	if want := migrationFiles(t); n != want {
		t.Fatalf("expected %d recorded migrations (one per file), got %d", want, n)
	}
}

// Two services starting together must not both apply the schema.
func TestMigrate_ConcurrentStartsApplyOnce(t *testing.T) {
	url := dbtest.URL(t)
	schema := fmt.Sprintf("t_%d_conc", time.Now().Unix())
	ctx := context.Background()
	first, err := db.OpenPool(ctx, url, schema)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		_, _ = first.Exec(`DROP SCHEMA IF EXISTS ` + schema + ` CASCADE`)
		first.Close()
	})
	var wg sync.WaitGroup
	errs := make(chan error, 6)
	for i := 0; i < 6; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			p, err := db.OpenPool(ctx, url, schema)
			if err != nil {
				errs <- err
				return
			}
			defer p.Close()
			errs <- db.Migrate(ctx, p, db.Migrations(), "migrations")
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
	if want := migrationFiles(t); n != want {
		t.Fatalf("expected %d recorded migrations (one per file), got %d", want, n)
	}
}

func TestOpen_RejectsBadInput(t *testing.T) {
	ctx := context.Background()
	if _, err := db.Open(ctx, "", ""); err == nil {
		t.Error("an empty URL must fail")
	}
	if _, err := db.Open(ctx, "postgres://u:p@localhost/d", "bad schema;"); err == nil {
		t.Error("an unsafe schema name must be refused")
	}
}

func TestOpen_ErrorDoesNotLeakThePassword(t *testing.T) {
	_, err := db.Open(context.Background(), "postgres://tracto:sup3rs3cret@127.0.0.1:1/tracto?sslmode=disable&connect_timeout=1", "")
	if err == nil {
		t.Fatal("expected a connection error")
	}
	if strings.Contains(err.Error(), "sup3rs3cret") {
		t.Fatalf("the error leaks the password: %s", err)
	}
}

// Times are returned in UTC, so the API's timestamps keep ending in Z.
func TestTimes_AreReturnedInUTC(t *testing.T) {
	d := dbtest.Open(t)
	if _, err := d.Exec(`INSERT INTO SecurityEvents(Id,EventType,OccurredAt,Details) VALUES('e1','T',$1,'d')`, "2026-11-24T10:30:00+05:30"); err != nil {
		t.Fatal(err)
	}
	var s string
	if err := d.QueryRow(`SELECT OccurredAt FROM SecurityEvents WHERE Id='e1'`).Scan(&s); err != nil {
		t.Fatal(err)
	}
	if s != "2026-11-24T05:00:00Z" {
		t.Fatalf("got %q", s)
	}
}

func TestMain(m *testing.M) {
	code := m.Run()
	dbtest.Cleanup()
	os.Exit(code)
}
