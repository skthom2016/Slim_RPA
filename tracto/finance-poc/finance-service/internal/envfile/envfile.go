// Package envfile loads KEY=VALUE settings from a .env file into the process environment.
package envfile

import (
	"bufio"
	"os"
	"strings"
)

// Load reads path and sets each variable that is not already set in the environment, so a real
// environment variable always wins over the file. It reports whether the file was found.
// Lines are KEY=VALUE; blank lines and lines starting with # are ignored, an optional
// "export " prefix is allowed, and a value may be wrapped in single or double quotes.
func Load(path string) (bool, error) {
	f, err := os.Open(path)
	if os.IsNotExist(err) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	defer f.Close()

	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(strings.TrimPrefix(sc.Text(), "\ufeff"))
		line = strings.TrimSpace(strings.TrimPrefix(line, "export "))
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		key, value, ok := strings.Cut(line, "=")
		key = strings.TrimSpace(key)
		if !ok || key == "" {
			continue
		}
		if _, set := os.LookupEnv(key); set {
			continue
		}
		if err := os.Setenv(key, unquote(strings.TrimSpace(value))); err != nil {
			return true, err
		}
	}
	return true, sc.Err()
}

func unquote(v string) string {
	if len(v) >= 2 && (v[0] == '"' || v[0] == '\'') && v[len(v)-1] == v[0] {
		return v[1 : len(v)-1]
	}
	return v
}
