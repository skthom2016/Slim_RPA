package msauth

import (
	"context"
	"sync"
	"testing"
	"time"

	"tracto/sheet-service/internal/pg/pgtest"
	"tracto/sheet-service/internal/secretbox"
)

func newTestStore(t *testing.T) *Store {
	t.Helper()
	db, _, _ := pgtest.Sheets(t)
	box, err := secretbox.New(secretbox.TestKey())
	if err != nil {
		t.Fatal(err)
	}
	return NewStore(db, box)
}

// Tokens are credentials: the table must never hold them in the clear.
func TestStore_TokensAreEncryptedAtRest(t *testing.T) {
	s := newTestStore(t)
	ctx := context.Background()
	exp := time.Now().Add(time.Hour).UTC().Truncate(time.Second)
	if err := s.Put(ctx, Token{Name: "default", Tenant: "t", ClientID: "c", Refresh: "REFRESH-SECRET", Access: "ACCESS-SECRET", Expires: exp}); err != nil {
		t.Fatal(err)
	}
	var refresh, access string
	if err := s.db.QueryRow(`SELECT refresh_token, access_token FROM ms_tokens WHERE name='default'`).Scan(&refresh, &access); err != nil {
		t.Fatal(err)
	}
	if refresh == "REFRESH-SECRET" || access == "ACCESS-SECRET" || refresh == "" {
		t.Fatalf("stored in the clear: %q %q", refresh, access)
	}
	got, err := s.Get(ctx, "default")
	if err != nil || got == nil {
		t.Fatalf("get: %v %v", got, err)
	}
	if got.Refresh != "REFRESH-SECRET" || got.Access != "ACCESS-SECRET" || !got.Expires.Equal(exp) {
		t.Fatalf("round trip lost data: %+v", got)
	}
}

func TestStore_GetUnknownIsNil(t *testing.T) {
	s := newTestStore(t)
	if got, err := s.Get(context.Background(), "nobody"); err != nil || got != nil {
		t.Fatalf("got %v %v", got, err)
	}
}

func TestStore_WrongKeyCannotReadTokens(t *testing.T) {
	s := newTestStore(t)
	ctx := context.Background()
	if err := s.Put(ctx, Token{Name: "default", Tenant: "t", ClientID: "c", Refresh: "r"}); err != nil {
		t.Fatal(err)
	}
	other, _ := secretbox.New("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=")
	if _, err := NewStore(s.db, other).Get(ctx, "default"); err == nil {
		t.Fatal("a different key must not decrypt the stored token")
	}
}

func TestStore_PutReplacesSameName(t *testing.T) {
	s := newTestStore(t)
	ctx := context.Background()
	_ = s.Put(ctx, Token{Name: "n", Tenant: "t", ClientID: "c", Refresh: "one"})
	_ = s.Put(ctx, Token{Name: "n", Tenant: "t2", ClientID: "c", Refresh: "two"})
	got, _ := s.Get(ctx, "n")
	if got.Refresh != "two" || got.Tenant != "t2" {
		t.Fatalf("got %+v", got)
	}
}

// A refresh token is single use, so two refreshes of one sign-in must never overlap. Locked has to
// serialise them across connections (and so across processes).
func TestStore_LockedSerialisesConcurrentRefreshes(t *testing.T) {
	s := newTestStore(t)
	ctx := context.Background()
	if err := s.Put(ctx, Token{Name: "default", Tenant: "t", ClientID: "c", Refresh: "0"}); err != nil {
		t.Fatal(err)
	}
	var mu sync.Mutex
	inside, maxInside := 0, 0
	var wg sync.WaitGroup
	for i := 0; i < 12; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			err := s.Locked(ctx, "default", func(cur *Token) (*Token, error) {
				mu.Lock()
				inside++
				if inside > maxInside {
					maxInside = inside
				}
				mu.Unlock()
				time.Sleep(15 * time.Millisecond)
				next := *cur
				next.Refresh = cur.Refresh + "+"
				mu.Lock()
				inside--
				mu.Unlock()
				return &next, nil
			})
			if err != nil {
				t.Error(err)
			}
		}()
	}
	wg.Wait()
	if maxInside != 1 {
		t.Fatalf("refreshes overlapped: %d at once", maxInside)
	}
	got, _ := s.Get(ctx, "default")
	if got.Refresh != "0++++++++++++" { // twelve updates, none lost
		t.Fatalf("lost an update: %q", got.Refresh)
	}
}

func TestStore_LockedFailureSavesNothing(t *testing.T) {
	s := newTestStore(t)
	ctx := context.Background()
	_ = s.Put(ctx, Token{Name: "default", Tenant: "t", ClientID: "c", Refresh: "keep"})
	err := s.Locked(ctx, "default", func(cur *Token) (*Token, error) {
		return &Token{Name: "default", Tenant: "t", ClientID: "c", Refresh: "lost"}, context.Canceled
	})
	if err == nil {
		t.Fatal("expected the error to be returned")
	}
	if got, _ := s.Get(ctx, "default"); got.Refresh != "keep" {
		t.Fatalf("got %q", got.Refresh)
	}
}
