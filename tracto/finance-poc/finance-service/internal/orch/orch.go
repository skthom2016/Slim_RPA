// Package orch is the part of the Tracto Orchestrator's HTTP API the finance service uses: it
// finds the main job that called the service and writes lines to that run's log.
package orch

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"strings"
	"time"
)

// Client talks to the Orchestrator at Base, for example http://127.0.0.1:5080.
type Client struct {
	Base string
	HTTP *http.Client
}

// New returns a client for the Orchestrator at base.
func New(base string) *Client {
	return &Client{Base: strings.TrimRight(base, "/"), HTTP: &http.Client{Timeout: 10 * time.Second}}
}

func (c *Client) do(ctx context.Context, method, path string, body, out any) error {
	var rd io.Reader
	if body != nil {
		b, err := json.Marshal(body)
		if err != nil {
			return err
		}
		rd = bytes.NewReader(b)
	}
	req, err := http.NewRequestWithContext(ctx, method, c.Base+path, rd)
	if err != nil {
		return err
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := c.HTTP.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	raw, _ := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return fmt.Errorf("orchestrator %s %s returned %d: %s", method, path, resp.StatusCode, strings.TrimSpace(string(raw)))
	}
	if out == nil {
		return nil
	}
	return json.Unmarshal(raw, out)
}

// FindRunning returns the id of the newest running run of a script, or "" when there is none.
// A script has no variable that holds its own run id, so the service looks the caller up: the
// main job is the running run of its script while it waits for the service's reply.
func (c *Client) FindRunning(ctx context.Context, artifactID string) (string, error) {
	var resp struct {
		Jobs []struct {
			RunID            string `json:"runId"`
			ScriptArtifactID string `json:"scriptArtifactId"`
		} `json:"jobs"`
	}
	q := url.Values{"state": {"RUNNING"}, "limit": {"200"}, "includeSegments": {"true"}}
	if err := c.do(ctx, http.MethodGet, "/jobs?"+q.Encode(), nil, &resp); err != nil {
		return "", err
	}
	for _, j := range resp.Jobs { // newest first
		if j.ScriptArtifactID == artifactID || strings.HasPrefix(j.ScriptArtifactID, artifactID+".seg") {
			return j.RunID, nil
		}
	}
	return "", nil
}

// Log adds one line to a run's log. Level is "Info", "Warning" or "Error". A line that cannot be
// stored is not worth failing a run for, so the error is only printed.
func (c *Client) Log(runID, level, message string) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	body := map[string]any{
		"runtime": "finance-service",
		"entries": []map[string]string{{
			"level":      level,
			"message":    message,
			"occurredAt": time.Now().UTC().Format(time.RFC3339Nano),
		}},
	}
	if err := c.do(ctx, http.MethodPost, "/jobs/"+url.PathEscape(runID)+"/logs", body, nil); err != nil {
		log.Printf("could not write to the run log of %s: %v", runID, err)
	}
}

// RunLog writes the lines of one service run to the console and, when RunID is set, to that
// Orchestrator run's log.
type RunLog struct {
	Client *Client
	RunID  string
	Prefix string
}

// Info logs an item that was done.
func (l RunLog) Info(msg string) { l.write("Info", msg) }

// Warn logs an exception.
func (l RunLog) Warn(msg string) { l.write("Warning", msg) }

func (l RunLog) write(level, msg string) {
	log.Printf("%s %-7s %s", l.Prefix, level, msg)
	if l.Client != nil && l.RunID != "" {
		l.Client.Log(l.RunID, level, msg)
	}
}
