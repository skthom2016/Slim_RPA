package handlers

import (
	"database/sql"
	"fmt"
	"net/http"

	"github.com/go-chi/chi/v5"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/vault"
)

// ── Group page and variables ─────────────────────────────────────────────────

type variableOut struct {
	Name     string  `json:"name"`
	FullName string  `json:"fullName"`
	Value    *string `json:"value"`
	Secret *string `json:"secret"`
}

func (h *CredentialHandler) groupVariables(group string, number int) ([]variableOut, error) {
	rows, err := h.db.Query(`SELECT VarName,PlainValue,SecretName FROM GroupVariables WHERE GroupName=$1 ORDER BY VarName`, group)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []variableOut{}
	for rows.Next() {
		var v variableOut
		var val, sec sql.NullString
		if err := rows.Scan(&v.Name, &val, &sec); err != nil {
			return nil, err
		}
		v.FullName = fullVarName(number, v.Name)
		if val.Valid {
			s := val.String
			v.Value = &s
		}
		if sec.Valid {
			s := sec.String
			v.Secret = &s
		}
		out = append(out, v)
	}
	return out, rows.Err()
}

// GET /admin/credential-groups/{name}
func (h *CredentialHandler) GetGroup(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var desc sql.NullString
	var number int
	err := h.db.QueryRow(`SELECT Description,GroupNo FROM CredentialGroups WHERE GroupName=$1`, name).Scan(&desc, &number)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such group")
		return
	}
	if err != nil {
		dbErr(w, err)
		return
	}
	if h.forbidGroup(w, p, name) {
		return
	}
	scripts := []string{}
	if sr, err := h.db.Query(`SELECT ArtifactId FROM CredentialGroupAccess WHERE GroupName=$1 ORDER BY ArtifactId`, name); err == nil {
		for sr.Next() {
			var a string
			if sr.Scan(&a) == nil {
				scripts = append(scripts, a)
			}
		}
		sr.Close()
	}
	vars, err := h.groupVariables(name, number)
	if err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"name": name, "number": number, "prefix": fmt.Sprintf("GP%d_", number), "description": desc.String, "scripts": scripts, "variables": vars})
}

// PUT /admin/credential-groups/{name}/variables   {"variables":[{"name","value"?,"secret"?}]}
// The list replaces the variables of the group in one change.
func (h *CredentialHandler) PutVariables(w http.ResponseWriter, r *http.Request) {
	p := auth.PrincipalFrom(r.Context())
	name := chi.URLParam(r, "name")
	var req struct {
		Variables []struct {
			Name   string  `json:"name"`
			Value  *string `json:"value"`
			Secret *string `json:"secret"`
		} `json:"variables"`
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
	if h.forbidGroup(w, p, name) {
		return
	}
	bad := func(msg string) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_VARIABLE", msg)
	}
	seen := map[string]bool{}
	for _, v := range req.Variables {
		if !varNameRE.MatchString(v.Name) {
			bad(fmt.Sprintf("Variable %q: %s", v.Name, varNameRule))
			return
		}
		if seen[v.Name] {
			bad("The variable " + v.Name + " appears twice")
			return
		}
		seen[v.Name] = true
		if (v.Value == nil) == (v.Secret == nil) {
			bad("The variable " + v.Name + " needs either a value or a secret, not both and not neither")
			return
		}
		if v.Secret != nil {
			var ok bool
			_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM Secrets WHERE Name=$1)`, *v.Secret).Scan(&ok)
			if !ok {
				bad("The variable " + v.Name + " uses the secret " + *v.Secret + ", which does not exist")
				return
			}
		}
	}
	now := h.now().UTC()
	x := newTxDB(h.db)
	if x == nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", "Could not start a change")
		return
	}
	defer x.tx.Rollback()
	x.exec(`DELETE FROM GroupVariables WHERE GroupName=$1`, name)
	names := []string{}
	for _, v := range req.Variables {
		var val, sec sql.NullString
		if v.Value != nil {
			val = sql.NullString{String: noNUL(*v.Value), Valid: true}
		} else {
			sec = sql.NullString{String: *v.Secret, Valid: true}
		}
		x.exec(`INSERT INTO GroupVariables(GroupName,VarName,PlainValue,SecretName) VALUES($1,$2,$3,$4)`, name, v.Name, val, sec)
		names = append(names, v.Name)
	}
	eventTx(x, now, "VARIABLES_CHANGED", name, p.Username, map[string]any{"variables": names})
	if err := x.commit(); err != nil {
		dbErr(w, err)
		return
	}
	respond(w, http.StatusOK, map[string]any{"name": name, "count": len(req.Variables)})
}

// ── Rekey ────────────────────────────────────────────────────────────────────

// VerifyAll opens one row per key id, so a start-up with the wrong key is noticed at once.
func VerifyAll(db *sql.DB, v *vault.Vault) error {
	if v.IsLocked() {
		return nil
	}
	rows, err := db.Query(`SELECT DISTINCT ON (KeyId) Name,ValueCipher,KeyId FROM Secrets ORDER BY KeyId,Name`)
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
			return fmt.Errorf("the stored secrets do not open with the master key (key id %q): %w", keyID, err)
		}
	}
	return rows.Err()
}

// Rekey re-seals every secret sealed with another key under the current key. It returns the number
// of secrets changed.
func Rekey(db *sql.DB, v *vault.Vault) (int, error) {
	if v.IsLocked() {
		return 0, vault.ErrLocked
	}
	rows, err := db.Query(`SELECT Name,ValueCipher,KeyId FROM Secrets WHERE KeyId<>$1`, v.KeyID())
	if err != nil {
		return 0, err
	}
	type row struct{ name, cipher, keyID string }
	var todo []row
	for rows.Next() {
		var x row
		if err := rows.Scan(&x.name, &x.cipher, &x.keyID); err != nil {
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
		plain, err := v.Open(x.cipher, x.keyID, x.name)
		if err != nil {
			return 0, fmt.Errorf("secret %s: %w", x.name, err)
		}
		sealed, err := v.Seal(plain, x.name)
		if err != nil {
			return 0, err
		}
		if _, err := tx.Exec(`UPDATE Secrets SET ValueCipher=$1,KeyId=$2 WHERE Name=$3`, sealed, v.KeyID(), x.name); err != nil {
			return 0, err
		}
	}
	return len(todo), tx.Commit()
}
