package server

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"sync"
	"testing"
	"time"

	"tracto/orchestrator/internal/handlers"
	"tracto/orchestrator/internal/vault"
)

// ── helpers ──────────────────────────────────────────────────────────────────

// groupBook remembers the number the server gave each group of a test, keyed by the test.
type groupBook struct {
	nos     map[string]int
	primary string
}

var (
	books   = map[*testing.T]*groupBook{}
	booksMu sync.Mutex
)

func (e *env) book() *groupBook {
	booksMu.Lock()
	defer booksMu.Unlock()
	b := books[e.t]
	if b == nil {
		b = &groupBook{nos: map[string]int{}}
		books[e.t] = b
		t := e.t
		t.Cleanup(func() { booksMu.Lock(); delete(books, t); booksMu.Unlock() })
	}
	return b
}

func (e *env) group(name string, scripts ...string) {
	e.t.Helper()
	out := e.must(http.StatusCreated, "POST", "/admin/credential-groups", map[string]any{"name": name, "description": "test", "scripts": scripts})
	no, _ := out["number"].(float64)
	if int(no) < 100 || out["prefix"] != fmt.Sprintf("GP%d_", int(no)) {
		e.t.Fatalf("a new group should come back with its number and prefix: %v", out)
	}
	b := e.book()
	booksMu.Lock()
	b.nos[name] = int(no)
	if b.primary == "" {
		b.primary = name
	}
	booksMu.Unlock()
}

// no is the number of a group made with e.group.
func (e *env) no(group string) int {
	b := e.book()
	booksMu.Lock()
	defer booksMu.Unlock()
	return b.nos[group]
}

// fnIn is the full name a script reads a variable of a group by.
func (e *env) fnIn(group, short string) string { return fmt.Sprintf("GP%d_%s", e.no(group), short) }

// fn is fnIn for the first group the test made.
func (e *env) fn(short string) string {
	b := e.book()
	booksMu.Lock()
	g := b.primary
	booksMu.Unlock()
	return e.fnIn(g, short)
}

func (e *env) secret(name, value string) {
	e.t.Helper()
	e.must(http.StatusCreated, "POST", "/admin/secrets", map[string]any{"name": name, "kind": "text", "value": value})
}

// vars replaces the variables of a group. Each entry is {name, value} or {name, secret}.
func (e *env) vars(group string, vs ...map[string]any) {
	e.t.Helper()
	e.must(http.StatusOK, "PUT", "/admin/credential-groups/"+url.PathEscape(group)+"/variables", map[string]any{"variables": vs})
}

func plain(name, value string) map[string]any { return map[string]any{"name": name, "value": value} }
func bySecret(name, secret string) map[string]any {
	return map[string]any{"name": name, "secret": secret}
}

func nilLogger() *slog.Logger { return slog.New(slog.NewTextHandler(io.Discard, nil)) }

func (e *env) callInto(dst *map[string]any, method, path string, body any) {
	e.t.Helper()
	_, out := e.call(method, path, body)
	*dst = out
}

// values that must never show up in a list, a log or a security event
const (
	pwOne   = "Zebra#Secret-4471"
	pwTwo   = "Quartz#Secret-9203"
	totpKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
)

// loginScript is a one-step script that reads variables.
func (e *env) loginScript(id string) string {
	return e.loginScriptFor(id, e.fn("SITE_USER"), e.fn("SITE_PASSWORD"), e.fn("")+"${param.site}_PASSWORD", e.fn("PORTAL_URL"))
}

func (e *env) loginScriptFor(id string, names ...string) string {
	step := func(n int, name string) string {
		return `{"stepNumber":` + fmt.Sprint(n) + `,"action":"type","parameters":{"value":"${env:` + name + `}"}}`
	}
	return `{"id":"` + id + `","name":"` + id + `","version":"1.0.0","targetRuntime":"desktop","steps":[` +
		step(1, names[0]) + "," + step(2, names[1]) + "," + step(3, names[2]) + "," + step(4, names[3]) + `]}`
}

// startRun publishes the script, queues it and walks it to Running on worker w1.
func (e *env) startRun(script, id string) string {
	e.t.Helper()
	e.publish(id, "1.0.0", script)
	e.register("w1")
	e.create(id, "1.0.0", map[string]string{"site": "BANK"})
	o := e.pull("w1")
	e.accept("w1", o)
	return o.runID
}

func (e *env) runEnv(runID, worker string, names ...string) (int, map[string]any) {
	e.t.Helper()
	return e.call("POST", "/jobs/"+runID+"/env", map[string]any{"workerId": worker, "names": names})
}

func valuesOf(out map[string]any) map[string]any {
	m, _ := out["values"].(map[string]any)
	return m
}

func valueOf(out map[string]any, name string) string {
	v, _ := valuesOf(out)[name].(map[string]any)
	s, _ := v["value"].(string)
	return s
}

func isSecret(out map[string]any, name string) bool {
	v, _ := valuesOf(out)[name].(map[string]any)
	b, _ := v["secret"].(bool)
	return b
}

// ── roles and permissions ────────────────────────────────────────────────────

func TestEachRoleHoldsExactlyItsPermissions(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.secret("site-a", pwOne)

	type check struct {
		name           string
		method, path   string
		body           map[string]any
		view, op, cadm bool // Viewer, Operator, CredentialAdmin; the Administrator can do all
	}
	checks := []check{
		{"list secrets", "GET", "/admin/secrets", nil, true, true, true},
		{"list groups", "GET", "/admin/credential-groups", nil, true, true, true},
		{"open a group", "GET", "/admin/credential-groups/g1", nil, true, true, true},
		{"history", "GET", "/admin/secrets/site-a/events", nil, true, true, true},
		{"reveal", "POST", "/admin/secrets/site-a/reveal", map[string]any{}, false, true, true},
		{"copy", "POST", "/admin/secrets/site-a/copy", map[string]any{}, false, true, true},
		{"create secret", "POST", "/admin/secrets", map[string]any{"name": "site-%s", "value": "p"}, false, false, true},
		{"create group", "POST", "/admin/credential-groups", map[string]any{"name": "grp-%s"}, false, false, true},
		{"edit variables", "PUT", "/admin/credential-groups/g1/variables", map[string]any{"variables": []any{plain("A", "1")}}, false, false, true},
	}
	tokens := map[string]string{
		"Viewer":          e.addUser("role-viewer", "Viewer", "Start#Pass1", nil),
		"Operator":        e.addUser("role-operator", "Operator", "Start#Pass1", nil),
		"CredentialAdmin": e.addUser("role-cadmin", "CredentialAdmin", "Start#Pass1", nil),
		"Administrator":   e.token,
	}
	allowed := func(role string, c check) bool {
		switch role {
		case "Viewer":
			return c.view
		case "Operator":
			return c.op
		}
		return true
	}
	for role, tok := range tokens {
		for i, c := range checks {
			path, body := c.path, c.body
			if strings.Contains(path, "%s") {
				path = fmt.Sprintf(path, strings.ToLower(role))
			}
			if body != nil && body["name"] != nil && strings.Contains(fmt.Sprint(body["name"]), "%s") {
				b := map[string]any{}
				for k, v := range body {
					b[k] = v
				}
				b["name"] = fmt.Sprintf(fmt.Sprint(body["name"]), strings.ToLower(role)+fmt.Sprint(i))
				body = b
			}
			code, out := e.as(tok).call(c.method, path, body)
			ok := code < 300
			if ok != allowed(role, c) {
				t.Errorf("%s / %s: status %d, allowed should be %v (%v)", role, c.name, code, allowed(role, c), out)
			}
			if !ok && code != http.StatusForbidden {
				t.Errorf("%s / %s: a refusal is 403, got %d", role, c.name, code)
			}
		}
	}
}

func TestAPermissionRefusalNamesThePermission(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("only-view", "Viewer", "Start#Pass1", nil)
	code, out := e.as(tok).call("POST", "/admin/secrets", map[string]any{"name": "x", "value": "p"})
	if code != http.StatusForbidden || !strings.Contains(str(out, "message"), "credentials.edit") {
		t.Fatalf("%d %v", code, out)
	}
}

func TestAUserLimitedToGroupsSeesAndEditsOnlyThem(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals")
	e.group("hr-portals")
	tok := e.addUser("fin-admin", "CredentialAdmin", "Start#Pass1", []string{"finance-portals"})
	c := e.as(tok)

	groups, _ := c.must(http.StatusOK, "GET", "/admin/credential-groups", nil)["groups"].([]any)
	if len(groups) != 1 || groups[0].(map[string]any)["name"] != "finance-portals" {
		t.Fatalf("groups seen: %v", groups)
	}
	c.must(http.StatusOK, "GET", "/admin/credential-groups/finance-portals", nil)
	c.must(http.StatusForbidden, "GET", "/admin/credential-groups/hr-portals", nil)
	c.must(http.StatusForbidden, "PUT", "/admin/credential-groups/hr-portals/variables", map[string]any{"variables": []any{plain("A", "1")}})
	c.must(http.StatusOK, "PUT", "/admin/credential-groups/finance-portals/variables", map[string]any{"variables": []any{plain("A", "1")}})
}

func TestJobsArtifactsAndSchedulesNeedNoSpecialRole(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("just-view", "Viewer", "Start#Pass1", nil)
	e.as(tok).must(http.StatusOK, "GET", "/jobs", nil)
	e.as(tok).must(http.StatusOK, "GET", "/artifacts", nil)
	e.as(tok).must(http.StatusOK, "GET", "/admin/schedules", nil)
}

// ── secrets: CRUD, sealing, the master key ───────────────────────────────────

func TestSecretsAreSealedAndNoReplyOrLogEverHoldsAValue(t *testing.T) {
	e := newAuthEnv(t)
	code, raw := e.raw("POST", "/admin/secrets", map[string]any{"name": "site-a", "kind": "text", "value": pwOne, "description": "the site"})
	if code != http.StatusCreated || strings.Contains(string(raw), pwOne) {
		t.Fatalf("create reply: %d %s", code, raw)
	}
	e.must(http.StatusCreated, "POST", "/admin/secrets", map[string]any{"name": "site-otp", "kind": "totp", "value": totpKey})

	var cipher, keyID string
	if err := e.db.QueryRow(`SELECT ValueCipher,KeyId FROM Secrets WHERE Name='site-a'`).Scan(&cipher, &keyID); err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(cipher, "v1:") || keyID != "1" || strings.Contains(cipher, "Zebra") {
		t.Fatalf("not sealed: %q %q", cipher, keyID)
	}
	e.must(http.StatusOK, "PUT", "/admin/secrets/site-a", map[string]any{"value": pwTwo})
	e.must(http.StatusOK, "POST", "/admin/secrets/site-a/reveal", map[string]any{})

	_, list := e.raw("GET", "/admin/secrets", nil)
	_, one := e.raw("GET", "/admin/secrets?q=site", nil)
	_, hist := e.raw("GET", "/admin/secrets/site-a/events", nil)
	for name, body := range map[string]string{"list": string(list), "filtered list": string(one), "history": string(hist)} {
		for _, s := range []string{pwOne, pwTwo, totpKey, "ValueCipher", "v1:"} {
			if strings.Contains(body, s) {
				t.Errorf("%s contains %q", name, s)
			}
		}
	}
	for _, q := range []string{
		`SELECT COUNT(*) FROM Secrets WHERE ValueCipher LIKE '%Zebra%' OR Description LIKE '%Zebra%' OR ValueCipher LIKE '%GEZD%'`,
		`SELECT COUNT(*) FROM CredentialEvents WHERE Detail LIKE '%Zebra%' OR Detail LIKE '%Quartz%' OR Detail LIKE '%GEZD%'`,
	} {
		if e.count(q) != 0 {
			t.Fatalf("a value is in the database: %s", q)
		}
	}
	time.Sleep(300 * time.Millisecond)
	for _, s := range []string{pwOne, pwTwo, totpKey} {
		if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+s+"%"); n != 0 {
			t.Errorf("a value reached the audit log %d time(s)", n)
		}
	}
}

func TestSecretNamesKindsAndValuesAreChecked(t *testing.T) {
	e := newAuthEnv(t)
	for _, b := range []map[string]any{
		{"name": "Has-Capital", "value": "v"},
		{"name": "has space", "value": "v"},
		{"name": "", "value": "v"},
		{"name": "ok", "value": ""},
		{"name": "ok", "kind": "nonsense", "value": "v"},
		{"name": "ok", "kind": "totp", "value": "not base32 !!"},
		{"name": "ok", "value": "v", "expiresOn": "tomorrow"},
	} {
		e.must(http.StatusUnprocessableEntity, "POST", "/admin/secrets", b)
	}
	e.secret("ok", "v")
	e.must(http.StatusConflict, "POST", "/admin/secrets", map[string]any{"name": "ok", "value": "w"})
}

func TestEditingKeepsTheValueUnlessANewOneIsGiven(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("site-a", pwOne)
	e.must(http.StatusOK, "PUT", "/admin/secrets/site-a", map[string]any{"description": "now with text"})
	e.must(http.StatusOK, "PUT", "/admin/secrets/site-a", map[string]any{"value": ""})
	if v := str(e.must(http.StatusOK, "POST", "/admin/secrets/site-a/reveal", map[string]any{}), "value"); v != pwOne {
		t.Fatalf("the value changed: %q", v)
	}
	e.must(http.StatusOK, "PUT", "/admin/secrets/site-a", map[string]any{"value": pwTwo})
	if v := str(e.must(http.StatusOK, "POST", "/admin/secrets/site-a/reveal", map[string]any{}), "value"); v != pwTwo {
		t.Fatalf("the new value is not kept: %q", v)
	}
	e.must(http.StatusNotFound, "PUT", "/admin/secrets/ghost", map[string]any{"value": "x"})
	list := e.must(http.StatusOK, "GET", "/admin/secrets", nil)["secrets"].([]any)
	if list[0].(map[string]any)["version"].(float64) != 4 || list[0].(map[string]any)["description"] != "now with text" {
		t.Fatalf("version or description: %v", list[0])
	}
}

func TestAValueMovedToAnotherSecretDoesNotOpen(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("site-a", pwOne)
	e.secret("site-b", pwTwo)
	var a string
	_ = e.db.QueryRow(`SELECT ValueCipher FROM Secrets WHERE Name='site-a'`).Scan(&a)
	if _, err := e.db.Exec(`UPDATE Secrets SET ValueCipher=$1 WHERE Name='site-b'`, a); err != nil {
		t.Fatal(err)
	}
	code, raw := e.raw("POST", "/admin/secrets/site-b/reveal", map[string]any{})
	if code != http.StatusInternalServerError || strings.Contains(string(raw), pwOne) || strings.Contains(string(raw), pwTwo) {
		t.Fatalf("a moved value opened or leaked: %d %s", code, raw)
	}
}

func TestWithoutAMasterKeyTheStoreIsLockedButPlainVariablesAndJobsStillWork(t *testing.T) {
	e := newAuthEnvWith(t, Config{})
	for _, p := range [][2]string{{"POST", "/admin/secrets"}, {"POST", "/admin/secrets/import"},
		{"POST", "/admin/secrets/x/reveal"}, {"POST", "/admin/secrets/x/copy"}} {
		code, out := e.call(p[0], p[1], map[string]any{})
		if code != http.StatusServiceUnavailable || str(out, "error") != "CREDENTIAL_STORE_LOCKED" {
			t.Errorf("%s %s: %d %v", p[0], p[1], code, out)
		}
	}
	// the list still opens and says the store is locked
	out := e.must(http.StatusOK, "GET", "/admin/secrets", nil)
	if out["locked"] != true {
		t.Fatalf("list does not say locked: %v", out)
	}
	// a run still reads plain variables, but refuses a secret-backed one with 503
	e.group("g1", "login-script")
	e.vars("g1", plain("SITE_USER", "alice"))
	e.db.Exec(`INSERT INTO Secrets(Name,Kind,ValueCipher,KeyId,UpdatedAt,UpdatedBy) VALUES('s1','text','v1:xx','1',now(),'t')`)
	e.vars("g1", plain("SITE_USER", "alice"), bySecret("SITE_PASSWORD", "s1"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")
	code, got := e.runEnv(runID, "w1", e.fn("SITE_USER"))
	if code != http.StatusOK || valueOf(got, e.fn("SITE_USER")) != "alice" {
		t.Fatalf("a plain variable while locked: %d %v", code, got)
	}
	code, got = e.runEnv(runID, "w1", e.fn("SITE_PASSWORD"))
	if code != http.StatusServiceUnavailable || str(got, "error") != "CREDENTIAL_STORE_LOCKED" {
		t.Fatalf("a secret-backed variable while locked: %d %v", code, got)
	}
}

func TestAWrongMasterKeyIsNoticedAtStartAndRekeyMovesEveryRow(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusCreated, "POST", "/admin/secrets", map[string]any{"name": "site-a", "kind": "totp", "value": totpKey})
	e.secret("site-b", pwTwo)
	e.secret("site-c", pwOne)

	one := mustVault(t, vault.TestKey(7), "1")
	if err := handlers.VerifyAll(e.db, one); err != nil {
		t.Fatalf("the right key does not verify: %v", err)
	}
	wrong := mustVault(t, vault.TestKey(9), "1")
	err := handlers.VerifyAll(e.db, wrong)
	if err == nil || strings.Contains(err.Error(), pwOne) || strings.Contains(err.Error(), vault.TestKey(9)) {
		t.Fatalf("a wrong key was not refused cleanly: %v", err)
	}

	two, err := vault.New(vault.TestKey(8), "2", vault.TestKey(7), "1")
	if err != nil {
		t.Fatal(err)
	}
	n, err := handlers.Rekey(e.db, two)
	if err != nil || n != 3 {
		t.Fatalf("rekey: %d %v", n, err)
	}
	if e.count(`SELECT COUNT(*) FROM Secrets WHERE KeyId<>'2'`) != 0 {
		t.Fatal("a row kept the old key id")
	}
	onlyNew := mustVault(t, vault.TestKey(8), "2")
	if err := handlers.VerifyAll(e.db, onlyNew); err != nil {
		t.Fatalf("the new key alone does not open the store: %v", err)
	}
	if err := handlers.VerifyAll(e.db, one); err == nil {
		t.Fatal("the old key still opens a rekeyed store")
	}
	if n, _ := handlers.Rekey(e.db, two); n != 0 {
		t.Fatalf("a second rekey changed %d rows", n)
	}

	e2 := &env{t: t, h: NewRouter(e.db, nilLogger(), Config{Vault: onlyNew}), db: e.db}
	e2.token = e2.login("admin", "Admin@123#")
	got := e2.must(http.StatusOK, "POST", "/admin/secrets/site-b/reveal", map[string]any{})
	if str(got, "value") != pwTwo {
		t.Fatalf("the rekeyed value: %v", got)
	}
}

func TestRevealAndCopyAreRecordedWithTheUserAndHistoryIsNewestFirst(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("site-a", pwOne)
	op := e.addUser("opera", "Operator", "Start#Pass1", nil)

	out := e.as(op).must(http.StatusOK, "POST", "/admin/secrets/site-a/reveal", map[string]any{})
	if str(out, "value") != pwOne {
		t.Fatalf("reveal: %v", out)
	}
	e.as(op).must(http.StatusOK, "POST", "/admin/secrets/site-a/copy", map[string]any{})

	events := e.must(http.StatusOK, "GET", "/admin/secrets/site-a/events", nil)["events"].([]any)
	var kinds []string
	for _, ev := range events {
		m := ev.(map[string]any)
		kinds = append(kinds, m["kind"].(string)+":"+m["actor"].(string))
	}
	want := []string{"COPIED:opera", "REVEALED:opera", "CREATED:admin"}
	if strings.Join(kinds, ",") != strings.Join(want, ",") {
		t.Fatalf("history %v, want %v", kinds, want)
	}
}

func TestRefusedRevealsAreRecordedToo(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("site-a", pwOne)
	v := e.addUser("viewer1", "Viewer", "Start#Pass1", nil)
	e.as(v).must(http.StatusForbidden, "POST", "/admin/secrets/site-a/reveal", map[string]any{})
	e.as(v).must(http.StatusForbidden, "POST", "/admin/secrets/site-a/copy", map[string]any{})
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='REVEAL_DENIED' AND Actor='viewer1' AND Credential='site-a'`) != 2 {
		t.Fatal("a refused reveal was not recorded")
	}
}

func TestDeletingKeepsTheHistoryButIsRefusedWhileAVariableUsesTheSecret(t *testing.T) {
	e := newAuthEnv(t)
	e.group("Portals one")
	e.secret("site-a", pwOne)
	e.vars("Portals one", bySecret("SITE_PASSWORD", "site-a"))

	code, out := e.call("DELETE", "/admin/secrets/site-a", nil)
	if code != http.StatusConflict || str(out, "error") != "SECRET_IN_USE" ||
		!strings.Contains(str(out, "message"), "SITE_PASSWORD") || !strings.Contains(str(out, "message"), "Portals one") {
		t.Fatalf("delete of a secret in use: %d %v", code, out)
	}
	used := e.must(http.StatusOK, "GET", "/admin/secrets", nil)["secrets"].([]any)[0].(map[string]any)["usedBy"].([]any)
	if len(used) != 1 || used[0].(map[string]any)["variable"] != "SITE_PASSWORD" || used[0].(map[string]any)["group"] != "Portals one" {
		t.Fatalf("usedBy: %v", used)
	}

	e.vars("Portals one", plain("SITE_PASSWORD", "x"))
	e.must(http.StatusNoContent, "DELETE", "/admin/secrets/site-a", nil)
	e.must(http.StatusNotFound, "DELETE", "/admin/secrets/site-a", nil)
	events := e.must(http.StatusOK, "GET", "/admin/secrets/site-a/events", nil)["events"].([]any)
	if len(events) != 2 {
		t.Fatalf("history after delete: %v", events)
	}
	e.must(http.StatusNotFound, "POST", "/admin/secrets/site-a/reveal", map[string]any{})
}

func TestExpiryStatusNoExpirySoonExpiredAndOk(t *testing.T) {
	e := newAuthEnv(t)
	day := func(d int) string { return time.Now().UTC().AddDate(0, 0, d).Format("2006-01-02") }
	mk := func(name string, exp any) {
		body := map[string]any{"name": name, "value": "p"}
		if exp != nil {
			body["expiresOn"] = exp
		}
		e.must(http.StatusCreated, "POST", "/admin/secrets", body)
	}
	mk("never", nil)
	mk("never-null", "")
	mk("tomorrow", day(1))
	mk("in-14-days", day(14))
	mk("in-30-days", day(30))
	mk("yesterday", day(-1))

	out := e.must(http.StatusOK, "GET", "/admin/secrets", nil)
	status := map[string]string{}
	for _, c := range out["secrets"].([]any) {
		m := c.(map[string]any)
		status[m["name"].(string)] = m["status"].(string)
	}
	want := map[string]string{"never": "none", "never-null": "none", "tomorrow": "soon", "in-14-days": "soon", "in-30-days": "ok", "yesterday": "expired"}
	for k, v := range want {
		if status[k] != v {
			t.Errorf("%s: %q, want %q", k, status[k], v)
		}
	}
	sum := out["summary"].(map[string]any)
	if sum["soon"].(float64) != 2 || sum["expired"].(float64) != 1 || sum["total"].(float64) != 6 {
		t.Fatalf("summary: %v", sum)
	}
	e.must(http.StatusOK, "PUT", "/admin/secrets/yesterday", map[string]any{"expiresOn": nil})
	sum = e.must(http.StatusOK, "GET", "/admin/secrets", nil)["summary"].(map[string]any)
	if sum["expired"].(float64) != 0 {
		t.Fatalf("setting no expiry did not clear the date: %v", sum)
	}
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/secrets/never", map[string]any{"expiresOn": "tomorrow"})
}

func TestTheSecretListCanBeSearched(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("alpha-key", "a")
	e.secret("beta-key", "b")
	count := func(q string) int {
		return len(e.must(http.StatusOK, "GET", "/admin/secrets"+q, nil)["secrets"].([]any))
	}
	if count("") != 2 || count("?q=alpha") != 1 || count("?q=KEY") != 2 || count("?q=zzz") != 0 {
		t.Fatal("search is wrong")
	}
}

// ── import ───────────────────────────────────────────────────────────────────

func importRows(n int) []map[string]any {
	rows := make([]map[string]any, 0, n)
	for i := 1; i <= n; i++ {
		rows = append(rows, map[string]any{
			"name": fmt.Sprintf("portal-%03d", i), "value": fmt.Sprintf("Pw#%03d-Secret", i), "kind": "text",
			"description": fmt.Sprintf("site %d", i), "expiresOn": "",
		})
	}
	return rows
}

func TestImportOfTwoHundredSecretsIsAllOrNothing(t *testing.T) {
	e := newAuthEnv(t)
	rows := importRows(200)
	rows[17]["name"] = "Bad Name"
	rows[42]["value"] = ""
	rows[99]["name"] = rows[98]["name"]
	code, out := e.call("POST", "/admin/secrets/import", map[string]any{"rows": rows})
	if code != http.StatusUnprocessableEntity || str(out, "error") != "IMPORT_ERRORS" {
		t.Fatalf("%d %v", code, out)
	}
	if n := len(out["errors"].([]any)); n != 3 {
		t.Fatalf("errors: %v", out["errors"])
	}
	if e.count(`SELECT COUNT(*) FROM Secrets`) != 0 {
		t.Fatal("a failed import saved rows")
	}

	good := importRows(200)
	dry := e.must(http.StatusOK, "POST", "/admin/secrets/import", map[string]any{"rows": good, "dryRun": true})
	if dry["wouldCreate"].(float64) != 200 || e.count(`SELECT COUNT(*) FROM Secrets`) != 0 {
		t.Fatalf("dry run: %v", dry)
	}
	done := e.must(http.StatusOK, "POST", "/admin/secrets/import", map[string]any{"rows": good})
	if done["created"].(float64) != 200 || e.count(`SELECT COUNT(*) FROM Secrets WHERE ValueCipher LIKE 'v1:%'`) != 200 {
		t.Fatalf("import: %v", done)
	}
}

func TestImportRefusesExistingNamesUnlessReplaceIsTicked(t *testing.T) {
	e := newAuthEnv(t)
	e.secret("portal-001", pwOne)
	rows := importRows(2)
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/secrets/import", map[string]any{"rows": rows})
	out := e.must(http.StatusOK, "POST", "/admin/secrets/import", map[string]any{"rows": rows, "replaceExisting": true})
	if out["created"].(float64) != 1 || out["replaced"].(float64) != 1 {
		t.Fatalf("%v", out)
	}
	if v := str(e.must(http.StatusOK, "POST", "/admin/secrets/portal-001/reveal", map[string]any{}), "value"); v != "Pw#001-Secret" {
		t.Fatalf("replaced value: %q", v)
	}
}

// ── groups and variables ─────────────────────────────────────────────────────

func TestGroupsHaveNamesScriptsAndTheirVariablesGoWithThem(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "   "})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "has,comma"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "g", "scripts": []string{"two words"}})
	e.group("ACME with login", "login-script")
	e.must(http.StatusConflict, "POST", "/admin/credential-groups", map[string]any{"name": "ACME with login"})
	e.vars("ACME with login", plain("A", "1"), plain("B", "2"))

	list := e.must(http.StatusOK, "GET", "/admin/credential-groups", nil)["groups"].([]any)
	g := list[0].(map[string]any)
	if g["variableCount"].(float64) != 2 || len(g["scripts"].([]any)) != 1 {
		t.Fatalf("group row: %v", g)
	}
	e.must(http.StatusOK, "PUT", "/admin/credential-groups/ACME%20with%20login", map[string]any{"scripts": []string{"a", "b"}, "description": "d"})
	one := e.must(http.StatusOK, "GET", "/admin/credential-groups/ACME%20with%20login", nil)
	if len(one["scripts"].([]any)) != 2 || one["description"] != "d" || len(one["variables"].([]any)) != 2 {
		t.Fatalf("group page: %v", one)
	}
	e.must(http.StatusNoContent, "DELETE", "/admin/credential-groups/ACME%20with%20login", nil)
	e.must(http.StatusNotFound, "DELETE", "/admin/credential-groups/ACME%20with%20login", nil)
	if e.count(`SELECT COUNT(*) FROM GroupVariables`) != 0 {
		t.Fatal("the variables stayed after the group was deleted")
	}
}

func TestVariablesAreValidatedAndReplacedAtomically(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.secret("s1", pwOne)
	e.vars("g1", plain("KEEP", "yes"))

	put := func(want int, vs ...map[string]any) map[string]any {
		return e.must(want, "PUT", "/admin/credential-groups/g1/variables", map[string]any{"variables": vs})
	}
	bad := []map[string]any{
		{"name": "1STARTS_WITH_DIGIT", "value": "x"},
		{"name": "has-dash", "value": "x"},
		{"name": "has space", "value": "x"},
		{"name": "", "value": "x"},
		{"name": strings.Repeat("A", 101), "value": "x"},
		{"name": "NEITHER"},
		{"name": "BOTH", "value": "x", "secret": "s1"},
		{"name": "MISSING_SECRET", "secret": "no-such-secret"},
	}
	for _, b := range bad {
		put(http.StatusUnprocessableEntity, b)
	}
	put(http.StatusUnprocessableEntity, plain("DUP", "1"), plain("DUP", "2"))
	// every refusal left the list as it was
	got := e.must(http.StatusOK, "GET", "/admin/credential-groups/g1", nil)["variables"].([]any)
	if len(got) != 1 || got[0].(map[string]any)["name"] != "KEEP" {
		t.Fatalf("a refused change altered the list: %v", got)
	}
	put(http.StatusOK, plain("lower_ok", ""), plain("MIXED_Case9", "v"), bySecret("TOKEN", "s1"))
	got = e.must(http.StatusOK, "GET", "/admin/credential-groups/g1", nil)["variables"].([]any)
	if len(got) != 3 {
		t.Fatalf("variables: %v", got)
	}
	for _, v := range got {
		m := v.(map[string]any)
		if m["name"] == "TOKEN" && (m["secret"] != "s1" || m["value"] != nil) {
			t.Fatalf("secret variable: %v", m)
		}
		if m["name"] == "MIXED_Case9" && (m["value"] != "v" || m["secret"] != nil) {
			t.Fatalf("plain variable: %v", m)
		}
	}
	put(http.StatusOK)
	if e.count(`SELECT COUNT(*) FROM GroupVariables WHERE GroupName='g1'`) != 0 {
		t.Fatal("an empty list did not clear the group")
	}
	e.must(http.StatusNotFound, "PUT", "/admin/credential-groups/ghost/variables", map[string]any{"variables": []any{}})
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='VARIABLES_CHANGED' AND Credential='g1'`) != 3 {
		t.Fatal("variable changes should be recorded")
	}
}

// ── what a running job reads ─────────────────────────────────────────────────

func TestARunningJobGetsTheVariablesItsScriptNames(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.secret("pw-one", pwOne)
	e.secret("pw-bank", pwTwo)
	e.vars("finance-portals",
		plain("SITE_USER", "fin.ops@acme.com"), bySecret("SITE_PASSWORD", "pw-one"),
		bySecret("BANK_PASSWORD", "pw-bank"), plain("PORTAL_URL", "https://portal.example.com"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")

	code, out := e.runEnv(runID, "w1", e.fn("SITE_USER"), e.fn("SITE_PASSWORD"), e.fn("BANK_PASSWORD"), e.fn("PORTAL_URL"))
	if code != http.StatusOK {
		t.Fatalf("%d %v", code, out)
	}
	if valueOf(out, e.fn("SITE_USER")) != "fin.ops@acme.com" || isSecret(out, e.fn("SITE_USER")) ||
		valueOf(out, e.fn("SITE_PASSWORD")) != pwOne || !isSecret(out, e.fn("SITE_PASSWORD")) ||
		valueOf(out, e.fn("BANK_PASSWORD")) != pwTwo || valueOf(out, e.fn("PORTAL_URL")) != "https://portal.example.com" || isSecret(out, e.fn("PORTAL_URL")) {
		t.Fatalf("values: %v", out)
	}
	// only reads of secrets are recorded, with the run and the worker
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_BY_RUN' AND Actor=$1 AND Credential IN ('pw-one','pw-bank')`, "run:"+runID+"@w1") != 2 {
		t.Fatal("each secret read should be recorded with the run and worker")
	}
	// BANK_PASSWORD came through ${env:${param.site}_PASSWORD} with site=BANK
}

func TestAReadIsRefusedForEveryWrongSituation(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.group("other-portals", "another-script")
	e.secret("pw-one", pwOne)
	e.secret("pw-other", pwTwo)
	e.vars("finance-portals", plain("SITE_USER", "u"), bySecret("SITE_PASSWORD", "pw-one"), plain("PORTAL_URL", "https://p"))
	e.vars("other-portals", bySecret("ELSEWHERE", "pw-other"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")
	elsewhere := e.fnIn("other-portals", "ELSEWHERE")

	check := func(why string, want int, wantCode string, code int, out map[string]any) {
		t.Helper()
		if code != want || (wantCode != "" && str(out, "error") != wantCode) {
			t.Errorf("%s: status %d code %q, want %d %q (%v)", why, code, str(out, "error"), want, wantCode, out)
		}
		if b := fmt.Sprint(out); strings.Contains(b, pwOne) || strings.Contains(b, pwTwo) {
			t.Errorf("%s: the refusal leaked a value", why)
		}
	}
	code, out := e.runEnv(runID, "another-worker", e.fn("SITE_USER"))
	check("another worker", http.StatusForbidden, "RUN_NOT_ACTIVE", code, out)
	code, out = e.runEnv(runID, "", e.fn("SITE_USER"))
	check("no worker", http.StatusForbidden, "RUN_NOT_ACTIVE", code, out)
	code, out = e.runEnv(runID, "w1", elsewhere)
	check("a variable the script does not name", http.StatusForbidden, "VARIABLE_DENIED", code, out)
	code, out = e.runEnv(runID, "w1", e.fn("SITE_USER"), elsewhere)
	check("one allowed and one not: nothing is returned", http.StatusForbidden, "VARIABLE_DENIED", code, out)
	code, out = e.runEnv(runID, "w1", e.fn("NOT_IN_SCRIPT"))
	check("a name the script does not write", http.StatusForbidden, "VARIABLE_DENIED", code, out)
	code, out = e.runEnv(runID, "w1", "SITE_USER")
	check("a name without the group prefix", http.StatusNotFound, "UNKNOWN_VARIABLE", code, out)
	if !strings.Contains(str(out, "message"), "GP<group number>_") {
		t.Errorf("the refusal should explain the prefix: %v", out)
	}
	code, out = e.runEnv(runID, "w1", "not-a-valid name")
	check("an invalid name", http.StatusNotFound, "UNKNOWN_VARIABLE", code, out)
	code, out = e.runEnv(runID, "w1", e.fn("BANK_PASSWORD")) // written through ${param.site}, but set nowhere
	check("a name set nowhere", http.StatusNotFound, "UNKNOWN_VARIABLE", code, out)
	if !strings.Contains(str(out, "message"), "BANK_PASSWORD") {
		t.Errorf("the refusal should name the variable: %v", out)
	}
	// a group that was deleted
	siteUser := e.fn("SITE_USER")
	e.must(http.StatusNoContent, "DELETE", "/admin/credential-groups/other-portals", nil)
	e.must(http.StatusNoContent, "DELETE", "/admin/credential-groups/finance-portals", nil)
	code, out = e.runEnv(runID, "w1", siteUser)
	check("a group that was deleted", http.StatusNotFound, "UNKNOWN_VARIABLE", code, out)

	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_DENIED'`) < 6 {
		t.Fatal("refusals should be recorded")
	}
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_BY_RUN'`) != 0 {
		t.Fatal("a refused read was recorded as a read")
	}
}

func TestAScriptCannotReadAVariableOfAGroupThatDoesNotAllowIt(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.group("hr-portals", "hr-script")
	e.vars("finance-portals", plain("SITE_USER", "fin"), plain("SITE_PASSWORD", "x"), plain("PORTAL_URL", "https://p"))
	e.vars("hr-portals", plain("SITE_USER", "hr"))
	// login-script names the variable of the group that does not allow it
	script := e.loginScriptFor("login-script", e.fnIn("hr-portals", "SITE_USER"), e.fn("SITE_PASSWORD"), e.fn("")+"${param.site}_PASSWORD", e.fn("PORTAL_URL"))
	runID := e.startRun(script, "login-script")
	code, out := e.runEnv(runID, "w1", e.fnIn("hr-portals", "SITE_USER"))
	if code != http.StatusForbidden || str(out, "error") != "VARIABLE_DENIED" || !strings.Contains(str(out, "message"), "login-script") {
		t.Fatalf("%d %v", code, out)
	}
	// its own group is fine
	code, out = e.runEnv(runID, "w1", e.fn("SITE_PASSWORD"))
	if code != http.StatusOK || valueOf(out, e.fn("SITE_PASSWORD")) != "x" {
		t.Fatalf("%d %v", code, out)
	}
}

func TestTheSameShortNameInTwoGroupsNeverClashes(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g-one", "login-script")
	e.group("g-two", "login-script")
	e.vars("g-one", plain("SITE_USER", "one"), plain("SITE_PASSWORD", "p1"), plain("PORTAL_URL", "https://one"))
	e.vars("g-two", plain("SITE_USER", "two"))
	script := e.loginScriptFor("login-script", e.fnIn("g-one", "SITE_USER"), e.fnIn("g-two", "SITE_USER"), e.fn("")+"${param.site}_PASSWORD", e.fn("PORTAL_URL"))
	runID := e.startRun(script, "login-script")
	code, out := e.runEnv(runID, "w1", e.fnIn("g-one", "SITE_USER"), e.fnIn("g-two", "SITE_USER"))
	if code != http.StatusOK || valueOf(out, e.fnIn("g-one", "SITE_USER")) != "one" || valueOf(out, e.fnIn("g-two", "SITE_USER")) != "two" {
		t.Fatalf("%d %v", code, out)
	}
	if e.fnIn("g-one", "SITE_USER") == e.fnIn("g-two", "SITE_USER") {
		t.Fatal("full names must differ")
	}
}

func TestGroupNumbersStartAt100AreNeverReusedAndSurviveARename(t *testing.T) {
	e := newAuthEnv(t)
	first := e.must(http.StatusCreated, "POST", "/admin/credential-groups", map[string]any{"name": "acme with login"})
	second := e.must(http.StatusCreated, "POST", "/admin/credential-groups", map[string]any{"name": "second"})
	n1, n2 := int(first["number"].(float64)), int(second["number"].(float64))
	if n1 < 100 || n2 != n1+1 {
		t.Fatalf("numbers %d and %d", n1, n2)
	}
	// deleting the newest group does not free its number
	e.must(http.StatusNoContent, "DELETE", "/admin/credential-groups/second", nil)
	third := e.must(http.StatusCreated, "POST", "/admin/credential-groups", map[string]any{"name": "third"})
	if n3 := int(third["number"].(float64)); n3 != n2+1 {
		t.Fatalf("a deleted number was reused: %d after %d", n3, n2)
	}
	// a refused create (a duplicate name) does not break the numbering
	e.must(http.StatusConflict, "POST", "/admin/credential-groups", map[string]any{"name": "third"})
	// a rename keeps the number and moves the variables, scripts and the users' limits with the group
	e.secret("s1", pwOne)
	e.vars("acme with login", plain("ACME_USERNAME", "u"), bySecret("ACME_PASSWORD", "s1"))
	e.must(http.StatusOK, "PUT", "/admin/credential-groups/acme%20with%20login", map[string]any{"scripts": []string{"child"}})
	e.must(http.StatusCreated, "POST", "/admin/users", map[string]any{"username": "limited", "role": "Viewer", "password": "Start#Pass1", "allowedGroups": []string{"acme with login", "third"}})
	e.must(http.StatusOK, "PUT", "/admin/credential-groups/acme%20with%20login", map[string]any{"name": "ACME Portal"})
	e.must(http.StatusNotFound, "GET", "/admin/credential-groups/acme%20with%20login", nil)
	g := e.must(http.StatusOK, "GET", "/admin/credential-groups/ACME%20Portal", nil)
	if int(g["number"].(float64)) != n1 || g["prefix"] != fmt.Sprintf("GP%d_", n1) {
		t.Fatalf("a rename changed the number: %v", g)
	}
	vs := g["variables"].([]any)
	if len(vs) != 2 || vs[0].(map[string]any)["fullName"] != fmt.Sprintf("GP%d_ACME_PASSWORD", n1) || fmt.Sprint(g["scripts"]) != "[child]" {
		t.Fatalf("variables or scripts did not follow the rename: %v", g)
	}
	var limits string
	if err := e.db.QueryRow(`SELECT AllowedGroups FROM Users WHERE Username='limited'`).Scan(&limits); err != nil || limits != "ACME Portal,third" {
		t.Fatalf("the user's group limit did not follow the rename: %q %v", limits, err)
	}
	// renaming onto another group's name is refused, a bad name is refused
	e.must(http.StatusConflict, "PUT", "/admin/credential-groups/ACME%20Portal", map[string]any{"name": "third"})
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/credential-groups/ACME%20Portal", map[string]any{"name": "a,b"})
	// the list shows the numbers, the secret lists the full names of its users
	list := e.must(http.StatusOK, "GET", "/admin/credential-groups", nil)["groups"].([]any)
	if list[0].(map[string]any)["number"].(float64) != float64(n1) {
		t.Fatalf("list: %v", list)
	}
	secrets := e.must(http.StatusOK, "GET", "/admin/secrets", nil)["secrets"].([]any)
	ub := secrets[0].(map[string]any)["usedBy"].([]any)[0].(map[string]any)
	if ub["fullName"] != fmt.Sprintf("GP%d_ACME_PASSWORD", n1) || int(ub["groupNumber"].(float64)) != n1 || ub["variable"] != "ACME_PASSWORD" || ub["group"] != "ACME Portal" {
		t.Fatalf("usedBy: %v", ub)
	}
	// and a run reads the renamed group's variable by its unchanged full name
	user, pass := fmt.Sprintf("GP%d_ACME_USERNAME", n1), fmt.Sprintf("GP%d_ACME_PASSWORD", n1)
	script := e.loginScriptFor("child", user, pass, fmt.Sprintf("GP%d_X", n1), fmt.Sprintf("GP%d_Y", n1))
	runID := e.startRun(script, "child")
	code, out := e.runEnv(runID, "w1", user, pass)
	if code != http.StatusOK || valueOf(out, pass) != pwOne || !isSecret(out, pass) {
		t.Fatalf("%d %v", code, out)
	}
}

func TestAnAllScriptsGroupServesEveryScript(t *testing.T) {
	e := newAuthEnv(t)
	e.group("everyone", "*")
	e.vars("everyone", plain("SITE_USER", "anyone"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")
	if code, out := e.runEnv(runID, "w1", e.fn("SITE_USER")); code != http.StatusOK || valueOf(out, e.fn("SITE_USER")) != "anyone" {
		t.Fatalf("%d %v", code, out)
	}
}

func TestAFinishedRunCannotReadVariables(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.vars("finance-portals", plain("SITE_USER", "u"))
	e.publish("login-script", "1.0.0", e.loginScript("login-script"))
	e.register("w1")
	e.create("login-script", "1.0.0", map[string]string{"site": "BANK"})

	o := e.pull("w1")
	if code, _ := e.runEnv(o.runID, "w1", e.fn("SITE_USER")); code != http.StatusForbidden {
		t.Fatalf("a run not yet running read variables: %d", code)
	}
	e.accept("w1", o)
	if code, _ := e.runEnv(o.runID, "w1", e.fn("SITE_USER")); code != http.StatusOK {
		t.Fatalf("a running run was refused: %d", code)
	}
	e.finish(o, "Succeeded", nil)
	if code, _ := e.runEnv(o.runID, "w1", e.fn("SITE_USER")); code != http.StatusForbidden {
		t.Fatalf("a finished run read variables: %d", code)
	}
	e.must(http.StatusNotFound, "POST", "/jobs/nope/env", map[string]any{"workerId": "w1", "names": []string{e.fn("SITE_USER")}})
}

func TestASegmentOfAScriptReadsWithTheMainScriptsAccess(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "two-stage")
	e.secret("pw-one", pwOne)
	e.vars("finance-portals", bySecret("SITE_PASSWORD", "pw-one"))
	script := `{"id":"two-stage","name":"two-stage","version":"1.0.0","segments":[` +
		`{"name":"first","runtime":"desktop","steps":[{"stepNumber":1,"action":"delay","parameters":{"ms":"1"}}]},` +
		`{"name":"second","runtime":"desktop","steps":[{"stepNumber":1,"action":"type","parameters":{"value":"${env:` + e.fn("SITE_PASSWORD") + `}"}}]}]}`
	e.publish("two-stage", "1.0.0", script)
	e.register("w1")
	e.create("two-stage", "1.0.0", nil)
	o1 := e.pull("w1")
	e.accept("w1", o1)
	e.finish(o1, "Succeeded", nil)
	o2 := e.pull("w1")
	if o2.runID == "" || o2.runID == o1.runID {
		t.Fatalf("the second segment was not offered: %v", o2)
	}
	e.accept("w1", o2)
	code, out := e.runEnv(o2.runID, "w1", e.fn("SITE_PASSWORD"))
	if code != http.StatusOK || valueOf(out, e.fn("SITE_PASSWORD")) != pwOne {
		t.Fatalf("segment read: %d %v", code, out)
	}
}

func TestAnExpiredSecretIsStillGivenWithAWarningInTheRunLog(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.must(http.StatusCreated, "POST", "/admin/secrets", map[string]any{
		"name": "pw-old", "value": pwOne, "expiresOn": time.Now().UTC().AddDate(0, 0, -3).Format("2006-01-02")})
	e.vars("finance-portals", bySecret("SITE_PASSWORD", "pw-old"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")
	code, out := e.runEnv(runID, "w1", e.fn("SITE_PASSWORD"))
	if code != http.StatusOK || valueOf(out, e.fn("SITE_PASSWORD")) != pwOne {
		t.Fatalf("%d %v", code, out)
	}
	if e.count(`SELECT COUNT(*) FROM RunLogs WHERE RunId=$1 AND Level='Warning' AND Message LIKE '%pw-old%expired on%'`, runID) != 1 {
		t.Fatal("no warning in the run log")
	}
}

func TestARunGetsTheCurrentCodeOfAOneTimeKeyAndNeverTheKey(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.must(http.StatusCreated, "POST", "/admin/secrets", map[string]any{"name": "site-otp", "kind": "totp", "value": totpKey})
	e.vars("finance-portals", bySecret("SITE_PASSWORD", "site-otp"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")

	code, raw := e.raw("POST", "/jobs/"+runID+"/env", map[string]any{"workerId": "w1", "names": []string{e.fn("SITE_PASSWORD")}})
	if code != http.StatusOK || strings.Contains(string(raw), totpKey) {
		t.Fatalf("%d %s", code, raw)
	}
	out := map[string]any{}
	e.callInto(&out, "POST", "/jobs/"+runID+"/env", map[string]any{"workerId": "w1", "names": []string{e.fn("SITE_PASSWORD")}})
	got := valueOf(out, e.fn("SITE_PASSWORD"))

	// the code is one of the windows around now (a request near the edge waits for the next window)
	now := time.Now()
	valid := map[string]bool{}
	for _, off := range []time.Duration{-30 * time.Second, 0, 30 * time.Second} {
		c, _ := vault.TOTPCode(totpKey, now.Add(off))
		valid[c] = true
	}
	if len(got) != 6 || !valid[got] || !isSecret(out, e.fn("SITE_PASSWORD")) {
		t.Fatalf("code %q is not a current code (secret=%v)", got, isSecret(out, e.fn("SITE_PASSWORD")))
	}

	// the reveal in the UI gives the key itself to those who may reveal
	rv := e.must(http.StatusOK, "POST", "/admin/secrets/site-otp/reveal", map[string]any{})
	if str(rv, "value") != totpKey {
		t.Fatalf("reveal: %v", rv)
	}
}

// ── replies that carry values are never cached ───────────────────────────────

func (e *env) headers(method, path string, body any) http.Header {
	e.t.Helper()
	b, _ := json.Marshal(body)
	req := httptest.NewRequest(method, path, bytes.NewReader(b))
	req.Header.Set("Content-Type", "application/json")
	if e.token != "" {
		req.Header.Set("Authorization", "Bearer "+e.token)
	}
	rec := httptest.NewRecorder()
	e.h.ServeHTTP(rec, req)
	return rec.Header()
}

func TestRepliesWithValuesCarryNoStoreAndLogsNeverHoldThem(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.secret("pw-one", pwOne)
	e.vars("finance-portals", plain("SITE_USER", "u"), bySecret("SITE_PASSWORD", "pw-one"))
	runID := e.startRun(e.loginScript("login-script"), "login-script")

	for name, h := range map[string]http.Header{
		"reveal":  e.headers("POST", "/admin/secrets/pw-one/reveal", map[string]any{}),
		"copy":    e.headers("POST", "/admin/secrets/pw-one/copy", map[string]any{}),
		"run env": e.headers("POST", "/jobs/"+runID+"/env", map[string]any{"workerId": "w1", "names": []string{e.fn("SITE_USER"), e.fn("SITE_PASSWORD")}}),
		"sign-in": e.as("").headers("POST", "/auth/login", map[string]any{"username": "admin", "password": "Admin@123#"}),
	} {
		if h.Get("Cache-Control") != "no-store" {
			t.Errorf("%s: Cache-Control is %q", name, h.Get("Cache-Control"))
		}
	}
	time.Sleep(300 * time.Millisecond)
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+pwOne+"%") != 0 {
		t.Fatal("a value reached the audit log")
	}
}
