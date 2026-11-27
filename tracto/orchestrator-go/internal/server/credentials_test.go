package server

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"tracto/orchestrator/internal/handlers"
	"tracto/orchestrator/internal/vault"
)

// ── helpers ──────────────────────────────────────────────────────────────────

func (e *env) group(name string, scripts ...string) {
	e.t.Helper()
	e.must(http.StatusCreated, "POST", "/admin/credential-groups", map[string]any{"name": name, "description": "test", "scripts": scripts})
}

func (e *env) cred(name, group, user, password string) {
	e.t.Helper()
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{
		"name": name, "group": group, "username": user, "password": password, "siteUrl": "https://" + name + ".example.com",
	})
}

func nilLogger() *slog.Logger { return slog.New(slog.NewTextHandler(io.Discard, nil)) }

func (e *env) callInto(dst *map[string]any, method, path string, body any) {
	e.t.Helper()
	_, out := e.call(method, path, body)
	*dst = out
}

// secrets that must never show up in a list, a log or a security event
const (
	pwOne   = "Zebra#Secret-4471"
	pwTwo   = "Quartz#Secret-9203"
	totpKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
)

// loginScript is a one-step script that signs in with a stored credential.
func loginScript(id string) string {
	return `{"id":"` + id + `","name":"` + id + `","version":"1.0.0","targetRuntime":"desktop","steps":[` +
		`{"stepNumber":1,"action":"type","parameters":{"value":"${credential:givewell-portal.username}"}},` +
		`{"stepNumber":2,"action":"type","parameters":{"value":"${credential:givewell-portal.password}"}},` +
		`{"stepNumber":3,"action":"type","parameters":{"value":"${credential:${param.site}.password}"}},` +
		`{"stepNumber":4,"action":"type","parameters":{"value":"${setting:portal-url}"}}]}`
}

// startRun publishes the script, queues it and walks it to Running on worker w1.
func (e *env) startRun(script, id string) string {
	e.t.Helper()
	e.publish(id, "1.0.0", script)
	e.register("w1")
	e.create(id, "1.0.0", map[string]string{"site": "bank.hdfc"})
	o := e.pull("w1")
	e.accept("w1", o)
	return o.runID
}

func (e *env) runCreds(runID, worker string, names ...string) (int, map[string]any) {
	e.t.Helper()
	return e.call("POST", "/jobs/"+runID+"/credentials", map[string]any{"workerId": worker, "names": names})
}

func credsOf(out map[string]any) map[string]any {
	m, _ := out["credentials"].(map[string]any)
	return m
}

// ── CS5: roles and permissions ───────────────────────────────────────────────

func TestEachRoleHoldsExactlyItsPermissions(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)

	type check struct {
		name           string
		method, path   string
		body           map[string]any
		view, op, cadm bool // Viewer, Operator, CredentialAdmin; the Administrator can do all
	}
	checks := []check{
		{"list credentials", "GET", "/admin/credentials", nil, true, true, true},
		{"list groups", "GET", "/admin/credential-groups", nil, true, true, true},
		{"history", "GET", "/admin/credentials/site-a/events", nil, true, true, true},
		{"reveal", "POST", "/admin/credentials/site-a/reveal", map[string]any{}, false, true, true},
		{"create", "POST", "/admin/credentials", map[string]any{"name": "site-%s", "group": "g1", "username": "u", "password": "p"}, false, false, true},
		{"create group", "POST", "/admin/credential-groups", map[string]any{"name": "grp-%s"}, false, false, true},
		{"edit setting", "PUT", "/admin/settings/set-%s", map[string]any{"value": "v"}, false, false, true},
		{"read settings", "GET", "/admin/settings", nil, true, true, true},
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
	code, out := e.as(tok).call("POST", "/admin/credentials", map[string]any{"name": "x", "group": "g", "username": "u", "password": "p"})
	if code != http.StatusForbidden || !strings.Contains(str(out, "message"), "credentials.edit") {
		t.Fatalf("%d %v", code, out)
	}
}

func TestAUserLimitedToGroupsSeesAndTouchesOnlyThem(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals")
	e.group("hr-portals")
	e.cred("bank-a", "finance-portals", "u1", pwOne)
	e.cred("hr-a", "hr-portals", "u2", pwTwo)
	tok := e.addUser("fin-admin", "CredentialAdmin", "Start#Pass1", []string{"finance-portals"})
	c := e.as(tok)

	list := c.must(http.StatusOK, "GET", "/admin/credentials", nil)
	creds, _ := list["credentials"].([]any)
	if len(creds) != 1 || creds[0].(map[string]any)["name"] != "bank-a" {
		t.Fatalf("a limited user sees: %v", creds)
	}
	groups, _ := c.must(http.StatusOK, "GET", "/admin/credential-groups", nil)["groups"].([]any)
	if len(groups) != 1 {
		t.Fatalf("groups seen: %v", groups)
	}
	c.must(http.StatusForbidden, "POST", "/admin/credentials/hr-a/reveal", map[string]any{})
	c.must(http.StatusForbidden, "PUT", "/admin/credentials/hr-a", map[string]any{"username": "x"})
	c.must(http.StatusForbidden, "DELETE", "/admin/credentials/hr-a", nil)
	c.must(http.StatusForbidden, "GET", "/admin/credentials/hr-a/events", nil)
	c.must(http.StatusForbidden, "POST", "/admin/credentials", map[string]any{"name": "new-hr", "group": "hr-portals", "username": "u", "password": "p"})
	c.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{"name": "new-fin", "group": "finance-portals", "username": "u", "password": "p"})
	c.must(http.StatusOK, "POST", "/admin/credentials/bank-a/reveal", map[string]any{})
}

func TestJobsArtifactsAndSchedulesNeedNoSpecialRole(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("just-view", "Viewer", "Start#Pass1", nil)
	e.as(tok).must(http.StatusOK, "GET", "/jobs", nil)
	e.as(tok).must(http.StatusOK, "GET", "/artifacts", nil)
	e.as(tok).must(http.StatusOK, "GET", "/admin/schedules", nil)
}

// ── CS6: encryption and the master key ───────────────────────────────────────

func TestPasswordsAreSealedInTheDatabase(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{
		"name": "site-a", "group": "g1", "username": "alice", "password": pwOne, "totpKey": totpKey,
	})
	var pw, totp, keyID string
	if err := e.db.QueryRow(`SELECT PasswordCipher,TotpCipher,KeyId FROM Credentials WHERE Name='site-a'`).Scan(&pw, &totp, &keyID); err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(pw, "v1:") || !strings.HasPrefix(totp, "v1:") || keyID != "1" {
		t.Fatalf("not sealed: %q %q %q", pw, totp, keyID)
	}
	if strings.Contains(pw, "Zebra") || strings.Contains(totp, "GEZD") {
		t.Fatal("a secret is readable in the row")
	}
	// the whole database holds the secrets nowhere in clear
	for _, q := range []string{
		`SELECT COUNT(*) FROM Credentials WHERE PasswordCipher LIKE '%Zebra%' OR Notes LIKE '%Zebra%' OR Username LIKE '%Zebra%'`,
		`SELECT COUNT(*) FROM CredentialEvents WHERE Detail LIKE '%Zebra%' OR Detail LIKE '%GEZD%'`,
	} {
		if e.count(q) != 0 {
			t.Fatalf("a secret is in the database: %s", q)
		}
	}
}

func TestAValueMovedToAnotherCredentialDoesNotOpen(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)
	e.cred("site-b", "g1", "bob", pwTwo)
	var a string
	_ = e.db.QueryRow(`SELECT PasswordCipher FROM Credentials WHERE Name='site-a'`).Scan(&a)
	if _, err := e.db.Exec(`UPDATE Credentials SET PasswordCipher=$1 WHERE Name='site-b'`, a); err != nil {
		t.Fatal(err)
	}
	code, raw := e.raw("POST", "/admin/credentials/site-b/reveal", map[string]any{})
	if code != http.StatusInternalServerError || strings.Contains(string(raw), pwOne) || strings.Contains(string(raw), pwTwo) {
		t.Fatalf("a moved value opened or leaked: %d %s", code, raw)
	}
}

func TestWithoutAMasterKeyTheStoreIsLockedButJobsStillRun(t *testing.T) {
	e := newAuthEnvWith(t, Config{})
	for _, p := range [][2]string{{"GET", "/admin/credentials"}, {"POST", "/admin/credentials"},
		{"POST", "/admin/credentials/import"}, {"POST", "/admin/credentials/x/reveal"}} {
		code, out := e.call(p[0], p[1], map[string]any{})
		if code != http.StatusServiceUnavailable || str(out, "error") != "CREDENTIAL_STORE_LOCKED" {
			t.Errorf("%s %s: %d %v", p[0], p[1], code, out)
		}
	}
	e.publish("s1", "1.0.0", flatScript("s1", "1.0.0"))
	e.register("w1")
	e.create("s1", "1.0.0", nil)
	if e.pull("w1").runID == "" {
		t.Fatal("jobs do not run while the store is locked")
	}
}

func TestAWrongMasterKeyIsNoticedAtStartAndRekeyMovesEveryRow(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{
		"name": "site-a", "group": "g1", "username": "alice", "password": pwOne, "totpKey": totpKey})
	e.cred("site-b", "g1", "bob", pwTwo)

	one := mustVault(t, vault.TestKey(7), "1")
	if err := handlers.VerifyAll(e.db, one); err != nil {
		t.Fatalf("the right key does not verify: %v", err)
	}
	wrong := mustVault(t, vault.TestKey(9), "1")
	err := handlers.VerifyAll(e.db, wrong)
	if err == nil || strings.Contains(err.Error(), pwOne) || strings.Contains(err.Error(), vault.TestKey(9)) {
		t.Fatalf("a wrong key was not refused cleanly: %v", err)
	}

	// replace the key: new key id 2, old key still held for the move
	two, err := vault.New(vault.TestKey(8), "2", vault.TestKey(7), "1")
	if err != nil {
		t.Fatal(err)
	}
	n, err := handlers.Rekey(e.db, two)
	if err != nil || n != 2 {
		t.Fatalf("rekey: %d %v", n, err)
	}
	if e.count(`SELECT COUNT(*) FROM Credentials WHERE KeyId<>'2'`) != 0 {
		t.Fatal("a row kept the old key id")
	}
	// afterwards the store opens with the new key alone, and not with the old one
	onlyNew := mustVault(t, vault.TestKey(8), "2")
	if err := handlers.VerifyAll(e.db, onlyNew); err != nil {
		t.Fatalf("the new key alone does not open the store: %v", err)
	}
	if err := handlers.VerifyAll(e.db, one); err == nil {
		t.Fatal("the old key still opens a rekeyed store")
	}
	// a second rekey has nothing to do
	if n, _ := handlers.Rekey(e.db, two); n != 0 {
		t.Fatalf("a second rekey changed %d rows", n)
	}

	// and a router started with the new key reads the rekeyed passwords and one-time keys
	e2 := &env{t: t, h: NewRouter(e.db, nilLogger(), Config{Vault: onlyNew}), db: e.db}
	e2.token = e2.login("admin", "Admin@123#") // a new router signs its own sessions
	got := e2.must(http.StatusOK, "POST", "/admin/credentials/site-a/reveal", map[string]any{})
	if str(got, "password") != pwOne {
		t.Fatalf("the rekeyed password: %v", got)
	}
}

// ── CS7: groups ──────────────────────────────────────────────────────────────

func TestGroupsHaveNamesScriptsAndRefuseDeleteWhenNotEmpty(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "Finance"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "has space"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credential-groups", map[string]any{"name": "g", "scripts": []string{"two words"}})
	e.group("finance-portals", "login-script")
	e.must(http.StatusConflict, "POST", "/admin/credential-groups", map[string]any{"name": "finance-portals"})

	e.cred("bank-a", "finance-portals", "u", pwOne)
	code, out := e.call("DELETE", "/admin/credential-groups/finance-portals", nil)
	if code != http.StatusConflict || !strings.Contains(str(out, "message"), "1 credential") {
		t.Fatalf("delete of a non-empty group: %d %v", code, out)
	}

	e.must(http.StatusOK, "PUT", "/admin/credential-groups/finance-portals", map[string]any{"description": "Bank sites", "scripts": []string{"a", "*"}})
	list := e.must(http.StatusOK, "GET", "/admin/credential-groups", nil)["groups"].([]any)
	g := list[0].(map[string]any)
	scripts, _ := g["scripts"].([]any)
	if g["credentialCount"].(float64) != 1 || g["description"] != "Bank sites" || len(scripts) != 2 {
		t.Fatalf("group: %v", g)
	}

	e.must(http.StatusNoContent, "DELETE", "/admin/credentials/bank-a", nil)
	e.must(http.StatusNoContent, "DELETE", "/admin/credential-groups/finance-portals", nil)
	e.must(http.StatusNotFound, "DELETE", "/admin/credential-groups/finance-portals", nil)
}

// ── CS8: credentials API ─────────────────────────────────────────────────────

func TestCredentialNamesFollowTheRuleAndNeedAGroup(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	for _, bad := range []string{"GiveWell", "give_well", "-start", "has space", "", strings.Repeat("a", 101)} {
		code, _ := e.call("POST", "/admin/credentials", map[string]any{"name": bad, "group": "g1", "username": "u", "password": "p"})
		if code != http.StatusUnprocessableEntity {
			t.Errorf("name %q: %d", bad, code)
		}
	}
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials", map[string]any{"name": "ok", "group": "nope", "username": "u", "password": "p"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials", map[string]any{"name": "ok", "group": "g1", "username": "", "password": "p"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials", map[string]any{"name": "ok", "group": "g1", "username": "u", "password": ""})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials", map[string]any{"name": "ok", "group": "g1", "username": "u", "password": "p", "totpKey": "not*base32"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials", map[string]any{"name": "ok", "group": "g1", "username": "u", "password": "p", "expiresOn": "31/12/2027"})
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{"name": "bank.hdfc-1", "group": "g1", "username": "u", "password": "p"})
	e.must(http.StatusConflict, "POST", "/admin/credentials", map[string]any{"name": "bank.hdfc-1", "group": "g1", "username": "u", "password": "p"})
}

func TestNoReplyOrLogEverHoldsAPassword(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	code, raw := e.raw("POST", "/admin/credentials", map[string]any{"name": "site-a", "group": "g1", "username": "alice", "password": pwOne, "totpKey": totpKey})
	if code != http.StatusCreated || strings.Contains(string(raw), pwOne) || strings.Contains(string(raw), totpKey) {
		t.Fatalf("create reply: %d %s", code, raw)
	}
	_, list := e.raw("GET", "/admin/credentials", nil)
	_, one := e.raw("GET", "/admin/credentials?group=g1&q=alice", nil)
	_, hist := e.raw("GET", "/admin/credentials/site-a/events", nil)
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"password": pwTwo})
	e.must(http.StatusOK, "POST", "/admin/credentials/site-a/reveal", map[string]any{})
	for name, body := range map[string]string{"list": string(list), "filtered list": string(one), "history": string(hist)} {
		for _, s := range []string{pwOne, pwTwo, totpKey, "PasswordCipher", "v1:"} {
			if strings.Contains(body, s) {
				t.Errorf("%s contains %q", name, s)
			}
		}
	}
	time.Sleep(300 * time.Millisecond)
	for _, s := range []string{pwOne, pwTwo, totpKey} {
		if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+s+"%"); n != 0 {
			t.Errorf("a secret reached the audit log %d time(s)", n)
		}
	}
}

func TestEditingKeepsThePasswordUnlessANewOneIsGiven(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.group("g2")
	e.cred("site-a", "g1", "alice", pwOne)

	reveal := func() string {
		return str(e.must(http.StatusOK, "POST", "/admin/credentials/site-a/reveal", map[string]any{}), "password")
	}
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"username": "alice2", "notes": "n", "password": ""})
	if reveal() != pwOne {
		t.Fatal("an empty password replaced the old one")
	}
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"group": "g2", "siteUrl": "https://new.example.com"})
	if reveal() != pwOne {
		t.Fatal("moving the group lost the password")
	}
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"password": pwTwo})
	if reveal() != pwTwo {
		t.Fatal("the new password was not stored")
	}
	var ver int
	var group, user string
	_ = e.db.QueryRow(`SELECT Version,GroupName,Username FROM Credentials WHERE Name='site-a'`).Scan(&ver, &group, &user)
	if ver != 4 || group != "g2" || user != "alice2" {
		t.Fatalf("version %d group %s user %s", ver, group, user)
	}
	e.must(http.StatusNotFound, "PUT", "/admin/credentials/ghost", map[string]any{"username": "x"})
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/credentials/site-a", map[string]any{"group": "nope"})
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/credentials/site-a", map[string]any{"username": ""})
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/credentials/site-a", map[string]any{"totpKey": "not*base32"})
}

func TestTheOneTimeKeyCanBeAddedChangedAndRemoved(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)
	has := func() bool {
		for _, c := range e.must(http.StatusOK, "GET", "/admin/credentials", nil)["credentials"].([]any) {
			return c.(map[string]any)["hasTotp"].(bool)
		}
		return false
	}
	if has() {
		t.Fatal("hasTotp before a key was given")
	}
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"totpKey": "gezd gnbv gy3t qojq gezd gnbv gy3t qojq"})
	if !has() {
		t.Fatal("hasTotp after a key was given")
	}
	e.must(http.StatusOK, "PUT", "/admin/credentials/site-a", map[string]any{"totpKey": ""})
	if has() {
		t.Fatal("the key was not removed")
	}
}

func TestRevealAndCopyAreRecordedWithTheUserAndHistoryIsNewestFirst(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)
	op := e.addUser("opera", "Operator", "Start#Pass1", nil)

	out := e.as(op).must(http.StatusOK, "POST", "/admin/credentials/site-a/reveal", map[string]any{})
	if str(out, "password") != pwOne {
		t.Fatalf("reveal: %v", out)
	}
	e.as(op).must(http.StatusOK, "POST", "/admin/credentials/site-a/reveal", map[string]any{"purpose": "copy"})

	events := e.must(http.StatusOK, "GET", "/admin/credentials/site-a/events", nil)["events"].([]any)
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
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)
	v := e.addUser("viewer1", "Viewer", "Start#Pass1", nil)
	e.as(v).must(http.StatusForbidden, "POST", "/admin/credentials/site-a/reveal", map[string]any{})
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='REVEAL_DENIED' AND Actor='viewer1' AND Credential='site-a'`) != 1 {
		t.Fatal("a refused reveal was not recorded")
	}
}

func TestDeletingKeepsTheHistory(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.cred("site-a", "g1", "alice", pwOne)
	e.must(http.StatusNoContent, "DELETE", "/admin/credentials/site-a", nil)
	e.must(http.StatusNotFound, "DELETE", "/admin/credentials/site-a", nil)
	events := e.must(http.StatusOK, "GET", "/admin/credentials/site-a/events", nil)["events"].([]any)
	if len(events) != 2 {
		t.Fatalf("history after delete: %v", events)
	}
	e.must(http.StatusNotFound, "POST", "/admin/credentials/site-a/reveal", map[string]any{})
}

// ── CS10: expiry or no expiry ────────────────────────────────────────────────

func TestExpiryStatusNoExpirySoonExpiredAndOk(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	day := func(d int) string { return time.Now().UTC().AddDate(0, 0, d).Format("2006-01-02") }
	mk := func(name string, exp any) {
		body := map[string]any{"name": name, "group": "g1", "username": "u", "password": "p"}
		if exp != nil {
			body["expiresOn"] = exp
		}
		e.must(http.StatusCreated, "POST", "/admin/credentials", body)
	}
	mk("never", nil)
	mk("never-null", "")
	mk("tomorrow", day(1))
	mk("in-14-days", day(14))
	mk("in-30-days", day(30))
	mk("yesterday", day(-1))

	out := e.must(http.StatusOK, "GET", "/admin/credentials", nil)
	status := map[string]string{}
	for _, c := range out["credentials"].([]any) {
		m := c.(map[string]any)
		status[m["name"].(string)] = m["expiryStatus"].(string)
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
	// a credential with no expiry stays out of both counts, and a date can be removed again
	e.must(http.StatusOK, "PUT", "/admin/credentials/yesterday", map[string]any{"expiresOn": nil})
	sum = e.must(http.StatusOK, "GET", "/admin/credentials", nil)["summary"].(map[string]any)
	if sum["expired"].(float64) != 0 {
		t.Fatalf("setting no expiry did not clear the date: %v", sum)
	}
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/credentials/never", map[string]any{"expiresOn": "tomorrow"})
}

func TestTheListCanBeFilteredAndSearched(t *testing.T) {
	e := newAuthEnv(t)
	e.group("g1")
	e.group("g2")
	e.cred("bank-a", "g1", "alice", "p")
	e.cred("bank-b", "g2", "bob", "p")
	e.cred("portal-c", "g2", "carol", "p")
	count := func(q string) int {
		return len(e.must(http.StatusOK, "GET", "/admin/credentials"+q, nil)["credentials"].([]any))
	}
	if count("") != 3 || count("?group=g2") != 2 || count("?q=BANK") != 2 || count("?q=carol") != 1 || count("?q=example.com") != 3 || count("?group=g1&q=bob") != 0 {
		t.Fatal("filter or search is wrong")
	}
}

// ── CS11: import ─────────────────────────────────────────────────────────────

func importRows(n int) []map[string]any {
	rows := make([]map[string]any, 0, n)
	for i := 1; i <= n; i++ {
		rows = append(rows, map[string]any{
			"name": fmt.Sprintf("portal-%03d", i), "group": "finance-portals", "username": fmt.Sprintf("user%03d", i),
			"password": fmt.Sprintf("Pw#%03d-Secret", i), "siteUrl": fmt.Sprintf("https://p%03d.example.com", i), "expiresOn": "",
		})
	}
	return rows
}

func TestImportOfTwoHundredPortalsIsAllOrNothing(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals")

	rows := importRows(200)
	dry := e.must(http.StatusOK, "POST", "/admin/credentials/import", map[string]any{"rows": rows, "dryRun": true})
	if dry["wouldCreate"].(float64) != 200 || e.count(`SELECT COUNT(*) FROM Credentials`) != 0 {
		t.Fatalf("dry run: %v", dry)
	}

	// one bad row stops everything and names the row
	bad := importRows(200)
	bad[41]["group"] = "no-such-group"
	bad[99]["name"] = "Bad Name"
	bad[150]["name"] = bad[10]["name"]
	code, out := e.call("POST", "/admin/credentials/import", map[string]any{"rows": bad})
	errs, _ := out["errors"].([]any)
	if code != http.StatusUnprocessableEntity || len(errs) != 3 || e.count(`SELECT COUNT(*) FROM Credentials`) != 0 {
		t.Fatalf("a bad row: %d %v (saved %d)", code, out, e.count(`SELECT COUNT(*) FROM Credentials`))
	}
	first := errs[0].(map[string]any)
	if first["row"].(float64) != 42 {
		t.Fatalf("the first error should name row 42: %v", first)
	}

	done := e.must(http.StatusOK, "POST", "/admin/credentials/import", map[string]any{"rows": rows})
	if done["created"].(float64) != 200 || e.count(`SELECT COUNT(*) FROM Credentials`) != 200 {
		t.Fatalf("import: %v", done)
	}
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='CREATED'`) != 200 {
		t.Fatal("one CREATED event per credential expected")
	}
	if e.count(`SELECT COUNT(*) FROM Credentials WHERE ExpiresOn IS NULL`) != 200 {
		t.Fatal("an empty expiresOn means no expiry")
	}
	got := e.must(http.StatusOK, "POST", "/admin/credentials/portal-137/reveal", map[string]any{})
	if str(got, "password") != "Pw#137-Secret" {
		t.Fatalf("an imported password: %v", got)
	}
	time.Sleep(300 * time.Millisecond)
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE '%Secret%' AND Details LIKE '%Pw#%'`) != 0 {
		t.Fatal("an imported password reached the audit log")
	}
}

func TestImportRefusesExistingNamesUnlessReplaceIsTicked(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals")
	e.cred("portal-001", "finance-portals", "old-user", "old-pass")

	rows := importRows(3)
	code, out := e.call("POST", "/admin/credentials/import", map[string]any{"rows": rows})
	if code != http.StatusUnprocessableEntity || len(out["errors"].([]any)) != 1 || e.count(`SELECT COUNT(*) FROM Credentials`) != 1 {
		t.Fatalf("existing name: %d %v", code, out)
	}
	done := e.must(http.StatusOK, "POST", "/admin/credentials/import", map[string]any{"rows": rows, "replaceExisting": true})
	if done["created"].(float64) != 2 || done["replaced"].(float64) != 1 {
		t.Fatalf("replace: %v", done)
	}
	var user string
	var ver int
	_ = e.db.QueryRow(`SELECT Username,Version FROM Credentials WHERE Name='portal-001'`).Scan(&user, &ver)
	if user != "user001" || ver != 2 {
		t.Fatalf("replaced row: %s v%d", user, ver)
	}
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/credentials/import", map[string]any{"rows": []any{}})
}

func TestImportHonoursAGroupLimitedUser(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals")
	e.group("hr-portals")
	tok := e.addUser("fin", "CredentialAdmin", "Start#Pass1", []string{"finance-portals"})
	rows := importRows(2)
	rows[1]["group"] = "hr-portals"
	code, out := e.as(tok).call("POST", "/admin/credentials/import", map[string]any{"rows": rows})
	if code != http.StatusUnprocessableEntity || e.count(`SELECT COUNT(*) FROM Credentials`) != 0 {
		t.Fatalf("%d %v", code, out)
	}
}

// ── CS12: settings ───────────────────────────────────────────────────────────

func TestSettingsAreEditableByTheRightRoleAndRefuseSecrets(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusOK, "PUT", "/admin/settings/portal-url", map[string]any{"value": "https://portal.example.com", "notes": "main portal"})
	e.must(http.StatusOK, "PUT", "/admin/settings/portal-url", map[string]any{"value": "https://portal2.example.com"})
	list := e.must(http.StatusOK, "GET", "/admin/settings", nil)["settings"].([]any)
	if len(list) != 1 || list[0].(map[string]any)["value"] != "https://portal2.example.com" {
		t.Fatalf("settings: %v", list)
	}
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/settings/Bad_Name", map[string]any{"value": "x"})
	for _, v := range []string{"password=hunter2", "api_key: abcdef", "Bearer abcdefghij", "-----BEGIN PRIVATE KEY-----"} {
		code, out := e.call("PUT", "/admin/settings/x", map[string]any{"value": v})
		if code != http.StatusUnprocessableEntity || str(out, "error") != "LOOKS_LIKE_A_SECRET" {
			t.Errorf("%q: %d %v", v, code, out)
		}
	}
	viewer := e.addUser("v2", "Viewer", "Start#Pass1", nil)
	e.as(viewer).must(http.StatusOK, "GET", "/admin/settings", nil)
	e.as(viewer).must(http.StatusForbidden, "PUT", "/admin/settings/portal-url", map[string]any{"value": "x"})
	e.as(viewer).must(http.StatusForbidden, "DELETE", "/admin/settings/portal-url", nil)
	e.must(http.StatusNoContent, "DELETE", "/admin/settings/portal-url", nil)
	e.must(http.StatusNotFound, "DELETE", "/admin/settings/portal-url", nil)
}

// ── CS13: credentials and settings for a run ─────────────────────────────────

func TestARunningJobGetsTheCredentialsItsScriptNames(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.cred("givewell-portal", "finance-portals", "fin.ops@acme.com", pwOne)
	e.cred("bank.hdfc", "finance-portals", "ACME001", pwTwo)
	e.must(http.StatusOK, "PUT", "/admin/settings/portal-url", map[string]any{"value": "https://portal.example.com"})
	runID := e.startRun(loginScript("login-script"), "login-script")

	code, out := e.runCreds(runID, "w1", "givewell-portal", "bank.hdfc")
	if code != http.StatusOK {
		t.Fatalf("%d %v", code, out)
	}
	c := credsOf(out)
	gw := c["givewell-portal"].(map[string]any)
	hd := c["bank.hdfc"].(map[string]any)
	if gw["username"] != "fin.ops@acme.com" || gw["password"] != pwOne || hd["password"] != pwTwo {
		t.Fatalf("values: %v", c)
	}
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_BY_RUN' AND Actor=$1`, "run:"+runID+"@w1") != 2 {
		t.Fatal("each read should be recorded with the run and worker")
	}

	st := e.must(http.StatusOK, "POST", "/jobs/"+runID+"/settings", map[string]any{"workerId": "w1", "names": []string{"portal-url"}})
	if st["settings"].(map[string]any)["portal-url"] != "https://portal.example.com" {
		t.Fatalf("settings: %v", st)
	}
}

func TestAReadIsRefusedForEveryWrongSituation(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.group("other-portals", "another-script")
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	e.cred("bank.hdfc", "finance-portals", "u", pwTwo)
	e.cred("secret-elsewhere", "other-portals", "u", "p")
	runID := e.startRun(loginScript("login-script"), "login-script")

	denied := func(why string, code int, out map[string]any) {
		t.Helper()
		if code != http.StatusForbidden {
			t.Errorf("%s: status %d %v", why, code, out)
		}
		if b := fmt.Sprint(out); strings.Contains(b, pwOne) || strings.Contains(b, pwTwo) {
			t.Errorf("%s: the refusal leaked a password", why)
		}
	}
	code, out := e.runCreds(runID, "another-worker", "givewell-portal")
	denied("another worker", code, out)
	code, out = e.runCreds(runID, "", "givewell-portal")
	denied("no worker", code, out)
	code, out = e.runCreds(runID, "w1", "secret-elsewhere")
	denied("a credential the script does not name", code, out)
	code, out = e.runCreds(runID, "w1", "givewell-portal", "secret-elsewhere")
	denied("one allowed and one not: nothing is returned", code, out)
	code, out = e.runCreds(runID, "w1", "Givewell-Portal")
	denied("a name with a capital", code, out)
	code, out = e.runCreds(runID, "w1", "no-such-credential")
	denied("a name the script does not name", code, out)

	// a credential the script names through a parameter, but whose group the script may not read
	e.must(http.StatusOK, "PUT", "/admin/credential-groups/finance-portals", map[string]any{"scripts": []string{"another-script"}})
	code, out = e.runCreds(runID, "w1", "givewell-portal")
	denied("a group the script may not read", code, out)

	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_DENIED'`) < 5 {
		t.Fatal("refusals should be recorded")
	}
	if e.count(`SELECT COUNT(*) FROM CredentialEvents WHERE Kind='READ_BY_RUN'`) != 0 {
		t.Fatal("a refused read was recorded as a read")
	}
}

func TestAFinishedRunCannotReadCredentials(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	e.publish("login-script", "1.0.0", loginScript("login-script"))
	e.register("w1")
	e.create("login-script", "1.0.0", map[string]string{"site": "bank.hdfc"})

	// queued, not yet accepted
	o := e.pull("w1")
	if code, _ := e.runCreds(o.runID, "w1", "givewell-portal"); code != http.StatusForbidden {
		t.Fatalf("a run not yet running read credentials: %d", code)
	}
	e.accept("w1", o)
	if code, _ := e.runCreds(o.runID, "w1", "givewell-portal"); code != http.StatusOK {
		t.Fatalf("a running run was refused: %d", code)
	}
	e.finish(o, "Succeeded", nil)
	if code, _ := e.runCreds(o.runID, "w1", "givewell-portal"); code != http.StatusForbidden {
		t.Fatalf("a finished run read credentials: %d", code)
	}
	e.must(http.StatusNotFound, "POST", "/jobs/nope/credentials", map[string]any{"workerId": "w1", "names": []string{"givewell-portal"}})
}

func TestACredentialNamedThroughAParameterIsReadableWhenItsGroupAllowsTheScript(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.cred("bank.hdfc", "finance-portals", "ACME001", pwTwo)
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	runID := e.startRun(loginScript("login-script"), "login-script")
	// ${credential:${param.site}.password}: any credential name fits the pattern, the group decides
	if code, out := e.runCreds(runID, "w1", "bank.hdfc"); code != http.StatusOK || credsOf(out)["bank.hdfc"].(map[string]any)["password"] != pwTwo {
		t.Fatalf("%d %v", code, out)
	}
}

func TestASegmentOfAScriptReadsWithTheMainScriptsAccess(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "two-stage")
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	script := `{"id":"two-stage","name":"two-stage","version":"1.0.0","segments":[` +
		`{"name":"first","runtime":"desktop","steps":[{"stepNumber":1,"action":"delay","parameters":{"ms":"1"}}]},` +
		`{"name":"second","runtime":"desktop","steps":[{"stepNumber":1,"action":"type","parameters":{"value":"${credential:givewell-portal.password}"}}]}]}`
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
	code, out := e.runCreds(o2.runID, "w1", "givewell-portal")
	if code != http.StatusOK || credsOf(out)["givewell-portal"].(map[string]any)["password"] != pwOne {
		t.Fatalf("segment read: %d %v", code, out)
	}
}

func TestSettingsForARunFollowTheSameChecks(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.must(http.StatusOK, "PUT", "/admin/settings/portal-url", map[string]any{"value": "https://p.example.com"})
	e.must(http.StatusOK, "PUT", "/admin/settings/not-in-script", map[string]any{"value": "x"})
	runID := e.startRun(loginScript("login-script"), "login-script")
	post := func(worker string, names ...string) int {
		code, _ := e.call("POST", "/jobs/"+runID+"/settings", map[string]any{"workerId": worker, "names": names})
		return code
	}
	if post("w1", "portal-url") != http.StatusOK || post("other", "portal-url") != http.StatusForbidden ||
		post("w1", "not-in-script") != http.StatusForbidden || post("w1", "ghost") != http.StatusForbidden {
		t.Fatal("settings checks are wrong")
	}
}

func TestAnExpiredCredentialIsStillGivenWithAWarningInTheRunLog(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{
		"name": "givewell-portal", "group": "finance-portals", "username": "u", "password": pwOne,
		"expiresOn": time.Now().UTC().AddDate(0, 0, -3).Format("2006-01-02"),
	})
	runID := e.startRun(loginScript("login-script"), "login-script")
	code, out := e.runCreds(runID, "w1", "givewell-portal")
	if code != http.StatusOK || credsOf(out)["givewell-portal"].(map[string]any)["password"] != pwOne {
		t.Fatalf("%d %v", code, out)
	}
	if e.count(`SELECT COUNT(*) FROM RunLogs WHERE RunId=$1 AND Level='Warning' AND Message LIKE '%givewell-portal expired on%'`, runID) != 1 {
		t.Fatal("no warning in the run log")
	}
}

func TestTheReplyIsNeverCachedAndLogsNeverHoldPasswords(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	runID := e.startRun(loginScript("login-script"), "login-script")
	e.runCreds(runID, "w1", "givewell-portal")
	time.Sleep(300 * time.Millisecond)
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+pwOne+"%") != 0 {
		t.Fatal("a password reached the audit log")
	}
}

// ── CS17: one-time codes ─────────────────────────────────────────────────────

func TestARunGetsTheCurrentCodeAndNeverTheKey(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.must(http.StatusCreated, "POST", "/admin/credentials", map[string]any{
		"name": "givewell-portal", "group": "finance-portals", "username": "u", "password": pwOne, "totpKey": totpKey})
	runID := e.startRun(loginScript("login-script"), "login-script")

	code, raw := e.raw("POST", "/jobs/"+runID+"/credentials", map[string]any{"workerId": "w1", "names": []string{"givewell-portal"}})
	if code != http.StatusOK || strings.Contains(string(raw), totpKey) {
		t.Fatalf("%d %s", code, raw)
	}
	out := map[string]any{}
	e.callInto(&out, "POST", "/jobs/"+runID+"/credentials", map[string]any{"workerId": "w1", "names": []string{"givewell-portal"}})
	got := credsOf(out)["givewell-portal"].(map[string]any)["totp"].(string)

	// the code is one of the windows around now (a request near the edge waits for the next window)
	now := time.Now()
	valid := map[string]bool{}
	for _, off := range []time.Duration{-30 * time.Second, 0, 30 * time.Second} {
		c, _ := vault.TOTPCode(totpKey, now.Add(off))
		valid[c] = true
	}
	if len(got) != 6 || !valid[got] {
		t.Fatalf("code %q is not a current code", got)
	}

	// the reveal in the UI gives the password only
	rv := e.must(http.StatusOK, "POST", "/admin/credentials/givewell-portal/reveal", map[string]any{})
	if _, has := rv["totpKey"]; has || strings.Contains(fmt.Sprint(rv), totpKey) {
		t.Fatalf("reveal leaked the key: %v", rv)
	}
}

// ── replies that carry secrets are never cached ──────────────────────────────

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

func TestRepliesWithSecretsCarryNoStore(t *testing.T) {
	e := newAuthEnv(t)
	e.group("finance-portals", "login-script")
	e.cred("givewell-portal", "finance-portals", "u", pwOne)
	e.must(http.StatusOK, "PUT", "/admin/settings/portal-url", map[string]any{"value": "https://p.example.com"})
	runID := e.startRun(loginScript("login-script"), "login-script")

	for name, h := range map[string]http.Header{
		"reveal":      e.headers("POST", "/admin/credentials/givewell-portal/reveal", map[string]any{}),
		"run creds":   e.headers("POST", "/jobs/"+runID+"/credentials", map[string]any{"workerId": "w1", "names": []string{"givewell-portal"}}),
		"run setting": e.headers("POST", "/jobs/"+runID+"/settings", map[string]any{"workerId": "w1", "names": []string{"portal-url"}}),
		"login":       e.as("").headers("POST", "/auth/login", map[string]any{"username": "admin", "password": "Admin@123#"}),
	} {
		if h.Get("Cache-Control") != "no-store" {
			t.Errorf("%s: Cache-Control is %q", name, h.Get("Cache-Control"))
		}
	}
}