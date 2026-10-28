namespace Tracto.Orchestrator.Domain.Journal;

/// <summary>
/// Immutable audit record appended in the same EF Core transaction as every aggregate
/// state mutation, satisfying domain invariant 5 (all writes atomic + journal entry).
/// </summary>
public sealed class JournalEntry
{
    // EF Core parameterless ctor.
    private JournalEntry() { }

    public JournalEntry(
        Guid runId,
        Guid? attemptId,
        string eventType,
        DateTimeOffset occurredAt,
        string? payload = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));

        Id = Guid.NewGuid();
        RunId = runId;
        AttemptId = attemptId;
        EventType = eventType;
        OccurredAt = occurredAt;
        Payload = payload;
    }

    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid? AttemptId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Payload { get; private set; }
}
