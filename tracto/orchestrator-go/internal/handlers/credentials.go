package handlers

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/vault"
)

// nameRE is the rule for secret names: small letters, digits, '-' and '.'.
var nameRE = regexp.MustCompile(`^[a-z0-9][a-z0-9.-]{0,99}$`)

// varNameRE is the rule for a variable name: environment-variable style, capitals allowed.
var varNameRE = regexp.MustCompile(`^[A-Za-z_][A-Za-z0-9_]{0,99}$`)

const varNameRule = "A variable name uses letters, digits and '_', starts with a letter or '_', and is at most 100 characters"

const nameRule = "A name uses small letters, digits, '-' and '.', starts with a letter or digit, and is at most 100 characters"

// expirySoonDays is how far ahead a credential's expiry date is flagged.
const expirySoonDays = 14

// CredentialHandler serves the secrets and the groups with their variables.
type CredentialHandler struct {
	db    *sql.DB
	vault *vault.Vault
	now   func() time.Time
}

func NewCredentialHandler(db *sql.DB, v *vault.Vault) *CredentialHandler {
	return &CredentialHandler{db: db, vault: v, now: time.Now}
}

func (h *CredentialHandler) locked(w http.ResponseWriter) bool {
	if h.vault.IsLocked() {
		errResp(w, http.StatusServiceUnavailable, "CREDENTIAL_STORE_LOCKED",
			"The secret store is locked: set "+vault.EnvKey+" for the orchestrator")
		return true
	}
	return false
}

func (h *CredentialHandler) event(kind, credential, actor string, detail any) {
	var d sql.NullString
	if detail != nil {
		b, _ := json.Marshal(detail)
		d = sql.NullString{String: string(b), Valid: true}
	}
	_, _ = h.db.Exec(`INSERT INTO CredentialEvents(EventId,At,Kind,Credential,Actor,Detail) VALUES($1,$2,$3,$4,$5,$6)`,
		uuid.NewString(), h.now().UTC(), kind, credential, actor, d)
}

func eventTx(x *txRun, now time.Time, kind, credential, actor string, detail any) {
	var d sql.NullString
	if detail != nil {
		b, _ := json.Marshal(detail)
		d = sql.NullString{String: string(b), Valid: true}
	}
	x.exec(`INSERT INTO CredentialEvents(EventId,At,Kind,Credential,Actor,Detail) VALUES($1,$2,$3,$4,$5,$6)`,
		uuid.NewString(), now.UTC(), kind, credential, actor, d)
}

func (h *CredentialHandler) forbidGroup(w http.ResponseWriter, p *auth.Principal, group string) bool {
	if !p.CanSeeGroup(group) {
		errResp(w, http.StatusForbidden, "FORBIDDEN", "You have no access to the group "+group)
		return true
	}
	return false
}

// ── Expiry ───────────────────────────────────────────────────────────────────

// expiryStatus is "none" (never expires), "ok", "soon" (within expirySoonDays) or "expired".
func expiryStatus(expires sql.NullTime, now time.Time) string {
	if !expires.Valid {
		return "none"
	}
	today := time.Date(now.Year(), now.Month(), now.Day(), 0, 0, 0, 0, time.UTC)
	day := time.Date(expires.Time.Year(), expires.Time.Month(), expires.Time.Day(), 0, 0, 0, 0, time.UTC)
	switch {
	case day.Before(today):
		return "expired"
	case !day.After(today.AddDate(0, 0, expirySoonDays)):
		return "soon"
	}
	return "ok"
}

func dateOrNil(t sql.NullTime) any {
	if !t.Valid {
		return nil
	}
	return t.Time.Format("2006-01-02")
}

// parseExpiry reads an expiresOn value: absent, null or empty means no expiry.
func parseExpiry(v any) (sql.NullTime, error) {
	s, _ := v.(string)
	if v == nil || strings.TrimSpace(s) == "" {
		return sql.NullTime{}, nil
	}
	t, err := time.Parse("2006-01-02", strings.TrimSpace(s))
	if err != nil {
		return sql.NullTime{}, errors.New("expiresOn must be a date as YYYY-MM-DD, or empty for no expiry")
	}
	return sql.NullTime{Time: t, Valid: true}, nil
}

// ── Groups ───────────────────────────────────────────────────────────────────

// groupNameOK is the rule for a group name: any text up to 100 characters, no commas (a user's group
// limit is a comma-separated list) and no control characters. Group names are never written in a script.
func groupNameOK(s string) bool {
	if strings.TrimSpace(s) == "" || len(s) > 100 || strings.Contains(s, ",") {
		return false
	}
	for _, r := range s {
		if r < 32 || r == 127 {
			return false
		}
	}
	return true
}

// fullVarName is the name a script reads a variable by: GP, the number of its group, an underscore
// and the name typed on the group page. Full names are unique across groups because group numbers are.
func fullVarName(groupNo int, name string) string {
	return fmt.Sprintf("GP%d_%s", groupNo, name)
}

func validScriptID(s string) bool {
	return s != "" && len(s) <= 200 && !strings.ContainsAny(s, " \t\r\n,")
}

// GET /admin/credential-groups
func (h *CredentialHandler) ListGroups(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	rows, err := h.db.Query(`SELECT g.GroupName,g.GroupNo,COALESCE(g.Description,''),
		(SELECT COUNT(*) FROM GroupVariables v WHERE v.GroupName=g.GroupName)
		FROM CredentialGroups g ORDER BY g.GroupNo`)
	if err != nil {
		dbErr(w, err)
		return
	}
	type g struct {
		name, desc string
		no, n      int
	}
	var all []g
	for rows.Next() {
		var x g
		if rows.Scan(&x.name, &x.no, &x.desc, &x.n) == nil && p.CanSeeGroup(x.name) {
			all = append(all, x)
		}
	}
	rows.Close()
	out := []map[string]any{}
	for _, x := range all {
		scripts := []string{}
		sr, err := h.db.Query(`SELECT ArtifactId FROM CredentialGroupAccess WHERE GroupName=$1 ORDER BY ArtifactId`, x.name)
		if err == nil {
			for sr.Next() {
				var a string
				if sr.Scan(&a) == nil {
					scripts = append(scripts, a)
				}
			}
			sr.Close()
		}
		out = append(out, map[string]any{"name": x.name, "number": x.no, "prefix": fmt.Sprintf("GP%d_", x.no), "description": x.desc, "variableCount": x.n, "scripts": scripts})
	}
	respond(w, http.StatusOK, map[string]any{"groups": out})
}

func (h *CredentialHandler) setScripts(x *txRun, group string, scripts []string) {
	x.exec(`DELETE FROM CredentialGroupAccess WHERE GroupName=$1`, group)
	seen := map[string]bool{}
	for _, s := range scripts {
		if !seen[s] {
			seen[s] = true
			x.exec(`INSERT INTO CredentialGroupAccess(GroupName,ArtifactId) VALUES($1,$2)`, group, s)
		}
	}
}

// POST /admin/credential-groups
func (h *CredentialHandler) CreateGroup(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	var req struct {
		Name        string   `json:"name"`
		Description string   `json:"description"`
		Scripts     []string `json:"scripts"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	req.Name = strings.TrimSpace(req.Name)
	if !groupNameOK(req.Name) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_NAME", "A group name is any text up to 100 characters, without commas")
		return
	}
	for _, s := range req.Scripts {
		if !validScriptID(s) {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_SCRIPT", "A script is named by its artifact id, or '*' for every script")
			return
		}
	}
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	var number int
	if err := x.tx.QueryRow(`INSERT INTO CredentialGroups(GroupName,Description,CreatedAt,CreatedBy) VALUES($1,$2,$3,$4) RETURNING GroupNo`,
		req.Name, req.Description, h.now().UTC(), p.Username).Scan(&number); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A group with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	h.setScripts(x, req.Name, req.Scripts)
	if err := x.commit(); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A group with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	respond(w, http.StatusCreated, map[string]any{"name": req.Name, "number": number, "prefix": fmt.Sprintf("GP%d_", number)})
}

// PUT /admin/credential-groups/{name}
func (h *CredentialHandler) UpdateGroup(w http.ResponseWriter, r *http.Request) {
	name := chi.URLParam(r, "name")
	var req struct {
		Name        *string   `json:"name"`
		Description *string   `json:"description"`
		Scripts     *[]string `json:"scripts"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	newName := name
	if req.Name != nil {
		newName = strings.TrimSpace(*req.Name)
		if !groupNameOK(newName) {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_NAME", "A group name is any text up to 100 characters, without commas")
			return
		}
	}
	var exists bool
	_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM CredentialGroups WHERE GroupName=$1)`, name).Scan(&exists)
	if !exists {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such group")
		return
	}
	if req.Scripts != nil {
		for _, s := range *req.Scripts {
			if !validScriptID(s) {
				errResp(w, http.StatusUnprocessableEntity, "INVALID_SCRIPT", "A script is named by its artifact id, or '*' for every script")
				return
			}
		}
	}
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	if req.Description != nil {
		x.exec(`UPDATE CredentialGroups SET Description=$1 WHERE GroupName=$2`, *req.Description, name)
	}
	if req.Scripts != nil {
		h.setScripts(x, name, *req.Scripts)
	}
	if newName != name {
		// The number stays with the group; the rows that name it follow (ON UPDATE CASCADE).
		x.exec(`UPDATE CredentialGroups SET GroupName=$1 WHERE GroupName=$2`, newName, name)
		x.exec(`UPDATE Users SET AllowedGroups=array_to_string(array(
			SELECT CASE WHEN g=$2 THEN $1 ELSE g END FROM unnest(string_to_array(AllowedGroups, ',')) AS g), ',')
			WHERE AllowedGroups IS NOT NULL AND $2 = ANY(string_to_array(AllowedGroups, ','))`, newName, name)
		eventTx(x, h.now().UTC(), "GROUP_RENAMED", newName, auth.PrincipalFrom(r.Context()).Username, map[string]any{"from": name})
	}
	if err := x.commit(); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A group with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"name": newName})
}

// DELETE /admin/credential-groups/{name}   (the variables of the group go with it)
func (h *CredentialHandler) DeleteGroup(w http.ResponseWriter, r *http.Request) {
	name := chi.URLParam(r, "name")
	res, err := h.db.Exec(`DELETE FROM CredentialGroups WHERE GroupName=$1`, name)
	if err != nil {
		dbErr(w, err)
		return
	}
	if c, _ := res.RowsAffected(); c == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such group")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
