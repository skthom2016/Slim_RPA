package secretbox

import (
	"strings"
	"testing"
)

func TestSealOpen_RoundTripAndNeverPlain(t *testing.T) {
	b, err := New(TestKey())
	if err != nil {
		t.Fatal(err)
	}
	sealed, err := b.Seal("refresh-token-value")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(sealed, "refresh-token-value") || !strings.HasPrefix(sealed, "v1:") {
		t.Fatalf("not encrypted: %q", sealed)
	}
	again, _ := b.Seal("refresh-token-value")
	if again == sealed {
		t.Fatal("two seals of one value must differ (random nonce)")
	}
	plain, err := b.Open(sealed)
	if err != nil || plain != "refresh-token-value" {
		t.Fatalf("got %q %v", plain, err)
	}
}

func TestSeal_EmptyStaysEmpty(t *testing.T) {
	b, _ := New(TestKey())
	s, err := b.Seal("")
	if err != nil || s != "" {
		t.Fatalf("got %q %v", s, err)
	}
	if p, err := b.Open(""); err != nil || p != "" {
		t.Fatalf("got %q %v", p, err)
	}
}

func TestOpen_RejectsPlainDamagedAndWrongKey(t *testing.T) {
	b, _ := New(TestKey())
	if _, err := b.Open("just-text"); err == nil {
		t.Error("a value that was never sealed must not be returned")
	}
	sealed, _ := b.Seal("x")
	if _, err := b.Open(sealed[:len(sealed)-3]); err == nil {
		t.Error("a damaged value must fail")
	}
	other, err := New("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=") // another 32-byte key
	if err != nil {
		t.Fatal(err)
	}
	if _, err := other.Open(sealed); err == nil {
		t.Error("another key must not open it")
	}
}

func TestNew_RejectsBadKeys(t *testing.T) {
	for _, k := range []string{"", "short", "not base64 !!", "c2hvcnQ="} {
		if _, err := New(k); err == nil {
			t.Errorf("key %q must be refused", k)
		}
	}
}

func TestFromEnv(t *testing.T) {
	t.Setenv(EnvKey, "")
	if _, err := FromEnv(); err != ErrNoKey {
		t.Fatalf("got %v", err)
	}
	t.Setenv(EnvKey, TestKey())
	if _, err := FromEnv(); err != nil {
		t.Fatal(err)
	}
}
