package sheet

import (
	"context"
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"strings"
	"sync"
	"time"
)

const sheetsScope = "https://www.googleapis.com/auth/spreadsheets"

// tokenSource supplies a bearer token for the Sheets API.
type tokenSource interface {
	Token(ctx context.Context) (string, error)
}

type staticToken string

func (t staticToken) Token(context.Context) (string, error) { return string(t), nil }

// serviceAccountToken exchanges a service-account key for short-lived access tokens
// (OAuth2 JWT bearer grant) and caches the token until shortly before it expires.
type serviceAccountToken struct {
	email    string
	key      *rsa.PrivateKey
	tokenURL string

	mu      sync.Mutex
	token   string
	expires time.Time
}

func newServiceAccountToken(credentialsFile string) (*serviceAccountToken, error) {
	raw, err := os.ReadFile(credentialsFile)
	if err != nil {
		return nil, fmt.Errorf("read credentials: %w", err)
	}
	var c struct {
		ClientEmail string `json:"client_email"`
		PrivateKey  string `json:"private_key"`
		TokenURI    string `json:"token_uri"`
	}
	if err := json.Unmarshal(raw, &c); err != nil {
		return nil, fmt.Errorf("parse credentials: %w", err)
	}
	block, _ := pem.Decode([]byte(c.PrivateKey))
	if block == nil {
		return nil, fmt.Errorf("credentials: private_key is not PEM")
	}
	parsed, err := x509.ParsePKCS8PrivateKey(block.Bytes)
	if err != nil {
		return nil, fmt.Errorf("credentials: %w", err)
	}
	key, ok := parsed.(*rsa.PrivateKey)
	if !ok {
		return nil, fmt.Errorf("credentials: private_key is not RSA")
	}
	if c.TokenURI == "" {
		c.TokenURI = "https://oauth2.googleapis.com/token"
	}
	return &serviceAccountToken{email: c.ClientEmail, key: key, tokenURL: c.TokenURI}, nil
}

func (s *serviceAccountToken) Token(ctx context.Context) (string, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.token != "" && time.Now().Before(s.expires.Add(-time.Minute)) {
		return s.token, nil
	}

	now := time.Now()
	claims, _ := json.Marshal(map[string]any{
		"iss": s.email, "scope": sheetsScope, "aud": s.tokenURL,
		"iat": now.Unix(), "exp": now.Add(time.Hour).Unix(),
	})
	header := base64.RawURLEncoding.EncodeToString([]byte(`{"alg":"RS256","typ":"JWT"}`))
	signing := header + "." + base64.RawURLEncoding.EncodeToString(claims)
	sum := sha256.Sum256([]byte(signing))
	sig, err := rsa.SignPKCS1v15(rand.Reader, s.key, crypto.SHA256, sum[:])
	if err != nil {
		return "", fmt.Errorf("sign token request: %w", err)
	}
	assertion := signing + "." + base64.RawURLEncoding.EncodeToString(sig)

	form := url.Values{
		"grant_type": {"urn:ietf:params:oauth:grant-type:jwt-bearer"},
		"assertion":  {assertion},
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, s.tokenURL, strings.NewReader(form.Encode()))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", fmt.Errorf("token request: %w", err)
	}
	defer resp.Body.Close()

	var out struct {
		AccessToken string `json:"access_token"`
		ExpiresIn   int    `json:"expires_in"`
		Error       string `json:"error_description"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return "", fmt.Errorf("token response: %w", err)
	}
	if resp.StatusCode != http.StatusOK || out.AccessToken == "" {
		return "", fmt.Errorf("token request failed (%d): %s", resp.StatusCode, out.Error)
	}
	s.token = out.AccessToken
	s.expires = now.Add(time.Duration(out.ExpiresIn) * time.Second)
	return s.token, nil
}
