package handlers

import (
	"database/sql"
	"encoding/json"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/middleware"
)

// AuthHandler serves sign-in, the password change and the users of the Orchestrator UI.
type AuthHandler struct {
	db  *sql.DB
	svc *auth.Service
}

func NewAuthHandler(db *sql.DB, svc *auth.Service) *AuthHandler { return &AuthHandler{db: db, svc: svc} }

// dummyHash is compared against when the username is unknown, so a wrong username and a wrong
// password take about the same time.
var dummyHash, _ = auth.HashPassword("not-a-real-password")

func jsonQuote(s string) string { b, _ := json.Marshal(s); return string(b) }

func (h *AuthHandler) event(kind, ip, username string) {
	middleware.AppendSecurityEvent(h.db, kind, ip, `{"username":`+jsonQuote(username)+`}`)
}

func userView(u *auth.User) map[string]any {
	groups := any(nil)
	if u.Groups != nil {
		groups = u.Groups
	}
	return map[string]any{
		"username": u.Username, "role": u.Role, "permissions": auth.PermissionsOf(u.Role),
		"allowedGroups": groups,
	}
}

// POST /auth/login
func (h *AuthHandler) Login(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	const refused = "Username or password is wrong"
	u, hash, err := h.svc.Find(strings.TrimSpace(req.Username))
	if err != nil {
		auth.CheckPassword(dummyHash, req.Password)
		h.event("SIGN_IN_FAILED", r.RemoteAddr, req.Username)
		errResp(w, http.StatusUnauthorized, "LOGIN_FAILED", refused)
		return
	}
	if !auth.CheckPassword(hash, req.Password) || u.Disabled {
		h.event("SIGN_IN_FAILED", r.RemoteAddr, req.Username)
		errResp(w, http.StatusUnauthorized, "LOGIN_FAILED", refused)
		return
	}
	token, exp, err := h.svc.Issue(u)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "TOKEN_ERROR", "Could not start a session")
		return
	}
	_, _ = h.db.Exec(`UPDATE Users SET LastSignInAt=$1 WHERE Username=$2`, time.Now().UTC(), u.Username)
	h.event("SIGN_IN", r.RemoteAddr, u.Username)
	out := userView(u)
	out["token"] = token
	out["expiresAt"] = exp.UTC().Format(time.RFC3339)
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, out)
}

// POST /auth/logout
func (h *AuthHandler) Logout(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	h.event("SIGN_OUT", r.RemoteAddr, p.Username)
	w.WriteHeader(http.StatusNoContent)
}

// GET /auth/me
func (h *AuthHandler) Me(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	groups := any(nil)
	if p.Groups != nil {
		groups = p.Groups
	}
	respond(w, http.StatusOK, map[string]any{
		"username": p.Username, "role": p.Role, "permissions": auth.PermissionsOf(p.Role), "allowedGroups": groups,
	})
}

// POST /auth/password
func (h *AuthHandler) ChangePassword(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	var req struct {
		CurrentPassword string `json:"currentPassword"`
		NewPassword     string `json:"newPassword"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	u, hash, err := h.svc.Find(p.Username)
	if err != nil {
		errResp(w, http.StatusUnauthorized, "INVALID_TOKEN", "Your session has ended; sign in again")
		return
	}
	if !auth.CheckPassword(hash, req.CurrentPassword) {
		h.event("PASSWORD_CHANGE_REFUSED", r.RemoteAddr, p.Username)
		errResp(w, http.StatusUnprocessableEntity, "CURRENT_PASSWORD_WRONG", "Current password is wrong")
		return
	}
	if err := auth.ValidatePassword(req.NewPassword, req.CurrentPassword); err != nil {
		errResp(w, http.StatusUnprocessableEntity, "PASSWORD_RULE", err.Error())
		return
	}
	newHash, err := auth.HashPassword(req.NewPassword)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "HASH_ERROR", "Could not store the password")
		return
	}
	if _, err := h.db.Exec(`UPDATE Users SET PasswordHash=$1, SessionVersion=SessionVersion+1, UpdatedAt=$2
		WHERE Username=$3`, newHash, time.Now().UTC(), u.Username); err != nil {
		dbErr(w, err)
		return
	}
	h.event("PASSWORD_CHANGED", r.RemoteAddr, u.Username)
	// The user's other sessions end with the version change; this one gets a fresh token.
	u.Version++
	token, exp, _ := h.svc.Issue(u)
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, map[string]any{"token": token, "expiresAt": exp.UTC().Format(time.RFC3339)})
}

// ── Users ────────────────────────────────────────────────────────────────────

var usernameRE = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$`)

func (h *AuthHandler) userRow(rows *sql.Rows) map[string]any {
	var name, role string
	var groups sql.NullString
	var disabled bool
	var last sql.NullTime
	if err := rows.Scan(&name, &role, &groups, &disabled, &last); err != nil {
		return nil
	}
	out := map[string]any{"username": name, "role": role, "disabled": disabled, "allowedGroups": nil, "lastSignInAt": nil}
	if groups.Valid {
		g := []string{}
		for _, x := range strings.Split(groups.String, ",") {
			if x = strings.TrimSpace(x); x != "" {
				g = append(g, x)
			}
		}
		out["allowedGroups"] = g
	}
	if last.Valid {
		out["lastSignInAt"] = last.Time.UTC().Format(time.RFC3339)
	}
	return out
}

// GET /admin/users
func (h *AuthHandler) ListUsers(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT Username,Role,AllowedGroups,Disabled,LastSignInAt FROM Users ORDER BY lower(Username)`)
	if err != nil {
		dbErr(w, err)
		return
	}
	defer rows.Close()
	list := []map[string]any{}
	for rows.Next() {
		if u := h.userRow(rows); u != nil {
			list = append(list, u)
		}
	}
	respond(w, http.StatusOK, map[string]any{"users": list, "roles": auth.Roles()})
}

func validGroupList(groups []string) bool {
	for _, g := range groups {
		if !nameRE.MatchString(g) {
			return false
		}
	}
	return true
}

// POST /admin/users
func (h *AuthHandler) CreateUser(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	var req struct {
		Username      string    `json:"username"`
		Role          string    `json:"role"`
		AllowedGroups *[]string `json:"allowedGroups"`
		Password      string    `json:"password"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if !usernameRE.MatchString(req.Username) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_USERNAME", "A username uses letters, digits, '.', '_' or '-', and starts with a letter or digit")
		return
	}
	if !auth.ValidRole(req.Role) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_ROLE", "The role must be one of: "+strings.Join(auth.Roles(), ", "))
		return
	}
	if err := auth.ValidatePassword(req.Password, ""); err != nil {
		errResp(w, http.StatusUnprocessableEntity, "PASSWORD_RULE", strings.Replace(err.Error(), "The new password", "The password", 1))
		return
	}
	var groups []string
	if req.AllowedGroups != nil {
		groups = *req.AllowedGroups
		if !validGroupList(groups) {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_GROUP", "Group names use small letters, digits, '-' and '.'")
			return
		}
	}
	hash, err := auth.HashPassword(req.Password)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "HASH_ERROR", "Could not store the password")
		return
	}
	now := time.Now().UTC()
	_, err = h.db.Exec(`INSERT INTO Users(Username,PasswordHash,Role,AllowedGroups,CreatedAt,UpdatedAt) VALUES($1,$2,$3,$4,$5,$5)`,
		req.Username, hash, req.Role, auth.JoinGroups(groups), now)
	if err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "USER_EXISTS", "A user with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	h.event("USER_CREATED", r.RemoteAddr, req.Username+" by "+p.Username)
	respond(w, http.StatusCreated, map[string]any{"username": req.Username, "role": req.Role})
}

// PUT /admin/users/{username}
func (h *AuthHandler) UpdateUser(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	target := chi.URLParam(r, "username")

	var raw map[string]json.RawMessage
	if !decodeJSON(w, r, &raw) {
		return
	}
	u, _, err := h.svc.Find(target)
	if err != nil {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such user")
		return
	}

	role, disabled, groups := u.Role, u.Disabled, u.Groups
	if v, ok := raw["role"]; ok {
		if json.Unmarshal(v, &role) != nil || !auth.ValidRole(role) {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_ROLE", "The role must be one of: "+strings.Join(auth.Roles(), ", "))
			return
		}
	}
	if v, ok := raw["disabled"]; ok && json.Unmarshal(v, &disabled) != nil {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_DISABLED", "disabled must be true or false")
		return
	}
	if v, ok := raw["allowedGroups"]; ok {
		var g *[]string
		if json.Unmarshal(v, &g) != nil {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_GROUP", "allowedGroups must be a list or null")
			return
		}
		groups = nil
		if g != nil {
			groups = *g
			if !validGroupList(groups) {
				errResp(w, http.StatusUnprocessableEntity, "INVALID_GROUP", "Group names use small letters, digits, '-' and '.'")
				return
			}
		}
	}

	// The last enabled administrator keeps the role and stays enabled.
	if u.Role == auth.RoleAdministrator && !u.Disabled && (role != auth.RoleAdministrator || disabled) {
		var others int
		_ = h.db.QueryRow(`SELECT COUNT(*) FROM Users WHERE Role=$1 AND NOT Disabled AND Username<>$2`,
			auth.RoleAdministrator, u.Username).Scan(&others)
		if others == 0 {
			errResp(w, http.StatusConflict, "LAST_ADMINISTRATOR", "This is the last enabled Administrator: it cannot be disabled or lose the role")
			return
		}
	}

	now := time.Now().UTC()
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`UPDATE Users SET Role=$1,Disabled=$2,AllowedGroups=$3,UpdatedAt=$4 WHERE Username=$5`,
		role, disabled, auth.JoinGroups(groups), now, u.Username)

	reset := false
	if v, ok := raw["newPassword"]; ok {
		var pw string
		if json.Unmarshal(v, &pw) != nil {
			errResp(w, http.StatusUnprocessableEntity, "PASSWORD_RULE", "newPassword must be text")
			return
		}
		if err := auth.ValidatePassword(pw, ""); err != nil {
			errResp(w, http.StatusUnprocessableEntity, "PASSWORD_RULE", strings.Replace(err.Error(), "The new password", "The password", 1))
			return
		}
		hash, err := auth.HashPassword(pw)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "HASH_ERROR", "Could not store the password")
			return
		}
		x.exec(`UPDATE Users SET PasswordHash=$1, SessionVersion=SessionVersion+1 WHERE Username=$2`, hash, u.Username)
		reset = true
	}
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	if reset {
		h.event("PASSWORD_RESET", r.RemoteAddr, u.Username+" by "+p.Username)
	}
	h.event("USER_UPDATED", r.RemoteAddr, u.Username+" by "+p.Username)
	respond(w, http.StatusOK, map[string]any{"username": u.Username, "role": role, "disabled": disabled})
}

// newTxDB begins a transaction as a txRun, or returns nil.
func newTxDB(db *sql.DB) *txRun {
	tx, err := db.Begin()
	if err != nil {
		return nil
	}
	return newTx(tx)
}
