package server

import (
	"encoding/base64"
	"fmt"
	"net/http"
	"testing"

	"tracto/orchestrator/internal/handlers"
)

// moveLatest moves the tag "latest" of an artifact to a version.
func (e *env) moveLatest(id, version string) map[string]any {
	e.t.Helper()
	return e.must(http.StatusOK, "PUT", "/artifacts/"+id+"/aliases/latest",
		map[string]any{"version": version, "updatedBy": "test"})
}

func latestOf(out map[string]any) map[string]any {
	aliases, _ := out["aliases"].(map[string]any)
	l, _ := aliases["latest"].(map[string]any)
	return l
}

// Publishing a version that is already stored with identical content succeeds without adding
// anything. The reply says so, so a client can tell "published" from "nothing new".
func TestPublish_ReplySaysWhenTheVersionWasAlreadyThere(t *testing.T) {
	e := newEnv(t)
	body := map[string]any{"schemaVersion": "1", "payload": base64.StdEncoding.EncodeToString([]byte(flatScript("child", "1.0.0")))}

	first := e.must(http.StatusCreated, "PUT", "/artifacts/child/versions/1.0.0", body)
	if first["alreadyPublished"] != false {
		t.Fatalf("a new version must say alreadyPublished=false: %v", first)
	}
	again := e.must(http.StatusCreated, "PUT", "/artifacts/child/versions/1.0.0", body)
	if again["alreadyPublished"] != true {
		t.Fatalf("a repeat of the same content must say alreadyPublished=true: %v", again)
	}
	if n := e.count(`SELECT COUNT(*) FROM ArtifactRecords WHERE ArtifactId='child'`); n != 1 {
		t.Fatalf("the repeat must not add a row, found %d", n)
	}

	// A change under the same number is still refused outright.
	changed := map[string]any{"schemaVersion": "1", "payload": base64.StdEncoding.EncodeToString([]byte(flatScript("child", "1.0.0") + " "))}
	e.must(http.StatusConflict, "PUT", "/artifacts/child/versions/1.0.0", changed)
}

func TestLatest_NotSetYet(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))

	// Asking for "latest" before the tag exists is refused, as asking for an unpublished version is.
	code, out := e.call("POST", "/jobs", map[string]any{
		"scriptArtifactId": "child", "scriptVersion": "latest", "schemaVersion": "1", "triggerType": "Manual"})
	if code != http.StatusUnprocessableEntity || str(out, "error") != "ARTIFACT_NOT_FOUND" {
		t.Fatalf("job with latest before the tag is set: %d %v", code, out)
	}
	e.must(http.StatusNotFound, "GET", "/artifacts/child/versions/latest", nil)

	if got := latestOf(e.must(http.StatusOK, "GET", "/artifacts/child/aliases", nil)); got != nil {
		t.Fatalf("no tag should exist yet, got %v", got)
	}
	if n := e.count(`SELECT COUNT(*) FROM RunRecords`); n != 0 {
		t.Fatalf("%d runs were created", n)
	}
}

func TestLatest_VersionNamedLatestCannotBePublished(t *testing.T) {
	e := newEnv(t)
	code, out := e.call("PUT", "/artifacts/child/versions/latest", map[string]any{
		"schemaVersion": "1", "payload": "e30="})
	if code != http.StatusUnprocessableEntity || str(out, "error") != "INVALID_VERSION" {
		t.Fatalf("publish version 'latest': %d %v", code, out)
	}
}

func TestLatest_MoveRefusals(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("seg", "1.0.0", segScript("seg", "1.0.0"))

	put := func(id, alias string, body map[string]any) (int, map[string]any) {
		return e.call("PUT", "/artifacts/"+id+"/aliases/"+alias, body)
	}
	cases := []struct {
		name  string
		id    string
		alias string
		body  map[string]any
		want  int
	}{
		{"unpublished version", "child", "latest", map[string]any{"version": "9.9.9"}, http.StatusNotFound},
		{"unknown script", "nope", "latest", map[string]any{"version": "1.0.0"}, http.StatusNotFound},
		{"other tag name", "child", "stable", map[string]any{"version": "1.0.0"}, http.StatusBadRequest},
		{"no version", "child", "latest", map[string]any{}, http.StatusBadRequest},
		{"version is the word latest", "child", "latest", map[string]any{"version": "latest"}, http.StatusBadRequest},
		{"a per-segment artifact", "seg.seg1", "latest", map[string]any{"version": "1.0.0"}, http.StatusNotFound},
	}
	for _, c := range cases {
		if code, out := put(c.id, c.alias, c.body); code != c.want {
			t.Errorf("%s: status %d, want %d (%v)", c.name, code, c.want, out)
		}
	}
	if n := e.count(`SELECT COUNT(*) FROM ArtifactAliases`); n != 0 {
		t.Fatalf("%d tags were created by refused requests", n)
	}

	// A deleted version cannot be tagged.
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))
	e.must(http.StatusNoContent, "DELETE", "/artifacts/child/versions/1.0.1", nil)
	if code, _ := put("child", "latest", map[string]any{"version": "1.0.1"}); code != http.StatusNotFound {
		t.Fatalf("tagging a deleted version: status %d", code)
	}
}

func TestLatest_RunsFollowTheTagAndStayPinned(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))
	e.moveLatest("child", "1.0.0")

	// A job asked for as "latest" runs the version the tag points at, and says which.
	out := e.must(http.StatusOK, "POST", "/jobs", map[string]any{
		"scriptArtifactId": "child", "scriptVersion": "latest", "schemaVersion": "1", "triggerType": "Manual"})
	first := str(out, "runId")
	if str(out, "scriptVersion") != "1.0.0" {
		t.Fatalf("reply should name the resolved version, got %v", out)
	}
	got := e.must(http.StatusOK, "GET", "/jobs/"+first, nil)
	if str(got, "scriptVersion") != "1.0.0" {
		t.Fatalf("run records %q, want 1.0.0", str(got, "scriptVersion"))
	}

	// Moving the tag does not change the run that was already queued.
	moved := e.moveLatest("child", "1.0.1")
	l := latestOf(moved)
	if str(l, "version") != "1.0.1" || str(l, "previousVersion") != "1.0.0" || str(l, "updatedBy") != "test" {
		t.Fatalf("after the move: %v", l)
	}
	if v := str(e.must(http.StatusOK, "GET", "/jobs/"+first, nil), "scriptVersion"); v != "1.0.0" {
		t.Fatalf("queued run changed to %q when the tag moved", v)
	}
	second := e.create("child", "latest", nil)
	if v := str(e.must(http.StatusOK, "GET", "/jobs/"+second, nil), "scriptVersion"); v != "1.0.1" {
		t.Fatalf("new run uses %q, want 1.0.1", v)
	}

	// A version asked for by number is untouched by the tag.
	explicit := e.create("child", "1.0.0", nil)
	if v := str(e.must(http.StatusOK, "GET", "/jobs/"+explicit, nil), "scriptVersion"); v != "1.0.0" {
		t.Fatalf("explicit version became %q", v)
	}

	// Moving back is a move like any other, and remembers where it came from.
	back := latestOf(e.moveLatest("child", "1.0.0"))
	if str(back, "version") != "1.0.0" || str(back, "previousVersion") != "1.0.1" {
		t.Fatalf("after moving back: %v", back)
	}
}

func TestLatest_TaggingTheSameVersionAgainKeepsThePreviousOne(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))
	e.moveLatest("child", "1.0.0")
	e.moveLatest("child", "1.0.1")
	again := latestOf(e.moveLatest("child", "1.0.1"))
	if str(again, "version") != "1.0.1" || str(again, "previousVersion") != "1.0.0" {
		t.Fatalf("a repeated move lost the previous version: %v", again)
	}
}

func TestLatest_GetByTagNamesTheVersion(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.moveLatest("child", "1.0.0")
	got := e.must(http.StatusOK, "GET", "/artifacts/child/versions/latest", nil)
	if str(got, "version") != "1.0.0" || str(got, "artifactId") != "child" || str(got, "payload") == "" {
		t.Fatalf("GET versions/latest: %v", got)
	}
}

func TestLatest_ListMarksTheTaggedVersion(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))
	e.moveLatest("child", "1.0.1")

	list := e.must(http.StatusOK, "GET", "/artifacts", nil)
	rows, _ := list["artifacts"].([]any)
	if len(rows) != 2 {
		t.Fatalf("want 2 rows, got %d", len(rows))
	}
	for _, r := range rows {
		m := r.(map[string]any)
		want := str(m, "version") == "1.0.1"
		if m["isLatest"] != want {
			t.Errorf("version %s: isLatest = %v, want %v", str(m, "version"), m["isLatest"], want)
		}
	}
}

func TestLatest_TheTaggedVersionCannotBeDeleted(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))
	e.moveLatest("child", "1.0.1")

	code, out := e.call("DELETE", "/artifacts/child/versions/1.0.1", nil)
	if code != http.StatusConflict || str(out, "error") != "VERSION_IS_LATEST" {
		t.Fatalf("delete of the tagged version: %d %v", code, out)
	}
	// An untagged version can be deleted, and the tagged one still resolves.
	e.must(http.StatusNoContent, "DELETE", "/artifacts/child/versions/1.0.0", nil)
	if v := str(e.must(http.StatusOK, "GET", "/artifacts/child/versions/latest", nil), "version"); v != "1.0.1" {
		t.Fatalf("latest resolves to %q after deleting another version", v)
	}

	// After the tag moves away, the old version can go.
	e.publish("child", "1.0.2", flatScript("child", "1.0.2"))
	e.moveLatest("child", "1.0.2")
	e.must(http.StatusNoContent, "DELETE", "/artifacts/child/versions/1.0.1", nil)
}

func TestLatest_SegmentedScriptKeepsTheResolvedVersion(t *testing.T) {
	e := newEnv(t)
	e.publish("seg", "1.0.0", segScript("seg", "1.0.0"))
	e.publish("seg", "1.0.1", segScript("seg", "1.0.1"))
	e.moveLatest("seg", "1.0.0")

	out := e.must(http.StatusOK, "POST", "/jobs", map[string]any{
		"scriptArtifactId": "seg", "scriptVersion": "latest", "schemaVersion": "1", "triggerType": "Manual"})
	parent := str(out, "runId")
	if out["segments"] != float64(2) || str(out, "scriptVersion") != "1.0.0" {
		t.Fatalf("segmented job: %v", out)
	}

	// The tag moves while the first segment is still waiting. The second segment is only created
	// after the first finishes, and it must still be the pinned 1.0.0, not the new "latest".
	e.moveLatest("seg", "1.0.1")
	e.register("w1")
	o := e.pull("w1")
	e.accept("w1", o)
	e.finish(o, "Succeeded", nil)
	eventually(t, "the second segment run", func() bool {
		return e.count(`SELECT COUNT(*) FROM RunRecords WHERE ParentRunId=$1`, parent) == 2
	})
	if n := e.count(`SELECT COUNT(*) FROM RunRecords WHERE (Id=$1 OR ParentRunId=$1) AND ScriptVersion<>'1.0.0'`, parent); n != 0 {
		t.Fatalf("%d runs of the parent are not on 1.0.0", n)
	}
}

func TestLatest_ScheduleResolvesWhenItFires(t *testing.T) {
	e := newEnv(t)
	e.publish("child", "1.0.0", flatScript("child", "1.0.0"))
	e.publish("child", "1.0.1", flatScript("child", "1.0.1"))

	// Before the tag exists the run cannot be queued.
	if _, err := handlers.EnqueueScheduled(e.db, handlers.ScheduledRun{ArtifactID: "child", Version: "latest", EventID: "e0"}); err != handlers.ErrArtifactNotFound {
		t.Fatalf("scheduled run with no tag: %v", err)
	}

	e.moveLatest("child", "1.0.0")
	for i, want := range []string{"1.0.0", "1.0.1"} {
		if i == 1 {
			e.moveLatest("child", "1.0.1")
		}
		id, err := handlers.EnqueueScheduled(e.db, handlers.ScheduledRun{
			ArtifactID: "child", Version: "latest", EventID: fmt.Sprintf("e%d", i+1)})
		if err != nil {
			t.Fatal(err)
		}
		if v := str(e.must(http.StatusOK, "GET", "/jobs/"+id, nil), "scriptVersion"); v != want {
			t.Fatalf("fire %d ran %q, want %q", i+1, v, want)
		}
	}
}

// Tagging a version and deleting it at the same moment must never leave the tag on a deleted version.
func TestLatest_TagAndDeleteRaceNeverLeavesATagOnADeletedVersion(t *testing.T) {
	e := newEnv(t)
	for i := 0; i < 15; i++ {
		id := fmt.Sprintf("race%d", i)
		e.publish(id, "1.0.0", flatScript(id, "1.0.0"))
		parallel(2, func(n int) {
			if n == 0 {
				e.call("PUT", "/artifacts/"+id+"/aliases/latest", map[string]any{"version": "1.0.0"})
			} else {
				e.call("DELETE", "/artifacts/"+id+"/versions/1.0.0", nil)
			}
		})
		bad := e.count(`SELECT COUNT(*) FROM ArtifactAliases al
		                JOIN ArtifactRecords ar ON ar.ArtifactId=al.ArtifactId AND ar.Version=al.Version
		                WHERE al.ArtifactId=$1 AND ar.IsDeleted`, id)
		if bad != 0 {
			t.Fatalf("round %d: the tag points at a deleted version", i)
		}
	}
}
