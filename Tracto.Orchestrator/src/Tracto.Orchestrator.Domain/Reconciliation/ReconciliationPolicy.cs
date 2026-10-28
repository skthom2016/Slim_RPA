#nullable enable
using Tracto.Orchestrator.Domain.Journal;

namespace Tracto.Orchestrator.Domain.Reconciliation;

/// <summary>
/// Pure domain policy (stateless, no framework dependencies) that classifies a set of
/// worker-supplied journal entries into a <see cref="ReconciliationDecision"/> for OC-10.
///
/// Algorithm:
/// <list type="number">
///   <item>
///     If the supplied entries contain at least one entry whose
///     <see cref="ReconciliationJournalEntry.EventType"/> ==
///     <see cref="JournalEventTypes.RunSucceeded"/> AND whose
///     <see cref="ReconciliationJournalEntry.AttemptId"/> matches the run's current AttemptId,
///     the decision is <see cref="ReconciliationOutcome.Succeeded"/>.
///   </item>
///   <item>
///     Otherwise (empty journal, no SUCCEEDED event, or SUCCEEDED with wrong AttemptId),
///     the decision is <see cref="ReconciliationOutcome.Unknown"/>.
///   </item>
/// </list>
///
/// Domain invariants preserved:
/// <list type="bullet">
///   <item>Invariant 2: UNKNOWN always results in NeedsAttention=true + a non-null UnknownReason.</item>
///   <item>Invariant 3: Only journal entries whose AttemptId matches the run's fence are treated as conclusive.</item>
///   <item>
///     UNKNOWN runs are NEVER transitioned to QUEUED or CREATED — the policy only returns
///     Succeeded or Unknown; the caller is responsible for applying the decision.
///   </item>
/// </list>
/// </summary>
public static class ReconciliationPolicy
{
    /// <summary>
    /// Evaluates the worker-supplied <paramref name="entries"/> against <paramref name="runAttemptId"/>
    /// and returns a <see cref="ReconciliationDecision"/>.
    /// </summary>
    /// <param name="runAttemptId">
    /// The AttemptId currently fencing the run.  Only journal entries whose AttemptId matches
    /// this value are treated as conclusive evidence.
    /// </param>
    /// <param name="entries">
    /// Zero or more journal entries submitted by the worker.
    /// Null is treated identically to an empty list.
    /// </param>
    /// <returns>
    /// A <see cref="ReconciliationDecision"/> of <see cref="ReconciliationOutcome.Succeeded"/>
    /// or <see cref="ReconciliationOutcome.Unknown"/>.
    /// </returns>
    public static ReconciliationDecision Evaluate(
        Guid runAttemptId,
        IReadOnlyList<ReconciliationJournalEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
            return ReconciliationDecision.Unknown(
                "Reconciliation: uncertain journal — no entries supplied.");

        bool hasConclusive = false;

        foreach (var entry in entries)
        {
            // Guard: skip entries with no AttemptId or with a mismatched AttemptId.
            if (entry.AttemptId != runAttemptId)
                continue;

            if (string.Equals(entry.EventType, JournalEventTypes.RunSucceeded,
                              StringComparison.OrdinalIgnoreCase))
            {
                hasConclusive = true;
                break;
            }
        }

        return hasConclusive
            ? ReconciliationDecision.Succeeded()
            : ReconciliationDecision.Unknown(
                "Reconciliation: uncertain journal — no conclusive SUCCEEDED event found for the current AttemptId.");
    }

    /// <summary>
    /// Returns true when the supplied <paramref name="decision"/> represents an UNKNOWN outcome.
    /// Convenience helper for Application-layer handlers.
    /// </summary>
    public static bool IsUnknown(ReconciliationDecision decision)
        => decision.Outcome == ReconciliationOutcome.Unknown;

    /// <summary>
    /// Returns true when the supplied <paramref name="decision"/> represents a SUCCEEDED outcome.
    /// Convenience helper for Application-layer handlers.
    /// </summary>
    public static bool IsSucceeded(ReconciliationDecision decision)
        => decision.Outcome == ReconciliationOutcome.Succeeded;
}
