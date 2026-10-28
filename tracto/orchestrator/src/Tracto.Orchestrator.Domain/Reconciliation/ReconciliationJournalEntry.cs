#nullable enable
namespace Tracto.Orchestrator.Domain.Reconciliation;

/// <summary>
/// Value object representing a single journal entry submitted by a worker as part of a
/// POST /jobs/{runId}/reconcile request (OC-10).
///
/// These entries are the worker's local record of what happened during the run — they are
/// NOT the persisted <see cref="Domain.Journal.JournalEntry"/> rows.  The
/// <see cref="ReconciliationPolicy"/> reads them to decide whether the run succeeded or
/// whether the outcome is uncertain (UNKNOWN).
///
/// Immutable record — equality is structural.
/// </summary>
/// <param name="EventType">
/// The type of event, e.g. <see cref="Domain.Journal.JournalEventTypes.RunSucceeded"/>.
/// Must not be null or whitespace.
/// </param>
/// <param name="AttemptId">
/// The AttemptId the worker was operating under when this event occurred.
/// Null is allowed for informational entries that were written before an AttemptId was assigned.
/// </param>
/// <param name="OccurredAt">UTC timestamp at which the worker recorded the event.</param>
/// <param name="Payload">Optional opaque JSON payload attached by the worker. May be null.</param>
public sealed record ReconciliationJournalEntry(
    string         EventType,
    Guid?          AttemptId,
    DateTimeOffset OccurredAt,
    string?        Payload = null)
{
    /// <summary>
    /// Creates a <see cref="ReconciliationJournalEntry"/> with basic validation.
    /// Prefer the primary constructor when the caller already validates inputs.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventType"/> is null or whitespace.</exception>
    public static ReconciliationJournalEntry Create(
        string         eventType,
        Guid?          attemptId,
        DateTimeOffset occurredAt,
        string?        payload = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType must not be null or whitespace.", nameof(eventType));

        return new ReconciliationJournalEntry(eventType, attemptId, occurredAt, payload);
    }
}
