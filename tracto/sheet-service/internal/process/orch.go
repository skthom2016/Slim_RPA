// Package process runs a child job for each pending row of a sheet and writes what the job
// returned back into that row. It is the loop that a Tracto script cannot express: scripts run
// in a straight line, so repetition lives here, one child job per row.
//
// The package knows nothing about any particular site. A request names the sheet, the child
// script, which column feeds which child parameter and which child output fills which column.
package process

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"maps"
	"net/http"
	"net/url"
	"strings"
	"time"
)

// Orchestrator is the part of the Tracto Orchestrator's HTTP API this package uses.
type Orchestrator struct {
	Base string // for example http://127.0.0.1:5080
	HTTP *http.Client
}

// NewOrchestrator returns a client for the Orchestrator at base.
func NewOrchestrator(base string) *Orchestrator {
	return &Orchestrator{
		Base: strings.TrimRight(base, "/"),
		HTTP: &http.Client{Timeout: 30 * time.Second},
	}
}

// SubmitRequest queues one run of a published script.
type SubmitRequest struct {
	ArtifactID    string
	Version       string
	BusinessJobID string // the same for every child of one batch, so they can be traced together
	EventID       string // unique per row: the Orchestrator returns the existing run on a repeat
	Params        map[string]string
}

// RunInfo is what the Orchestrator reports about a run.
type RunInfo struct {
	State   string // upper case: RUNNING, SUCCEEDED, FAILED, ...
	Outputs map[string]string
}

// IsTerminal reports whether a run in this state will never change again.
func IsTerminal(state string) bool {
	switch strings.ToUpper(state) {
	case "SUCCEEDED", "FAILED", "CANCELLED", "TIMEDOUT", "UNKNOWN", "HANDEDOFF":
		return true
	}
	return false
}

func (o *Orchestrator) do(ctx context.Context, method, path string, body any, out any) error {
	var rd io.Reader
	if body != nil {
		b, err := json.Marshal(body)
		if err != nil {
			return err
		}
		rd = bytes.NewReader(b)
	}
	req, err := http.NewRequestWithContext(ctx, method, o.Base+path, rd)
	if err != nil {
		return err
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := o.HTTP.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	raw, _ := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		msg := strings.TrimSpace(string(raw))
		if len(msg) > 300 {
			msg = msg[:300] + "…"
		}
		return fmt.Errorf("orchestrator %s %s returned %d: %s", method, path, resp.StatusCode, msg)
	}
	if out == nil {
		return nil
	}
	return json.Unmarshal(raw, out)
}

// Submit queues the script and returns the run id.
func (o *Orchestrator) Submit(ctx context.Context, r SubmitRequest) (string, error) {
	body := map[string]any{
		"schemaVersion":    "1",
		"scriptArtifactId": r.ArtifactID,
		"scriptVersion":    r.Version,
		"triggerType":      "Event",
		"params":           r.Params,
	}
	if r.BusinessJobID != "" {
		body["businessJobId"] = r.BusinessJobID
	}
	if r.EventID != "" {
		body["eventId"] = r.EventID
	}
	var resp struct {
		RunID string `json:"runId"`
	}
	if err := o.do(ctx, http.MethodPost, "/jobs", body, &resp); err != nil {
		return "", err
	}
	if resp.RunID == "" {
		return "", fmt.Errorf("orchestrator accepted the job but returned no run id")
	}
	return resp.RunID, nil
}

// Get reads a run. A script made of segments reports its values on each segment rather than on
// the run itself, so both are merged; the run's own value wins on a name clash.
func (o *Orchestrator) Get(ctx context.Context, runID string) (RunInfo, error) {
	var resp struct {
		State    string            `json:"state"`
		Outputs  map[string]string `json:"outputs"`
		Segments []struct {
			Outputs map[string]string `json:"outputs"`
		} `json:"segments"`
	}
	if err := o.do(ctx, http.MethodGet, "/jobs/"+url.PathEscape(runID), nil, &resp); err != nil {
		return RunInfo{}, err
	}
	merged := map[string]string{}
	for _, s := range resp.Segments {
		maps.Copy(merged, s.Outputs)
	}
	maps.Copy(merged, resp.Outputs)
	return RunInfo{State: strings.ToUpper(resp.State), Outputs: merged}, nil
}

// ArtifactPublished reports whether a script version is published and not deleted. Any answer
// other than a plain yes or no is an error, so an unreachable Orchestrator is not mistaken for a
// missing script.
func (o *Orchestrator) ArtifactPublished(ctx context.Context, id, version string) (bool, error) {
	err := o.do(ctx, http.MethodGet, "/artifacts/"+url.PathEscape(id)+"/versions/"+url.PathEscape(version), nil, nil)
	if err == nil {
		return true, nil
	}
	if strings.Contains(err.Error(), " returned 404:") {
		return false, nil
	}
	return false, err
}

// Cancel asks the Orchestrator to stop a run. Failing to is not worth failing anything else for.
func (o *Orchestrator) Cancel(ctx context.Context, runID, requestedBy string) error {
	return o.do(ctx, http.MethodPost, "/jobs/"+url.PathEscape(runID)+"/cancel",
		map[string]string{"requestedBy": requestedBy}, nil)
}

// FirstError returns the first error line a runtime logged for the run, or "" when there is none
// or the Orchestrator cannot say. It is what turns "FAILED" into a reason a person can act on.
func (o *Orchestrator) FirstError(ctx context.Context, runID string) string {
	var resp struct {
		Logs []struct {
			Message string `json:"message"`
		} `json:"logs"`
	}
	if err := o.do(ctx, http.MethodGet, "/admin/logs/"+url.PathEscape(runID)+"?level=Error", nil, &resp); err != nil {
		return ""
	}
	if len(resp.Logs) == 0 {
		return ""
	}
	return strings.TrimSpace(resp.Logs[0].Message)
}
