// Package vault seals the passwords and one-time-code keys of the credential store, so that a copy
// of the database alone does not give them away.
//
// The master key is 32 random bytes, base64 encoded, in the environment variable
// CREDENTIAL_MASTER_KEY. Values are sealed with AES-256-GCM, each with its own random nonce, in
// the same format as the sheet service's secretbox ("v1:" and base64 of nonce and ciphertext).
// The credential's name is bound to the value as additional authenticated data, so a sealed value
// copied onto another credential does not open.
package vault

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"fmt"
	"strings"
)

const (
	// EnvKey holds the current master key.
	EnvKey = "CREDENTIAL_MASTER_KEY"
	// EnvKeyID names the current key; rows record the id of the key that sealed them.
	EnvKeyID = "CREDENTIAL_MASTER_KEY_ID"
	// EnvOldKey holds the key a rekey replaces, and EnvOldKeyID its id.
	EnvOldKey   = "CREDENTIAL_MASTER_KEY_OLD"
	EnvOldKeyID = "CREDENTIAL_MASTER_KEY_OLD_ID"

	prefix = "v1:"
)

// ErrLocked means no master key is set: the store cannot seal or open anything.
var ErrLocked = errors.New(EnvKey + " is not set")

// Vault seals under the current key and opens under any key it holds.
type Vault struct {
	current string
	keys    map[string]cipher.AEAD
}

// Locked returns a vault that holds no key.
func Locked() *Vault { return &Vault{keys: map[string]cipher.AEAD{}} }

// New builds a vault whose current key is key (base64, 32 bytes) named id. oldKey may be empty;
// when given it is held under oldID so that rows sealed with it still open.
func New(key, id, oldKey, oldID string) (*Vault, error) {
	if strings.TrimSpace(key) == "" {
		return Locked(), nil
	}
	if id == "" {
		id = "1"
	}
	v := &Vault{current: id, keys: map[string]cipher.AEAD{}}
	a, err := aeadFrom(key, EnvKey)
	if err != nil {
		return nil, err
	}
	v.keys[id] = a
	if strings.TrimSpace(oldKey) != "" {
		if oldID == "" {
			oldID = "1"
		}
		if oldID == id {
			return nil, fmt.Errorf("%s and %s name the same key id %q: give the new key a new %s", EnvKey, EnvOldKey, id, EnvKeyID)
		}
		o, err := aeadFrom(oldKey, EnvOldKey)
		if err != nil {
			return nil, err
		}
		v.keys[oldID] = o
	}
	return v, nil
}

func aeadFrom(key, name string) (cipher.AEAD, error) {
	raw, err := base64.StdEncoding.DecodeString(strings.TrimSpace(key))
	if err != nil || len(raw) != 32 {
		return nil, fmt.Errorf("%s must be 32 random bytes, base64 encoded", name)
	}
	block, err := aes.NewCipher(raw)
	if err != nil {
		return nil, err
	}
	return cipher.NewGCM(block)
}

// IsLocked reports whether no key is held.
func (v *Vault) IsLocked() bool { return v == nil || v.current == "" }

// KeyID is the id of the key new values are sealed with.
func (v *Vault) KeyID() string { return v.current }

// Seal encrypts plain under the current key, bound to aad (the credential's name). An empty value
// stays empty.
func (v *Vault) Seal(plain, aad string) (string, error) {
	if v.IsLocked() {
		return "", ErrLocked
	}
	if plain == "" {
		return "", nil
	}
	a := v.keys[v.current]
	nonce := make([]byte, a.NonceSize())
	if _, err := rand.Read(nonce); err != nil {
		return "", err
	}
	out := a.Seal(nonce, nonce, []byte(plain), []byte(aad))
	return prefix + base64.RawURLEncoding.EncodeToString(out), nil
}

// Open decrypts a value sealed with the key named keyID and bound to aad. The error never holds
// the value or the key.
func (v *Vault) Open(sealed, keyID, aad string) (string, error) {
	if v.IsLocked() {
		return "", ErrLocked
	}
	if sealed == "" {
		return "", nil
	}
	a, ok := v.keys[keyID]
	if !ok {
		return "", fmt.Errorf("the master key with id %q is not available: set %s or %s", keyID, EnvKey, EnvOldKey)
	}
	body, ok := strings.CutPrefix(sealed, prefix)
	if !ok {
		return "", errors.New("stored secret is not encrypted")
	}
	raw, err := base64.RawURLEncoding.DecodeString(body)
	if err != nil || len(raw) < a.NonceSize() {
		return "", errors.New("stored secret is damaged")
	}
	plain, err := a.Open(nil, raw[:a.NonceSize()], raw[a.NonceSize():], []byte(aad))
	if err != nil {
		return "", errors.New("stored secret cannot be decrypted with the master key")
	}
	return string(plain), nil
}

// TestKey returns a valid key for tests.
func TestKey(fill byte) string {
	b := make([]byte, 32)
	for i := range b {
		b[i] = fill
	}
	return base64.StdEncoding.EncodeToString(b)
}
