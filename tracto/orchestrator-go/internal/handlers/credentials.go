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

// nameRE is the rule for credential, group and setting names: small letters, digits, '-' and '.'.
var nameRE = regexp.MustCompile(`^[a-z0-9][a-z0-9.-]{0,99}$`)

const nameRule = "A name uses small letters, digits, '-' and '.', starts with a letter or digit, and is at most 100 characters"

// expirySoonDays is how far ahead a credential's expiry date is flagged.
const expirySoonDays = 14

// CredentialHandler serves the credential store: groups, credentials, import, history, settings.
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
			"The credential store is locked: set "+vault.EnvKey+" for the orchestrator")
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
		errResp(w, http.StatusForbidden, "FORBIDDEN", "You have no access to the credential group "+group)
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

func validScriptID(s string) bool {
	return s != "" && len(s) <= 200 && !strings.ContainsAny(s, " \t\r\n,")
}

// GET /admin/credential-groups
func (h *CredentialHandler) ListGroups(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	rows, err := h.db.Query(`SELECT g.GroupName,COALESCE(g.Description,''),
		(SELECT COUNT(*) FROM Credentials c WHERE c.GroupName=g.GroupName)
		FROM CredentialGroups g ORDER BY g.GroupName`)
	if err != nil {
		dbErr(w, err)
		return
	}
	type g struct {
		name, desc string
		n          int
	}
	var all []g
	for rows.Next() {
		var x g
		if rows.Scan(&x.name, &x.desc, &x.n) == nil && p.CanSeeGroup(x.name) {
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
		out = append(out, map[string]any{"name": x.name, "description": x.desc, "credentialCount": x.n, "scripts": scripts})
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
	if !nameRE.MatchString(req.Name) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_NAME", nameRule)
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
	x.exec(`INSERT INTO CredentialGroups(GroupName,Description,CreatedAt,CreatedBy) VALUES($1,$2,$3,$4)`,
		req.Name, req.Description, h.now().UTC(), p.Username)
	h.setScripts(x, req.Name, req.Scripts)
	if err := x.commit(); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A group with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	respond(w, http.StatusCreated, map[string]any{"name": req.Name})
}

// PUT /admin/credential-groups/{name}
func (h *CredentialHandler) UpdateGroup(w http.ResponseWriter, r *http.Request) {
	name := chi.URLParam(r, "name")
	var req struct {
		Description *string   `json:"description"`
		Scripts     *[]string `json:"scripts"`
	}
	if !decodeJSON(w, r, &req) {
		return
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
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"name": name})
}

// DELETE /admin/credential-groups/{name}
func (h *CredentialHandler) DeleteGroup(w http.ResponseWriter, r *http.Request) {
	name := chi.URLParam(r, "name")
	var n int
	_ = h.db.QueryRow(`SELECT COUNT(*) FROM Credentials WHERE GroupName=$1`, name).Scan(&n)
	if n > 0 {
		errResp(w, http.StatusConflict, "GROUP_NOT_EMPTY", fmt.Sprintf("The group holds %d credential(s); move or delete them first", n))
		return
	}
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

// ── Credentials ──────────────────────────────────────────────────────────────

// credIn is one credential as the API receives it.
type credIn struct {
	Name      string  `json:"name"`
	Group     string  `json:"group"`
	Username  string  `json:"username"`
	Password  string  `json:"password"`
	TotpKey   string  `json:"totpKey"`
	SiteURL   string  `json:"siteUrl"`
	Notes     string  `json:"notes"`
	ExpiresOn *string `json:"expiresOn"`
}

// check validates what every create and import row needs; it returns a message or "".
func (c *credIn) check(h *CredentialHandler) (sql.NullTime, string) {
	if !nameRE.MatchString(c.Name) {
		return sql.NullTime{}, nameRule
	}
	if !nameRE.MatchString(c.Group) {
		return sql.NullTime{}, "The group name is missing or breaks the name rule"
	}
	var ok bool
	_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM CredentialGroups WHERE GroupName=$1)`, c.Group).Scan(&ok)
	if !ok {
		return sql.NullTime{}, "The group " + c.Group + " does not exist"
	}
	if strings.TrimSpace(c.Username) == "" {
		return sql.NullTime{}, "The username is missing"
	}
	if c.Password == "" {
		return sql.NullTime{}, "The password is missing"
	}
	if c.TotpKey != "" {
		if _, err := vault.NormalizeTOTPKey(c.TotpKey); err != nil {
			return sql.NullTime{}, err.Error()
		}
	}
	var exp any
	if c.ExpiresOn != nil {
		exp = *c.ExpiresOn
	}
	e, err := parseExpiry(exp)
	if err != nil {
		return sql.NullTime{}, err.Error()
	}
	return e, ""
}

func (h *CredentialHandler) seal(c *credIn) (pw string, totp sql.NullString, err error) {
	pw, err = h.vault.Seal(c.Password, c.Name)
	if err != nil {
		return
	}
	if c.TotpKey != "" {
		k, _ := vault.NormalizeTOTPKey(c.TotpKey)
		var s string
		if s, err = h.vault.Seal(k, c.Name); err != nil {
			return
		}
		totp = sql.NullString{String: s, Valid: true}
	}
	return
}

func nullText(s string) sql.NullString { return sql.NullString{String: s, Valid: s != ""} }

// GET /admin/credentials?group=&q=
func (h *CredentialHandler) List(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	query := `SELECT Name,GroupName,Username,COALESCE(SiteUrl,''),COALESCE(Notes,''),ExpiresOn,TotpCipher IS NOT NULL,Version,UpdatedAt,UpdatedBy
		FROM Credentials WHERE 1=1`
	var args []any
	if g := r.URL.Query().Get("group"); g != "" {
		args = append(args, g)
		query += " AND GroupName=" + argN(args[:len(args)-1])
	}
	if q := strings.ToLower(strings.TrimSpace(r.URL.Query().Get("q"))); q != "" {
		args = append(args, "%"+q+"%")
		n := argN(args[:len(args)-1])
		query += " AND (lower(Name) LIKE " + n + " OR lower(Username) LIKE " + n + " OR lower(COALESCE(SiteUrl,'')) LIKE " + n + ")"
	}
	query += " ORDER BY Name"
	rows, err := h.db.Query(query, args...)
	if err != nil {
		dbErr(w, err)
		return
	}
	defer rows.Close()
	now := h.now()
	list := []map[string]any{}
	var soon, expired int
	for rows.Next() {
		var name, group, user, site, notes, by string
		var exp sql.NullTime
		var hasTotp bool
		var ver int
		var at time.Time
		if err := rows.Scan(&name, &group, &user, &site, &notes, &exp, &hasTotp, &ver, &at, &by); err != nil {
			dbErr(w, err)
			return
		}
		if !p.CanSeeGroup(group) {
			continue
		}
		st := expiryStatus(exp, now)
		switch st {
		case "soon":
			soon++
		case "expired":
			expired++
		}
		list = append(list, map[string]any{
			"name": name, "group": group, "username": user, "siteUrl": site, "notes": notes,
			"expiresOn": dateOrNil(exp), "expiryStatus": st, "hasTotp": hasTotp, "version": ver,
			"updatedAt": at.UTC().Format(time.RFC3339), "updatedBy": by,
		})
	}
	respond(w, http.StatusOK, map[string]any{
		"credentials": list, "summary": map[string]int{"total": len(list), "soon": soon, "expired": expired},
	})
}

// POST /admin/credentials
func (h *CredentialHandler) Create(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	var in credIn
	if !decodeJSON(w, r, &in) {
		return
	}
	exp, msg := in.check(h)
	if msg != "" {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", msg)
		return
	}
	if h.forbidGroup(w, p, in.Group) {
		return
	}
	pw, totp, err := h.seal(&in)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt the password")
		return
	}
	now := h.now().UTC()
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`INSERT INTO Credentials(Name,GroupName,Username,PasswordCipher,TotpCipher,KeyId,SiteUrl,Notes,ExpiresOn,UpdatedAt,UpdatedBy)
		VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)`,
		in.Name, in.Group, in.Username, pw, totp, h.vault.KeyID(), nullText(in.SiteURL), nullText(in.Notes), exp, now, p.Username)
	eventTx(x, now, "CREATED", in.Name, p.Username, map[string]any{"group": in.Group})
	if err := x.commit(); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A credential with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	respond(w, http.StatusCreated, map[string]any{"name": in.Name, "group": in.Group})
}

// PUT /admin/credentials/{name}
func (h *CredentialHandler) Update(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var raw map[string]json.RawMessage
	if !decodeJSON(w, r, &raw) {
		return
	}
	var group, user, oldPw, oldKeyID string
	var oldTotp sql.NullString
	err := h.db.QueryRow(`SELECT GroupName,Username,PasswordCipher,TotpCipher,KeyId FROM Credentials WHERE Name=$1`, name).
		Scan(&group, &user, &oldPw, &oldTotp, &oldKeyID)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such credential")
		return
	}
	if err != nil {
		dbErr(w, err)
		return
	}
	if h.forbidGroup(w, p, group) {
		return
	}

	sets := []string{"Version=Version+1", "UpdatedAt=$1", "UpdatedBy=$2"}
	args := []any{h.now().UTC(), p.Username}
	var changed []string
	var add func(col string, val any, field string)
	keyIDSet := false
	setKeyID := func() {
		if !keyIDSet {
			keyIDSet = true
			add("KeyId", h.vault.KeyID(), "keyId")
		}
	}
	add = func(col string, val any, field string) {
		args = append(args, val)
		sets = append(sets, fmt.Sprintf("%s=$%d", col, len(args)))
		changed = append(changed, field)
	}
	str := func(key string) (string, bool, bool) { // value, present, ok
		v, ok := raw[key]
		if !ok {
			return "", false, true
		}
		var s *string
		if json.Unmarshal(v, &s) != nil {
			return "", true, false
		}
		if s == nil {
			return "", true, true
		}
		return *s, true, true
	}
	bad := func(field string) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", field+" must be text")
	}

	if s, present, ok := str("group"); !ok {
		bad("group")
		return
	} else if present {
		if !nameRE.MatchString(s) {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", "The group name breaks the name rule")
			return
		}
		var ex bool
		_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM CredentialGroups WHERE GroupName=$1)`, s).Scan(&ex)
		if !ex {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", "The group "+s+" does not exist")
			return
		}
		if h.forbidGroup(w, p, s) {
			return
		}
		add("GroupName", s, "group")
	}
	if s, present, ok := str("username"); !ok {
		bad("username")
		return
	} else if present {
		if strings.TrimSpace(s) == "" {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", "The username is missing")
			return
		}
		add("Username", s, "username")
	}
	if s, present, ok := str("password"); !ok {
		bad("password")
		return
	} else if present && s != "" { // an empty password keeps the old one
		sealed, err := h.vault.Seal(s, name)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt the password")
			return
		}
		add("PasswordCipher", sealed, "password")
		setKeyID()
	}
	if s, present, ok := str("totpKey"); !ok {
		bad("totpKey")
		return
	} else if present {
		if s == "" {
			add("TotpCipher", sql.NullString{}, "totpKey")
		} else {
			k, err := vault.NormalizeTOTPKey(s)
			if err != nil {
				errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", err.Error())
				return
			}
			sealed, err := h.vault.Seal(k, name)
			if err != nil {
				errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt the key")
				return
			}
			add("TotpCipher", sealed, "totpKey")
			setKeyID()
		}
	}
	if s, present, ok := str("siteUrl"); !ok {
		bad("siteUrl")
		return
	} else if present {
		add("SiteUrl", nullText(s), "siteUrl")
	}
	if s, present, ok := str("notes"); !ok {
		bad("notes")
		return
	} else if present {
		add("Notes", nullText(s), "notes")
	}
	if v, present := raw["expiresOn"]; present {
		var anyv any
		_ = json.Unmarshal(v, &anyv)
		e, err := parseExpiry(anyv)
		if err != nil {
			errResp(w, http.StatusUnprocessableEntity, "INVALID_CREDENTIAL", err.Error())
			return
		}
		add("ExpiresOn", e, "expiresOn")
	}

	// KeyId names the key of both sealed values, so when one is re-sealed under a new key the other
	// is re-sealed with it.
	if keyIDSet && oldKeyID != h.vault.KeyID() {
		touched := map[string]bool{}
		for _, c := range changed {
			touched[c] = true
		}
		if !touched["password"] {
			plain, err := h.vault.Open(oldPw, oldKeyID, name)
			if err == nil {
				var s string
				if s, err = h.vault.Seal(plain, name); err == nil {
					sets = append(sets, fmt.Sprintf("PasswordCipher=$%d", len(args)+1))
					args = append(args, s)
				}
			}
			if err != nil {
				errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
				return
			}
		}
		if !touched["totpKey"] && oldTotp.Valid {
			plain, err := h.vault.Open(oldTotp.String, oldKeyID, name)
			if err == nil {
				var s string
				if s, err = h.vault.Seal(plain, name); err == nil {
					sets = append(sets, fmt.Sprintf("TotpCipher=$%d", len(args)+1))
					args = append(args, s)
				}
			}
			if err != nil {
				errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
				return
			}
		}
	}
	args = append(args, name)
	if _, err := h.db.Exec("UPDATE Credentials SET "+strings.Join(sets, ",")+fmt.Sprintf(" WHERE Name=$%d", len(args)), args...); err != nil {
		dbErr(w, err)
		return
	}
	h.event("UPDATED", name, p.Username, map[string]any{"fields": changed})
	respond(w, http.StatusOK, map[string]any{"name": name})
}

// DELETE /admin/credentials/{name}
func (h *CredentialHandler) Delete(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var group string
	err := h.db.QueryRow(`SELECT GroupName FROM Credentials WHERE Name=$1`, name).Scan(&group)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such credential")
		return
	}
	if err != nil {
		dbErr(w, err)
		return
	}
	if h.forbidGroup(w, p, group) {
		return
	}
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`DELETE FROM Credentials WHERE Name=$1`, name)
	eventTx(x, h.now(), "DELETED", name, p.Username, map[string]any{"group": group})
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// POST /admin/credentials/{name}/reveal   {"purpose":"reveal"|"copy"}
func (h *CredentialHandler) Reveal(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var req struct {
		Purpose string `json:"purpose"`
	}
	_ = json.NewDecoder(r.Body).Decode(&req)

	var group, cipher, keyID string
	err := h.db.QueryRow(`SELECT GroupName,PasswordCipher,KeyId FROM Credentials WHERE Name=$1`, name).Scan(&group, &cipher, &keyID)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such credential")
		return
	}
	if err != nil {
		dbErr(w, err)
		return
	}
	if !p.CanSeeGroup(group) {
		h.event("REVEAL_DENIED", name, p.Username, map[string]any{"reason": "group"})
		errResp(w, http.StatusForbidden, "FORBIDDEN", "You have no access to the credential group "+group)
		return
	}
	plain, err := h.vault.Open(cipher, keyID, name)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
		return
	}
	kind := "REVEALED"
	if req.Purpose == "copy" {
		kind = "COPIED"
	}
	h.event(kind, name, p.Username, nil)
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, map[string]any{"password": plain})
}

// RevealDenied records a refused reveal for a user who lacks the permission. It is called by the
// route wrapper before the request is turned away.
func (h *CredentialHandler) RevealDenied(name, actor string) {
	var one int
	if h.db.QueryRow(`SELECT 1 FROM Credentials WHERE Name=$1`, name).Scan(&one) == nil {
		h.event("REVEAL_DENIED", name, actor, map[string]any{"reason": "permission"})
	}
}

// GET /admin/credentials/{name}/events
func (h *CredentialHandler) Events(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var group string
	if err := h.db.QueryRow(`SELECT GroupName FROM Credentials WHERE Name=$1`, name).Scan(&group); err == nil && !p.CanSeeGroup(group) {
		errResp(w, http.StatusForbidden, "FORBIDDEN", "You have no access to the credential group "+group)
		return
	}
	rows, err := h.db.Query(`SELECT At,Kind,Actor,COALESCE(Detail,'') FROM CredentialEvents WHERE Credential=$1 ORDER BY At DESC LIMIT 500`, name)
	if err != nil {
		dbErr(w, err)
		return
	}
	defer rows.Close()
	list := []map[string]any{}
	for rows.Next() {
		var at time.Time
		var kind, actor, detail string
		if rows.Scan(&at, &kind, &actor, &detail) == nil {
			e := map[string]any{"at": at.UTC().Format(time.RFC3339), "kind": kind, "actor": actor}
			if detail != "" {
				var d any
				if json.Unmarshal([]byte(detail), &d) == nil {
					e["detail"] = d
				}
			}
			list = append(list, e)
		}
	}
	respond(w, http.StatusOK, map[string]any{"events": list})
}

// POST /admin/credentials/import   {"rows":[...], "replaceExisting":false, "dryRun":false}
func (h *CredentialHandler) Import(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	var req struct {
		Rows            []credIn `json:"rows"`
		ReplaceExisting bool     `json:"replaceExisting"`
		DryRun          bool     `json:"dryRun"`
	}
	dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8<<20))
	if err := dec.Decode(&req); err != nil {
		errResp(w, http.StatusBadRequest, "INVALID_JSON", err.Error())
		return
	}
	if len(req.Rows) == 0 {
		errResp(w, http.StatusUnprocessableEntity, "NO_ROWS", "The file holds no credentials")
		return
	}

	type rowErr struct {
		Row     int    `json:"row"`
		Name    string `json:"name"`
		Message string `json:"message"`
	}
	var errs []rowErr
	exps := make([]sql.NullTime, len(req.Rows))
	existing := map[string]bool{}
	seen := map[string]int{}
	for i, c := range req.Rows {
		if first, dup := seen[c.Name]; dup && c.Name != "" {
			errs = append(errs, rowErr{i + 1, c.Name, fmt.Sprintf("The name appears twice in the file (first on row %d)", first)})
			continue
		}
		seen[c.Name] = i + 1
		exp, msg := c.check(h)
		if msg == "" && !p.CanSeeGroup(c.Group) {
			msg = "You have no access to the credential group " + c.Group
		}
		if msg == "" {
			var ex bool
			_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM Credentials WHERE Name=$1)`, c.Name).Scan(&ex)
			existing[c.Name] = ex
			if ex && !req.ReplaceExisting {
				msg = "A credential with that name exists already (tick Replace existing to update it)"
			}
		}
		if msg != "" {
			errs = append(errs, rowErr{i + 1, c.Name, msg})
			continue
		}
		exps[i] = exp
	}
	if len(errs) > 0 {
		respond(w, http.StatusUnprocessableEntity, map[string]any{"error": "IMPORT_ERRORS", "message": fmt.Sprintf("%d row(s) have errors; nothing was saved", len(errs)), "errors": errs})
		return
	}
	if req.DryRun {
		created, replaced := 0, 0
		for _, c := range req.Rows {
			if existing[c.Name] {
				replaced++
			} else {
				created++
			}
		}
		respond(w, http.StatusOK, map[string]any{"dryRun": true, "wouldCreate": created, "wouldReplace": replaced})
		return
	}

	now := h.now().UTC()
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	created, replaced := 0, 0
	for i := range req.Rows {
		c := req.Rows[i]
		pw, totp, err := h.seal(&c)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt a password")
			return
		}
		if existing[c.Name] {
			x.exec(`UPDATE Credentials SET GroupName=$2,Username=$3,PasswordCipher=$4,TotpCipher=$5,KeyId=$6,SiteUrl=$7,Notes=$8,ExpiresOn=$9,
				Version=Version+1,UpdatedAt=$10,UpdatedBy=$11 WHERE Name=$1`,
				c.Name, c.Group, c.Username, pw, totp, h.vault.KeyID(), nullText(c.SiteURL), nullText(c.Notes), exps[i], now, p.Username)
			eventTx(x, now, "UPDATED", c.Name, p.Username, map[string]any{"fields": []string{"import"}})
			replaced++
		} else {
			x.exec(`INSERT INTO Credentials(Name,GroupName,Username,PasswordCipher,TotpCipher,KeyId,SiteUrl,Notes,ExpiresOn,UpdatedAt,UpdatedBy)
				VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)`,
				c.Name, c.Group, c.Username, pw, totp, h.vault.KeyID(), nullText(c.SiteURL), nullText(c.Notes), exps[i], now, p.Username)
			eventTx(x, now, "CREATED", c.Name, p.Username, map[string]any{"group": c.Group, "import": true})
			created++
		}
	}
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"created": created, "replaced": replaced})
}

// ── Rekey ────────────────────────────────────────────────────────────────────

// VerifyAll opens one row per key id, so a start-up with the wrong key is noticed at once.
func VerifyAll(db *sql.DB, v *vault.Vault) error {
	if v.IsLocked() {
		return nil
	}
	rows, err := db.Query(`SELECT DISTINCT ON (KeyId) Name,PasswordCipher,KeyId FROM Credentials ORDER BY KeyId,Name`)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var name, cipher, keyID string
		if err := rows.Scan(&name, &cipher, &keyID); err != nil {
			return err
		}
		if _, err := v.Open(cipher, keyID, name); err != nil {
			return fmt.Errorf("the stored credentials do not open with the master key (key id %q): %w", keyID, err)
		}
	}
	return rows.Err()
}

// Rekey re-seals every row sealed with another key under the current key. It returns the number
// of credentials changed.
func Rekey(db *sql.DB, v *vault.Vault) (int, error) {
	if v.IsLocked() {
		return 0, vault.ErrLocked
	}
	rows, err := db.Query(`SELECT Name,PasswordCipher,TotpCipher,KeyId FROM Credentials WHERE KeyId<>$1`, v.KeyID())
	if err != nil {
		return 0, err
	}
	type row struct {
		name, pw, keyID string
		totp            sql.NullString
	}
	var todo []row
	for rows.Next() {
		var x row
		if err := rows.Scan(&x.name, &x.pw, &x.totp, &x.keyID); err != nil {
			rows.Close()
			return 0, err
		}
		todo = append(todo, x)
	}
	rows.Close()

	tx, err := db.Begin()
	if err != nil {
		return 0, err
	}
	defer tx.Rollback()
	for _, x := range todo {
		pw, err := v.Open(x.pw, x.keyID, x.name)
		if err != nil {
			return 0, fmt.Errorf("credential %s: %w", x.name, err)
		}
		newPw, err := v.Seal(pw, x.name)
		if err != nil {
			return 0, err
		}
		newTotp := sql.NullString{}
		if x.totp.Valid {
			t, err := v.Open(x.totp.String, x.keyID, x.name)
			if err != nil {
				return 0, fmt.Errorf("credential %s: %w", x.name, err)
			}
			s, err := v.Seal(t, x.name)
			if err != nil {
				return 0, err
			}
			newTotp = sql.NullString{String: s, Valid: true}
		}
		if _, err := tx.Exec(`UPDATE Credentials SET PasswordCipher=$1,TotpCipher=$2,KeyId=$3 WHERE Name=$4`,
			newPw, newTotp, v.KeyID(), x.name); err != nil {
			return 0, err
		}
	}
	return len(todo), tx.Commit()
}

// ── Settings ─────────────────────────────────────────────────────────────────

// GET /admin/settings
func (h *CredentialHandler) ListSettings(w http.ResponseWriter, r *http.Request) {
	rows, err := h.db.Query(`SELECT Name,Value,COALESCE(Notes,''),UpdatedAt,UpdatedBy FROM Settings ORDER BY Name`)
	if err != nil {
		dbErr(w, err)
		return
	}
	defer rows.Close()
	list := []map[string]any{}
	for rows.Next() {
		var name, value, notes, by string
		var at time.Time
		if rows.Scan(&name, &value, &notes, &at, &by) == nil {
			list = append(list, map[string]any{"name": name, "value": value, "notes": notes, "updatedAt": at.UTC().Format(time.RFC3339), "updatedBy": by})
		}
	}
	respond(w, http.StatusOK, map[string]any{"settings": list})
}

func looksSecret(s string) bool {
	for _, p := range secretPatterns {
		if p.MatchString(s) {
			return true
		}
	}
	return false
}

// PUT /admin/settings/{name}
func (h *CredentialHandler) PutSetting(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var req struct {
		Value string `json:"value"`
		Notes string `json:"notes"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if !nameRE.MatchString(name) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_NAME", nameRule)
		return
	}
	if looksSecret(req.Value) || looksSecret(req.Notes) {
		errResp(w, http.StatusUnprocessableEntity, "LOOKS_LIKE_A_SECRET", "This value looks like a password or key. Settings are shown in clear: store it as a credential instead")
		return
	}
	if _, err := h.db.Exec(`INSERT INTO Settings(Name,Value,Notes,UpdatedAt,UpdatedBy) VALUES($1,$2,$3,$4,$5)
		ON CONFLICT (Name) DO UPDATE SET Value=EXCLUDED.Value,Notes=EXCLUDED.Notes,UpdatedAt=EXCLUDED.UpdatedAt,UpdatedBy=EXCLUDED.UpdatedBy`,
		name, noNUL(req.Value), nullText(noNUL(req.Notes)), h.now().UTC(), p.Username); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"name": name})
}

// DELETE /admin/settings/{name}
func (h *CredentialHandler) DeleteSetting(w http.ResponseWriter, r *http.Request) {
	res, err := h.db.Exec(`DELETE FROM Settings WHERE Name=$1`, chi.URLParam(r, "name"))
	if err != nil {
		dbErr(w, err)
		return
	}
	if c, _ := res.RowsAffected(); c == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such setting")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
