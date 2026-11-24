package services

import (
	"context"
	"database/sql"
	"encoding/json"
	"log/slog"
	"time"

	"github.com/google/uuid"
)

// LeaseExpiryMonitor expires Accepted leases that have missed heartbeats.
// Mirrors OC-05 LeaseExpiryMonitorService — runs every 15 seconds.
func LeaseExpiryMonitor(ctx context.Context, db *sql.DB, logger *slog.Logger) {
	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			expireStaleLeases(db, logger)
		}
	}
}

func expireStaleLeases(db *sql.DB, logger *slog.Logger) {
	heartbeatTimeout := time.Now().UTC().Add(-45 * time.Second).Format(time.RFC3339Nano)
	rows, err := db.Query(
		`SELECT LeaseId,RunId,AttemptId,WorkerId FROM LeaseRecords
		 WHERE State='Accepted' AND COALESCE(LastHeartbeatAt, AcceptedAt, 'epoch'::timestamptz) < $1`,
		heartbeatTimeout)
	if err != nil {
		logger.Error("lease expiry: query failed", "err", err)
		return
	}
	defer rows.Close()

	type stale struct {
		leaseID, runID, attemptID, workerID string
	}
	var stalelist []stale
	for rows.Next() {
		var s stale
		rows.Scan(&s.leaseID, &s.runID, &s.attemptID, &s.workerID)
		stalelist = append(stalelist, s)
	}
	rows.Close()

	for _, s := range stalelist {
		now := time.Now().UTC().Format(time.RFC3339Nano)
		tx, err := db.Begin()
		if err != nil {
			logger.Error("lease expiry: begin failed", "leaseId", s.leaseID, "err", err)
			continue
		}
		x := newTx(tx)

		// Only a lease that is still Accepted is expired: a result that arrived a moment ago has
		// already closed it, and must not be requeued.
		if x.execRows(`UPDATE LeaseRecords SET State='Expired' WHERE LeaseId=$1 AND State='Accepted'`, s.leaseID) == 0 && x.err == nil {
			tx.Rollback()
			continue
		}

		// Check if run was ever running
		var runningCount int
		if x.err == nil {
			x.err = tx.QueryRow(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=$1 AND EventType='RUN_RUNNING'`, s.runID).Scan(&runningCount)
		}

		newAttemptID := uuid.NewString()
		if runningCount > 0 {
			// Requeue
			x.exec(`UPDATE RunRecords SET State='Queued',AttemptId=$1,WorkerId=NULL,UpdatedAt=$2 WHERE Id=$3`,
				newAttemptID, now, s.runID)
			jID := uuid.NewString()
			p, _ := json.Marshal(map[string]string{"reason": "lease_expired_requeue", "previousAttemptId": s.attemptID})
			x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
				jID, s.runID, newAttemptID, "RUN_REQUEUED", now, string(p))
		} else {
			x.exec(`UPDATE RunRecords SET State='Unknown',NeedsAttention=TRUE,UnknownReason=$1,UpdatedAt=$2 WHERE Id=$3`,
				"Lease expired with no heartbeat and no confirmed start", now, s.runID)
			jID := uuid.NewString()
			p, _ := json.Marshal(map[string]string{"reason": "lease_expired_unknown", "leaseId": s.leaseID})
			x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
				jID, s.runID, s.attemptID, "RUN_RECONCILED_UNKNOWN", now, string(p))
			// A segment run that ends Unknown takes its parent with it.
			x.exec(`UPDATE RunRecords SET State='Unknown',NeedsAttention=TRUE,UnknownReason=$1,UpdatedAt=$2
			         WHERE State='Running' AND Id=(SELECT ParentRunId FROM RunRecords WHERE Id=$3)`,
				"A segment run ended Unknown: "+s.runID, now, s.runID)
		}

		// Release worker slot
		x.exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=$1`, s.workerID)

		if err := x.commit(); err != nil {
			tx.Rollback()
			logger.Error("lease expiry: commit failed", "leaseId", s.leaseID, "err", err)
		} else {
			logger.Info("lease expired", "leaseId", s.leaseID, "runId", s.runID)
		}
	}
}

// RestartRecovery runs once at startup to mark absent workers offline and requeue their running jobs.
// Mirrors OC-11 RestartRecoveryService.
func RestartRecovery(db *sql.DB, logger *slog.Logger) {
	now := time.Now().UTC().Format(time.RFC3339Nano)

	// Find workers that missed their absence threshold
	rows, err := db.Query(`SELECT WorkerId,LastHeartbeatAt,AbsenceThresholdSeconds FROM WorkerRecords WHERE IsOnline`)
	if err != nil {
		logger.Error("restart recovery: query failed", "err", err)
		return
	}
	defer rows.Close()

	type absent struct {
		workerID string
		lastHB   time.Time
	}
	var absentWorkers []absent
	for rows.Next() {
		var wid string
		var lastHBStr string
		var threshold int
		rows.Scan(&wid, &lastHBStr, &threshold)
		lastHB, _ := time.Parse(time.RFC3339Nano, lastHBStr)
		if time.Since(lastHB) > time.Duration(threshold)*time.Second {
			absentWorkers = append(absentWorkers, absent{wid, lastHB})
		}
	}
	rows.Close()

	for _, w := range absentWorkers {
		logger.Info("restart recovery: marking worker offline", "workerId", w.workerID)
		db.Exec(`UPDATE WorkerRecords SET IsOnline=FALSE,State='Offline' WHERE WorkerId=$1`, w.workerID)

		// Requeue runs assigned to this absent worker
		runRows, _ := db.Query(
			`SELECT Id,AttemptId FROM RunRecords WHERE WorkerId=$1 AND State IN ('Running','Leased')`,
			w.workerID)
		if runRows != nil {
			type run struct{ id, attemptID string }
			var runs []run
			for runRows.Next() {
				var rn run
				runRows.Scan(&rn.id, &rn.attemptID)
				runs = append(runs, rn)
			}
			runRows.Close()
			for _, rn := range runs {
				newAttemptID := uuid.NewString()
				tx, err := db.Begin()
				if err != nil {
					logger.Error("restart recovery: begin failed", "runId", rn.id, "err", err)
					continue
				}
				x := newTx(tx)
				x.exec(`UPDATE RunRecords SET State='Queued',AttemptId=$1,WorkerId=NULL,UpdatedAt=$2 WHERE Id=$3`,
					newAttemptID, now, rn.id)
				x.exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=$1 AND State<>'Expired'`, rn.id)
				jID := uuid.NewString()
				p, _ := json.Marshal(map[string]string{"reason": "worker_absent", "workerId": w.workerID})
				x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
					jID, rn.id, newAttemptID, "RUN_REQUEUED", now, string(p))
				if err := x.commit(); err != nil {
					tx.Rollback()
					logger.Error("restart recovery: requeue failed", "runId", rn.id, "err", err)
					continue
				}
				logger.Info("restart recovery: requeued run", "runId", rn.id)
			}
		}
	}
}

// DeadlineMonitor checks for runs past their deadline and marks them TimedOut.
func DeadlineMonitor(ctx context.Context, db *sql.DB, logger *slog.Logger) {
	ticker := time.NewTicker(30 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			TimeOutOverdue(db, logger)
		}
	}
}

// txRun runs the statements of one transaction and keeps the first error. PostgreSQL aborts a
// transaction at its first failed statement, so every later statement fails as well; keeping the
// first error lets a function write its statements in a row and check once before commit.
type txRun struct {
	tx  *sql.Tx
	err error
}

func newTx(tx *sql.Tx) *txRun { return &txRun{tx: tx} }

func (t *txRun) exec(query string, args ...any) {
	if t.err != nil {
		return
	}
	if _, err := t.tx.Exec(query, args...); err != nil {
		t.err = err
	}
}

// execRows is exec for a statement whose affected-row count matters.
func (t *txRun) execRows(query string, args ...any) int64 {
	if t.err != nil {
		return 0
	}
	res, err := t.tx.Exec(query, args...)
	if err != nil {
		t.err = err
		return 0
	}
	n, _ := res.RowsAffected()
	return n
}

func (t *txRun) commit() error {
	if t.err != nil {
		return t.err
	}
	return t.tx.Commit()
}

// TimeOutOverdue marks every live run whose deadline has passed as TimedOut, together with the
// active segment run of a segmented script, and frees the worker each was on.
func TimeOutOverdue(db *sql.DB, logger *slog.Logger) {
	now := time.Now().UTC().Format(time.RFC3339Nano)
	rows, err := db.Query(
		`SELECT Id,AttemptId,WorkerId FROM RunRecords
		 WHERE Deadline IS NOT NULL AND Deadline < $1 AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
		now)
	if err != nil {
		logger.Error("deadline monitor: query failed", "err", err)
		return
	}
	// rows is closed explicitly below: a defer inside this loop would only run when the
	// function returns, which a background monitor never does, and the connection would
	// stay checked out of the pool.
	type timedOut struct{ id, attemptID, workerID string }
	var touts []timedOut
	for rows.Next() {
		var t timedOut
		var wid sql.NullString
		rows.Scan(&t.id, &t.attemptID, &wid)
		if wid.Valid {
			t.workerID = wid.String
		}
		touts = append(touts, t)
	}
	rows.Close()

	// A parent run of a segmented script carries the deadline; its active segment
	// run is timed out with it.
	for _, parent := range touts {
		childRows, err := db.Query(
			`SELECT Id,AttemptId,WorkerId FROM RunRecords
			 WHERE ParentRunId=$1 AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
			parent.id)
		if err != nil {
			continue
		}
		for childRows.Next() {
			var t timedOut
			var wid sql.NullString
			childRows.Scan(&t.id, &t.attemptID, &wid)
			if wid.Valid {
				t.workerID = wid.String
			}
			touts = append(touts, t)
		}
		childRows.Close()
	}

	for _, t := range touts {
		tx, err := db.Begin()
		if err != nil {
			logger.Error("deadline monitor: begin failed", "runId", t.id, "err", err)
			continue
		}
		x := newTx(tx)
		// Only a run that is still live is timed out; one that finished meanwhile is left alone.
		if x.execRows(`UPDATE RunRecords SET State='TimedOut',UpdatedAt=$1 WHERE Id=$2 AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`, now, t.id) == 0 && x.err == nil {
			tx.Rollback()
			continue
		}
		x.exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=$1 AND State<>'Expired'`, t.id)
		if t.workerID != "" {
			x.exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=$1`, t.workerID)
		}
		frID := uuid.NewString()
		x.exec(`INSERT INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,EvidenceRefsJson,CompletedAt) VALUES($1,$2,$3,'TimedOut',FALSE,'[]',$4)`,
			frID, t.id, t.attemptID, now)
		jID := uuid.NewString()
		x.exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES($1,$2,$3,$4,$5,$6)`,
			jID, t.id, t.attemptID, "RUN_TIMED_OUT", now, `{"reason":"deadline_exceeded"}`)
		if err := x.commit(); err != nil {
			tx.Rollback()
			logger.Error("deadline monitor: could not time out run", "runId", t.id, "err", err)
			continue
		}
		logger.Info("deadline exceeded: timed out run", "runId", t.id)
	}
}
