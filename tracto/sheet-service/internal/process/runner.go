package process

import (
	"context"
	"errors"
	"fmt"
	"slices"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/google/uuid"

	"tracto/sheet-service/internal/engine"
	"tracto/sheet-service/internal/sheet"
)

// StartRequest describes a batch: which sheet, which child script, and how a row maps onto the
// child's parameters and the child's outputs map back onto the row.
type StartRequest struct {
	Source sheet.Config `json:"source"`
	Child  struct {
		ArtifactID string `json:"artifactId"`
		Version    string `json:"version"`
	} `json:"child"`
	// Params maps a child parameter name to the column that supplies it.
	Params map[string]string `json:"params"`
	// Outputs maps a name the child published (its storeAs value, such as "invItem.value") to
	// the column it is written to.
	Outputs map[string]string `json:"outputs"`
	// StatusColumn holds Done or Failed for each row. A row is pending until it holds a DoneValue.
	StatusColumn string   `json:"statusColumn"`
	DoneValues   []string `json:"doneValues"`
	// MessageColumn, when set, says why a row failed, and is cleared when it later succeeds.
	MessageColumn string `json:"messageColumn"`
	// TimeoutSeconds is how long one child may run before it is cancelled (default 300).
	TimeoutSeconds int `json:"timeoutSeconds"`
}

const (
	defaultStatusColumn   = "Status"
	defaultTimeoutSeconds = 300
	statusDone            = "Done"
	statusFailed          = "Failed"
	maxMessageChars       = 500
	// maxConsecutiveOrchestratorErrors is how many rows in a row may fail to reach the
	// Orchestrator before the batch stops instead of marking every remaining row Failed.
	maxConsecutiveOrchestratorErrors = 3
)

// ErrInvalid wraps a request that cannot be run as described.
var ErrInvalid = errors.New("invalid request")

func invalid(format string, a ...any) error {
	return fmt.Errorf("%w: %s", ErrInvalid, fmt.Sprintf(format, a...))
}

// BusyError means a batch is already running on the same sheet.
type BusyError struct{ BatchID string }

func (e *BusyError) Error() string {
	return "a batch is already running on this sheet: " + e.BatchID
}

// OrchestratorError wraps a failure to reach the Orchestrator while starting a batch.
type OrchestratorError struct{ Err error }

func (e *OrchestratorError) Error() string { return e.Err.Error() }
func (e *OrchestratorError) Unwrap() error { return e.Err }

// SourceError wraps a failure to open or read the sheet, so the caller can pick a status.
type SourceError struct{ Err error }

func (e *SourceError) Error() string { return e.Err.Error() }
func (e *SourceError) Unwrap() error { return e.Err }

// Manager starts batches and runs them one row at a time.
type Manager struct {
	store *Store
	orch  *Orchestrator

	// Timing knobs. They are fields so tests do not wait in real seconds.
	PollEvery      time.Duration
	TimeoutUnit    time.Duration
	WriteRetries   int
	WriteRetryWait time.Duration

	mu      sync.Mutex
	active  map[string]string             // sheet key -> batch id
	cancels map[string]context.CancelFunc // batch id -> cancel
}

// NewManager returns a Manager that records progress in store and queues jobs through orch.
func NewManager(store *Store, orch *Orchestrator) *Manager {
	return &Manager{
		store: store, orch: orch,
		PollEvery: 2 * time.Second, TimeoutUnit: time.Second,
		WriteRetries: 3, WriteRetryWait: 2 * time.Second,
		active: map[string]string{}, cancels: map[string]context.CancelFunc{},
	}
}

// Store exposes the progress store to the HTTP layer.
func (m *Manager) Store() *Store { return m.store }

// StartResult is what starting a batch returns at once.
type StartResult struct {
	BatchID string `json:"batchId"`
	State   string `json:"state"`
	Total   int    `json:"total"`
}

func (r *StartRequest) applyDefaults() {
	if r.StatusColumn == "" {
		r.StatusColumn = defaultStatusColumn
	}
	if len(r.DoneValues) == 0 {
		r.DoneValues = []string{statusDone}
	}
	if r.TimeoutSeconds <= 0 {
		r.TimeoutSeconds = defaultTimeoutSeconds
	}
}

func (r *StartRequest) validate() error {
	if strings.TrimSpace(r.Source.Path) == "" {
		return invalid("source.path is required")
	}
	if strings.TrimSpace(r.Child.ArtifactID) == "" || strings.TrimSpace(r.Child.Version) == "" {
		return invalid("child.artifactId and child.version are required")
	}
	if len(r.Params) == 0 {
		return invalid("params is required: say which column feeds which child parameter")
	}
	if len(r.Outputs) == 0 {
		return invalid("outputs is required: say which child output fills which column")
	}
	used := map[string]string{}
	for name, col := range r.Params {
		if strings.TrimSpace(name) == "" || strings.TrimSpace(col) == "" {
			return invalid("params needs a parameter name and a column for every entry")
		}
		used[col] = "a param"
	}
	for out, col := range r.Outputs {
		if strings.TrimSpace(out) == "" || strings.TrimSpace(col) == "" {
			return invalid("outputs needs a child output name and a column for every entry")
		}
		if used[col] == "a param" {
			return invalid("column %q is both read as a param and written as an output", col)
		}
		used[col] = "an output"
	}
	if used[r.StatusColumn] != "" {
		return invalid("statusColumn %q is also used as %s", r.StatusColumn, used[r.StatusColumn])
	}
	if r.MessageColumn != "" {
		if r.MessageColumn == r.StatusColumn {
			return invalid("messageColumn and statusColumn must be different columns")
		}
		if used[r.MessageColumn] != "" {
			return invalid("messageColumn %q is also used as %s", r.MessageColumn, used[r.MessageColumn])
		}
	}
	return nil
}

func (r *StartRequest) columns() []string {
	var cols []string
	for _, c := range r.Params {
		cols = append(cols, c)
	}
	for _, c := range r.Outputs {
		cols = append(cols, c)
	}
	cols = append(cols, r.StatusColumn)
	if r.MessageColumn != "" {
		cols = append(cols, r.MessageColumn)
	}
	return cols
}

func sourceKey(c sheet.Config) string {
	return strings.ToLower(strings.TrimSpace(c.Path)) + "|" + strings.ToLower(strings.TrimSpace(c.Sheet))
}

// readPending reads the whole sheet in pages and returns the header and the rows that still need
// action. A batch starts by reading everything so that a wrong path or column is refused to the
// caller, not discovered in the background.
func readPending(ctx context.Context, src sheet.Source, headerRow int) ([]string, []sheet.Row, error) {
	if headerRow <= 0 {
		headerRow = 1
	}
	const page = 1000
	var header []string
	var all []sheet.Row
	start := headerRow + 1
	for range 10000 { // a bound, not a limit anyone should reach
		b, err := src.ReadRows(ctx, start, start+page-1)
		if err != nil {
			return nil, nil, err
		}
		header = b.Header
		all = append(all, b.Rows...)
		if b.EndOfData {
			break
		}
		start = b.EndRow + 1
	}
	return header, all, nil
}

func hasColumn(header []string, name string) bool { return slices.Contains(header, name) }

// Start checks the request, reads the sheet, and begins the batch in the background. It returns
// as soon as the batch is recorded; the result is read with the Store's Get.
func (m *Manager) Start(ctx context.Context, req StartRequest) (*StartResult, error) {
	req.applyDefaults()
	if err := req.validate(); err != nil {
		return nil, err
	}

	id := "b-" + time.Now().UTC().Format("20060102-150405") + "-" + uuid.NewString()[:6]
	key := sourceKey(req.Source)

	m.mu.Lock()
	if running, busy := m.active[key]; busy {
		m.mu.Unlock()
		return nil, &BusyError{BatchID: running}
	}
	m.active[key] = id
	m.mu.Unlock()
	release := func() {
		m.mu.Lock()
		delete(m.active, key)
		delete(m.cancels, id)
		m.mu.Unlock()
	}

	src, err := sheet.Open(req.Source)
	if err != nil {
		release()
		return nil, &SourceError{Err: err}
	}
	header, all, err := readPending(ctx, src, req.Source.HeaderRow)
	if err != nil {
		release()
		return nil, &SourceError{Err: err}
	}
	for _, col := range req.columns() {
		if !hasColumn(header, col) {
			release()
			return nil, invalid("column %q not found in header", col)
		}
	}
	rows, err := engine.PendingRows(&sheet.Batch{Header: header, Rows: all},
		&engine.Pending{Column: req.StatusColumn, DoneValues: req.DoneValues})
	if err != nil {
		release()
		return nil, invalid("%v", err)
	}

	// A script that is not published would fail every row one by one in the background. Say so
	// now, to whoever started the batch.
	published, err := m.orch.ArtifactPublished(ctx, req.Child.ArtifactID, req.Child.Version)
	if err != nil {
		release()
		return nil, &OrchestratorError{Err: err}
	}
	if !published {
		release()
		return nil, invalid("child script %s version %s is not published on the Orchestrator", req.Child.ArtifactID, req.Child.Version)
	}

	items := make([]ItemRecord, len(rows))
	for i, r := range rows {
		items[i] = ItemRecord{Row: r.Number, Label: label(req, r)}
	}
	rec := BatchRecord{
		ID: id, SourcePath: req.Source.Path, Sheet: req.Source.Sheet,
		Child: req.Child.ArtifactID + "@" + req.Child.Version,
	}
	if err := m.store.CreateBatch(rec, items); err != nil {
		release()
		return nil, err
	}

	if len(rows) == 0 {
		_ = m.store.SetBatchState(id, BatchCompleted, "nothing to do: every row is already done")
		release()
		return &StartResult{BatchID: id, State: BatchCompleted, Total: 0}, nil
	}

	runCtx, cancel := context.WithCancel(context.Background())
	m.mu.Lock()
	m.cancels[id] = cancel
	m.mu.Unlock()
	go func() {
		defer release()
		defer cancel()
		m.run(runCtx, id, req, rows)
	}()
	return &StartResult{BatchID: id, State: BatchRunning, Total: len(rows)}, nil
}

// label is what identifies a row to a person reading the batch: its param values, by name.
func label(req StartRequest, r sheet.Row) string {
	names := make([]string, 0, len(req.Params))
	for n := range req.Params {
		names = append(names, n)
	}
	sort.Strings(names)
	parts := make([]string, len(names))
	for i, n := range names {
		parts[i] = strings.TrimSpace(r.Values[req.Params[n]])
	}
	return strings.Join(parts, " / ")
}

// Cancel stops a running batch after cancelling the child it is waiting on. It reports false
// when no such batch is running.
func (m *Manager) Cancel(id string) bool {
	m.mu.Lock()
	cancel, ok := m.cancels[id]
	m.mu.Unlock()
	if ok {
		cancel()
	}
	return ok
}

// outcome is how one row ended.
type outcome int

const (
	rowDone outcome = iota
	rowFailed
	rowCancelled
	rowOrchestratorDown // the child never started: the Orchestrator could not be reached
	rowFatal            // the result could not be saved to the sheet: carrying on would lose work
)

func (m *Manager) run(ctx context.Context, id string, req StartRequest, rows []sheet.Row) {
	final, message := BatchCompleted, ""
	failed, orchErrs := 0, 0

loop:
	for _, row := range rows {
		if ctx.Err() != nil {
			final = BatchCancelled
			break
		}
		o, why := m.processRow(ctx, id, req, row)
		switch o {
		case rowDone:
			orchErrs = 0
		case rowFailed:
			failed++
			orchErrs = 0
		case rowCancelled:
			final = BatchCancelled
			break loop
		case rowOrchestratorDown:
			failed++
			orchErrs++
			if orchErrs >= maxConsecutiveOrchestratorErrors {
				final = BatchStopped
				message = fmt.Sprintf("stopped after %d rows in a row could not reach the Orchestrator: %s", orchErrs, why)
				break loop
			}
		case rowFatal:
			final, message = BatchStopped, why
			break loop
		}
	}

	if final == BatchCompleted && failed > 0 {
		final = BatchCompletedWithErrors
	}
	_ = m.store.SetBatchState(id, final, message)
}

func clip(s string) string {
	s = strings.TrimSpace(s)
	if r := []rune(s); len(r) > maxMessageChars {
		return string(r[:maxMessageChars]) + "…"
	}
	return s
}

func (m *Manager) processRow(ctx context.Context, batchID string, req StartRequest, row sheet.Row) (outcome, string) {
	params := make(map[string]string, len(req.Params))
	for name, col := range req.Params {
		v := strings.TrimSpace(row.Values[col])
		if v == "" {
			return m.fail(batchID, req, row, "", fmt.Sprintf("column %q is empty on row %d", col, row.Number), rowFailed)
		}
		params[name] = v
	}

	_ = m.store.SetItem(batchID, row.Number, ItemRunning, "", "")
	// Submitting is never abandoned halfway: if the batch is cancelled while the request is in
	// flight, the Orchestrator may still have queued the job, and without its run id there
	// would be nothing to cancel. The cancel is handled by wait, just below.
	runID, err := m.orch.Submit(context.WithoutCancel(ctx), SubmitRequest{
		ArtifactID: req.Child.ArtifactID, Version: req.Child.Version,
		BusinessJobID: batchID, EventID: fmt.Sprintf("%s:%d", batchID, row.Number), Params: params,
	})
	if err != nil {
		if ctx.Err() != nil {
			_ = m.store.SetItem(batchID, row.Number, ItemCancelled, "", "cancelled before the child job was started")
			return rowCancelled, ""
		}
		o, why := m.fail(batchID, req, row, "", "could not start the child job: "+err.Error(), rowOrchestratorDown)
		if o == rowFatal {
			return o, why
		}
		return rowOrchestratorDown, err.Error()
	}
	_ = m.store.SetItem(batchID, row.Number, ItemRunning, runID, "")

	info, how := m.wait(ctx, runID, time.Duration(req.TimeoutSeconds)*m.TimeoutUnit)
	switch how {
	case waitCancelled:
		m.cancelChild(runID, batchID)
		_ = m.store.SetItem(batchID, row.Number, ItemCancelled, runID, "the batch was cancelled")
		return rowCancelled, ""
	case waitTimeout:
		m.cancelChild(runID, batchID)
		return m.fail(batchID, req, row, runID,
			fmt.Sprintf("child run %s did not finish within %d seconds and was cancelled", runID, req.TimeoutSeconds), rowFailed)
	case waitError:
		return m.fail(batchID, req, row, runID,
			fmt.Sprintf("lost contact with the Orchestrator while child run %s was running", runID), rowOrchestratorDown)
	}

	if info.State != "SUCCEEDED" {
		msg := fmt.Sprintf("child run %s ended %s", runID, info.State)
		if e := m.orch.FirstError(context.Background(), runID); e != "" {
			msg += ": " + e
		}
		return m.fail(batchID, req, row, runID, msg, rowFailed)
	}

	cells := make([]sheet.CellUpdate, 0, len(req.Outputs)+2)
	for out, col := range req.Outputs {
		v, ok := info.Outputs[out]
		if !ok {
			return m.fail(batchID, req, row, runID,
				fmt.Sprintf("child run %s succeeded but did not return %q", runID, out), rowFailed)
		}
		cells = append(cells, sheet.CellUpdate{Row: row.Number, Column: col, Value: v})
	}
	cells = append(cells, sheet.CellUpdate{Row: row.Number, Column: req.StatusColumn, Value: statusDone})
	if req.MessageColumn != "" {
		cells = append(cells, sheet.CellUpdate{Row: row.Number, Column: req.MessageColumn, Value: ""})
	}
	if err := m.write(req.Source, cells); err != nil {
		msg := fmt.Sprintf("child run %s succeeded but its result could not be written to the sheet: %v", runID, err)
		_ = m.store.SetItem(batchID, row.Number, ItemFailed, runID, clip(msg))
		return rowFatal, msg
	}
	_ = m.store.SetItem(batchID, row.Number, ItemDone, runID, "")
	return rowDone, ""
}

// fail records a row as Failed in the sheet and the store. If even that cannot be written, the
// batch must stop: the sheet is the record of what is done, and it is out of reach.
func (m *Manager) fail(batchID string, req StartRequest, row sheet.Row, runID, msg string, kind outcome) (outcome, string) {
	msg = clip(msg)
	cells := []sheet.CellUpdate{{Row: row.Number, Column: req.StatusColumn, Value: statusFailed}}
	if req.MessageColumn != "" {
		cells = append(cells, sheet.CellUpdate{Row: row.Number, Column: req.MessageColumn, Value: msg})
	}
	_ = m.store.SetItem(batchID, row.Number, ItemFailed, runID, msg)
	if err := m.write(req.Source, cells); err != nil {
		return rowFatal, fmt.Sprintf("row %d failed (%s) and the sheet could not be updated: %v", row.Number, msg, err)
	}
	return kind, msg
}

func (m *Manager) cancelChild(runID, batchID string) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	_ = m.orch.Cancel(ctx, runID, "sheet-service batch "+batchID)
}

// write saves cells with a few retries: the usual reason a write fails is that the workbook is
// open in Excel, which a person can fix while the batch waits. It uses its own context so that
// cancelling a batch never throws away a result that has already come back.
func (m *Manager) write(cfg sheet.Config, cells []sheet.CellUpdate) error {
	var err error
	for attempt := 0; attempt <= m.WriteRetries; attempt++ {
		if attempt > 0 {
			time.Sleep(m.WriteRetryWait)
		}
		ctx, cancel := context.WithTimeout(context.Background(), time.Minute)
		// A fresh Source each time: a connector that tracks the version it last read must not
		// be reused after a write of its own.
		var src sheet.Source
		src, err = sheet.Open(cfg)
		if err == nil {
			err = src.Write(ctx, cells)
		}
		cancel()
		if err == nil {
			return nil
		}
	}
	return err
}

type waitResult int

const (
	waitTerminal waitResult = iota
	waitTimeout
	waitCancelled
	waitError
)

// wait polls a run until it ends, the timeout passes, the batch is cancelled, or the
// Orchestrator stays unreachable.
func (m *Manager) wait(ctx context.Context, runID string, timeout time.Duration) (RunInfo, waitResult) {
	deadline := time.Now().Add(timeout)
	errs := 0
	for {
		info, err := m.orch.Get(ctx, runID)
		switch {
		case ctx.Err() != nil:
			return RunInfo{}, waitCancelled
		case err != nil:
			errs++
			if errs >= 5 {
				return RunInfo{}, waitError
			}
		default:
			errs = 0
			if IsTerminal(info.State) {
				return info, waitTerminal
			}
		}
		if time.Now().After(deadline) {
			return RunInfo{}, waitTimeout
		}
		select {
		case <-ctx.Done():
			return RunInfo{}, waitCancelled
		case <-time.After(m.PollEvery):
		}
	}
}
