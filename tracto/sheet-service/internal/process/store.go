package process

import (
	"database/sql"
	"errors"
	"time"

	_ "modernc.org/sqlite" // pure-Go SQLite driver
)

// Batch states.
const (
	BatchRunning             = "Running"
	BatchCompleted           = "Completed"
	BatchCompletedWithErrors = "CompletedWithErrors"
	BatchCancelled           = "Cancelled"
	BatchStopped             = "Stopped"     // the batch gave up: see Message
	BatchInterrupted         = "Interrupted" // the service restarted while it was running
)

// Item states.
const (
	ItemPending     = "Pending"
	ItemRunning     = "Running"
	ItemDone        = "Done"
	ItemFailed      = "Failed"
	ItemCancelled   = "Cancelled"
	ItemInterrupted = "Interrupted"
)

// ErrNotFound means no batch has that id.
var ErrNotFound = errors.New("batch not found")

// Store keeps batch progress in a SQLite file of its own. The sheet-service's other database
// holds saved sign-ins, which have no business sharing a file with run data.
//
// It holds no spreadsheet values: those are in the sheet. Only what a person needs to follow a
// batch is kept: which rows, which child runs, and how each ended.
type Store struct{ db *sql.DB }

// OpenStore opens (creating when needed) the database at path.
func OpenStore(path string) (*Store, error) {
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}
	// One connection: calls from the loop and from request handlers queue instead of colliding.
	db.SetMaxOpenConns(1)
	for _, stmt := range []string{
		`PRAGMA busy_timeout = 30000`,
		`CREATE TABLE IF NOT EXISTS process_batches (
			id          TEXT PRIMARY KEY,
			state       TEXT    NOT NULL,
			source_path TEXT    NOT NULL,
			sheet       TEXT    NOT NULL,
			child       TEXT    NOT NULL,
			total       INTEGER NOT NULL,
			message     TEXT    NOT NULL DEFAULT '',
			started_at  TEXT    NOT NULL,
			finished_at TEXT    NOT NULL DEFAULT ''
		)`,
		`CREATE TABLE IF NOT EXISTS process_items (
			batch_id   TEXT    NOT NULL,
			row        INTEGER NOT NULL,
			label      TEXT    NOT NULL DEFAULT '',
			run_id     TEXT    NOT NULL DEFAULT '',
			state      TEXT    NOT NULL,
			message    TEXT    NOT NULL DEFAULT '',
			updated_at TEXT    NOT NULL,
			PRIMARY KEY (batch_id, row)
		)`,
	} {
		if _, err := db.Exec(stmt); err != nil {
			db.Close()
			return nil, err
		}
	}
	return &Store{db: db}, nil
}

// Close releases the database file.
func (s *Store) Close() error { return s.db.Close() }

func now() string { return time.Now().UTC().Format(time.RFC3339) }

// BatchRecord is a batch as it is first stored.
type BatchRecord struct {
	ID, SourcePath, Sheet, Child string
}

// ItemRecord is one row of a batch as it is first stored.
type ItemRecord struct {
	Row   int
	Label string
}

// CreateBatch stores a Running batch and its pending items together.
func (s *Store) CreateBatch(b BatchRecord, items []ItemRecord) error {
	tx, err := s.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	t := now()
	if _, err := tx.Exec(
		`INSERT INTO process_batches(id,state,source_path,sheet,child,total,started_at) VALUES(?,?,?,?,?,?,?)`,
		b.ID, BatchRunning, b.SourcePath, b.Sheet, b.Child, len(items), t); err != nil {
		return err
	}
	for _, it := range items {
		if _, err := tx.Exec(
			`INSERT INTO process_items(batch_id,row,label,state,updated_at) VALUES(?,?,?,?,?)`,
			b.ID, it.Row, it.Label, ItemPending, t); err != nil {
			return err
		}
	}
	return tx.Commit()
}

// SetBatchState moves a batch to a state. A state other than Running also stamps its end time.
func (s *Store) SetBatchState(id, state, message string) error {
	finished := ""
	if state != BatchRunning {
		finished = now()
	}
	_, err := s.db.Exec(`UPDATE process_batches SET state=?, message=?, finished_at=? WHERE id=?`,
		state, message, finished, id)
	return err
}

// SetItem records how far a row has got. An empty runID leaves the stored one alone.
func (s *Store) SetItem(batchID string, row int, state, runID, message string) error {
	_, err := s.db.Exec(
		`UPDATE process_items SET state=?, message=?, run_id=CASE WHEN ?='' THEN run_id ELSE ? END, updated_at=?
		 WHERE batch_id=? AND row=?`,
		state, message, runID, runID, now(), batchID, row)
	return err
}

// Item is one row of a batch, as reported.
type Item struct {
	Row     int    `json:"row"`
	Label   string `json:"label"`
	RunID   string `json:"runId,omitempty"`
	State   string `json:"state"`
	Message string `json:"message,omitempty"`
}

// Counts is how many items are in each state.
type Counts struct {
	Pending     int `json:"pending"`
	Running     int `json:"running"`
	Done        int `json:"done"`
	Failed      int `json:"failed"`
	Cancelled   int `json:"cancelled"`
	Interrupted int `json:"interrupted"`
}

// Batch is a batch as reported.
type Batch struct {
	ID         string `json:"batchId"`
	State      string `json:"state"`
	Message    string `json:"message,omitempty"`
	Total      int    `json:"total"`
	Counts     Counts `json:"counts"`
	StartedAt  string `json:"startedAt"`
	FinishedAt string `json:"finishedAt,omitempty"`
	SourcePath string `json:"sourcePath"`
	Sheet      string `json:"sheet"`
	Child      string `json:"child"`
	Items      []Item `json:"items"`
}

// Get returns a batch with its items in row order.
func (s *Store) Get(id string) (*Batch, error) {
	var b Batch
	err := s.db.QueryRow(
		`SELECT id,state,message,total,started_at,finished_at,source_path,sheet,child FROM process_batches WHERE id=?`, id).
		Scan(&b.ID, &b.State, &b.Message, &b.Total, &b.StartedAt, &b.FinishedAt, &b.SourcePath, &b.Sheet, &b.Child)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, ErrNotFound
	}
	if err != nil {
		return nil, err
	}
	rows, err := s.db.Query(
		`SELECT row,label,run_id,state,message FROM process_items WHERE batch_id=? ORDER BY row`, id)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	b.Items = []Item{}
	for rows.Next() {
		var it Item
		if err := rows.Scan(&it.Row, &it.Label, &it.RunID, &it.State, &it.Message); err != nil {
			return nil, err
		}
		switch it.State {
		case ItemPending:
			b.Counts.Pending++
		case ItemRunning:
			b.Counts.Running++
		case ItemDone:
			b.Counts.Done++
		case ItemFailed:
			b.Counts.Failed++
		case ItemCancelled:
			b.Counts.Cancelled++
		case ItemInterrupted:
			b.Counts.Interrupted++
		}
		b.Items = append(b.Items, it)
	}
	return &b, rows.Err()
}

// InterruptRunning marks every batch left Running by a previous process as Interrupted, and
// the rows it was in the middle of. Nothing is lost: a row that did not reach Done is still
// pending in the sheet, so starting the batch again carries on from there.
func (s *Store) InterruptRunning() (int, error) {
	tx, err := s.db.Begin()
	if err != nil {
		return 0, err
	}
	defer tx.Rollback()
	t := now()
	if _, err := tx.Exec(
		`UPDATE process_items SET state=?, updated_at=? WHERE state=? AND batch_id IN (SELECT id FROM process_batches WHERE state=?)`,
		ItemInterrupted, t, ItemRunning, BatchRunning); err != nil {
		return 0, err
	}
	res, err := tx.Exec(
		`UPDATE process_batches SET state=?, finished_at=?, message=? WHERE state=?`,
		BatchInterrupted, t, "the service restarted while this batch was running; start it again to continue", BatchRunning)
	if err != nil {
		return 0, err
	}
	n, _ := res.RowsAffected()
	return int(n), tx.Commit()
}
