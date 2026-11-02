package msauth

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

// fakeLogin imitates the Microsoft token endpoints used here.
type fakeLogin struct {
	srv *httptest.Server

	mu          sync.Mutex
	polls       int // device-code polls before sign-in completes
	pendingFor  int
	slowDownAt  int
	refreshOK   map[string]bool // refresh tokens that are still valid
	refreshSeq  int
	refreshCall int
	revoked     bool
}

func newFakeLogin(t *testing.T) *fakeLogin {
	t.Helper()
	f := &fakeLogin{refreshOK: map[string]bool{}}
	mux := http.NewServeMux()
	mux.HandleFunc("/tenant-1/oauth2/v2.0/devicecode", func(w http.ResponseWriter, r *http.Request) {
		_ = r.ParseForm()
		if r.Form.Get("client_id") != "app" || !strings.Contains(r.Form.Get("scope"), "offline_access") {
			w.WriteHeader(http.StatusBadRequest)
			_ = json.NewEncoder(w).Encode(map[string]string{"error": "invalid_request"})
			return
		}
		_ = json.NewEncoder(w).Encode(map[string]any{
			"device_code": "dev-1", "user_code": "ABCD-1234", "verification_uri": "https://microsoft.com/devicelogin",
			"expires_in": 900, "interval": 1, "message": "To sign in, open https://microsoft.com/devicelogin and enter ABCD-1234",
		})
	})
	mux.HandleFunc("/tenant-1/oauth2/v2.0/token", func(w http.ResponseWriter, r *http.Request) {
		_ = r.ParseForm()
		f.mu.Lock()
		defer f.mu.Unlock()
		write := func(code int, v any) {
			w.WriteHeader(code)
			_ = json.NewEncoder(w).Encode(v)
		}
		switch r.Form.Get("grant_type") {
		case "urn:ietf:params:oauth:grant-type:device_code":
			f.polls++
			switch {
			case f.slowDownAt > 0 && f.polls == f.slowDownAt:
				write(400, map[string]string{"error": "slow_down"})
			case f.polls <= f.pendingFor:
				write(400, map[string]string{"error": "authorization_pending"})
			default:
				f.refreshSeq++
				rt := f.newRefresh()
				write(200, map[string]any{"access_token": "access-0", "refresh_token": rt, "expires_in": 3600})
			}
		case "refresh_token":
			f.refreshCall++
			rt := r.Form.Get("refresh_token")
			if f.revoked || !f.refreshOK[rt] {
				write(400, map[string]string{"error": "invalid_grant", "error_description": "AADSTS70008: The refresh token has expired.\r\nTrace ID: x"})
				return
			}
			delete(f.refreshOK, rt) // rotation: the old one is no longer used
			next := f.newRefresh()
			write(200, map[string]any{"access_token": "access-" + next, "refresh_token": next, "expires_in": 3600})
		default:
			write(400, map[string]string{"error": "unsupported_grant_type"})
		}
	})
	f.srv = httptest.NewServer(mux)
	t.Cleanup(f.srv.Close)
	return f
}

func (f *fakeLogin) newRefresh() string {
	f.refreshSeq++
	rt := "refresh-" + string(rune('a'+f.refreshSeq))
	f.refreshOK[rt] = true
	return rt
}

func setup(t *testing.T) (*fakeLogin, *Store, Config) {
	t.Helper()
	sleep = func(context.Context, time.Duration) error { return nil }
	t.Cleanup(func() { sleep = realSleep })
	f := newFakeLogin(t)
	store, err := OpenStore(filepath.Join(t.TempDir(), "tokens.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { store.Close() }) // Windows cannot delete an open database file
	return f, store, Config{Tenant: "tenant-1", ClientID: "app", LoginBase: f.srv.URL}
}

var realSleep = sleep

func TestLogin_WaitsForSignInAndSavesTokens(t *testing.T) {
	f, store, cfg := setup(t)
	f.pendingFor, f.slowDownAt = 3, 2
	var out bytes.Buffer

	if err := Login(context.Background(), store, cfg, "sp", &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "ABCD-1234") {
		t.Fatalf("the user must be shown the code, got %q", out.String())
	}
	tok, _ := store.Get(context.Background(), "sp")
	if tok == nil || tok.Refresh == "" || tok.Access != "access-0" || tok.Tenant != "tenant-1" || tok.ClientID != "app" {
		t.Fatalf("saved token wrong: %+v", tok)
	}
	if f.polls != 4 {
		t.Fatalf("polled %d times, want 4 (3 pending, then success)", f.polls)
	}
}

func TestLogin_RefusedCodeIsReported(t *testing.T) {
	_, store, cfg := setup(t)
	cfg.ClientID = "wrong"
	err := Login(context.Background(), store, cfg, "", &bytes.Buffer{})
	if err == nil || !strings.Contains(err.Error(), "device code request failed") {
		t.Fatalf("got %v", err)
	}
}

func TestToken_ValidAccessTokenIsReusedWithoutCallingMicrosoft(t *testing.T) {
	f, store, cfg := setup(t)
	ctx := context.Background()
	_ = store.Put(ctx, Token{Name: "default", Tenant: "tenant-1", ClientID: "app", Refresh: "r", Access: "still-good", Expires: time.Now().Add(30 * time.Minute)})

	got, err := NewSource(store, cfg, "").Token(ctx)
	if err != nil || got != "still-good" {
		t.Fatalf("got %q, %v", got, err)
	}
	if f.refreshCall != 0 {
		t.Fatalf("refresh called %d times, want 0", f.refreshCall)
	}
}

func TestToken_ExpiredAccessTokenIsRenewedAndRefreshTokenRotated(t *testing.T) {
	f, store, cfg := setup(t)
	ctx := context.Background()
	f.mu.Lock()
	first := f.newRefresh()
	f.mu.Unlock()
	_ = store.Put(ctx, Token{Name: "default", Tenant: "tenant-1", ClientID: "app", Refresh: first, Access: "old", Expires: time.Now().Add(-time.Minute)})
	src := NewSource(store, cfg, "")

	a1, err := src.Token(ctx)
	if err != nil || a1 == "old" {
		t.Fatalf("expected a renewed token, got %q, %v", a1, err)
	}
	saved, _ := store.Get(ctx, "default")
	if saved.Refresh == first || saved.Access != a1 {
		t.Fatalf("the new refresh token must replace the old one: %+v", saved)
	}

	// Second call reuses the renewed token; and a later expiry uses the rotated refresh token.
	if a2, _ := src.Token(ctx); a2 != a1 || f.refreshCall != 1 {
		t.Fatalf("second call: %q, refreshCall=%d", a2, f.refreshCall)
	}
	saved.Expires = time.Now().Add(time.Minute) // inside the renew-before window
	_ = store.Put(ctx, *saved)
	if _, err := src.Token(ctx); err != nil || f.refreshCall != 2 {
		t.Fatalf("renewal with the rotated refresh token failed: %v (calls %d)", err, f.refreshCall)
	}
}

func TestToken_RevokedRefreshTokenAsksForLogin(t *testing.T) {
	f, store, cfg := setup(t)
	ctx := context.Background()
	_ = store.Put(ctx, Token{Name: "default", Tenant: "tenant-1", ClientID: "app", Refresh: "dead", Access: "old", Expires: time.Now().Add(-time.Hour)})
	f.revoked = true

	_, err := NewSource(store, cfg, "").Token(ctx)
	if !errors.Is(err, ErrLoginRequired) {
		t.Fatalf("got %v, want ErrLoginRequired", err)
	}
	if strings.Contains(err.Error(), "Trace ID") {
		t.Fatalf("only the first line of Microsoft's message belongs in the error: %v", err)
	}
	if saved, _ := store.Get(ctx, "default"); saved.Refresh != "dead" {
		t.Fatal("a failed refresh must not change the saved token")
	}
}

func TestToken_NoSavedConnectionAsksForLogin(t *testing.T) {
	_, store, cfg := setup(t)
	if _, err := NewSource(store, cfg, "other").Token(context.Background()); !errors.Is(err, ErrLoginRequired) {
		t.Fatalf("got %v", err)
	}
}

func TestConfigFromEnv(t *testing.T) {
	get := func(m map[string]string) func(string) string { return func(k string) string { return m[k] } }
	if _, _, err := ConfigFromEnv(get(map[string]string{"MS_TENANT_ID": "t"})); err == nil {
		t.Fatal("client id is required")
	}
	cfg, db, err := ConfigFromEnv(get(map[string]string{"MS_TENANT_ID": "t", "MS_CLIENT_ID": "c"}))
	if err != nil || cfg.Tenant != "t" || cfg.ClientID != "c" || db != DefaultDBFile {
		t.Fatalf("got %+v %q %v", cfg, db, err)
	}
}
