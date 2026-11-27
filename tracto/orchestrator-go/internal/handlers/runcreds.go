package handlers

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"

	"tracto/orchestrator/internal/vault"
)

// RunCredentialHandler gives a running job the credentials and settings its script names.
type RunCredentialHandler struct {
	db    *sql.DB
	vault *vault.Vault
	now   func() time.Time
	sleep func(time.Duration)
}

func NewRunCredentialHandler(db *sql.DB, v *vault.Vault) *RunCredentialHandler {
	return &RunCredentialHandler{db: db, vault: v, now: time.Now, sleep: time.Sleep}
}

// credRefRE finds ${credential:NAME.field} in a script, where NAME may hold ${param.x} parts.
var credRefRE = regexp.MustCompile(`\$\{credential:((?:[^${}]|\$\{[^}]*\})+)\.(username|password|totp)\}`)

// settingRefRE finds ${setting:NAME}.
var settingRefRE = regexp.MustCompile(`\$\{setting:((?:[^${}]|\$\{[^}]*\})+)\}`)

var paramPartRE = regexp.MustCompile(`\$\{[^}]*\}`)

// refMatcher turns the name of a reference into a pattern: the literal parts must match as
// written, and each ${...} part stands for any run of name characters. A name asked for is
// allowed when it fully matches the pattern of any reference in the script.
func refMatcher(ref string) *regexp.Regexp {
	var b strings.Builder
	b.WriteString("^")
	last := 0
	for _, loc := range paramPartRE.FindAllStringIndex(ref, -1) {
		b.WriteString(regexp.QuoteMeta(ref[last:loc[0]]))
		b.WriteString(`[a-z0-9.-]+`)
		last = loc[1]
	}
	b.WriteString(regexp.QuoteMeta(ref[last:]))
	b.WriteString("$")
	return regexp.MustCompile(b.String())
}

// ReferencedNames returns a matcher per credential reference (field "") or setting reference found
// in the script text.
func referencedMatchers(script string, re *regexp.Regexp) []*regexp.Regexp {
	var out []*regexp.Regexp
	for _, m := range re.FindAllStringSubmatch(script, -1) {
		out = append(out, refMatcher(m[1]))
	}
	return out
}

func entitled(matchers []*regexp.Regexp, name string) bool {
	for _, m := range matchers {
		if m.MatchString(name) {
			return true
		}
	}
	return false
}

type activeRun struct {
	state, worker, artifact, mainArtifact, script string
}

// load returns the run, its script text and the artifact that group access is checked against (the
// main script when the run is one segment of it). It writes the reply and returns false when the
// run is unknown, not running, or not leased to workerID.
func (h *RunCredentialHandler) load(w http.ResponseWriter, runID, workerID string, names []string, kind string) (*activeRun, bool) {
	var ar activeRun
	var worker, ver, artifact sql.NullString
	err := h.db.QueryRow(`SELECT State,WorkerId,ScriptArtifactId,ScriptVersion FROM RunRecords WHERE Id=$1`, runID).
		Scan(&ar.state, &worker, &artifact, &ver)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "No such run")
		return nil, false
	}
	if err != nil {
		dbErr(w, err)
		return nil, false
	}
	ar.worker, ar.artifact = worker.String, artifact.String
	if ar.state != "Running" || workerID == "" || ar.worker != workerID {
		h.denyAll(runID, workerID, names, kind, "the run is not running on this worker")
		errResp(w, http.StatusForbidden, "RUN_NOT_ACTIVE", "The run is not running on this worker")
		return nil, false
	}
	var payload []byte
	_ = h.db.QueryRow(`SELECT Payload FROM ArtifactContents WHERE ArtifactId=$1 AND Version=$2`, ar.artifact, ver.String).Scan(&payload)
	ar.script = string(payload)
	ar.mainArtifact = ar.artifact
	var parent sql.NullString
	_ = h.db.QueryRow(`SELECT ParentArtifactId FROM ArtifactRecords WHERE ArtifactId=$1 AND Version=$2`, ar.artifact, ver.String).Scan(&parent)
	if parent.Valid && parent.String != "" {
		ar.mainArtifact = parent.String
	}
	return &ar, true
}

func (h *RunCredentialHandler) denyAll(runID, workerID string, names []string, kind, reason string) {
	if kind != "credential" {
		return
	}
	for _, n := range names {
		h.event("READ_DENIED", n, runID, workerID, map[string]any{"reason": reason})
	}
}

func (h *RunCredentialHandler) event(kind, credential, runID, workerID string, detail map[string]any) {
	d := sql.NullString{}
	if detail != nil {
		b, _ := json.Marshal(detail)
		d = sql.NullString{String: string(b), Valid: true}
	}
	_, _ = h.db.Exec(`INSERT INTO CredentialEvents(EventId,At,Kind,Credential,Actor,Detail) VALUES($1,$2,$3,$4,$5,$6)`,
		uuid.NewString(), h.now().UTC(), kind, credential, "run:"+runID+"@"+workerID, d)
}

func (h *RunCredentialHandler) warn(runID, workerID, msg string) {
	now := h.now().UTC()
	_, _ = h.db.Exec(`INSERT INTO RunLogs(Id,RunId,WorkerId,Level,Message,OccurredAt,ReceivedAt) VALUES($1,$2,$3,'Warning',$4,$5,$5)`,
		uuid.NewString(), runID, workerID, msg, now)
}

// POST /jobs/{runId}/credentials   {"workerId":"...","names":["a","b"]}
func (h *RunCredentialHandler) Credentials(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		WorkerID string   `json:"workerId"`
		Names    []string `json:"names"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if h.vault.IsLocked() {
		errResp(w, http.StatusServiceUnavailable, "CREDENTIAL_STORE_LOCKED", "The credential store is locked: set "+vault.EnvKey+" for the orchestrator")
		return
	}
	run, ok := h.load(w, runID, req.WorkerID, req.Names, "credential")
	if !ok {
		return
	}
	matchers := referencedMatchers(run.script, credRefRE)

	deny := func(name, reason, msg string) {
		h.event("READ_DENIED", name, runID, req.WorkerID, map[string]any{"reason": reason})
		errResp(w, http.StatusForbidden, "CREDENTIAL_DENIED", fmt.Sprintf("Credential %s: %s", name, msg))
	}
	type result struct {
		Username string `json:"username"`
		Password string `json:"password"`
		Totp     string `json:"totp,omitempty"`
		Warning  string `json:"warning,omitempty"`
	}
	out := map[string]result{}
	var warnings []string
	for _, name := range req.Names {
		if !nameRE.MatchString(name) {
			deny(name, "name", "this is not a valid credential name")
			return
		}
		if !entitled(matchers, name) {
			deny(name, "not_in_script", "the script does not name this credential")
			return
		}
		var group, user, pwCipher, keyID string
		var totpCipher sql.NullString
		var exp sql.NullTime
		err := h.db.QueryRow(`SELECT GroupName,Username,PasswordCipher,TotpCipher,KeyId,ExpiresOn FROM Credentials WHERE Name=$1`, name).
			Scan(&group, &user, &pwCipher, &totpCipher, &keyID, &exp)
		if err == sql.ErrNoRows {
			deny(name, "unknown", "there is no such credential")
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		var allowed bool
		_ = h.db.QueryRow(`SELECT EXISTS(SELECT 1 FROM CredentialGroupAccess WHERE GroupName=$1 AND (ArtifactId='*' OR ArtifactId=$2))`,
			group, run.mainArtifact).Scan(&allowed)
		if !allowed {
			deny(name, "group", "the script "+run.mainArtifact+" may not read the credentials of group "+group)
			return
		}
		pw, err := h.vault.Open(pwCipher, keyID, name)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
			return
		}
		res := result{Username: user, Password: pw}
		if totpCipher.Valid {
			key, err := h.vault.Open(totpCipher.String, keyID, name)
			if err != nil {
				errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
				return
			}
			if res.Totp, err = vault.CurrentTOTP(key, h.now, h.sleep); err != nil {
				errResp(w, http.StatusInternalServerError, "TOTP_ERROR", err.Error())
				return
			}
		}
		if expiryStatus(exp, h.now()) == "expired" {
			msg := fmt.Sprintf("Credential %s expired on %s; it may no longer work", name, exp.Time.Format("2006-01-02"))
			res.Warning = msg
			warnings = append(warnings, msg)
		}
		out[name] = res
	}
	// Everything checked: record the reads and the warnings, then answer.
	for name := range out {
		h.event("READ_BY_RUN", name, runID, req.WorkerID, map[string]any{"reason": "run"})
	}
	for _, m := range warnings {
		h.warn(runID, req.WorkerID, m)
	}
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, map[string]any{"credentials": out})
}

// POST /jobs/{runId}/settings   {"workerId":"...","names":["a"]}
func (h *RunCredentialHandler) Settings(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		WorkerID string   `json:"workerId"`
		Names    []string `json:"names"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	run, ok := h.load(w, runID, req.WorkerID, req.Names, "setting")
	if !ok {
		return
	}
	matchers := referencedMatchers(run.script, settingRefRE)
	out := map[string]string{}
	for _, name := range req.Names {
		if !nameRE.MatchString(name) || !entitled(matchers, name) {
			errResp(w, http.StatusForbidden, "SETTING_DENIED", fmt.Sprintf("Setting %s: the script does not name this setting", name))
			return
		}
		var v string
		err := h.db.QueryRow(`SELECT Value FROM Settings WHERE Name=$1`, name).Scan(&v)
		if err == sql.ErrNoRows {
			errResp(w, http.StatusNotFound, "SETTING_NOT_FOUND", fmt.Sprintf("Setting %s does not exist", name))
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		out[name] = v
	}
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, map[string]any{"settings": out})
}
