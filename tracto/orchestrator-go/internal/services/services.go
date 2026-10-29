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
		 WHERE State='Accepted' AND COALESCE(LastHeartbeatAt, AcceptedAt, '') < ?`,
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
			continue
		}

		tx.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE LeaseId=?`, s.leaseID)

		// Check if run was ever running
		var runningCount int
		tx.QueryRow(`SELECT COUNT(*) FROM JournalEntries WHERE RunId=? AND EventType='RUN_RUNNING'`, s.runID).Scan(&runningCount)

		newAttemptID := uuid.NewString()
		if runningCount > 0 {
			// Requeue
			tx.Exec(`UPDATE RunRecords SET State='Queued',AttemptId=?,WorkerId=NULL,UpdatedAt=? WHERE Id=?`,
				newAttemptID, now, s.runID)
			jID := uuid.NewString()
			p, _ := json.Marshal(map[string]string{"reason": "lease_expired_requeue", "previousAttemptId": s.attemptID})
			tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
				jID, s.runID, newAttemptID, "RUN_REQUEUED", now, string(p))
		} else {
			tx.Exec(`UPDATE RunRecords SET State='Unknown',NeedsAttention=1,UnknownReason=?,UpdatedAt=? WHERE Id=?`,
				"Lease expired with no heartbeat and no confirmed start", now, s.runID)
			jID := uuid.NewString()
			p, _ := json.Marshal(map[string]string{"reason": "lease_expired_unknown", "leaseId": s.leaseID})
			tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
				jID, s.runID, s.attemptID, "RUN_RECONCILED_UNKNOWN", now, string(p))
		}

		// Release worker slot
		tx.Exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=?`, s.workerID)

		if err := tx.Commit(); err != nil {
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
	rows, err := db.Query(`SELECT WorkerId,LastHeartbeatAt,AbsenceThresholdSeconds FROM WorkerRecords WHERE IsOnline=1`)
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
		db.Exec(`UPDATE WorkerRecords SET IsOnline=0,State='Offline' WHERE WorkerId=?`, w.workerID)

		// Requeue runs assigned to this absent worker
		runRows, _ := db.Query(
			`SELECT Id,AttemptId FROM RunRecords WHERE WorkerId=? AND State IN ('Running','Leased')`,
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
				tx, _ := db.Begin()
				tx.Exec(`UPDATE RunRecords SET State='Queued',AttemptId=?,WorkerId=NULL,UpdatedAt=? WHERE Id=?`,
					newAttemptID, now, rn.id)
				tx.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=? AND State<>'Expired'`, rn.id)
				jID := uuid.NewString()
				p, _ := json.Marshal(map[string]string{"reason": "worker_absent", "workerId": w.workerID})
				tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
					jID, rn.id, newAttemptID, "RUN_REQUEUED", now, string(p))
				tx.Commit()
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
			now := time.Now().UTC().Format(time.RFC3339Nano)
			rows, err := db.Query(
				`SELECT Id,AttemptId,WorkerId FROM RunRecords
				 WHERE Deadline IS NOT NULL AND Deadline < ? AND State NOT IN ('Succeeded','Failed','HandedOff','Cancelled','TimedOut','Unknown')`,
				now)
			if err != nil {
				continue
			}
			// NOTE: explicit rows.Close() below — defer inside a loop never fires until
			// the function returns (which is never for a background monitor), so it would
			// permanently hold the single DB connection and starve all request handlers.
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

			for _, t := range touts {
				tx, _ := db.Begin()
				tx.Exec(`UPDATE RunRecords SET State='TimedOut',UpdatedAt=? WHERE Id=?`, now, t.id)
				tx.Exec(`UPDATE LeaseRecords SET State='Expired' WHERE RunId=? AND State<>'Expired'`, t.id)
				if t.workerID != "" {
					tx.Exec(`UPDATE WorkerRecords SET State='Available' WHERE WorkerId=?`, t.workerID)
				}
				frID := uuid.NewString()
				tx.Exec(`INSERT INTO FinalResultRecords(Id,RunId,AttemptId,TerminalState,NeedsAttention,EvidenceRefsJson,CompletedAt) VALUES(?,?,?,'TimedOut',0,'[]',?)`,
					frID, t.id, t.attemptID, now)
				jID := uuid.NewString()
				tx.Exec(`INSERT INTO JournalEntries(Id,RunId,AttemptId,EventType,OccurredAt,Payload) VALUES(?,?,?,?,?,?)`,
					jID, t.id, t.attemptID, "RUN_TIMED_OUT", now, `{"reason":"deadline_exceeded"}`)
				tx.Commit()
				logger.Info("deadline exceeded: timed out run", "runId", t.id)
			}
		}
	}
}
