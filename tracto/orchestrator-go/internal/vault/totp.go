package vault

import (
	"crypto/hmac"
	"crypto/sha1"
	"encoding/base32"
	"encoding/binary"
	"fmt"
	"hash"
	"strings"
	"time"
)

// totpPeriod is the length of one code's window in seconds, and totpDigits the code's length.
const (
	totpPeriod = 30
	totpDigits = 6
	// totpEdge: a code asked for in the last seconds of its window is about to expire on the
	// site's side, so the next one is waited for.
	totpEdge = 5
)

// NormalizeTOTPKey accepts the setup key the way a site shows it (groups of letters, spaces,
// lower case, no padding) and returns it as plain upper case base32, or an error.
func NormalizeTOTPKey(key string) (string, error) {
	k := strings.ToUpper(strings.NewReplacer(" ", "", "-", "", "=", "").Replace(strings.TrimSpace(key)))
	if k == "" {
		return "", fmt.Errorf("the one-time-code key is empty")
	}
	if _, err := base32.StdEncoding.WithPadding(base32.NoPadding).DecodeString(k); err != nil {
		return "", fmt.Errorf("the one-time-code key must be the base32 setup key shown by the site")
	}
	return k, nil
}

// TOTPCode returns the 6 digit code of RFC 6238 for the base32 key at time t (SHA-1, 30 seconds).
func TOTPCode(key string, t time.Time) (string, error) {
	k, err := NormalizeTOTPKey(key)
	if err != nil {
		return "", err
	}
	secret, _ := base32.StdEncoding.WithPadding(base32.NoPadding).DecodeString(k)
	return hotp(secret, uint64(t.Unix()/totpPeriod), sha1.New, totpDigits), nil
}

func hotp(secret []byte, counter uint64, h func() hash.Hash, digits int) string {
	var msg [8]byte
	binary.BigEndian.PutUint64(msg[:], counter)
	m := hmac.New(h, secret)
	m.Write(msg[:])
	sum := m.Sum(nil)
	off := sum[len(sum)-1] & 0x0f
	bin := binary.BigEndian.Uint32(sum[off:off+4]) & 0x7fffffff
	mod := uint32(1)
	for i := 0; i < digits; i++ {
		mod *= 10
	}
	return fmt.Sprintf("%0*d", digits, bin%mod)
}

// CurrentTOTP returns a code that is still valid for a few seconds at least. When now is in the
// last totpEdge seconds of a window it returns the next window's code after waiting for the window
// to start, using sleep (time.Sleep in production).
func CurrentTOTP(key string, now func() time.Time, sleep func(time.Duration)) (string, error) {
	t := now()
	into := t.Unix() % totpPeriod
	if into >= totpPeriod-totpEdge {
		sleep(time.Duration(totpPeriod-into)*time.Second - time.Duration(t.Nanosecond()))
		t = now()
	}
	return TOTPCode(key, t)
}
