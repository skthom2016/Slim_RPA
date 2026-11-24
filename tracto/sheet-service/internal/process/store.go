package process

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"tracto/sheet-service/internal/pg"
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

// Store keeps batch progress in the process_batches and process_items tables.
//
// It holds no spreadsheet values: those are in the sheet. Only what a person needs to follow a
// batch is kept: which rows, which child runs, and how each ended.
type Store struct {
	db   *sql.DB
	owns bool // the Store opened the pool itself and closes it
}

// NewStore wraps an open pool. The caller owns the pool.
func NewStore(db *sql.DB) *Store { return &Store{db: db} }

// OpenStore connects to the database (creating the schema and tables when needed). schema is
// pg.Schema when empty.
func OpenStore(ctx context.Context, url, schema string) (*Store, error) {
	db, err := pg.OpenSheets(ctx, url, schema)
	if err != nil {
		return nil, err
	}
	return &Store{db: db, owns: true}, nil
}

// Close releases the pool when the Store opened it.
func (s *Store) Close() error {
	if s.owns {
		return s.db.Close()
	}
	return nil
}

func now() time.Time { return time.Now().UTC() }

func stamp(t time.Time) string { return t.UTC().Format(time.RFC3339) }

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
		`INSERT INTO process_batches(id,state,source_path,sheet,child,total,started_at) VALUES($1,$2,$3,$4,$5,$6,$7)`,
		b.ID, BatchRunning, b.SourcePath, b.Sheet, b.Child, len(items), t); err != nil {
		return err
	}
	for _, it := range items {
		if _, err := tx.Exec(
			`INSERT INTO process_items(batch_id,row_no,label,state,updated_at) VALUES($1,$2,$3,$4,$5)`,
			b.ID, it.Row, it.Label, ItemPending, t); err != nil {
			return err
		}
	}
	return tx.Commit()
}

// SetBatchState moves a batch to a state. A state other than Running also stamps its end time.
func (s *Store) SetBatchState(id, state, message string) error {
	var finished any // NULL while the batch runs
	if state != BatchRunning {
		finished = now()
	}
	_, err := s.db.Exec(`UPDATE process_batches SET state=$1, message=$2, finished_at=$3 WHERE id=$4`,
		state, message, finished, id)
	return err
}

// SetItem records how far a row has got. An empty runID leaves the stored one alone.
func (s *Store) SetItem(batchID string, row int, state, runID, message string) error {
	_, err := s.db.Exec(
		`UPDATE process_items SET state=$1, message=$2, run_id=CASE WHEN $3::text='' THEN run_id ELSE $3::text END, updated_at=$4
		 WHERE batch_id=$5 AND row_no=$6`,
		state, message, runID, now(), batchID, row)
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
	var started time.Time
	var finished sql.NullTime
	err := s.db.QueryRow(
		`SELECT id,state,message,total,started_at,finished_at,source_path,sheet,child FROM process_batches WHERE id=$1`, id).
		Scan(&b.ID, &b.State, &b.Message, &b.Total, &started, &finished, &b.SourcePath, &b.Sheet, &b.Child)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, ErrNotFound
	}
	if err != nil {
		return nil, err
	}
	b.StartedAt = stamp(started)
	if finished.Valid {
		b.FinishedAt = stamp(finished.Time)
	}
	rows, err := s.db.Query(
		`SELECT row_no,label,run_id,state,message FROM process_items WHERE batch_id=$1 ORDER BY row_no`, id)
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
		`UPDATE process_items SET state=$1, updated_at=$2 WHERE state=$3 AND batch_id IN (SELECT id FROM process_batches WHERE state=$4)`,
		ItemInterrupted, t, ItemRunning, BatchRunning); err != nil {
		return 0, err
	}
	res, err := tx.Exec(
		`UPDATE process_batches SET state=$1, finished_at=$2, message=$3 WHERE state=$4`,
		BatchInterrupted, t, "the service restarted while this batch was running; start it again to continue", BatchRunning)
	if err != nil {
		return 0, err
	}
	n, _ := res.RowsAffected()
	return int(n), tx.Commit()
}
