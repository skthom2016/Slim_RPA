package handlers

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/vault"
)

func nullText(s string) sql.NullString { return sql.NullString{String: s, Valid: s != ""} }

// secretIn is one secret as the API receives it.
type secretIn struct {
	Name        string  `json:"name"`
	Kind        string  `json:"kind"`
	Value       string  `json:"value"`
	Description string  `json:"description"`
	ExpiresOn   *string `json:"expiresOn"`
}

// check validates what every create and import row needs; it returns the kind, the value to seal
// and the expiry, or a message.
func (c *secretIn) check() (kind, value string, exp sql.NullTime, msg string) {
	if !nameRE.MatchString(c.Name) {
		return "", "", exp, nameRule
	}
	kind = c.Kind
	if kind == "" {
		kind = "text"
	}
	if kind != "text" && kind != "totp" {
		return "", "", exp, "The kind is text or totp"
	}
	if c.Value == "" {
		return "", "", exp, "The value is missing"
	}
	value = c.Value
	if kind == "totp" {
		k, err := vault.NormalizeTOTPKey(c.Value)
		if err != nil {
			return "", "", exp, err.Error()
		}
		value = k
	}
	var e any
	if c.ExpiresOn != nil {
		e = *c.ExpiresOn
	}
	exp, err := parseExpiry(e)
	if err != nil {
		return "", "", exp, err.Error()
	}
	return kind, value, exp, ""
}

// usedBy lists which variable of which group uses each secret.
func (h *CredentialHandler) usedBy(secret string) []map[string]any {
	out := []map[string]any{}
	q := `SELECT v.GroupName,g.GroupNo,v.VarName,v.SecretName FROM GroupVariables v JOIN CredentialGroups g ON g.GroupName=v.GroupName WHERE v.SecretName IS NOT NULL`
	var args []any
	if secret != "" {
		q += ` AND v.SecretName=$1`
		args = append(args, secret)
	}
	rows, err := h.db.Query(q+` ORDER BY g.GroupNo,v.VarName`, args...)
	if err != nil {
		return out
	}
	defer rows.Close()
	for rows.Next() {
		var g, v, s string
		var no int
		if rows.Scan(&g, &no, &v, &s) == nil {
			out = append(out, map[string]any{"group": g, "groupNumber": no, "variable": v, "fullName": fullVarName(no, v), "secret": s})
		}
	}
	return out
}

// GET /admin/secrets?q=   (never the values)
func (h *CredentialHandler) ListSecrets(w http.ResponseWriter, r *http.Request) {
	query := `SELECT Name,Kind,COALESCE(Description,''),ExpiresOn,Version,UpdatedAt,UpdatedBy FROM Secrets`
	var args []any
	if q := strings.ToLower(strings.TrimSpace(r.URL.Query().Get("q"))); q != "" {
		args = append(args, "%"+q+"%")
		n := argN(args[:len(args)-1])
		query += " WHERE lower(Name) LIKE " + n + " OR lower(COALESCE(Description,'')) LIKE " + n
	}
	rows, err := h.db.Query(query+" ORDER BY Name", args...)
	if err != nil {
		dbErr(w, err)
		return
	}
	type srow struct {
		name, kind, desc, by string
		exp                  sql.NullTime
		ver                  int
		at                   time.Time
	}
	var all []srow
	for rows.Next() {
		var x srow
		if err := rows.Scan(&x.name, &x.kind, &x.desc, &x.exp, &x.ver, &x.at, &x.by); err != nil {
			rows.Close()
			dbErr(w, err)
			return
		}
		all = append(all, x)
	}
	rows.Close()
	users := map[string][]map[string]any{}
	for _, u := range h.usedBy("") {
		sn, _ := u["secret"].(string)
		users[sn] = append(users[sn], map[string]any{"group": u["group"], "groupNumber": u["groupNumber"], "variable": u["variable"], "fullName": u["fullName"]})
	}
	now := h.now()
	list := []map[string]any{}
	var soon, expired int
	for _, x := range all {
		st := expiryStatus(x.exp, now)
		switch st {
		case "soon":
			soon++
		case "expired":
			expired++
		}
		ub := users[x.name]
		if ub == nil {
			ub = []map[string]any{}
		}
		list = append(list, map[string]any{
			"name": x.name, "kind": x.kind, "description": x.desc, "expiresOn": dateOrNil(x.exp), "status": st,
			"usedBy": ub, "version": x.ver, "updatedAt": x.at.UTC().Format(time.RFC3339), "updatedBy": x.by,
		})
	}
	respond(w, http.StatusOK, map[string]any{
		"secrets": list, "locked": h.vault.IsLocked(),
		"summary": map[string]int{"total": len(list), "soon": soon, "expired": expired},
	})
}

// POST /admin/secrets
func (h *CredentialHandler) CreateSecret(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	var in secretIn
	if !decodeJSON(w, r, &in) {
		return
	}
	kind, value, exp, msg := in.check()
	if msg != "" {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_SECRET", msg)
		return
	}
	sealed, err := h.vault.Seal(value, in.Name)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt the value")
		return
	}
	now := h.now().UTC()
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`INSERT INTO Secrets(Name,Kind,ValueCipher,KeyId,Description,ExpiresOn,UpdatedAt,UpdatedBy) VALUES($1,$2,$3,$4,$5,$6,$7,$8)`,
		in.Name, kind, sealed, h.vault.KeyID(), nullText(in.Description), exp, now, p.Username)
	eventTx(x, now, "CREATED", in.Name, p.Username, map[string]any{"kind": kind})
	if err := x.commit(); err != nil {
		if isUniqueViolation(err) {
			errResp(w, http.StatusConflict, "NAME_EXISTS", "A secret with that name exists already")
			return
		}
		dbErr(w, err)
		return
	}
	respond(w, http.StatusCreated, map[string]any{"name": in.Name, "kind": kind})
}

// PUT /admin/secrets/{name}   {"value"?,"description"?,"expiresOn"?}; a value left out is kept.
func (h *CredentialHandler) UpdateSecret(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var raw map[string]json.RawMessage
	if !decodeJSON(w, r, &raw) {
		return
	}
	var kind string
	err := h.db.QueryRow(`SELECT Kind FROM Secrets WHERE Name=$1`, name).Scan(&kind)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such secret")
		return
	}
	if err != nil {
		dbErr(w, err)
		return
	}
	sets := []string{"Version=Version+1", "UpdatedAt=$1", "UpdatedBy=$2"}
	args := []any{h.now().UTC(), p.Username}
	var changed []string
	add := func(col string, val any, field string) {
		args = append(args, val)
		sets = append(sets, fmt.Sprintf("%s=$%d", col, len(args)))
		changed = append(changed, field)
	}
	bad := func(msg string) { errResp(w, http.StatusUnprocessableEntity, "INVALID_SECRET", msg) }
	if v, ok := raw["value"]; ok {
		var s *string
		if json.Unmarshal(v, &s) != nil {
			bad("value must be text")
			return
		}
		if s != nil && *s != "" { // an empty value keeps the old one
			if h.locked(w) {
				return
			}
			plain := *s
			if kind == "totp" {
				k, err := vault.NormalizeTOTPKey(plain)
				if err != nil {
					bad(err.Error())
					return
				}
				plain = k
			}
			sealed, err := h.vault.Seal(plain, name)
			if err != nil {
				errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt the value")
				return
			}
			add("ValueCipher", sealed, "value")
			add("KeyId", h.vault.KeyID(), "keyId")
		}
	}
	if v, ok := raw["description"]; ok {
		var s *string
		if json.Unmarshal(v, &s) != nil {
			bad("description must be text")
			return
		}
		d := ""
		if s != nil {
			d = *s
		}
		add("Description", nullText(noNUL(d)), "description")
	}
	if v, ok := raw["expiresOn"]; ok {
		var anyv any
		_ = json.Unmarshal(v, &anyv)
		e, err := parseExpiry(anyv)
		if err != nil {
			bad(err.Error())
			return
		}
		add("ExpiresOn", e, "expiresOn")
	}
	args = append(args, name)
	if _, err := h.db.Exec("UPDATE Secrets SET "+strings.Join(sets, ",")+fmt.Sprintf(" WHERE Name=$%d", len(args)), args...); err != nil {
		dbErr(w, err)
		return
	}
	h.event("UPDATED", name, p.Username, map[string]any{"fields": changed})
	respond(w, http.StatusOK, map[string]any{"name": name})
}

// DELETE /admin/secrets/{name}   (refused while a variable uses it)
func (h *CredentialHandler) DeleteSecret(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var one int
	if err := h.db.QueryRow(`SELECT 1 FROM Secrets WHERE Name=$1`, name).Scan(&one); err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such secret")
		return
	} else if err != nil {
		dbErr(w, err)
		return
	}
	if used := h.usedBy(name); len(used) > 0 {
		parts := []string{}
		for _, u := range used {
			parts = append(parts, fmt.Sprint(u["fullName"])+" in group "+fmt.Sprint(u["group"]))
		}
		errResp(w, http.StatusConflict, "SECRET_IN_USE", "The secret is used by "+strings.Join(parts, ", ")+"; change those variables first")
		return
	}
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`DELETE FROM Secrets WHERE Name=$1`, name)
	eventTx(x, h.now(), "DELETED", name, p.Username, nil)
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// RevealSecret serves POST /admin/secrets/{name}/reveal and /copy; purpose is "reveal" or "copy".
func (h *CredentialHandler) RevealSecret(purpose string) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if h.locked(w) {
			return
		}
		p := auth.PrincipalFrom(r.Context())
		name := chi.URLParam(r, "name")
		var cipher, keyID string
		err := h.db.QueryRow(`SELECT ValueCipher,KeyId FROM Secrets WHERE Name=$1`, name).Scan(&cipher, &keyID)
		if err == sql.ErrNoRows {
			errResp(w, http.StatusNotFound, "NOT_FOUND", "No such secret")
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		plain, err := h.vault.Open(cipher, keyID, name)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
			return
		}
		kind := "REVEALED"
		if purpose == "copy" {
			kind = "COPIED"
		}
		h.event(kind, name, p.Username, nil)
		w.Header().Set("Cache-Control", "no-store")
		respond(w, http.StatusOK, map[string]any{"value": plain})
	}
}

// RevealDenied records a refused reveal for a user who lacks the permission. It is called by the
// route wrapper before the request is turned away.
func (h *CredentialHandler) RevealDenied(name, actor string) {
	var one int
	if h.db.QueryRow(`SELECT 1 FROM Secrets WHERE Name=$1`, name).Scan(&one) == nil {
		h.event("REVEAL_DENIED", name, actor, map[string]any{"reason": "permission"})
	}
}

// GET /admin/secrets/{name}/events   (also the events of a group name, for variable changes)
func (h *CredentialHandler) Events(w http.ResponseWriter, r *http.Request) {
	name := chi.URLParam(r, "name")
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

// POST /admin/secrets/import   {"rows":[{name,value,kind,description,expiresOn}], "replaceExisting":false, "dryRun":false}
func (h *CredentialHandler) ImportSecrets(w http.ResponseWriter, r *http.Request) {
	if h.locked(w) {
		return
	}
	p := auth.PrincipalFrom(r.Context())
	var req struct {
		Rows            []secretIn `json:"rows"`
		ReplaceExisting bool       `json:"replaceExisting"`
		DryRun          bool       `json:"dryRun"`
	}
	dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8<<20))
	if err := dec.Decode(&req); err != nil {
		errResp(w, http.StatusBadRequest, "INVALID_JSON", err.Error())
		return
	}
	if len(req.Rows) == 0 {
		errResp(w, http.StatusUnprocessableEntity, "NO_ROWS", "The file holds no secrets")
		return
	}

	type rowErr struct {
		Row     int    `json:"row"`
		Name    string `json:"name"`
		Message string `json:"message"`
	}
	var errs []rowErr
	kinds := make([]string, len(req.Rows))
	values := make([]string, len(req.Rows))
	exps := make([]sql.NullTime, len(req.Rows))
	existing := map[string]bool{}
	seen := map[string]int{}
	for i, c := range req.Rows {
		if first, dup := seen[c.Name]; dup && c.Name != "" {
			errs = append(errs, rowErr{i + 1, c.Name, fmt.Sprintf("The name appears twice in the file (first on row %d)", first)})
			continue
		}
		seen[c.Name] = i + 1
		kind, value, exp, msg := c.check()
		if msg == "" {
			var ex bool
			_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM Secrets WHERE Name=$1)`, c.Name).Scan(&ex)
			existing[c.Name] = ex
			if ex && !req.ReplaceExisting {
				msg = "A secret with that name exists already (tick Replace existing to update it)"
			}
		}
		if msg != "" {
			errs = append(errs, rowErr{i + 1, c.Name, msg})
			continue
		}
		kinds[i], values[i], exps[i] = kind, value, exp
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
	for i, c := range req.Rows {
		sealed, err := h.vault.Seal(values[i], c.Name)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "SEAL_ERROR", "Could not encrypt a value")
			return
		}
		if existing[c.Name] {
			x.exec(`UPDATE Secrets SET Kind=$2,ValueCipher=$3,KeyId=$4,Description=$5,ExpiresOn=$6,Version=Version+1,UpdatedAt=$7,UpdatedBy=$8 WHERE Name=$1`,
				c.Name, kinds[i], sealed, h.vault.KeyID(), nullText(c.Description), exps[i], now, p.Username)
			eventTx(x, now, "UPDATED", c.Name, p.Username, map[string]any{"fields": []string{"import"}})
			replaced++
		} else {
			x.exec(`INSERT INTO Secrets(Name,Kind,ValueCipher,KeyId,Description,ExpiresOn,UpdatedAt,UpdatedBy) VALUES($1,$2,$3,$4,$5,$6,$7,$8)`,
				c.Name, kinds[i], sealed, h.vault.KeyID(), nullText(c.Description), exps[i], now, p.Username)
			eventTx(x, now, "CREATED", c.Name, p.Username, map[string]any{"kind": kinds[i], "import": true})
			created++
		}
	}
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"created": created, "replaced": replaced})
}
