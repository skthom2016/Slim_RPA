package handlers

import (
	"crypto/hmac"
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"net/http"
	"strings"
	"time"

	"github.com/google/uuid"
)

type CallbackHandler struct {
	db       *sql.DB
	hmacKeys map[string]string // keyId -> secret
}

func NewCallbackHandler(db *sql.DB, hmacKeys map[string]string) *CallbackHandler {
	return &CallbackHandler{db: db, hmacKeys: hmacKeys}
}

// POST /callbacks
func (h *CallbackHandler) Receive(w http.ResponseWriter, r *http.Request) {
	var req struct {
		EventId       string          `json:"eventId"`
		CorrelationId string          `json:"correlationId"`
		KeyId         string          `json:"keyId"`
		Signature     string          `json:"signature"`
		Payload       json.RawMessage `json:"payload"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.EventId == "" || req.CorrelationId == "" || req.KeyId == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "eventId, correlationId, keyId required")
		return
	}

	// HMAC validation (if keys configured)
	if len(h.hmacKeys) > 0 {
		secret, ok := h.hmacKeys[req.KeyId]
		if !ok {
			errResp(w, http.StatusUnauthorized, "UNKNOWN_KEY", "unknown keyId")
			return
		}
		mac := hmac.New(sha256.New, []byte(secret))
		mac.Write(req.Payload)
		expected := hex.EncodeToString(mac.Sum(nil))
		if !hmac.Equal([]byte(req.Signature), []byte(expected)) {
			errResp(w, http.StatusUnauthorized, "INVALID_SIGNATURE", "HMAC signature mismatch")
			return
		}
	}

	// Dedup by EventId
	var existingId string
	err := h.db.QueryRow(`SELECT Id FROM CallbackRecords WHERE EventId=?`, req.EventId).Scan(&existingId)
	if err == nil {
		// Already received — idempotent
		respond(w, http.StatusOK, map[string]string{"callbackId": existingId, "status": "duplicate"})
		return
	}

	now := time.Now().UTC().Format(time.RFC3339Nano)
	cbID := uuid.NewString()
	payloadStr := string(req.Payload)
	_, err = h.db.Exec(`
		INSERT INTO CallbackRecords(Id,EventId,CorrelationId,KeyId,ReceivedAt,PayloadJson,Processed)
		VALUES(?,?,?,?,?,?,0)`,
		cbID, req.EventId, req.CorrelationId, req.KeyId, now, payloadStr)
	if err != nil {
		if strings.Contains(err.Error(), "UNIQUE") {
			// Race — idempotent
			h.db.QueryRow(`SELECT Id FROM CallbackRecords WHERE EventId=?`, req.EventId).Scan(&existingId)
			respond(w, http.StatusOK, map[string]string{"callbackId": existingId, "status": "duplicate"})
			return
		}
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	respond(w, http.StatusAccepted, map[string]string{"callbackId": cbID, "status": "accepted"})
}
