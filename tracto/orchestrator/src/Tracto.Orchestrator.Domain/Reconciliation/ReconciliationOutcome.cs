namespace Tracto.Orchestrator.Domain.Reconciliation;

/// <summary>
/// Discriminated set of decisions that <see cref="ReconciliationPolicy"/> can return.
///
/// <list type="bullet">
///   <item>
///     <see cref="Succeeded"/>: The worker's journal contains a conclusive RUN_SUCCEEDED
///     event with the correct AttemptId.  The run transitions RUNNING → SUCCEEDED.
///   </item>
///   <item>
///     <see cref="Unknown"/>: The journal is empty, ambiguous, or contains no conclusive
///     success signal.  The run transitions to UNKNOWN + NeedsAttention=true.
///     Domain invariant 2: UNKNOWN is NEVER auto-replayed (no transition to QUEUED/CREATED).
///   </item>
/// </list>
/// </summary>
public enum ReconciliationOutcome
{
    /// <summary>Journal contains conclusive success evidence matching the current AttemptId.</summary>
    Succeeded = 0,

    /// <summary>
    /// Journal is empty, uncertain, or does not contain a SUCCEEDED event with the correct AttemptId.
    /// Domain invariant 2 applies: NeedsAttention=true is mandatory; the run is never auto-replayed.
    /// </summary>
    Unknown = 1
}
