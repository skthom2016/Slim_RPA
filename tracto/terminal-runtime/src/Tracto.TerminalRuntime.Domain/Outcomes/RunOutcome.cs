namespace Tracto.TerminalRuntime.Domain.Outcomes;

/// <summary>
/// Immutable value object describing the classified outcome of a run execution attempt.
///
/// CEC v1.0.1 invariants encoded here:
///   - When <see cref="State"/> is <see cref="RunState.Unknown"/>,
///     <see cref="NeedsAttention"/> is always <c>true</c> and <see cref="UnknownReason"/> is always set.
///   - <see cref="ShouldRetry"/> is <c>false</c> for any terminal state or UNKNOWN state —
///     the runtime MUST NOT auto-replay an irreversible command that reached UNKNOWN.
/// </summary>
/// <param name="State">The classified CEC v1.0.1 <see cref="RunState"/>.</param>
/// <param name="NeedsAttention">
///   True when the run requires human review. Always <c>true</c> when State is UNKNOWN.
/// </param>
/// <param name="UnknownReason">
///   Populated with a C3-approved code when <see cref="State"/> is <see cref="RunState.Unknown"/>.
///   Null for all other states.
/// </param>
/// <param name="ErrorCategory">
///   The Orchestrator-facing error category for the failure. Null when the run succeeded.
/// </param>
/// <param name="ShouldRetry">
///   True only when the classifier determined that the failure is safely retryable
///   (read-only or idempotent command, transient error, attempt count below the configured limit).
///   Always <c>false</c> for UNKNOWN and all irreversible-command failures.
/// </param>
public sealed record RunOutcome(
    RunState       State,
    bool           NeedsAttention,
    UnknownReason? UnknownReason,
    ErrorCategory? ErrorCategory,
    bool           ShouldRetry)
{
    // ── Convenience factories ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="RunState.Unknown"/> outcome with <c>needsAttention=true</c>
    /// and no-retry semantics (CEC v1.0.1 invariants #2 and #3).
    /// </summary>
    public static RunOutcome UnknownNoRetry(UnknownReason reason, ErrorCategory category) =>
        new(RunState.Unknown, NeedsAttention: true, reason, category, ShouldRetry: false);

    /// <summary>
    /// Creates a <see cref="RunState.Succeeded"/> outcome.
    /// </summary>
    public static RunOutcome Succeeded() =>
        new(RunState.Succeeded, NeedsAttention: false, null, null, ShouldRetry: false);

    /// <summary>
    /// Creates a <see cref="RunState.Failed"/> outcome without retry (irreversible or limit reached).
    /// </summary>
    public static RunOutcome Failed(ErrorCategory? category = null) =>
        new(RunState.Failed, NeedsAttention: false, null, category, ShouldRetry: false);

    /// <summary>
    /// Creates a retriable failure outcome for read-only / idempotent commands where
    /// the attempt count is still below the configured limit.
    /// </summary>
    public static RunOutcome RetriableFailure(ErrorCategory? category = null) =>
        new(RunState.Running, NeedsAttention: false, null, category, ShouldRetry: true);

    // ── Invariant guard ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates CEC v1.0.1 invariant #2:
    /// UNKNOWN state must carry <c>NeedsAttention=true</c> and a non-null <c>UnknownReason</c>.
    /// </summary>
    public bool SatisfiesInvariant() =>
        State != RunState.Unknown ||
        (NeedsAttention && UnknownReason.HasValue);
}
