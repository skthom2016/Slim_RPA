package envfile

import (
	"os"
	"path/filepath"
	"testing"
)

func write(t *testing.T, content string) string {
	t.Helper()
	p := filepath.Join(t.TempDir(), ".env")
	if err := os.WriteFile(p, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
	return p
}

func TestLoad_ParsesValuesCommentsAndQuotes(t *testing.T) {
	p := write(t, "\ufeff# comment\n\nSS_PORT=5099\nexport SS_KEY = \"C:\\keys\\a.json\"\nSS_SINGLE='x y'\nSS_EQ=a=b\nbroken line\n")
	for _, k := range []string{"SS_PORT", "SS_KEY", "SS_SINGLE", "SS_EQ"} {
		t.Setenv(k, "")
		os.Unsetenv(k)
	}
	found, err := Load(p)
	if err != nil || !found {
		t.Fatalf("found=%v err=%v", found, err)
	}
	want := map[string]string{"SS_PORT": "5099", "SS_KEY": `C:\keys\a.json`, "SS_SINGLE": "x y", "SS_EQ": "a=b"}
	for k, v := range want {
		if got := os.Getenv(k); got != v {
			t.Errorf("%s = %q, want %q", k, got, v)
		}
	}
}

func TestLoad_RealEnvironmentWins(t *testing.T) {
	t.Setenv("SS_WINS", "from-env")
	if _, err := Load(write(t, "SS_WINS=from-file\n")); err != nil {
		t.Fatal(err)
	}
	if got := os.Getenv("SS_WINS"); got != "from-env" {
		t.Fatalf("got %q, the environment variable must not be overridden", got)
	}
}

func TestLoad_MissingFileIsNotAnError(t *testing.T) {
	found, err := Load(filepath.Join(t.TempDir(), "nope.env"))
	if found || err != nil {
		t.Fatalf("found=%v err=%v", found, err)
	}
}
