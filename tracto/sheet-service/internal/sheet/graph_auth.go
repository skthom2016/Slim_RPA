package sheet

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
	"strings"
	"sync"
	"time"
)

// msClientCredentials gets app-only tokens for Microsoft Graph (OAuth2 client-credentials flow)
// and caches them until shortly before they expire.
type msClientCredentials struct {
	tokenURL     string
	clientID     string
	clientSecret string

	mu      sync.Mutex
	token   string
	expires time.Time
}

// graphTokens picks the token source from the environment:
//
//	MS_ACCESS_TOKEN                              a ready access token, or
//	MS_TENANT_ID + MS_CLIENT_ID + MS_CLIENT_SECRET   app-only login
func graphTokens(cfg Config) (tokenSource, error) {
	if t := cfg.getenv("MS_ACCESS_TOKEN"); t != "" {
		return staticToken(t), nil
	}
	tenant, id, secret := cfg.getenv("MS_TENANT_ID"), cfg.getenv("MS_CLIENT_ID"), cfg.getenv("MS_CLIENT_SECRET")
	if tenant == "" || id == "" || secret == "" {
		return nil, fmt.Errorf("microsoft credentials missing: set MS_TENANT_ID, MS_CLIENT_ID and MS_CLIENT_SECRET (or MS_ACCESS_TOKEN)")
	}
	login := cfg.LoginBase
	if login == "" {
		login = "https://login.microsoftonline.com"
	}
	return &msClientCredentials{
		tokenURL:     fmt.Sprintf("%s/%s/oauth2/v2.0/token", login, url.PathEscape(tenant)),
		clientID:     id,
		clientSecret: secret,
	}, nil
}

func (m *msClientCredentials) Token(ctx context.Context) (string, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.token != "" && time.Now().Before(m.expires.Add(-time.Minute)) {
		return m.token, nil
	}

	form := url.Values{
		"grant_type":    {"client_credentials"},
		"client_id":     {m.clientID},
		"client_secret": {m.clientSecret},
		"scope":         {"https://graph.microsoft.com/.default"},
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, m.tokenURL, strings.NewReader(form.Encode()))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", fmt.Errorf("microsoft token request: %w", err)
	}
	defer resp.Body.Close()

	var out struct {
		AccessToken string `json:"access_token"`
		ExpiresIn   int    `json:"expires_in"`
		Error       string `json:"error_description"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return "", fmt.Errorf("microsoft token response: %w", err)
	}
	if resp.StatusCode != http.StatusOK || out.AccessToken == "" {
		return "", fmt.Errorf("microsoft token request failed (%d): %s", resp.StatusCode, out.Error)
	}
	m.token = out.AccessToken
	m.expires = time.Now().Add(time.Duration(out.ExpiresIn) * time.Second)
	return m.token, nil
}
