// Package auth holds the Orchestrator UI's users: roles and permissions, password rules and
// hashing, signed sessions, and the first administrator.
package auth

import (
	"context"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"strings"
	"time"
	"unicode"

	"github.com/golang-jwt/jwt/v5"
	"golang.org/x/crypto/bcrypt"

	"tracto/orchestrator/internal/middleware"
)

// Roles.
const (
	RoleViewer          = "Viewer"
	RoleOperator        = "Operator"
	RoleCredentialAdmin = "CredentialAdmin"
	RoleAdministrator   = "Administrator"
)

// Permissions.
const (
	PermView     = "credentials.view"
	PermReveal   = "credentials.reveal"
	PermEdit     = "credentials.edit"
	PermGroups   = "credentials.groups"
	PermSettings = "settings.edit"
	PermUsers    = "users.manage"
)

var rolePermissions = map[string][]string{
	RoleViewer:          {PermView},
	RoleOperator:        {PermView, PermReveal},
	RoleCredentialAdmin: {PermView, PermReveal, PermEdit, PermGroups, PermSettings},
	RoleAdministrator:   {PermView, PermReveal, PermEdit, PermGroups, PermSettings, PermUsers},
}

// Roles lists the valid roles.
func Roles() []string {
	return []string{RoleViewer, RoleOperator, RoleCredentialAdmin, RoleAdministrator}
}

// ValidRole reports whether role is one of the four.
func ValidRole(role string) bool { _, ok := rolePermissions[role]; return ok }

// PermissionsOf returns the permissions of role.
func PermissionsOf(role string) []string { return append([]string(nil), rolePermissions[role]...) }

// FirstAdminUsername and FirstAdminPassword are the first administrator, created once when there
// are no users.
const (
	FirstAdminUsername = "admin"
	FirstAdminPassword = "Admin@123#"
)

// SessionLifetime is how long a sign-in lasts.
const SessionLifetime = 8 * time.Hour

const bcryptCost = 12

// Principal is the signed-in user of a request.
type Principal struct {
	Username string
	Role     string
	// Groups lists the credential groups the user is limited to; nil means every group.
	Groups []string
}

// Has reports whether the user holds perm.
func (p *Principal) Has(perm string) bool {
	for _, x := range rolePermissions[p.Role] {
		if x == perm {
			return true
		}
	}
	return false
}

// CanSeeGroup reports whether the user may work with the credentials of group.
func (p *Principal) CanSeeGroup(group string) bool {
	if p.Groups == nil {
		return true
	}
	for _, g := range p.Groups {
		if g == group {
			return true
		}
	}
	return false
}

type ctxKey struct{}

// PrincipalFrom returns the signed-in user of the request, or nil.
func PrincipalFrom(ctx context.Context) *Principal {
	p, _ := ctx.Value(ctxKey{}).(*Principal)
	return p
}

// WithPrincipal returns a context carrying p (used by tests).
func WithPrincipal(ctx context.Context, p *Principal) context.Context {
	return context.WithValue(ctx, ctxKey{}, p)
}

// ── Passwords ────────────────────────────────────────────────────────────────

// HashPassword returns the bcrypt hash of password.
func HashPassword(password string) (string, error) {
	h, err := bcrypt.GenerateFromPassword([]byte(password), bcryptCost)
	return string(h), err
}

// CheckPassword reports whether password matches hash.
func CheckPassword(hash, password string) bool {
	return bcrypt.CompareHashAndPassword([]byte(hash), []byte(password)) == nil
}

// ValidatePassword returns the first rule password breaks, or nil. current is the password it
// replaces (empty for a first password).
func ValidatePassword(password, current string) error {
	if len(password) < 8 {
		return errors.New("The new password must be at least 8 characters")
	}
	var upper, lower, digit, symbol bool
	for _, r := range password {
		switch {
		case unicode.IsUpper(r):
			upper = true
		case unicode.IsLower(r):
			lower = true
		case unicode.IsDigit(r):
			digit = true
		default:
			symbol = true
		}
	}
	switch {
	case !upper:
		return errors.New("The new password needs a capital letter")
	case !lower:
		return errors.New("The new password needs a small letter")
	case !digit:
		return errors.New("The new password needs a digit")
	case !symbol:
		return errors.New("The new password needs a symbol")
	}
	if current != "" && password == current {
		return errors.New("The new password must differ from the current one")
	}
	return nil
}

// ── First administrator ──────────────────────────────────────────────────────

// EnsureFirstAdmin creates the first administrator when the users table is empty. It changes
// nothing when any user exists.
func EnsureFirstAdmin(db *sql.DB, logger *slog.Logger) error {
	var n int
	if err := db.QueryRow(`SELECT COUNT(*) FROM Users`).Scan(&n); err != nil {
		return err
	}
	if n > 0 {
		return nil
	}
	hash, err := HashPassword(FirstAdminPassword)
	if err != nil {
		return err
	}
	now := time.Now().UTC()
	res, err := db.Exec(`INSERT INTO Users(Username,PasswordHash,Role,CreatedAt,UpdatedAt)
		VALUES($1,$2,$3,$4,$4) ON CONFLICT DO NOTHING`, FirstAdminUsername, hash, RoleAdministrator, now)
	if err != nil {
		return err
	}
	if c, _ := res.RowsAffected(); c > 0 && logger != nil {
		logger.Info("first administrator created", "username", FirstAdminUsername)
	}
	return nil
}

// ── Sessions ─────────────────────────────────────────────────────────────────

// Service issues and checks sessions.
type Service struct {
	db  *sql.DB
	key []byte
}

// NewService builds the session service. The signing key is derived from secret, so a session
// token is never valid as a worker token and the other way round; with no secret a random key is
// made, and sessions end when the process restarts.
func NewService(db *sql.DB, secret string) *Service {
	base := []byte(secret)
	if secret == "" {
		base = make([]byte, 32)
		_, _ = rand.Read(base)
	}
	m := hmac.New(sha256.New, base)
	m.Write([]byte("tracto-user-session"))
	return &Service{db: db, key: m.Sum(nil)}
}

// User is a row of the users table without its hash.
type User struct {
	Username     string
	Role         string
	Groups       []string // nil: every group
	Disabled     bool
	Version      int
	LastSignInAt *time.Time
}

func splitGroups(s sql.NullString) []string {
	if !s.Valid {
		return nil
	}
	out := []string{}
	for _, g := range strings.Split(s.String, ",") {
		if g = strings.TrimSpace(g); g != "" {
			out = append(out, g)
		}
	}
	return out
}

// JoinGroups stores a group list: nil (every group) as NULL.
func JoinGroups(groups []string) sql.NullString {
	if groups == nil {
		return sql.NullString{}
	}
	return sql.NullString{String: strings.Join(groups, ","), Valid: true}
}

// Find loads a user ignoring capitals, with the password hash.
func (s *Service) Find(username string) (*User, string, error) {
	var u User
	var hash string
	var groups sql.NullString
	var last sql.NullTime
	err := s.db.QueryRow(`SELECT Username,PasswordHash,Role,AllowedGroups,Disabled,SessionVersion,LastSignInAt
		FROM Users WHERE lower(Username)=lower($1)`, username).
		Scan(&u.Username, &hash, &u.Role, &groups, &u.Disabled, &u.Version, &last)
	if err != nil {
		return nil, "", err
	}
	u.Groups = splitGroups(groups)
	if last.Valid {
		t := last.Time
		u.LastSignInAt = &t
	}
	return &u, hash, nil
}

type claims struct {
	Role string `json:"role"`
	SV   int    `json:"sv"`
	jwt.RegisteredClaims
}

// Issue signs a session for u.
func (s *Service) Issue(u *User) (string, time.Time, error) {
	exp := time.Now().Add(SessionLifetime)
	c := claims{Role: u.Role, SV: u.Version, RegisteredClaims: jwt.RegisteredClaims{
		Subject: u.Username, ExpiresAt: jwt.NewNumericDate(exp), IssuedAt: jwt.NewNumericDate(time.Now()),
	}}
	tok, err := jwt.NewWithClaims(jwt.SigningMethodHS256, c).SignedString(s.key)
	return tok, exp, err
}

// Authenticate checks a bearer token and returns the user it names, loaded afresh: a disabled
// user, or a token issued before the password changed, is refused.
func (s *Service) Authenticate(token string) (*Principal, error) {
	var c claims
	_, err := jwt.ParseWithClaims(token, &c, func(t *jwt.Token) (any, error) {
		if _, ok := t.Method.(*jwt.SigningMethodHMAC); !ok {
			return nil, jwt.ErrSignatureInvalid
		}
		return s.key, nil
	}, jwt.WithLeeway(30*time.Second))
	if err != nil {
		return nil, err
	}
	u, _, err := s.Find(c.Subject)
	if err != nil || u.Disabled || u.Version != c.SV {
		return nil, errors.New("session is no longer valid")
	}
	return &Principal{Username: u.Username, Role: u.Role, Groups: u.Groups}, nil
}

// Require wraps a handler: the caller must be signed in and, when perm is not empty, hold it.
func (s *Service) Require(perm string, next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		h := r.Header.Get("Authorization")
		if !strings.HasPrefix(h, "Bearer ") {
			middleware.AppendSecurityEvent(s.db, "AUTH_FAILURE", r.RemoteAddr,
				`{"reason":"missing_session","path":`+jsonString(r.URL.Path)+`}`)
			writeErr(w, http.StatusUnauthorized, "AUTH_REQUIRED", "Sign in first")
			return
		}
		p, err := s.Authenticate(strings.TrimPrefix(h, "Bearer "))
		if err != nil {
			middleware.AppendSecurityEvent(s.db, "AUTH_FAILURE", r.RemoteAddr,
				`{"reason":"invalid_session","path":`+jsonString(r.URL.Path)+`}`)
			writeErr(w, http.StatusUnauthorized, "INVALID_TOKEN", "Your session has ended; sign in again")
			return
		}
		if perm != "" && !p.Has(perm) {
			writeErr(w, http.StatusForbidden, "FORBIDDEN", fmt.Sprintf("This needs the permission %s", perm))
			return
		}
		next(w, r.WithContext(context.WithValue(r.Context(), ctxKey{}, p)))
	}
}

func jsonString(s string) string { b, _ := json.Marshal(s); return string(b) }

func writeErr(w http.ResponseWriter, code int, errCode, msg string) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(code)
	_ = json.NewEncoder(w).Encode(map[string]string{"error": errCode, "message": msg})
}
