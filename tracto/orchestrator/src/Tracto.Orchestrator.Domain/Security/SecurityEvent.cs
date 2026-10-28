namespace Tracto.Orchestrator.Domain.Security;

/// <summary>
/// Immutable record of a security-relevant event.
/// Details must be sanitised before construction: no secret values, no raw auth tokens.
/// </summary>
public sealed class SecurityEvent
{
    public Guid           Id         { get; private set; }
    public string         EventType  { get; private set; }
    public string?        SourceIp   { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Sanitised human-readable description (no secrets, no PII passwords).</summary>
    public string Details { get; private set; }

    // EF Core parameterless constructor.
    private SecurityEvent()
    {
        Id        = Guid.Empty;
        EventType = string.Empty;
        Details   = string.Empty;
    }

    // Maximum lengths mirror the EF Core column constraints in OrchestratorDbContext.
    private const int MaxEventTypeLength = 128;
    private const int MaxDetailsLength   = 4096;
    private const int MaxSourceIpLength  = 64;

    public SecurityEvent(
        Guid           id,
        string         eventType,
        string?        sourceIp,
        DateTimeOffset occurredAt,
        string         details)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("SecurityEvent Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType must not be blank.", nameof(eventType));
        if (eventType.Length > MaxEventTypeLength)
            throw new ArgumentException($"EventType must not exceed {MaxEventTypeLength} characters.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(details))
            throw new ArgumentException("Details must not be blank.", nameof(details));
        if (details.Length > MaxDetailsLength)
            throw new ArgumentException($"Details must not exceed {MaxDetailsLength} characters.", nameof(details));
        if (sourceIp is not null && sourceIp.Length > MaxSourceIpLength)
            throw new ArgumentException($"SourceIp must not exceed {MaxSourceIpLength} characters.", nameof(sourceIp));

        Id         = id;
        EventType  = eventType;
        SourceIp   = sourceIp;
        OccurredAt = occurredAt;
        Details    = details;
    }
}
