using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application.Heartbeat;

/// <summary>
/// Tracks the mutable liveness state for a single run's heartbeat session (TER-US-018).
///
/// Thread-safe: all counter mutations use Interlocked operations; the termination
/// <see cref="CancellationTokenSource"/> is cancel-only after construction.
///
/// <para>Ownership: the caller that constructs a <see cref="HeartbeatContext"/> is responsible
/// for disposing it after the heartbeat session has fully stopped.  The
/// <see cref="HeartbeatService"/> does NOT dispose this object.</para>
/// </summary>
public sealed class HeartbeatContext : IDisposable
{
    private long _seqNo;
    private int  _consecutiveFailures;
    private readonly CancellationTokenSource _terminationCts = new();

    /// <param name="runId">Logical run identifier.</param>
    /// <param name="attemptId">Current attempt identifier (fence for duplicate detection).</param>
    /// <param name="leaseId">Lease identifier owned by this worker for this run.</param>
    /// <param name="leaseExpiresAt">UTC time at which the active lease expires.</param>
    /// <param name="maxDeadline">
    ///   Absolute UTC deadline; the run must complete before this point regardless of lease renewals.
    /// </param>
    /// <param name="policy">Heartbeat interval and failure-tolerance configuration.</param>
    /// <param name="hasCommittedIrreversibleSideEffect">
    ///   Pass <c>true</c> when the command has already committed an irreversible side effect.
    ///   Drives the UNKNOWN vs TIMED_OUT classification when the lease expires.
    /// </param>
    public HeartbeatContext(
        string          runId,
        string          attemptId,
        string          leaseId,
        DateTimeOffset  leaseExpiresAt,
        DateTimeOffset  maxDeadline,
        HeartbeatPolicy policy,
        bool            hasCommittedIrreversibleSideEffect = false)
    {
        RunId          = runId;
        AttemptId      = attemptId;
        LeaseId        = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        MaxDeadline    = maxDeadline;
        Policy         = policy;
        HasCommittedIrreversibleSideEffect = hasCommittedIrreversibleSideEffect;
    }

    // ── Immutable identity ────────────────────────────────────────────────────

    public string          RunId          { get; }
    public string          AttemptId      { get; }
    public string          LeaseId        { get; }
    public DateTimeOffset  LeaseExpiresAt { get; }
    public DateTimeOffset  MaxDeadline    { get; }
    public HeartbeatPolicy Policy         { get; }

    // ── Mutable state ─────────────────────────────────────────────────────────

    /// <summary>
    /// Set to <c>true</c> once the running command commits an irreversible side effect.
    ///
    /// When the heartbeat service then forces termination due to lease expiry or max-deadline,
    /// it will write <see cref="RunState.Unknown"/> to the journal instead of
    /// <see cref="RunState.TimedOut"/> (CEC v1.0.1 invariant #2: UNKNOWN always
    /// implies <c>needsAttention = true</c>).
    /// </summary>
    public bool HasCommittedIrreversibleSideEffect { get; set; }

    /// <summary>
    /// A <see cref="CancellationToken"/> cancelled when the heartbeat service determines
    /// the run must be terminated (lease expired or max-deadline reached).
    ///
    /// The command runner SHOULD observe this token and stop cleanly.  Termination of
    /// the running command is the runner's responsibility; this service only signals intent.
    /// </summary>
    public CancellationToken TerminationToken => _terminationCts.Token;

    // ── Thread-safe mutators ──────────────────────────────────────────────────

    /// <summary>Returns the next monotonically-increasing heartbeat sequence number.</summary>
    public long NextSeqNo() => Interlocked.Increment(ref _seqNo);

    /// <summary>
    /// Increments the consecutive-failure counter and returns the new value.
    /// Called by the heartbeat loop on each delivery failure.
    /// </summary>
    public int IncrementFailures() => Interlocked.Increment(ref _consecutiveFailures);

    /// <summary>
    /// Resets the consecutive-failure counter to zero.
    /// Called by the heartbeat loop when a delivery succeeds after prior failures.
    /// </summary>
    public void ResetFailures() => Interlocked.Exchange(ref _consecutiveFailures, 0);

    /// <summary>Point-in-time snapshot of the consecutive failure count.</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// Signals any observer of <see cref="TerminationToken"/> that the run must stop.
    /// Called by the <see cref="HeartbeatService"/> when it decides to terminate the run.
    /// </summary>
    public void RequestTermination() => _terminationCts.Cancel();

    /// <inheritdoc/>
    public void Dispose() => _terminationCts.Dispose();
}
