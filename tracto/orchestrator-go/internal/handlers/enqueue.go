package handlers

import (
	"database/sql"
	"errors"
	"strings"
)

// ErrArtifactNotFound means the script version is not published or has been deleted.
var ErrArtifactNotFound = errors.New("artifact not found")

// enqueueRun queues a script: a segmented script becomes a parent run plus one child run per
// segment, anything else becomes a single Queued run. It returns the run id, the state to
// report and, for a segmented script, how many segments it has.
func enqueueRun(db *sql.DB, spec runSpec) (runID, state string, segments int, err error) {
	if seg := loadSegmentedScript(db, spec.ArtifactId, spec.Version); seg != nil {
		runID, err = startSegmentedRun(db, seg, spec)
		if err != nil {
			return "", "", 0, err
		}
		return runID, "RUNNING", len(seg.Segments), nil
	}
	if spec.SchemaVersion == "" {
		spec.SchemaVersion = "1"
	}
	runID, err = insertRun(db, spec)
	if err != nil {
		return "", "", 0, err
	}
	return runID, "QUEUED", 0, nil
}

// ArtifactPublished reports whether a script version exists and is not deleted.
func ArtifactPublished(db *sql.DB, artifactID, version string) (bool, error) {
	var isDeleted int
	err := db.QueryRow(
		`SELECT IsDeleted FROM ArtifactRecords WHERE ArtifactId=? AND Version=?`,
		artifactID, version).Scan(&isDeleted)
	if errors.Is(err, sql.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	return isDeleted == 0, nil
}

// ScheduledRun is one run the scheduler queues on a schedule's behalf.
type ScheduledRun struct {
	ArtifactID string
	Version    string
	Params     map[string]string
	// EventID identifies this fire of this schedule, so a repeat of the same fire is not queued twice.
	EventID string
}

// EnqueueScheduled queues a run for a schedule with triggerType "Schedule". It is the same
// path POST /jobs takes once a request is accepted. It returns ErrArtifactNotFound when the
// script version is not published.
func EnqueueScheduled(db *sql.DB, r ScheduledRun) (string, error) {
	ok, err := ArtifactPublished(db, r.ArtifactID, r.Version)
	if err != nil {
		return "", err
	}
	if !ok {
		return "", ErrArtifactNotFound
	}
	eventID := r.EventID
	runID, _, _, err := enqueueRun(db, runSpec{
		ArtifactId:    r.ArtifactID,
		Version:       r.Version,
		SchemaVersion: "1",
		TriggerType:   "Schedule",
		Params:        r.Params,
		EventId:       &eventID,
	})
	return runID, err
}

// RunIsActive reports whether a run exists and has not reached a terminal state.
func RunIsActive(db *sql.DB, runID string) (bool, error) {
	var state string
	err := db.QueryRow(`SELECT State FROM RunRecords WHERE Id=?`, runID).Scan(&state)
	if errors.Is(err, sql.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	switch strings.ToLower(state) {
	case "succeeded", "failed", "handedoff", "cancelled", "timedout", "unknown":
		return false, nil
	}
	return true, nil
}
