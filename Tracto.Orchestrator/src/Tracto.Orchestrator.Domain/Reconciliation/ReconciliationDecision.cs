#nullable enable
namespace Tracto.Orchestrator.Domain.Reconciliation;

/// <summary>
/// Immutable value object that carries the result of a <see cref="ReconciliationPolicy"/> evaluation.
///
/// Two possible decisions:
/// <list type="bullet">
///   <item><see cref="Succeeded"/>: the journal contains a RUN_SUCCEEDED event with the correct AttemptId.</item>
///   <item><see cref="Unknown"/>: the journal is empty, uncertain, or contains no conclusive success evidence.</item>
/// </list>
///
/// Domain invariant 2 is preserved by construction:
/// <see cref="NeedsAttention"/> and <see cref="UnknownReason"/> are always populated for the UNKNOWN decision.
/// </summary>
public sealed class ReconciliationDecision
{
    /// <summary>Whether the reconciliation determined the run succeeded.</summary>
    public ReconciliationOutcome Outcome { get; }

    /// <summary>
    /// True when the decision is <see cref="ReconciliationOutcome.Unknown"/>.
    /// Always false for <see cref="ReconciliationOutcome.Succeeded"/>.
    /// </summary>
    public bool NeedsAttention { get; }

    /// <summary>
    /// Human-readable reason required when <see cref="Outcome"/> is
    /// <see cref="ReconciliationOutcome.Unknown"/> (domain invariant 2).
    /// Null for <see cref="ReconciliationOutcome.Succeeded"/>.
    /// </summary>
    public string? UnknownReason { get; }

    private ReconciliationDecision(ReconciliationOutcome outcome, bool needsAttention, string? unknownReason)
    {
        Outcome        = outcome;
        NeedsAttention = needsAttention;
        UnknownReason  = unknownReason;
    }

    // ── Factory methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a SUCCEEDED decision.
    /// NeedsAttention is false; UnknownReason is null.
    /// </summary>
    public static ReconciliationDecision Succeeded()
        => new(ReconciliationOutcome.Succeeded, needsAttention: false, unknownReason: null);

    /// <summary>
    /// Creates an UNKNOWN decision with the supplied reason (domain invariant 2).
    /// NeedsAttention is always true.
    /// </summary>
    /// <param name="reason">
    /// Non-empty reason explaining why the outcome is uncertain.
    /// Must not be null or whitespace.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or whitespace.</exception>
    public static ReconciliationDecision Unknown(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "A non-empty reason is required for an UNKNOWN reconciliation decision (invariant 2).",
                nameof(reason));

        return new(ReconciliationOutcome.Unknown, needsAttention: true, unknownReason: reason);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        Outcome == ReconciliationOutcome.Succeeded
            ? "SUCCEEDED"
            : $"UNKNOWN({UnknownReason})";
}
