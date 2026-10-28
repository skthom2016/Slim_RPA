using Tracto.TerminalRuntime.Domain.Wsl;

namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Immutable snapshot of a single run's lifecycle state persisted to the local
/// run journal by <c>IRunJournal.WriteEntryAsync</c>.
///
/// CEC v1.0.1 invariants encoded here:
///   - <see cref="AttemptId"/> fences all stage transitions (invariant #3).
///   - <see cref="IdempotencyClass"/> guards reconciliation after lease expiry (invariant #3).
///   - <see cref="FinalResult"/> is null until the run reaches a terminal state.
/// </summary>
/// <param name="RunId">Unique identifier for the logical run.</param>
/// <param name="AttemptId">
///   Monotonically-increasing identifier for the current execution attempt.
///   Duplicate messages carrying the same AttemptId are idempotent.
/// </param>
/// <param name="LeaseId">
///   Unique identifier of the lease that owns this run.
///   Changes on every new attempt so that stale workers can be fenced out.
/// </param>
/// <param name="WorkerId">
///   Identifier of the Terminal worker that holds the lease.
///   Used by <c>HasActiveLeaseAsync</c> to enforce the one-lease-per-worker invariant.
/// </param>
/// <param name="HeartbeatSeq">
///   Monotonically-increasing heartbeat counter.  The Orchestrator bumps this
///   on each heartbeat to detect stale leases.
/// </param>
/// <param name="CheckpointAt">
///   Wall-clock time of the most recent checkpoint or state transition.
/// </param>
/// <param name="FinalResult">
///   Null while the run is in-flight.  Set to one of the five legal
///   <c>serviceOutcome</c> values when the run reaches a terminal state.
///   Carry no plaintext secrets in this field.
/// </param>
/// <param name="IdempotencyClass">
///   Whether the most-recently-active command step may be safely replayed
///   after an Orchestrator restart.
/// </param>
/// <param name="State">Current CEC v1.0.1 <see cref="RunState"/> of the run.</param>
public sealed record RunJournalEntry(
    string RunId,
    string AttemptId,
    string LeaseId,
    string WorkerId,
    long   HeartbeatSeq,
    DateTimeOffset CheckpointAt,
    string? FinalResult,
    IdempotencyClass IdempotencyClass,
    RunState State)
{
    /// <summary>
    /// True when the run has reached a terminal state and the lease is released.
    /// A lease with <c>IsCompleted = true</c> does NOT count toward the
    /// one-lease-per-worker limit.
    /// </summary>
    public bool IsCompleted => FinalResult is not null;

    /// <summary>
    /// Signed output artifact references recorded during the run.
    /// Each entry is a pipe-delimited string of the form
    /// <c>canonicalPath|sha256:{hex}</c>.
    /// No plaintext secrets are stored here (CEC invariant #4).
    /// </summary>
    public IReadOnlyList<string> OutputArtifactRefs { get; init; } = [];

    /// <summary>
    /// WSL distro outage events recorded during or before execution of this run.
    /// Allows the Orchestrator to reconcile the attempt without launching a
    /// duplicate command (TER-US-017 AC-3).
    /// </summary>
    public IReadOnlyList<WslOutageEvent> WslOutageEvents { get; init; } = [];

    /// <summary>
    /// Idempotency keys of callback deliveries that have already been processed
    /// for this run. Prevents duplicate Run B creation when the same callback
    /// is delivered more than once (TER-US-019 AC-3 deduplication).
    /// No plaintext secrets are stored here (CEC invariant #4).
    /// </summary>
    public IReadOnlyList<string> ProcessedCallbackIds { get; init; } = [];

    /// <summary>
    /// True when the run requires human or Orchestrator attention before any
    /// reconciliation can proceed.  Must be <see langword="true"/> whenever
    /// <see cref="State"/> is <see cref="RunState.Unknown"/>
    /// (CEC v1.0.1 invariant #2).
    /// </summary>
    public bool NeedsAttention { get; init; }

    /// <summary>
    /// Human-readable code explaining why the run was placed into
    /// <see cref="RunState.Unknown"/>.  Non-null whenever
    /// <see cref="NeedsAttention"/> is <see langword="true"/>
    /// (CEC v1.0.1 invariant #2).
    /// </summary>
    public string? UnknownReason { get; init; }
}
