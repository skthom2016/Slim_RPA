package sheet

import (
	"encoding/json"
	"testing"
)

func TestDetectType(t *testing.T) {
	cases := map[string]string{
		`C:\Users\x\Downloads\book.xlsx`:                                      TypeXlsx,
		`\server\share\book.xlsx`:                                             TypeXlsx,
		"/mnt/data/book.xlsx":                                                 TypeXlsx,
		"https://docs.google.com/spreadsheets/d/1AbC/edit":                    TypeGSheet,
		"https://contoso.sharepoint.com/sites/team/Shared%20Documents/a.xlsx": TypeGraph,
		"https://contoso-my.sharepoint.com/:x:/g/personal/u/Eabc":             TypeGraph,
		"https://1drv.ms/x/s!Abc":                                             TypeGraph,
	}
	for path, want := range cases {
		if got := DetectType(path); got != want {
			t.Errorf("DetectType(%q) = %s, want %s", path, got, want)
		}
	}
}

func TestRequestsCannotSupplyCredentialsOrEndpoints(t *testing.T) {
	var c Config
	body := `{"path":"p","sheet":"s","accessToken":"stolen","GraphBase":"http://evil","graphBase":"http://evil","loginBase":"http://evil","workDir":"/etc"}`
	if err := json.Unmarshal([]byte(body), &c); err != nil {
		t.Fatal(err)
	}
	if c.GraphBase != "" || c.LoginBase != "" || c.GoogleAPIBase != "" || c.WorkDir != "" || c.Env != nil {
		t.Fatalf("request set internal fields: %+v", c)
	}
}

func TestOpen_MissingMicrosoftCredentialsNamesTheEnvVars(t *testing.T) {
	_, err := Open(Config{Path: "https://contoso.sharepoint.com/a.xlsx", Sheet: "S", Env: env()})
	if err == nil {
		t.Fatal("expected error")
	}
	for _, v := range []string{"MS_TENANT_ID", "MS_CLIENT_ID", "MS_CLIENT_SECRET"} {
		if !contains(err.Error(), v) {
			t.Errorf("error %q should name %s", err, v)
		}
	}
}

func contains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
}
