package handlers

import (
	"crypto/sha256"
	"database/sql"
	"encoding/base64"
	"encoding/hex"
	"net/http"
	"time"

	"github.com/go-chi/chi/v5"
)

// GET /artifacts?includeDeleted=false
// Lists all artifact IDs and their versions, newest first.
func (h *ArtifactHandler) List(w http.ResponseWriter, r *http.Request) {
	includeDeleted := r.URL.Query().Get("includeDeleted") == "true"

	query := `SELECT ArtifactId, Version, SchemaVersion, ContentHash, StoredAt, IsDeleted, SignatureValid
	          FROM ArtifactRecords`
	if !includeDeleted {
		query += ` WHERE IsDeleted=0`
	}
	query += ` ORDER BY StoredAt DESC`

	rows, err := h.db.Query(query)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	type artifactRow struct {
		ArtifactId    string `json:"artifactId"`
		Version       string `json:"version"`
		SchemaVersion string `json:"schemaVersion"`
		ContentHash   string `json:"contentHash"`
		StoredAt      string `json:"storedAt"`
		IsDeleted     bool   `json:"isDeleted"`
		SignatureValid bool   `json:"signatureValid"`
	}

	artifacts := []artifactRow{}
	for rows.Next() {
		var a artifactRow
		var isDeleted, sigValid int
		if err := rows.Scan(
			&a.ArtifactId, &a.Version, &a.SchemaVersion, &a.ContentHash,
			&a.StoredAt, &isDeleted, &sigValid,
		); err != nil {
			continue
		}
		a.IsDeleted = isDeleted != 0
		a.SignatureValid = sigValid != 0
		artifacts = append(artifacts, a)
	}

	respond(w, http.StatusOK, map[string]any{
		"artifacts": artifacts,
		"count":     len(artifacts),
	})
}

type ArtifactHandler struct{ db *sql.DB }

func NewArtifactHandler(db *sql.DB) *ArtifactHandler { return &ArtifactHandler{db: db} }

// PUT /artifacts/{id}/versions/{version}
func (h *ArtifactHandler) Publish(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	version := chi.URLParam(r, "version")

	var req struct {
		SchemaVersion   string  `json:"schemaVersion"`
		Payload         string  `json:"payload"` // base64-encoded
		Signature       *string `json:"signature"`
		SelectorContent *string `json:"selectorContent"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.SchemaVersion == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "schemaVersion required")
		return
	}

	raw, err := base64.StdEncoding.DecodeString(req.Payload)
	if err != nil {
		errResp(w, http.StatusBadRequest, "INVALID_PAYLOAD", "payload must be base64-encoded")
		return
	}

	hash := sha256.Sum256(raw)
	contentHash := hex.EncodeToString(hash[:])
	now := time.Now().UTC().Format(time.RFC3339Nano)

	// Check if version already exists
	var existing string
	err = h.db.QueryRow(
		`SELECT ContentHash FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
		artifactID, version,
	).Scan(&existing)
	if err == nil {
		if existing != contentHash {
			errResp(w, http.StatusConflict, "VERSION_CONFLICT",
				"version already exists with different content")
			return
		}
		// Same content — idempotent 201
		respond(w, http.StatusCreated, map[string]string{
			"artifactId": artifactID, "version": version, "contentHash": contentHash,
		})
		return
	}

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()

	tx.Exec(`
		INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt,IsDeleted,SignatureValid,SelectorContent)
		VALUES(?,?,?,?,?,0,?,?)`,
		artifactID, version, req.SchemaVersion, contentHash, now,
		req.Signature != nil && *req.Signature != "",
		req.SelectorContent,
	)
	tx.Exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES(?,?,?)`,
		artifactID, version, raw)

	if err := tx.Commit(); err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	w.Header().Set("ETag", `"`+contentHash+`"`)
	respond(w, http.StatusCreated, map[string]string{
		"artifactId": artifactID, "version": version, "contentHash": contentHash,
	})
}

// GET /artifacts/{id}/versions/{version}
func (h *ArtifactHandler) Get(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	version := chi.URLParam(r, "version")

	var contentHash string
	var payload []byte
	err := h.db.QueryRow(
		`SELECT ar.ContentHash, ac.Payload
		 FROM ArtifactRecords ar
		 JOIN ArtifactContents ac ON ar.ArtifactId=ac.ArtifactId AND ar.Version=ac.Version
		 WHERE ar.ArtifactId=? AND ar.Version=? AND ar.IsDeleted=0`,
		artifactID, version,
	).Scan(&contentHash, &payload)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "artifact not found")
		return
	}
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}

	w.Header().Set("ETag", `"`+contentHash+`"`)
	respond(w, http.StatusOK, map[string]string{
		"payload":     base64.StdEncoding.EncodeToString(payload),
		"contentHash": contentHash,
	})
}

// DELETE /artifacts/{id}/versions/{version}
func (h *ArtifactHandler) Delete(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	version := chi.URLParam(r, "version")

	// Check artifact exists
	var isDeleted int
	err := h.db.QueryRow(
		`SELECT IsDeleted FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
		artifactID, version,
	).Scan(&isDeleted)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "artifact not found")
		return
	}
	if isDeleted != 0 {
		// Already deleted — idempotent 204
		w.WriteHeader(http.StatusNoContent)
		return
	}

	// Check not referenced by active run
	var activeCount int
	h.db.QueryRow(
		`SELECT COUNT(*) FROM RunRecords WHERE ScriptArtifactId=? AND ScriptVersion=? AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
		artifactID, version,
	).Scan(&activeCount)
	if activeCount > 0 {
		errResp(w, http.StatusConflict, "ARTIFACT_IN_USE",
			"artifact is referenced by an active run and cannot be deleted")
		return
	}

	h.db.Exec(`UPDATE ArtifactRecords SET IsDeleted=1 WHERE ArtifactId=? AND Version=?`,
		artifactID, version)
	w.WriteHeader(http.StatusNoContent)
}
