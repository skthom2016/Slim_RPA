// Package msauth signs in to Microsoft with the device-code flow and keeps the connection alive:
// the refresh token and access token are saved in a SQLite file, and the access token is renewed
// silently shortly before it expires. A person is needed only for the first sign-in, or again if
// Microsoft revokes the refresh token.
package msauth

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

const (
	// DefaultScope lets the service read and write files, including files on SharePoint sites, and
	// asks for a refresh token.
	DefaultScope     = "https://graph.microsoft.com/Files.ReadWrite.All offline_access"
	DefaultLoginBase = "https://login.microsoftonline.com"
	DefaultName      = "default"
	DefaultDBFile    = "sheet-service.db"

	renewBefore = 5 * time.Minute
)

// ErrLoginRequired means there is no usable sign-in: none was ever saved, or Microsoft no longer
// accepts the saved refresh token (revoked, expired, password changed, policy).
var ErrLoginRequired = errors.New("microsoft sign-in is missing or expired; run: sheet-service login")

// Config identifies the Microsoft app. Tenant must be a tenant ID or domain: the device-code flow
// does not work with the common endpoint.
type Config struct {
	Tenant    string
	ClientID  string
	Scope     string // DefaultScope when empty
	LoginBase string // DefaultLoginBase when empty
}

// ConfigFromEnv reads MS_TENANT_ID, MS_CLIENT_ID and the optional MS_SCOPES, and returns the
// database path from SHEET_SERVICE_DB (default sheet-service.db in the working directory).
func ConfigFromEnv(get func(string) string) (cfg Config, dbPath string, err error) {
	cfg = Config{Tenant: get("MS_TENANT_ID"), ClientID: get("MS_CLIENT_ID"), Scope: get("MS_SCOPES")}
	if cfg.Tenant == "" || cfg.ClientID == "" {
		return cfg, "", fmt.Errorf("microsoft settings missing: set MS_TENANT_ID and MS_CLIENT_ID (or MS_ACCESS_TOKEN)")
	}
	dbPath = get("SHEET_SERVICE_DB")
	if dbPath == "" {
		dbPath = DefaultDBFile
	}
	return cfg, dbPath, nil
}

func (c Config) endpoint(name string) string {
	base := c.LoginBase
	if base == "" {
		base = DefaultLoginBase
	}
	return fmt.Sprintf("%s/%s/oauth2/v2.0/%s", strings.TrimRight(base, "/"), url.PathEscape(c.Tenant), name)
}

func (c Config) scope() string {
	if c.Scope != "" {
		return c.Scope
	}
	return DefaultScope
}

type tokenReply struct {
	AccessToken  string `json:"access_token"`
	RefreshToken string `json:"refresh_token"`
	ExpiresIn    int    `json:"expires_in"`
	Error        string `json:"error"`
	Description  string `json:"error_description"`
}

func post(ctx context.Context, endpoint string, form url.Values, out any) (int, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, strings.NewReader(form.Encode()))
	if err != nil {
		return 0, err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return 0, fmt.Errorf("microsoft login: %w", err)
	}
	defer resp.Body.Close()
	if err := json.NewDecoder(resp.Body).Decode(out); err != nil {
		return resp.StatusCode, fmt.Errorf("microsoft login response (%d): %w", resp.StatusCode, err)
	}
	return resp.StatusCode, nil
}

// Source supplies access tokens for one saved connection and renews them as needed.
type Source struct {
	store *Store
	cfg   Config
	name  string
}

// NewSource returns the token source for the connection called name (DefaultName when empty).
func NewSource(store *Store, cfg Config, name string) *Source {
	if name == "" {
		name = DefaultName
	}
	return &Source{store: store, cfg: cfg, name: name}
}

// Token returns a valid access token. When the saved one is close to expiring it is renewed with
// the refresh token, and the new refresh token Microsoft returns replaces the old one in the same
// database transaction.
func (s *Source) Token(ctx context.Context) (string, error) {
	var access string
	err := s.store.Locked(ctx, s.name, func(cur *Token) (*Token, error) {
		if cur == nil {
			return nil, ErrLoginRequired
		}
		if cur.Access != "" && time.Now().Before(cur.Expires.Add(-renewBefore)) {
			access = cur.Access
			return nil, nil
		}
		var r tokenReply
		status, err := post(ctx, s.cfg.endpoint("token"), url.Values{
			"grant_type":    {"refresh_token"},
			"client_id":     {s.cfg.ClientID},
			"refresh_token": {cur.Refresh},
			"scope":         {s.cfg.scope()},
		}, &r)
		if err != nil {
			return nil, err
		}
		if status != http.StatusOK || r.AccessToken == "" {
			switch r.Error {
			case "invalid_grant", "interaction_required", "consent_required", "invalid_client":
				return nil, fmt.Errorf("%w (%s)", ErrLoginRequired, firstLine(r.Description))
			}
			return nil, fmt.Errorf("microsoft token refresh failed (%d %s): %s", status, r.Error, firstLine(r.Description))
		}
		next := *cur
		next.Tenant, next.ClientID = s.cfg.Tenant, s.cfg.ClientID
		next.Access = r.AccessToken
		next.Expires = time.Now().Add(time.Duration(r.ExpiresIn) * time.Second)
		if r.RefreshToken != "" {
			next.Refresh = r.RefreshToken // the old refresh token must be discarded
		}
		access = next.Access
		return &next, nil
	})
	if err != nil {
		return "", err
	}
	return access, nil
}

func firstLine(s string) string {
	s, _, _ = strings.Cut(s, "\n")
	return strings.TrimSpace(s)
}

// sleep is replaced in tests.
var sleep = func(ctx context.Context, d time.Duration) error {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-t.C:
		return nil
	}
}

// Login signs in with the device-code flow and saves the connection under name. It prints the
// instructions to out, then waits until the user finishes signing in on any device, or the code
// expires (15 minutes by default).
func Login(ctx context.Context, store *Store, cfg Config, name string, out io.Writer) error {
	if name == "" {
		name = DefaultName
	}
	var dc struct {
		DeviceCode string `json:"device_code"`
		UserCode   string `json:"user_code"`
		VerifyURI  string `json:"verification_uri"`
		ExpiresIn  int    `json:"expires_in"`
		Interval   int    `json:"interval"`
		Message    string `json:"message"`
		Error      string `json:"error"`
		Desc       string `json:"error_description"`
	}
	status, err := post(ctx, cfg.endpoint("devicecode"), url.Values{
		"client_id": {cfg.ClientID},
		"scope":     {cfg.scope()},
	}, &dc)
	if err != nil {
		return err
	}
	if status != http.StatusOK || dc.DeviceCode == "" {
		return fmt.Errorf("device code request failed (%d %s): %s", status, dc.Error, firstLine(dc.Desc))
	}
	if dc.Message != "" {
		fmt.Fprintln(out, dc.Message)
	} else {
		fmt.Fprintf(out, "Open %s and enter the code %s\n", dc.VerifyURI, dc.UserCode)
	}

	interval := time.Duration(dc.Interval) * time.Second
	if interval <= 0 {
		interval = 5 * time.Second
	}
	deadline := time.Now().Add(time.Duration(dc.ExpiresIn) * time.Second)
	if dc.ExpiresIn <= 0 {
		deadline = time.Now().Add(15 * time.Minute)
	}
	for {
		if err := sleep(ctx, interval); err != nil {
			return err
		}
		if time.Now().After(deadline) {
			return errors.New("sign-in code expired before sign-in finished; run login again")
		}
		var r tokenReply
		_, err := post(ctx, cfg.endpoint("token"), url.Values{
			"grant_type":  {"urn:ietf:params:oauth:grant-type:device_code"},
			"client_id":   {cfg.ClientID},
			"device_code": {dc.DeviceCode},
		}, &r)
		if err != nil {
			return err
		}
		switch r.Error {
		case "":
			if r.AccessToken == "" || r.RefreshToken == "" {
				return errors.New("microsoft returned no refresh token; the scope must include offline_access")
			}
			return store.Put(ctx, Token{
				Name: name, Tenant: cfg.Tenant, ClientID: cfg.ClientID,
				Refresh: r.RefreshToken, Access: r.AccessToken,
				Expires: time.Now().Add(time.Duration(r.ExpiresIn) * time.Second),
			})
		case "authorization_pending":
		case "slow_down":
			interval += 5 * time.Second
		default:
			return fmt.Errorf("sign-in failed (%s): %s", r.Error, firstLine(r.Description))
		}
	}
}
