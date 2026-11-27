package handlers

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"net/http"
	"regexp"
	"strconv"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"

	"tracto/orchestrator/internal/vault"
)

// RunCredentialHandler gives a running job the environment variables its script names.
type RunCredentialHandler struct {
	db    *sql.DB
	vault *vault.Vault
	now   func() time.Time
	sleep func(time.Duration)
}

func NewRunCredentialHandler(db *sql.DB, v *vault.Vault) *RunCredentialHandler {
	return &RunCredentialHandler{db: db, vault: v, now: time.Now, sleep: time.Sleep}
}

// envRefRE finds ${env:NAME} in a script, where NAME may hold ${param.x} parts.
var envRefRE = regexp.MustCompile(`\$\{env:((?:[^${}]|\$\{[^}]*\})+)\}`)

// fullNameRE splits a variable name a script reads: GP, the group number, an underscore, the name.
var fullNameRE = regexp.MustCompile(`^GP(\d+)_([A-Za-z_][A-Za-z0-9_]{0,99})$`)

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
		b.WriteString(`[A-Za-z0-9_]+`)
		last = loc[1]
	}
	b.WriteString(regexp.QuoteMeta(ref[last:]))
	b.WriteString("$")
	return regexp.MustCompile(b.String())
}

// referencedMatchers returns a matcher per ${env:...} reference found in the script text.
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
	if kind != "env" {
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

// POST /jobs/{runId}/env   {"workerId":"...","names":["A","B"]}
func (h *RunCredentialHandler) Env(w http.ResponseWriter, r *http.Request) {
	runID := chi.URLParam(r, "runId")
	var req struct {
		WorkerID string   `json:"workerId"`
		Names    []string `json:"names"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	run, ok := h.load(w, runID, req.WorkerID, req.Names, "env")
	if !ok {
		return
	}
	matchers := referencedMatchers(run.script, envRefRE)

	deny := func(name, reason, msg string) {
		h.event("READ_DENIED", name, runID, req.WorkerID, map[string]any{"reason": reason})
		errResp(w, http.StatusForbidden, "VARIABLE_DENIED", fmt.Sprintf("Variable %s: %s", name, msg))
	}
	type result struct {
		Value   string `json:"value"`
		Secret  bool   `json:"secret"`
		Warning string `json:"warning,omitempty"`
	}
	out := map[string]result{}
	var warnings []string
	var secretReads []string
	for _, name := range req.Names {
		m := fullNameRE.FindStringSubmatch(name)
		if m == nil {
			h.event("READ_DENIED", name, runID, req.WorkerID, map[string]any{"reason": "unknown"})
			errResp(w, http.StatusNotFound, "UNKNOWN_VARIABLE",
				fmt.Sprintf("Variable %s does not exist: variable names start with GP<group number>_, as shown on the group page", name))
			return
		}
		if !entitled(matchers, name) {
			deny(name, "not_in_script", "the script does not name this variable")
			return
		}
		groupNo, _ := strconv.Atoi(m[1])
		short := m[2]
		var group string
		var allowed bool
		err := h.db.QueryRow(`SELECT g.GroupName,
				EXISTS(SELECT 1 FROM CredentialGroupAccess a WHERE a.GroupName=g.GroupName AND (a.ArtifactId='*' OR a.ArtifactId=$2))
			FROM CredentialGroups g WHERE g.GroupNo=$1`, groupNo, run.mainArtifact).Scan(&group, &allowed)
		if err == sql.ErrNoRows {
			h.event("READ_DENIED", name, runID, req.WorkerID, map[string]any{"reason": "unknown"})
			errResp(w, http.StatusNotFound, "UNKNOWN_VARIABLE", fmt.Sprintf("Variable %s does not exist: there is no group %d", name, groupNo))
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		if !allowed {
			deny(name, "group", fmt.Sprintf("the script %s is not allowed in group %d (%s)", run.mainArtifact, groupNo, group))
			return
		}
		type found struct{ plain, sec sql.NullString }
		var f found
		err = h.db.QueryRow(`SELECT PlainValue,SecretName FROM GroupVariables WHERE GroupName=$1 AND VarName=$2`, group, short).Scan(&f.plain, &f.sec)
		if err == sql.ErrNoRows {
			h.event("READ_DENIED", name, runID, req.WorkerID, map[string]any{"reason": "unknown"})
			errResp(w, http.StatusNotFound, "UNKNOWN_VARIABLE", fmt.Sprintf("Variable %s does not exist in group %d (%s)", name, groupNo, group))
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		if f.plain.Valid {
			out[name] = result{Value: f.plain.String}
			continue
		}
		if h.vault.IsLocked() {
			errResp(w, http.StatusServiceUnavailable, "CREDENTIAL_STORE_LOCKED", "The secret store is locked: set "+vault.EnvKey+" for the orchestrator")
			return
		}
		var kind, cipher, keyID string
		var exp sql.NullTime
		err = h.db.QueryRow(`SELECT Kind,ValueCipher,KeyId,ExpiresOn FROM Secrets WHERE Name=$1`, f.sec.String).Scan(&kind, &cipher, &keyID, &exp)
		if err == sql.ErrNoRows {
			errResp(w, http.StatusNotFound, "UNKNOWN_VARIABLE", fmt.Sprintf("Variable %s uses the secret %s, which does not exist", name, f.sec.String))
			return
		}
		if err != nil {
			dbErr(w, err)
			return
		}
		plain, err := h.vault.Open(cipher, keyID, f.sec.String)
		if err != nil {
			errResp(w, http.StatusInternalServerError, "OPEN_ERROR", err.Error())
			return
		}
		if kind == "totp" {
			if plain, err = vault.CurrentTOTP(plain, h.now, h.sleep); err != nil {
				errResp(w, http.StatusInternalServerError, "TOTP_ERROR", err.Error())
				return
			}
		}
		res := result{Value: plain, Secret: true}
		if expiryStatus(exp, h.now()) == "expired" {
			msg := fmt.Sprintf("Secret %s (variable %s) expired on %s; it may no longer work", f.sec.String, name, exp.Time.Format("2006-01-02"))
			res.Warning = msg
			warnings = append(warnings, msg)
		}
		out[name] = res
		secretReads = append(secretReads, f.sec.String)
	}
	// Everything checked: record the reads and the warnings, then answer.
	for _, s := range secretReads {
		h.event("READ_BY_RUN", s, runID, req.WorkerID, map[string]any{"reason": "run"})
	}
	for _, m := range warnings {
		h.warn(runID, req.WorkerID, m)
	}
	w.Header().Set("Cache-Control", "no-store")
	respond(w, http.StatusOK, map[string]any{"values": out})
}
