using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Wsl;

namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Persistence contract for run lifecycle state.
/// All writes must be idempotent for a given (runId, attemptId) pair (CEC v1.0.1 invariant #3).
/// No plaintext secrets are written to any persisted field (CEC invariant #4).
/// </summary>
public interface IRunJournal
{
    // ── Pre-existing methods (TER-US-022) ────────────────────────────────────

    /// <summary>Persists a journal entry (upsert by RunId).</summary>
    Task<Result<bool, Error>> WriteEntryAsync(
        RunJournalEntry entry,
        CancellationToken ct = default);

    /// <summary>Reads the journal entry for a run, or null if none exists.</summary>
    Task<Result<RunJournalEntry?, Error>> ReadEntryAsync(
        string runId,
        CancellationToken ct = default);

    /// <summary>Returns true when the given worker already holds an active (non-completed) lease.</summary>
    Task<Result<bool, Error>> HasActiveLeaseAsync(
        string workerId,
        CancellationToken ct = default);

    // ── Methods added by TER-US-005 ──────────────────────────────────────────

    /// <summary>Records a run state transition. Idempotent for a given (runId, attemptId).</summary>
    Task<Result<bool, Error>> RecordStateAsync(
        string runId,
        string attemptId,
        string leaseId,
        RunState state,
        CancellationToken ct = default);

    /// <summary>Returns the current state for the given run.</summary>
    Task<Result<RunState, Error>> GetStateAsync(
        string runId,
        CancellationToken ct = default);

    /// <summary>Appends a named checkpoint with an opaque payload.</summary>
    Task<Result<bool, Error>> RecordCheckpointAsync(
        string runId,
        string attemptId,
        string checkpointName,
        string payload,
        CancellationToken ct = default);

    /// <summary>Records a heartbeat sequence number for liveness tracking.</summary>
    Task<Result<bool, Error>> RecordHeartbeatAsync(
        string runId,
        string attemptId,
        int sequence,
        CancellationToken ct = default);

    // ── Methods added by TER-US-014 ──────────────────────────────────────────

    /// <summary>
    /// Appends a signed output artifact reference to the run journal.
    /// The <paramref name="canonicalPath"/> and its <paramref name="sha256Hex"/>
    /// digest are stored as evidence for AC-3 of TER-US-014.
    /// No plaintext secrets may appear in either parameter (CEC invariant #4).
    /// </summary>
    Task<Result<bool, Error>> AppendOutputArtifactRefAsync(
        string runId,
        string attemptId,
        string canonicalPath,
        string sha256Hex,
        CancellationToken ct = default);

    // ── Methods added by TER-US-017 ──────────────────────────────────────────

    /// <summary>
    /// Appends a <see cref="WslOutageEvent"/> to the run journal so that the
    /// Orchestrator can reconcile the attempt without launching a duplicate command
    /// (TER-US-017 AC-3).
    /// </summary>
    Task<Result<bool, Error>> RecordWslOutageAsync(
        string runId,
        string attemptId,
        string leaseId,
        WslOutageEvent outageEvent,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions the run to <see cref="RunState.Unknown"/> and persists the
    /// mandatory CEC v1.0.1 invariant #2 fields:
    /// <see cref="RunJournalEntry.NeedsAttention"/> = <see langword="true"/> and
    /// <see cref="RunJournalEntry.UnknownReason"/> = <paramref name="unknownReason"/>.
    /// Idempotent for a given (runId, attemptId) pair.
    /// </summary>
    Task<Result<bool, Error>> RecordUnknownStateAsync(
        string runId,
        string attemptId,
        string leaseId,
        string unknownReason,
        CancellationToken ct = default);

    // ── Methods added by TER-US-019 ──────────────────────────────────────────

    /// <summary>
    /// Returns true when the given <paramref name="callbackId"/> has already been
    /// processed for the specified source run. Used to enforce the AC-3 deduplication
    /// requirement: exactly one Run B per eligible, deduplicated callback.
    /// </summary>
    Task<Result<bool, Error>> HasProcessedCallbackAsync(
        string sourceRunId,
        string callbackId,
        CancellationToken ct = default);

    /// <summary>
    /// Records that the given <paramref name="callbackId"/> has been processed for
    /// the specified source run. Must be called before <see cref="WriteEntryAsync"/>
    /// creates Run B so that a crash between the two writes cannot result in a
    /// second Run B on retry.
    /// No plaintext secrets may appear in <paramref name="callbackId"/> (CEC invariant #4).
    /// </summary>
    Task<Result<bool, Error>> RecordProcessedCallbackAsync(
        string sourceRunId,
        string callbackId,
        CancellationToken ct = default);
}
