#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Immutable entity that captures the terminal outcome of a run.
/// Written exactly once in the same transaction that transitions RunRecord to a terminal state.
///
/// Domain invariants enforced here:
///   - TerminalState must be a legal terminal RunState value.
///   - UNKNOWN TerminalState requires NeedsAttention=true and a non-null UnknownReason (invariant 2).
///   - EvidenceRefs is never null (may be empty).
/// </summary>
public sealed class FinalResultRecord
{
    // EF Core parameterless constructor.
    private FinalResultRecord() { }

    public FinalResultRecord(
        Guid runId,
        Guid attemptId,
        RunState terminalState,
        string? errorCategory,
        string? errorMessage,
        string? businessOutcome,
        string[] evidenceRefs,
        string? businessJobId,
        DateTimeOffset completedAt,
        string? unknownReason = null)
    {
        if (!IsTerminalState(terminalState))
            throw new ArgumentException(
                $"State '{terminalState}' is not a legal terminal state.", nameof(terminalState));

        if (terminalState == RunState.Unknown)
        {
            if (string.IsNullOrWhiteSpace(unknownReason))
                throw new ArgumentException(
                    "UnknownReason is required when TerminalState is UNKNOWN (invariant 2).",
                    nameof(unknownReason));
            NeedsAttention = true;
        }

        Id              = Guid.NewGuid();
        RunId           = runId;
        AttemptId       = attemptId;
        TerminalState   = terminalState;
        ErrorCategory   = errorCategory;
        ErrorMessage    = errorMessage;
        BusinessOutcome = businessOutcome;
        EvidenceRefs    = evidenceRefs ?? [];
        BusinessJobId   = businessJobId;
        CompletedAt     = completedAt;
        UnknownReason   = unknownReason;
    }

    public Guid   Id            { get; private set; }
    public Guid   RunId         { get; private set; }
    public Guid   AttemptId     { get; private set; }

    /// <summary>The terminal RunState (SUCCEEDED, FAILED, HANDED_OFF, CANCELLED, TIMED_OUT, UNKNOWN).</summary>
    public RunState TerminalState { get; private set; }

    /// <summary>Opaque error category string (e.g. "TIMEOUT", "SCRIPT_ERROR"). Null for non-error terminals.</summary>
    public string? ErrorCategory { get; private set; }

    /// <summary>Human-readable error description. Null for non-error terminals.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Caller-supplied business outcome label (e.g. "ORDER_FULFILLED").</summary>
    public string? BusinessOutcome { get; private set; }

    /// <summary>References to evidence artifacts (logs, screenshots, etc.). Never null.</summary>
    public string[] EvidenceRefs { get; private set; } = [];

    /// <summary>Caller-supplied business-level job identifier.</summary>
    public string? BusinessJobId { get; private set; }

    /// <summary>UTC timestamp when the final result was committed.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>
    /// True when TerminalState == UNKNOWN. Invariant 2: UNKNOWN always requires attention.
    /// </summary>
    public bool NeedsAttention { get; private set; }

    /// <summary>Required when TerminalState == UNKNOWN. Invariant 2.</summary>
    public string? UnknownReason { get; private set; }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsTerminalState(RunState state) =>
        state is RunState.Succeeded or RunState.Failed or RunState.HandedOff
               or RunState.Cancelled or RunState.TimedOut or RunState.Unknown;
}
