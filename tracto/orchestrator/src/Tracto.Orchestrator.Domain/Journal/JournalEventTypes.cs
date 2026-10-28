#nullable enable
namespace Tracto.Orchestrator.Domain.Journal;

/// <summary>
/// Canonical string constants for every event type written to <see cref="JournalEntry"/>.
/// Using these constants prevents magic strings and ensures consistency between the
/// Application-layer handlers that write entries and the ReconciliationPolicy that reads them.
/// </summary>
public static class JournalEventTypes
{
    // ── Run lifecycle ──────────────────────────────────────────────────────────
    public const string RunCreated     = "RUN_CREATED";
    public const string RunQueued      = "RUN_QUEUED";
    public const string RunLeased      = "RUN_LEASED";
    public const string RunRunning     = "RUN_RUNNING";
    public const string RunSucceeded   = "RUN_SUCCEEDED";
    public const string RunFailed      = "RUN_FAILED";
    public const string RunHandedOff   = "RUN_HANDED_OFF";
    public const string RunCancelled   = "RUN_CANCELLED";
    public const string RunTimedOut    = "RUN_TIMED_OUT";
    public const string RunUnknown     = "RUN_UNKNOWN";

    // ── Reconciliation (OC-10) ─────────────────────────────────────────────────
    /// <summary>
    /// Written when the reconcile command handler commits a reconciliation decision,
    /// regardless of outcome (SUCCEEDED or UNKNOWN).
    /// </summary>
    public const string RunReconciled  = "RUN_RECONCILED";

    // ── Heartbeat ─────────────────────────────────────────────────────────────
    public const string HeartbeatReceived = "HEARTBEAT_RECEIVED";

    // ── Lease lifecycle ────────────────────────────────────────────────────────
    public const string LeaseOffered   = "LEASE_OFFERED";
    public const string LeaseAccepted  = "LEASE_ACCEPTED";
    public const string LeaseExpired   = "LEASE_EXPIRED";

    // ── Worker lifecycle ───────────────────────────────────────────────────────
    public const string WorkerRegistered = "WORKER_REGISTERED";
    public const string WorkerOffline    = "WORKER_OFFLINE";

    /// <summary>
    /// Written by the restart-recovery service when a run is marked UNKNOWN because
    /// its worker was absent at Orchestrator restart.
    /// </summary>
    public const string WorkerAbsentAtRestart = "WorkerAbsentAtRestart";

    // ── Async Callback Intake (OC-07) ──────────────────────────────────────────

    /// <summary>
    /// An inbound callback was accepted: HMAC verified, timestamp fresh, CorrelationId
    /// matched a HANDED_OFF run, CallbackRecord persisted, continuation enqueued.
    /// Written in the same transaction as the CallbackRecord commit (invariant 5).
    /// </summary>
    public const string CallbackIngested = "CALLBACK_INGESTED";

    /// <summary>
    /// A duplicate EventId was received. The original CallbackRecord already exists;
    /// the request is returned 200 DUPLICATE without reprocessing.
    /// </summary>
    public const string CallbackDuplicate = "CALLBACK_DUPLICATE";

    /// <summary>
    /// The CorrelationId in an accepted callback did not match any known run.
    /// The callback is silently acknowledged (200) but no continuation is created.
    /// Logged for audit purposes with a sanitised message (no caller payload echoed).
    /// </summary>
    public const string CallbackCorrelationNotFound = "CALLBACK_CORRELATION_NOT_FOUND";

    /// <summary>
    /// The run referenced by CorrelationId was not in HANDED_OFF state.
    /// The callback is rejected with 422 Unprocessable Entity.
    /// </summary>
    public const string CallbackRunNotHandedOff = "CALLBACK_RUN_NOT_HANDED_OFF";
}
