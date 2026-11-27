package server

import (
	"io"
	"log/slog"
	"net/http"
	"strings"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"

	"tracto/orchestrator/internal/auth"
	"tracto/orchestrator/internal/db/dbtest"
	"tracto/orchestrator/internal/vault"
)

// newAuthEnv is the router with a master key, the first administrator created, and the test
// signed in as that administrator.
func newAuthEnv(t *testing.T) *env {
	t.Helper()
	return newAuthEnvWith(t, Config{Vault: mustVault(t, vault.TestKey(7), "1")})
}

func mustVault(t *testing.T, key, id string) *vault.Vault {
	t.Helper()
	v, err := vault.New(key, id, "", "")
	if err != nil {
		t.Fatal(err)
	}
	return v
}

func newAuthEnvWith(t *testing.T, cfg Config) *env {
	t.Helper()
	d := dbtest.Open(t)
	if err := auth.EnsureFirstAdmin(d, nil); err != nil {
		t.Fatal(err)
	}
	e := &env{t: t, h: NewRouter(d, slog.New(slog.NewTextHandler(io.Discard, nil)), cfg), db: d}
	e.token = e.login(auth.FirstAdminUsername, auth.FirstAdminPassword)
	return e
}

// login signs in and returns the session token.
func (e *env) login(user, password string) string {
	e.t.Helper()
	saved := e.token
	e.token = ""
	defer func() { e.token = saved }()
	out := e.must(http.StatusOK, "POST", "/auth/login", map[string]any{"username": user, "password": password})
	return str(out, "token")
}

// as returns the same environment acting as another signed-in user.
func (e *env) as(token string) *env {
	c := *e
	c.token = token
	return &c
}

// addUser creates a user as the administrator and returns that user's token.
func (e *env) addUser(name, role, password string, groups []string) string {
	e.t.Helper()
	body := map[string]any{"username": name, "role": role, "password": password}
	if groups != nil {
		body["allowedGroups"] = groups
	}
	e.must(http.StatusCreated, "POST", "/admin/users", body)
	return e.login(name, password)
}

// ── CS1: users table and the first administrator ─────────────────────────────

func TestFirstAdministratorIsCreatedOnceWithAHashedPassword(t *testing.T) {
	e := newAuthEnv(t)

	var hash, role string
	if err := e.db.QueryRow(`SELECT PasswordHash,Role FROM Users WHERE Username='admin'`).Scan(&hash, &role); err != nil {
		t.Fatal(err)
	}
	if role != "Administrator" || !strings.HasPrefix(hash, "$2") || strings.Contains(hash, "Admin@123#") {
		t.Fatalf("first administrator row is wrong: role=%s hash=%q", role, hash)
	}
	if !auth.CheckPassword(hash, "Admin@123#") {
		t.Fatal("the stored hash does not match the first password")
	}

	// A second start-up changes nothing, even after the password was changed.
	e.must(http.StatusOK, "POST", "/auth/password", map[string]any{"currentPassword": "Admin@123#", "newPassword": "Better#Pass9"})
	var before string
	_ = e.db.QueryRow(`SELECT PasswordHash FROM Users WHERE Username='admin'`).Scan(&before)
	if err := auth.EnsureFirstAdmin(e.db, nil); err != nil {
		t.Fatal(err)
	}
	var after string
	_ = e.db.QueryRow(`SELECT PasswordHash FROM Users WHERE Username='admin'`).Scan(&after)
	if before != after || e.count(`SELECT COUNT(*) FROM Users`) != 1 {
		t.Fatal("a second start-up changed the users")
	}
}

func TestNoFirstAdministratorWhenAnyUserExists(t *testing.T) {
	d := dbtest.Open(t)
	h, _ := auth.HashPassword("Other#Pass1")
	if _, err := d.Exec(`INSERT INTO Users(Username,PasswordHash,Role,CreatedAt,UpdatedAt) VALUES('someone',$1,'Viewer',now(),now())`, h); err != nil {
		t.Fatal(err)
	}
	if err := auth.EnsureFirstAdmin(d, nil); err != nil {
		t.Fatal(err)
	}
	var n int
	_ = d.QueryRow(`SELECT COUNT(*) FROM Users WHERE lower(Username)='admin'`).Scan(&n)
	if n != 0 {
		t.Fatal("an administrator was created although a user existed")
	}
}

func TestUsernamesAreUniqueAndSignInIgnoresCapitals(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusCreated, "POST", "/admin/users", map[string]any{"username": "Priya", "role": "Viewer", "password": "Start#Pass1"})
	e.must(http.StatusConflict, "POST", "/admin/users", map[string]any{"username": "PRIYA", "role": "Viewer", "password": "Start#Pass1"})
	if e.login("pRiYa", "Start#Pass1") == "" {
		t.Fatal("sign-in with other capitals failed")
	}
	e.login("ADMIN", "Admin@123#")
}

// ── CS2: login and sessions ──────────────────────────────────────────────────

func TestLoginReturnsASessionAndMeShowsTheUser(t *testing.T) {
	e := newAuthEnv(t)
	out := e.must(http.StatusOK, "GET", "/auth/me", nil)
	if str(out, "username") != "admin" || str(out, "role") != "Administrator" {
		t.Fatalf("me: %v", out)
	}
	perms, _ := out["permissions"].([]any)
	if len(perms) != 6 {
		t.Fatalf("an administrator holds 6 permissions: %v", perms)
	}
	login := e.as("").must(http.StatusOK, "POST", "/auth/login", map[string]any{"username": "admin", "password": "Admin@123#"})
	if str(login, "token") == "" || str(login, "expiresAt") == "" {
		t.Fatalf("login reply: %v", login)
	}
	exp, _ := time.Parse(time.RFC3339, str(login, "expiresAt"))
	if d := time.Until(exp); d < 7*time.Hour || d > 8*time.Hour+time.Minute {
		t.Fatalf("a session lasts 8 hours, got %v", d)
	}
}

func TestWrongUsernameAndWrongPasswordGetTheSameMessageAndAreRecorded(t *testing.T) {
	e := newAuthEnv(t).as("")
	_, a := e.call("POST", "/auth/login", map[string]any{"username": "admin", "password": "wrong-Password-1"})
	_, b := e.call("POST", "/auth/login", map[string]any{"username": "nobody", "password": "wrong-Password-1"})
	if str(a, "message") != "Username or password is wrong" || str(a, "message") != str(b, "message") || str(a, "error") != str(b, "error") {
		t.Fatalf("messages differ: %v / %v", a, b)
	}
	code, _ := e.call("POST", "/auth/login", map[string]any{"username": "admin", "password": "wrong-Password-1"})
	if code != http.StatusUnauthorized {
		t.Fatalf("status %d", code)
	}
	if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE EventType='SIGN_IN_FAILED' AND Details LIKE '%nobody%'`); n != 1 {
		t.Fatalf("failed sign-ins recorded: %d", n)
	}
	if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE '%wrong-Password-1%'`); n != 0 {
		t.Fatal("a password reached the security events")
	}
}

func TestDisabledUserCannotSignIn(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("dana", "Viewer", "Start#Pass1", nil)
	e.must(http.StatusOK, "PUT", "/admin/users/dana", map[string]any{"disabled": true})
	code, out := e.as("").call("POST", "/auth/login", map[string]any{"username": "dana", "password": "Start#Pass1"})
	if code != http.StatusUnauthorized || str(out, "message") != "Username or password is wrong" {
		t.Fatalf("%d %v", code, out)
	}
	// and the session she already held ends at once
	if code, _ := e.as(tok).call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatalf("a disabled user's session still works: %d", code)
	}
}

func TestProtectedRoutesNeedASessionEvenWithNoSigningKey(t *testing.T) {
	e := newAuthEnv(t)
	anon := e.as("")
	for _, p := range [][2]string{{"GET", "/auth/me"}, {"GET", "/admin/users"}, {"GET", "/admin/credentials"},
		{"GET", "/admin/credential-groups"}, {"GET", "/admin/settings"}, {"POST", "/auth/password"}} {
		if code, out := anon.call(p[0], p[1], nil); code != http.StatusUnauthorized || str(out, "error") != "AUTH_REQUIRED" {
			t.Errorf("%s %s without a session: %d %v", p[0], p[1], code, out)
		}
	}
	if code, _ := anon.call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatal("me works without a session")
	}
	if code, _ := e.as("not.a.token").call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatal("a garbage token was accepted")
	}
	// the routes workers use are untouched
	anon.must(http.StatusOK, "GET", "/health", nil)
}

func TestSessionAndWorkerTokensAreNotInterchangeable(t *testing.T) {
	const secret = "a-shared-signing-key"
	e := newAuthEnvWith(t, Config{JWTSecret: secret, Vault: mustVault(t, vault.TestKey(7), "1")})

	// the user's session works on the session routes
	e.must(http.StatusOK, "GET", "/auth/me", nil)
	e.must(http.StatusOK, "GET", "/admin/credentials", nil)

	// a worker token (signed with the raw key) is not a session
	worker, _ := jwt.NewWithClaims(jwt.SigningMethodHS256, jwt.MapClaims{
		"sub": "admin", "role": "Administrator", "sv": 1, "exp": time.Now().Add(time.Hour).Unix(),
	}).SignedString([]byte(secret))
	if code, _ := e.as(worker).call("GET", "/admin/credentials", nil); code != http.StatusUnauthorized {
		t.Fatalf("a worker token opened the credential store: %d", code)
	}
	// and a user's session is not a worker token
	if code, _ := e.call("GET", "/jobs", nil); code != http.StatusUnauthorized {
		t.Fatalf("a user session was accepted on /jobs: %d", code)
	}
	// a worker token still works for workers
	if code, _ := e.as(worker).call("GET", "/jobs", nil); code != http.StatusOK {
		t.Fatalf("a worker token no longer works on /jobs: %d", code)
	}
}

func TestSignOutIsRecordedAndNeedsASession(t *testing.T) {
	e := newAuthEnv(t)
	if code, _ := e.as("").call("POST", "/auth/logout", nil); code != http.StatusUnauthorized {
		t.Fatal("logout without a session")
	}
	if code, _ := e.call("POST", "/auth/logout", nil); code != http.StatusNoContent {
		t.Fatal("logout failed")
	}
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE EventType='SIGN_OUT'`) != 1 {
		t.Fatal("sign-out not recorded")
	}
}

// ── CS3: password change ─────────────────────────────────────────────────────

func TestPasswordRulesAreEnforcedWithTheRuleNamed(t *testing.T) {
	e := newAuthEnv(t)
	cases := map[string]string{
		"Sh0rt#":        "at least 8",
		"nocapital#123": "capital",
		"NOSMALL#123":   "small",
		"NoDigits#here": "digit",
		"NoSymbol1234a": "symbol",
		"Admin@123#":    "differ",
	}
	for pw, want := range cases {
		code, out := e.call("POST", "/auth/password", map[string]any{"currentPassword": "Admin@123#", "newPassword": pw})
		if code != http.StatusUnprocessableEntity || !strings.Contains(strings.ToLower(str(out, "message")), want) {
			t.Errorf("%q: %d %v (want a message with %q)", pw, code, out, want)
		}
	}
	// nothing changed: the old password still signs in
	e.login("admin", "Admin@123#")
}

func TestChangingThePasswordNeedsTheCurrentOne(t *testing.T) {
	e := newAuthEnv(t)
	code, out := e.call("POST", "/auth/password", map[string]any{"currentPassword": "not-it-A1#", "newPassword": "Fresh#Pass12"})
	if code != http.StatusUnprocessableEntity || str(out, "message") != "Current password is wrong" {
		t.Fatalf("%d %v", code, out)
	}
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE EventType='PASSWORD_CHANGE_REFUSED'`) != 1 {
		t.Fatal("the refusal was not recorded")
	}
}

func TestChangingThePasswordEndsOtherSessionsAndKeepsThisOne(t *testing.T) {
	e := newAuthEnv(t)
	other := e.login("admin", "Admin@123#") // a second session of the same user

	out := e.must(http.StatusOK, "POST", "/auth/password", map[string]any{"currentPassword": "Admin@123#", "newPassword": "Fresh#Pass12"})
	fresh := str(out, "token")
	if fresh == "" {
		t.Fatalf("no fresh token: %v", out)
	}
	if code, _ := e.as(other).call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatal("another session survived the password change")
	}
	if code, _ := e.as(e.token).call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatal("the old token of this session survived")
	}
	if code, _ := e.as(fresh).call("GET", "/auth/me", nil); code != http.StatusOK {
		t.Fatal("the fresh token does not work")
	}
	e.login("admin", "Fresh#Pass12")
	if code, _ := e.as("").call("POST", "/auth/login", map[string]any{"username": "admin", "password": "Admin@123#"}); code != http.StatusUnauthorized {
		t.Fatal("the old password still signs in")
	}
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE EventType='PASSWORD_CHANGED'`) != 1 {
		t.Fatal("the change was not recorded")
	}
}

func TestNoPasswordReachesTheAuditLog(t *testing.T) {
	e := newAuthEnv(t)
	e.token = str(e.must(http.StatusOK, "POST", "/auth/password", map[string]any{"currentPassword": "Admin@123#", "newPassword": "Audit#Secret77"}), "token")
	e.must(http.StatusCreated, "POST", "/admin/users", map[string]any{"username": "eve", "role": "Viewer", "password": "EveSecret#88"})
	time.Sleep(300 * time.Millisecond) // the audit rows are written in the background
	for _, s := range []string{"Audit#Secret77", "EveSecret#88", "Admin@123#"} {
		if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+s+"%"); n != 0 {
			t.Errorf("%q reached the audit log %d time(s)", s, n)
		}
	}
	var hash string
	_ = e.db.QueryRow(`SELECT PasswordHash FROM Users WHERE Username='eve'`).Scan(&hash)
	if n := e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE Details LIKE $1`, "%"+hash+"%"); hash != "" && n != 0 {
		t.Error("a hash reached the audit log")
	}
}

// ── CS4: users ───────────────────────────────────────────────────────────────

func TestUsersListShowsNoPasswordOrHash(t *testing.T) {
	e := newAuthEnv(t)
	e.addUser("kim", "Operator", "Start#Pass1", []string{"finance"})
	code, raw := e.raw("GET", "/admin/users", nil)
	if code != http.StatusOK || strings.Contains(string(raw), "$2") || strings.Contains(strings.ToLower(string(raw)), "hash") || strings.Contains(string(raw), "Start#Pass1") {
		t.Fatalf("users reply leaks: %s", raw)
	}
	out := e.must(http.StatusOK, "GET", "/admin/users", nil)
	users, _ := out["users"].([]any)
	if len(users) != 2 {
		t.Fatalf("users: %v", users)
	}
}

func TestAddingAUserChecksTheNameRoleAndPassword(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/users", map[string]any{"username": "bad name", "role": "Viewer", "password": "Start#Pass1"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/users", map[string]any{"username": "ok", "role": "Boss", "password": "Start#Pass1"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/users", map[string]any{"username": "ok", "role": "Viewer", "password": "weak"})
	e.must(http.StatusUnprocessableEntity, "POST", "/admin/users", map[string]any{"username": "ok", "role": "Viewer", "password": "Start#Pass1", "allowedGroups": []string{"Bad Group"}})
	e.must(http.StatusCreated, "POST", "/admin/users", map[string]any{"username": "ok", "role": "Viewer", "password": "Start#Pass1"})
}

func TestRoleGroupsAndDisabledChangesApplyToTheNextCall(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("lee", "Viewer", "Start#Pass1", nil)
	if code, _ := e.as(tok).call("POST", "/admin/credential-groups", map[string]any{"name": "g1"}); code != http.StatusForbidden {
		t.Fatalf("a viewer created a group: %d", code)
	}
	e.must(http.StatusOK, "PUT", "/admin/users/lee", map[string]any{"role": "CredentialAdmin"})
	if code, _ := e.as(tok).call("POST", "/admin/credential-groups", map[string]any{"name": "g1"}); code != http.StatusCreated {
		t.Fatalf("the new role did not apply: %d", code)
	}
	e.must(http.StatusOK, "PUT", "/admin/users/lee", map[string]any{"allowedGroups": []string{"other"}})
	me := e.as(tok).must(http.StatusOK, "GET", "/auth/me", nil)
	if g, _ := me["allowedGroups"].([]any); len(g) != 1 || g[0] != "other" {
		t.Fatalf("groups: %v", me)
	}
	e.must(http.StatusOK, "PUT", "/admin/users/lee", map[string]any{"allowedGroups": nil})
	me = e.as(tok).must(http.StatusOK, "GET", "/auth/me", nil)
	if me["allowedGroups"] != nil {
		t.Fatalf("null should mean every group: %v", me)
	}
}

func TestAdministratorSetsANewPasswordAndTheUsersSessionsEnd(t *testing.T) {
	e := newAuthEnv(t)
	tok := e.addUser("mo", "Viewer", "Start#Pass1", nil)
	e.must(http.StatusOK, "PUT", "/admin/users/mo", map[string]any{"newPassword": "Reset#Pass22"})
	if code, _ := e.as(tok).call("GET", "/auth/me", nil); code != http.StatusUnauthorized {
		t.Fatal("the user's session survived a reset")
	}
	e.login("mo", "Reset#Pass22")
	if e.count(`SELECT COUNT(*) FROM SecurityEvents WHERE EventType='PASSWORD_RESET' AND Details LIKE '%mo by admin%'`) != 1 {
		t.Fatal("the reset was not recorded with the administrator's name")
	}
	e.must(http.StatusUnprocessableEntity, "PUT", "/admin/users/mo", map[string]any{"newPassword": "weak"})
	e.must(http.StatusNotFound, "PUT", "/admin/users/ghost", map[string]any{"role": "Viewer"})
}

func TestTheLastEnabledAdministratorCannotBeDisabledOrDemoted(t *testing.T) {
	e := newAuthEnv(t)
	e.must(http.StatusConflict, "PUT", "/admin/users/admin", map[string]any{"disabled": true})
	e.must(http.StatusConflict, "PUT", "/admin/users/admin", map[string]any{"role": "Viewer"})

	// with a second administrator either can go
	e.addUser("second", "Administrator", "Start#Pass1", nil)
	e.must(http.StatusOK, "PUT", "/admin/users/second", map[string]any{"disabled": true})
	e.must(http.StatusConflict, "PUT", "/admin/users/admin", map[string]any{"disabled": true})
	e.must(http.StatusOK, "PUT", "/admin/users/second", map[string]any{"disabled": false})
	e.must(http.StatusOK, "PUT", "/admin/users/admin", map[string]any{"disabled": true})
}

func TestOnlyUserManagersMayUseTheUsersRoutes(t *testing.T) {
	e := newAuthEnv(t)
	for _, role := range []string{"Viewer", "Operator", "CredentialAdmin"} {
		tok := e.addUser("u-"+strings.ToLower(role), role, "Start#Pass1", nil)
		c := e.as(tok)
		if code, out := c.call("GET", "/admin/users", nil); code != http.StatusForbidden || !strings.Contains(str(out, "message"), "users.manage") {
			t.Errorf("%s listed users: %d %v", role, code, out)
		}
		if code, _ := c.call("POST", "/admin/users", map[string]any{"username": "x", "role": "Viewer", "password": "Start#Pass1"}); code != http.StatusForbidden {
			t.Errorf("%s added a user: %d", role, code)
		}
	}
}
