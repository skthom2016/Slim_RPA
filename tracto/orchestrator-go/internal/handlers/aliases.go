package handlers

import (
	"database/sql"
	"errors"
	"net/http"
	"time"

	"github.com/go-chi/chi/v5"
)

// LatestAlias is the one movable tag. A script version can be asked for by number ("1.0.3") or as
// "latest", which means whatever version the tag was last moved to.
const LatestAlias = "latest"

// ResolveVersion turns "latest" into the version the tag points at. Any other version is returned
// unchanged and is not checked here. found is false when "latest" has not been set for the script.
func ResolveVersion(db *sql.DB, artifactID, version string) (concrete string, found bool, err error) {
	if version != LatestAlias {
		return version, true, nil
	}
	err = db.QueryRow(
		`SELECT Version FROM ArtifactAliases WHERE ArtifactId=$1 AND Alias=$2`,
		artifactID, LatestAlias).Scan(&concrete)
	if errors.Is(err, sql.ErrNoRows) {
		return "", false, nil
	}
	if err != nil {
		return "", false, err
	}
	return concrete, true, nil
}

// PUT /artifacts/{id}/aliases/{alias}   body: {"version":"1.0.3","updatedBy":"designer"}
// Moves the tag to the given version. The tag is created on first use. The version must be
// published, not deleted, and a script of its own (not one of the per-segment artifacts).
func (h *ArtifactHandler) SetAlias(w http.ResponseWriter, r *http.Request) {
	artifactID := chi.URLParam(r, "id")
	alias := chi.URLParam(r, "alias")
	if alias != LatestAlias {
		errResp(w, http.StatusBadRequest, "INVALID_ALIAS", "the only tag is '"+LatestAlias+"'")
		return
	}

	var req struct {
		Version   string `json:"version"`
		UpdatedBy string `json:"updatedBy"`
	}
	if !decodeJSON(w, r, &req) {
		return
	}
	if req.Version == "" || req.Version == LatestAlias {
		errResp(w, http.StatusBadRequest, "INVALID_REQUEST", "version is required and must be a published version number")
		return
	}

	var updatedBy *string
	if req.UpdatedBy != "" {
		updatedBy = &req.UpdatedBy
	}
	now := time.Now().UTC().Format(time.RFC3339Nano)
	// One statement checks the version and writes the tag, so a version that is deleted at the
	// same moment cannot end up tagged.
	res, err := h.db.Exec(`
		INSERT INTO ArtifactAliases(ArtifactId,Alias,Version,PreviousVersion,UpdatedAt,UpdatedBy)
		SELECT ArtifactId,$3,Version,NULL,$4,$5 FROM ArtifactRecords
		WHERE ArtifactId=$1 AND Version=$2 AND NOT IsDeleted AND ParentArtifactId IS NULL
		ON CONFLICT (ArtifactId,Alias) DO UPDATE SET
			PreviousVersion = CASE WHEN ArtifactAliases.Version <> EXCLUDED.Version
			                       THEN ArtifactAliases.Version ELSE ArtifactAliases.PreviousVersion END,
			Version   = EXCLUDED.Version,
			UpdatedAt = EXCLUDED.UpdatedAt,
			UpdatedBy = EXCLUDED.UpdatedBy`,
		artifactID, req.Version, alias, now, nullStr(updatedBy))
	if err != nil {
		dbErr(w, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		errResp(w, http.StatusNotFound, "NOT_FOUND",
			"artifact "+artifactID+"/"+req.Version+" is not published")
		return
	}
	h.respondAliases(w, artifactID)
}

// GET /artifacts/{id}/aliases
func (h *ArtifactHandler) GetAliases(w http.ResponseWriter, r *http.Request) {
	h.respondAliases(w, chi.URLParam(r, "id"))
}

func (h *ArtifactHandler) respondAliases(w http.ResponseWriter, artifactID string) {
	rows, err := h.db.Query(
		`SELECT Alias,Version,PreviousVersion,UpdatedAt,UpdatedBy FROM ArtifactAliases WHERE ArtifactId=$1`,
		artifactID)
	if err != nil {
		dbErr(w, err)
		return
	}
	defer rows.Close()

	type aliasRow struct {
		Version         string  `json:"version"`
		PreviousVersion *string `json:"previousVersion"`
		UpdatedAt       string  `json:"updatedAt"`
		UpdatedBy       *string `json:"updatedBy"`
	}
	aliases := map[string]aliasRow{}
	for rows.Next() {
		var name string
		var a aliasRow
		var prev, by sql.NullString
		if err := rows.Scan(&name, &a.Version, &prev, &a.UpdatedAt, &by); err != nil {
			dbErr(w, err)
			return
		}
		if prev.Valid {
			a.PreviousVersion = &prev.String
		}
		if by.Valid {
			a.UpdatedBy = &by.String
		}
		aliases[name] = a
	}
	respond(w, http.StatusOK, map[string]any{"artifactId": artifactID, "aliases": aliases})
}
