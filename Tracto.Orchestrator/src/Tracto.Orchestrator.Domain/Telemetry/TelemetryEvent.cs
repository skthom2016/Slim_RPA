#nullable enable
namespace Tracto.Orchestrator.Domain.Telemetry;

/// <summary>
/// Immutable domain entity representing a single observable event emitted by the orchestrator.
///
/// Design invariants:
///   - All fields are set at construction time; the entity is append-only (no mutation).
///   - No plaintext secrets are stored. Any secret reference must be stored as "REDACTED".
///   - CALLBACK_AUTH_FAILED events must include sourceIp in Metadata; callback body must be absent.
///   - RUN_SUCCEEDED events must include stepCount, durationMs, and businessOutcome in Metadata.
///   - Metadata is serialised as a JSON object string and stored verbatim; the orchestrator
///     never parses it at runtime — it is opaque to the domain and structured for log consumers.
/// </summary>
public sealed class TelemetryEvent
{
    // EF Core requires a parameterless constructor for change-tracking.
    private TelemetryEvent() { }

    /// <summary>
    /// Full constructor used by all application command handlers and infrastructure services.
    /// </summary>
    public TelemetryEvent(
        Guid id,
        Guid runId,
        string eventType,
        DateTimeOffset occurredAt,
        string? workerId = null,
        Guid? leaseId = null,
        Guid? attemptId = null,
        string? metadata = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("TelemetryEvent Id must not be empty.", nameof(id));
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType must be a non-empty string.", nameof(eventType));
        if (!TelemetryEventType.All.Contains(eventType))
            throw new ArgumentException(
                $"Unknown event type '{eventType}'. Must be one of the constants in TelemetryEventType.",
                nameof(eventType));

        Id          = id;
        RunId       = runId;
        EventType   = eventType;
        OccurredAt  = occurredAt;
        WorkerId    = workerId;
        LeaseId     = leaseId;
        AttemptId   = attemptId;
        Metadata    = metadata;
    }

    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The run this event belongs to. Always populated.</summary>
    public Guid RunId { get; private set; }

    /// <summary>
    /// The worker involved in this event, if applicable.
    /// Null for events not associated with a specific worker (e.g. RUN_CREATED, RUN_QUEUED).
    /// </summary>
    public string? WorkerId { get; private set; }

    /// <summary>
    /// The lease identifier at the time this event was emitted.
    /// Null for events that occur before or outside a lease context.
    /// </summary>
    public Guid? LeaseId { get; private set; }

    /// <summary>
    /// The AttemptId fence value at the time this event was emitted.
    /// Null for events that occur outside an AttemptId context (e.g. RUN_CREATED).
    /// </summary>
    public Guid? AttemptId { get; private set; }

    /// <summary>
    /// One of the string constants defined in <see cref="TelemetryEventType"/>.
    /// Stored as a plain string so the schema remains forward-compatible.
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>UTC timestamp at which this event occurred.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// JSON object string carrying event-specific key/value pairs.
    ///
    /// Mandatory keys per event type:
    ///   RUN_SUCCEEDED:        stepCount (int), durationMs (long), businessOutcome (string)
    ///   CALLBACK_AUTH_FAILED: sourceIp (string)  — callback body MUST be absent
    ///   SECURITY_EVENT:       eventDetail (string), any secretRef stored as "REDACTED"
    ///
    /// All events: any secret reference value must be stored as the literal string "REDACTED".
    ///
    /// Null is valid for events that carry no additional structured context.
    /// </summary>
    public string? Metadata { get; private set; }
}
