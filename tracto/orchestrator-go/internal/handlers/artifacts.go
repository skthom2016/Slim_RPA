package handlers

import (
	"crypto/sha256"
	"database/sql"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"net/http"
	"time"

	"github.com/go-chi/chi/v5"
)

// GET /artifacts?includeDeleted=false&includeSegments=false
// Lists all artifact IDs and their versions, newest first. The per-segment artifacts derived
// from a segmented script are hidden unless includeSegments=true.
func (h *ArtifactHandler) List(w http.ResponseWriter, r *http.Request) {
	includeDeleted := r.URL.Query().Get("includeDeleted") == "true"
	includeSegments := r.URL.Query().Get("includeSegments") == "true"

	query := `SELECT ArtifactId, Version, SchemaVersion, ContentHash, StoredAt, IsDeleted, SignatureValid, Name,
	                 EXISTS (SELECT 1 FROM ArtifactAliases al
	                         WHERE al.ArtifactId = ArtifactRecords.ArtifactId AND al.Alias = '` + LatestAlias + `'
	                           AND al.Version = ArtifactRecords.Version) AS IsLatest
	          FROM ArtifactRecords WHERE 1=1`
	if !includeDeleted {
		query += ` AND NOT IsDeleted`
	}
	if !includeSegments {
		query += ` AND ParentArtifactId IS NULL`
	}
	query += ` ORDER BY StoredAt DESC`

	rows, err := h.db.Query(query)
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer rows.Close()

	type artifactRow struct {
		ArtifactId     string `json:"artifactId"`
		Name           string `json:"name"`
		Version        string `json:"version"`
		SchemaVersion  string `json:"schemaVersion"`
		ContentHash    string `json:"contentHash"`
		StoredAt       string `json:"storedAt"`
		IsDeleted      bool   `json:"isDeleted"`
		SignatureValid bool   `json:"signatureValid"`
		IsLatest       bool   `json:"isLatest"`
	}

	artifacts := []artifactRow{}
	for rows.Next() {
		var a artifactRow
		var name sql.NullString
		if err := rows.Scan(
			&a.ArtifactId, &a.Version, &a.SchemaVersion, &a.ContentHash,
			&a.StoredAt, &a.IsDeleted, &a.SignatureValid, &name, &a.IsLatest,
		); err != nil {
			dbErr(w, err)
			return
		}
		a.Name = name.String
		artifacts = append(artifacts, a)
	}

	respond(w, http.StatusOK, map[string]any{
		"artifacts": artifacts,
		"count":     len(artifacts),
	})
}

// parseScriptName reads the script's own "name" out of a published payload so the console can
// show something a person chose rather than only the artifact id. The payload may be bare or
// wrapped as {"schemaVersion":"1","script":{...}}, and is the same shape for flat and
// segmented scripts. An unreadable payload yields "", which lists fall back from.
func parseScriptName(raw []byte) string {
	var root map[string]json.RawMessage
	if err := json.Unmarshal(raw, &root); err != nil {
		return ""
	}
	if inner, ok := root["script"]; ok {
		var innerMap map[string]json.RawMessage
		if json.Unmarshal(inner, &innerMap) == nil && innerMap != nil {
			root = innerMap
		}
	}
	nameRaw, ok := root["name"]
	if !ok {
		return ""
	}
	var name string
	if err := json.Unmarshal(nameRaw, &name); err != nil {
		return ""
	}
	return name
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
		SelectorContent *string `json:"selectorContent"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.SchemaVersion == "" {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "schemaVersion required")
		return
	}
	if version == LatestAlias {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_VERSION",
			"'"+LatestAlias+"' is a tag, not a version; publish a version number and move the tag to it")
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

	// A segmented script is also stored as one flat artifact per segment.
	segmented, segErr := parseSegmentedScript(raw)
	if segErr != nil {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_SCRIPT", segErr.Error())
		return
	}
	if segmented != nil && (segmented.Id != artifactID || segmented.Version != version) {
		errResp(w, http.StatusUnprocessableEntity, "INVALID_SCRIPT",
			"the script's id and version must match the artifact id and version")
		return
	}
	segmentCount := 0
	if segmented != nil {
		segmentCount = len(segmented.Segments)
	}
	scriptName := parseScriptName(raw)

	// Check if version already exists
	var existing string
	err = h.db.QueryRow(
		`SELECT ContentHash FROM ArtifactRecords WHERE ArtifactId=$1 AND Version=$2`,
		artifactID, version,
	).Scan(&existing)
	if err == nil {
		if existing != contentHash {
			errResp(w, http.StatusConflict, "VERSION_CONFLICT",
				"version already exists with different content")
			return
		}
		// Same content — idempotent 201. Segment artifacts missing from an earlier store are added.
		for i := range segmentCount {
			segID := segmentArtifactID(artifactID, i)
			var n int
			h.db.QueryRow(`SELECT COUNT(*) FROM ArtifactRecords WHERE ArtifactId=$1 AND Version=$2`, segID, version).Scan(&n)
			if n > 0 {
				continue
			}
			segRaw := segmentPayload(segmented, i)
			segHash := sha256.Sum256(segRaw)
			if _, err := h.db.Exec(`
				INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt,IsDeleted,SignatureValid,ParentArtifactId,Name)
				VALUES($1,$2,$3,$4,$5,FALSE,FALSE,$6,$7)`,
				segID, version, req.SchemaVersion, hex.EncodeToString(segHash[:]), now, artifactID, scriptName); err != nil {
				dbErr(w, err)
				return
			}
			if _, err := h.db.Exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES($1,$2,$3)
				ON CONFLICT (ArtifactId,Version) DO UPDATE SET Payload=EXCLUDED.Payload`, segID, version, segRaw); err != nil {
				dbErr(w, err)
				return
			}
		}
		// alreadyPublished lets a client say "nothing was added" instead of "published".
		respond(w, http.StatusCreated, map[string]any{
			"artifactId": artifactID, "version": version, "contentHash": contentHash, "segments": segmentCount,
			"alreadyPublished": true,
		})
		return
	}

	tx, err := h.db.Begin()
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	defer tx.Rollback()
	x := newTx(tx)

	x.exec(`
		INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt,IsDeleted,SignatureValid,SelectorContent,Name)
		VALUES($1,$2,$3,$4,$5,FALSE,FALSE,$6,$7)`,
		artifactID, version, req.SchemaVersion, contentHash, now,
		req.SelectorContent, scriptName,
	)
	x.exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES($1,$2,$3)`,
		artifactID, version, raw)

	for i := range segmentCount {
		segID := segmentArtifactID(artifactID, i)
		segRaw := segmentPayload(segmented, i)
		segHash := sha256.Sum256(segRaw)
		x.exec(`DELETE FROM ArtifactRecords WHERE ArtifactId=$1 AND Version=$2`, segID, version)
		x.exec(`DELETE FROM ArtifactContents WHERE ArtifactId=$1 AND Version=$2`, segID, version)
		x.exec(`
			INSERT INTO ArtifactRecords(ArtifactId,Version,SchemaVersion,ContentHash,StoredAt,IsDeleted,SignatureValid,ParentArtifactId,Name)
			VALUES($1,$2,$3,$4,$5,FALSE,FALSE,$6,$7)`,
			segID, version, req.SchemaVersion, hex.EncodeToString(segHash[:]), now, artifactID, scriptName)
		x.exec(`INSERT INTO ArtifactContents(ArtifactId,Version,Payload) VALUES($1,$2,$3)`, segID, version, segRaw)
	}

	if err := x.commit(); err != nil {
		if isUniqueViolation(err) { // the same version was published by another request at the same moment
			errResp(w, http.StatusConflict, "VERSION_CONFLICT", "version was published concurrently; retry")
			return
		}
		dbErr(w, err)
		return
	}

	w.Header().Set("ETag", `"`+contentHash+`"`)
	respond(w, http.StatusCreated, map[string]any{
		"artifactId": artifactID, "version": version, "contentHash": contentHash, "segments": segmentCount,
		"alreadyPublished": false,
	})
}

// GET /artifacts/{id}/versions/{version}
// The version may be "latest"; the reply always names the version number it resolved to.
func (h *ArtifactHandler) Get(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	version, found, err := ResolveVersion(h.db, artifactID, chi.URLParam(r, "version"))
	if err != nil {
		errResp(w, http.StatusInternalServerError, "DB_ERROR", err.Error())
		return
	}
	if !found {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "the tag '"+LatestAlias+"' has not been set for "+artifactID)
		return
	}

	var contentHash string
	var payload []byte
	err = h.db.QueryRow(
		`SELECT ar.ContentHash, ac.Payload
		 FROM ArtifactRecords ar
		 JOIN ArtifactContents ac ON ar.ArtifactId=ac.ArtifactId AND ar.Version=ac.Version
		 WHERE ar.ArtifactId=$1 AND ar.Version=$2 AND NOT ar.IsDeleted`,
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
		"artifactId":  artifactID,
		"version":     version,
		"payload":     base64.StdEncoding.EncodeToString(payload),
		"contentHash": contentHash,
	})
}

// DELETE /artifacts/{id}/versions/{version}
func (h *ArtifactHandler) Delete(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	version := chi.URLParam(r, "version")

	// Check artifact exists
	var isDeleted bool
	var parentArtifact sql.NullString
	err := h.db.QueryRow(
		`SELECT IsDeleted,ParentArtifactId FROM ArtifactRecords WHERE ArtifactId=$1 AND Version=$2`,
		artifactID, version,
	).Scan(&isDeleted, &parentArtifact)
	if err == sql.ErrNoRows {
		errResp(w, http.StatusNotFound, "NOT_FOUND", "artifact not found")
		return
	}
	if parentArtifact.Valid && parentArtifact.String != "" {
		errResp(w, http.StatusConflict, "ARTIFACT_IS_SEGMENT",
			"this artifact is a segment of "+parentArtifact.String+"; delete that artifact instead")
		return
	}
	if isDeleted {
		// Already deleted — idempotent 204
		w.WriteHeader(http.StatusNoContent)
		return
	}

	// Check not referenced by an active run, including the segment runs of a segmented script
	var activeCount int
	h.db.QueryRow(
		`SELECT COUNT(*) FROM RunRecords r
		 JOIN ArtifactRecords a ON a.ArtifactId=r.ScriptArtifactId AND a.Version=r.ScriptVersion
		 WHERE (a.ArtifactId=$1 OR a.ParentArtifactId=$1) AND a.Version=$2
		   AND r.State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
		artifactID, version,
	).Scan(&activeCount)
	if activeCount > 0 {
		errResp(w, http.StatusConflict, "ARTIFACT_IN_USE",
			"artifact is referenced by an active run and cannot be deleted")
		return
	}

	// The version the tag "latest" points at cannot be deleted; move the tag first. The check is part
	// of the update, so a tag moved here at the same moment cannot end up on a deleted version.
	res, err := h.db.Exec(`UPDATE ArtifactRecords SET IsDeleted=TRUE
		WHERE (ArtifactId=$1 OR ParentArtifactId=$1) AND Version=$2
		  AND NOT EXISTS (SELECT 1 FROM ArtifactAliases WHERE ArtifactId=$1 AND Version=$2)`,
		artifactID, version)
	if err != nil {
		dbErr(w, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		errResp(w, http.StatusConflict, "VERSION_IS_LATEST",
			"version "+version+" is what the tag '"+LatestAlias+"' points at; move the tag to another version first")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
