package vault

import (
	"strings"
	"testing"
	"time"
)

func mustVault(t *testing.T, key, id, old, oldID string) *Vault {
	t.Helper()
	v, err := New(key, id, old, oldID)
	if err != nil {
		t.Fatal(err)
	}
	return v
}

func TestSealOpenRoundTrip(t *testing.T) {
	v := mustVault(t, TestKey(1), "1", "", "")
	sealed, err := v.Seal("S3cret!pass", "bank.hdfc")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(sealed, "S3cret") || !strings.HasPrefix(sealed, "v1:") {
		t.Fatalf("sealed value looks wrong: %q", sealed)
	}
	got, err := v.Open(sealed, "1", "bank.hdfc")
	if err != nil || got != "S3cret!pass" {
		t.Fatalf("open: %q, %v", got, err)
	}
	// each seal uses a fresh nonce
	again, _ := v.Seal("S3cret!pass", "bank.hdfc")
	if again == sealed {
		t.Fatal("two seals of the same value are identical")
	}
}

func TestSealedValueMovedToAnotherCredentialDoesNotOpen(t *testing.T) {
	v := mustVault(t, TestKey(1), "1", "", "")
	sealed, _ := v.Seal("pw", "bank.hdfc")
	if _, err := v.Open(sealed, "1", "bank.icici"); err == nil {
		t.Fatal("a value sealed for one credential opened under another name")
	}
}

func TestWrongKeyDoesNotOpenAndErrorHoldsNoSecret(t *testing.T) {
	a := mustVault(t, TestKey(1), "1", "", "")
	b := mustVault(t, TestKey(2), "1", "", "")
	sealed, _ := a.Seal("pw-value", "n")
	_, err := b.Open(sealed, "1", "n")
	if err == nil || strings.Contains(err.Error(), "pw-value") || strings.Contains(err.Error(), TestKey(2)) {
		t.Fatalf("expected a clean failure, got %v", err)
	}
}

func TestLockedVault(t *testing.T) {
	v := mustVault(t, "", "", "", "")
	if !v.IsLocked() {
		t.Fatal("a vault without a key is not locked")
	}
	if _, err := v.Seal("x", "n"); err != ErrLocked {
		t.Fatalf("seal on a locked vault: %v", err)
	}
	if _, err := v.Open("v1:abc", "1", "n"); err != ErrLocked {
		t.Fatalf("open on a locked vault: %v", err)
	}
}

func TestBadKeyIsRefused(t *testing.T) {
	for _, k := range []string{"not base64 !!", "c2hvcnQ="} {
		if _, err := New(k, "1", "", ""); err == nil {
			t.Fatalf("key %q accepted", k)
		}
	}
}

func TestOldKeyStillOpensRowsSealedWithIt(t *testing.T) {
	old := mustVault(t, TestKey(1), "1", "", "")
	sealed, _ := old.Seal("pw", "n")

	both := mustVault(t, TestKey(2), "2", TestKey(1), "1")
	if got, err := both.Open(sealed, "1", "n"); err != nil || got != "pw" {
		t.Fatalf("old row did not open: %q %v", got, err)
	}
	fresh, _ := both.Seal("pw2", "n")
	onlyNew := mustVault(t, TestKey(2), "2", "", "")
	if got, err := onlyNew.Open(fresh, "2", "n"); err != nil || got != "pw2" {
		t.Fatalf("new row: %q %v", got, err)
	}
	if _, err := onlyNew.Open(sealed, "1", "n"); err == nil {
		t.Fatal("an old row opened without the old key")
	}
}

func TestSameIdForOldAndNewKeyIsRefused(t *testing.T) {
	if _, err := New(TestKey(2), "1", TestKey(1), "1"); err == nil {
		t.Fatal("two keys with one id accepted")
	}
}

// RFC 6238 appendix B, SHA-1 secret "12345678901234567890", 6 digits (the last 6 of the 8 digit
// values in the RFC).
func TestTOTPMatchesRFC6238(t *testing.T) {
	const key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
	cases := map[int64]string{
		59:          "287082",
		1111111109:  "081804",
		1111111111:  "050471",
		1234567890:  "005924",
		2000000000:  "279037",
		20000000000: "353130",
	}
	for unix, want := range cases {
		got, err := TOTPCode(key, time.Unix(unix, 0))
		if err != nil || got != want {
			t.Errorf("T=%d: got %q (%v), want %q", unix, got, err, want)
		}
	}
}

func TestTOTPKeyAsASiteShowsIt(t *testing.T) {
	got, err := NormalizeTOTPKey("gezd gnbv-gy3t qojq GEZDGNBVGY3TQOJQ====")
	if err != nil || got != "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" {
		t.Fatalf("%q %v", got, err)
	}
	if _, err := NormalizeTOTPKey("not*base32"); err == nil {
		t.Fatal("garbage accepted")
	}
	if _, err := NormalizeTOTPKey("  "); err == nil {
		t.Fatal("empty accepted")
	}
}

func TestTOTPWaitsForTheNextWindowNearTheEdge(t *testing.T) {
	const key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
	clock := time.Unix(1111111109, 0) // 29 seconds into a window: 1 second left
	slept := time.Duration(0)
	now := func() time.Time { return clock }
	sleep := func(d time.Duration) { slept += d; clock = clock.Add(d) }

	got, err := CurrentTOTP(key, now, sleep)
	if err != nil {
		t.Fatal(err)
	}
	want, _ := TOTPCode(key, time.Unix(1111111110, 0)) // the next window
	if got != want || slept != time.Second {
		t.Fatalf("near the edge: code %q (want %q), slept %v", got, want, slept)
	}

	// early in a window: no wait
	clock = time.Unix(1111111100, 0) // 20 seconds in
	slept = 0
	got, _ = CurrentTOTP(key, now, sleep)
	want, _ = TOTPCode(key, time.Unix(1111111100, 0))
	if got != want || slept != 0 {
		t.Fatalf("early in a window: code %q (want %q), slept %v", got, want, slept)
	}
}
