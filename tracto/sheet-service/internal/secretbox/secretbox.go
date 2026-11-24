// Package secretbox encrypts small secrets (tokens, passwords) before they are stored in the
// database, so a copy of the database alone does not give them away.
//
// The key comes from the environment variable TRACTO_SECRET_KEY: 32 random bytes, base64 encoded.
// Values are sealed with AES-256-GCM; each carries its own random nonce and the prefix "v1:".
package secretbox

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"fmt"
	"os"
	"strings"
)

// EnvKey is the environment variable that holds the key.
const EnvKey = "TRACTO_SECRET_KEY"

const prefix = "v1:"

// ErrNoKey means TRACTO_SECRET_KEY is not set.
var ErrNoKey = errors.New(EnvKey + " is not set: it must be 32 random bytes, base64 encoded")

// Box seals and opens values under one key.
type Box struct{ aead cipher.AEAD }

// New builds a Box from a base64 encoded 32 byte key.
func New(key string) (*Box, error) {
	raw, err := base64.StdEncoding.DecodeString(strings.TrimSpace(key))
	if err != nil || len(raw) != 32 {
		return nil, fmt.Errorf("%s must be 32 random bytes, base64 encoded", EnvKey)
	}
	block, err := aes.NewCipher(raw)
	if err != nil {
		return nil, err
	}
	aead, err := cipher.NewGCM(block)
	if err != nil {
		return nil, err
	}
	return &Box{aead: aead}, nil
}

// FromEnv builds a Box from TRACTO_SECRET_KEY.
func FromEnv() (*Box, error) {
	key := os.Getenv(EnvKey)
	if key == "" {
		return nil, ErrNoKey
	}
	return New(key)
}

// Seal encrypts plain. An empty value stays empty, so "no value" is still visible in the table.
func (b *Box) Seal(plain string) (string, error) {
	if plain == "" {
		return "", nil
	}
	nonce := make([]byte, b.aead.NonceSize())
	if _, err := rand.Read(nonce); err != nil {
		return "", err
	}
	out := b.aead.Seal(nonce, nonce, []byte(plain), nil)
	return prefix + base64.RawURLEncoding.EncodeToString(out), nil
}

// Open decrypts a value written by Seal. A value that was not sealed, or was sealed under another
// key, is an error: it is never returned as it is.
func (b *Box) Open(sealed string) (string, error) {
	if sealed == "" {
		return "", nil
	}
	body, ok := strings.CutPrefix(sealed, prefix)
	if !ok {
		return "", errors.New("stored secret is not encrypted")
	}
	raw, err := base64.RawURLEncoding.DecodeString(body)
	if err != nil || len(raw) < b.aead.NonceSize() {
		return "", errors.New("stored secret is damaged")
	}
	nonce, ct := raw[:b.aead.NonceSize()], raw[b.aead.NonceSize():]
	plain, err := b.aead.Open(nil, nonce, ct, nil)
	if err != nil {
		return "", errors.New("stored secret cannot be decrypted with " + EnvKey)
	}
	return string(plain), nil
}

// TestKey returns a valid key for tests.
func TestKey() string { return base64.StdEncoding.EncodeToString([]byte("0123456789abcdef0123456789abcdef")) }
