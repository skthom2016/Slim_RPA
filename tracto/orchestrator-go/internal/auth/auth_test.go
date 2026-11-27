package auth

import "testing"

func TestPasswordRules(t *testing.T) {
	cases := []struct {
		password, current string
		ok                bool
	}{
		{"Admin@123#", "", true},
		{"Fresh#Admin456", "Admin@123#", true},
		{"Sh0rt#", "", false},        // under 8 characters
		{"nocapital#123", "", false}, // no capital letter
		{"NOSMALL#123", "", false},   // no small letter
		{"NoDigits#here", "", false}, // no digit
		{"NoSymbol1234a", "", false}, // no symbol
		{"Admin@123#", "Admin@123#", false},
		{"Pässword#1x", "", true}, // other alphabets count as letters
	}
	for _, c := range cases {
		err := ValidatePassword(c.password, c.current)
		if (err == nil) != c.ok {
			t.Errorf("%q (current %q): err=%v, want ok=%v", c.password, c.current, err, c.ok)
		}
	}
}

func TestHashIsNotThePasswordAndChecksBack(t *testing.T) {
	h, err := HashPassword("Admin@123#")
	if err != nil {
		t.Fatal(err)
	}
	if h == "Admin@123#" || len(h) < 50 {
		t.Fatalf("hash looks wrong: %q", h)
	}
	if !CheckPassword(h, "Admin@123#") || CheckPassword(h, "admin@123#") {
		t.Fatal("hash does not check")
	}
	h2, _ := HashPassword("Admin@123#")
	if h == h2 {
		t.Fatal("two hashes of one password are identical: no salt")
	}
}

func TestRolesHoldExactlyTheirPermissions(t *testing.T) {
	want := map[string][]string{
		RoleViewer:          {PermView},
		RoleOperator:        {PermView, PermReveal},
		RoleCredentialAdmin: {PermView, PermReveal, PermEdit, PermGroups, PermSettings},
		RoleAdministrator:   {PermView, PermReveal, PermEdit, PermGroups, PermSettings, PermUsers},
	}
	for role, perms := range want {
		p := &Principal{Role: role}
		got := PermissionsOf(role)
		if len(got) != len(perms) {
			t.Errorf("%s holds %v, want %v", role, got, perms)
		}
		for _, perm := range perms {
			if !p.Has(perm) {
				t.Errorf("%s lacks %s", role, perm)
			}
		}
	}
	if (&Principal{Role: RoleViewer}).Has(PermReveal) || (&Principal{Role: "Nobody"}).Has(PermView) {
		t.Error("a role holds a permission it should not")
	}
	if ValidRole("Boss") || !ValidRole(RoleOperator) {
		t.Error("ValidRole is wrong")
	}
}

func TestGroupLimits(t *testing.T) {
	all := &Principal{Role: RoleViewer}
	one := &Principal{Role: RoleViewer, Groups: []string{"finance-portals"}}
	none := &Principal{Role: RoleViewer, Groups: []string{}}
	if !all.CanSeeGroup("x") || !one.CanSeeGroup("finance-portals") || one.CanSeeGroup("hr") || none.CanSeeGroup("finance-portals") {
		t.Fatal("group limits are wrong")
	}
	if JoinGroups(nil).Valid || !JoinGroups([]string{}).Valid || JoinGroups([]string{"a", "b"}).String != "a,b" {
		t.Fatal("JoinGroups is wrong")
	}
}
