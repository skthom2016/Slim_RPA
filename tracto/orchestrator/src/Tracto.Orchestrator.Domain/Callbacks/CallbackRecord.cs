#nullable enable
namespace Tracto.Orchestrator.Domain.Callbacks;

/// <summary>
/// Aggregate representing a single inbound async callback.
/// Invariants:
///   - EventId is immutable once set (deduplication key).
///   - Processed transitions false → true exactly once; once true it is never reset.
///   - Domain layer has zero EF / framework references.
/// </summary>
public sealed class CallbackRecord
{
    // EF Core parameterless constructor.
    private CallbackRecord() { }

    public CallbackRecord(
        Guid id,
        string eventId,
        Guid correlationId,
        string keyId,
        DateTimeOffset receivedAt,
        string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("KeyId is required.", nameof(keyId));
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("PayloadJson is required.", nameof(payloadJson));

        Id            = id;
        EventId       = eventId;
        CorrelationId = correlationId;
        KeyId         = keyId;
        ReceivedAt    = receivedAt;
        PayloadJson   = payloadJson;
        Processed     = false;
    }

    public Guid           Id            { get; private set; }

    /// <summary>
    /// External event identifier supplied by the caller.
    /// Used as the deduplication key: a second arrival with the same EventId within
    /// or outside the replay window is returned as 200 DUPLICATE without re-processing.
    /// </summary>
    public string         EventId       { get; private set; } = string.Empty;

    /// <summary>
    /// Correlation token used to locate the originating <c>RunRecord</c>.
    /// The run must be in HANDED_OFF state before the callback is accepted.
    /// </summary>
    public Guid           CorrelationId { get; private set; }

    /// <summary>
    /// Identifier of the HMAC signing key used to validate this callback.
    /// Resolved against <c>OrchestratorSettings.HmacKeys[KeyId]</c> at ingestion time.
    /// The actual key material is never stored here — only the identifier.
    /// </summary>
    public string         KeyId         { get; private set; } = string.Empty;

    /// <summary>UTC wall-clock time at which the callback arrived at the API boundary.</summary>
    public DateTimeOffset ReceivedAt    { get; private set; }

    /// <summary>
    /// Raw JSON payload as received from the caller.
    /// The orchestrator does not interpret the contents beyond security checks;
    /// it is forwarded opaquely to the continuation creator (OC-08).
    /// </summary>
    public string         PayloadJson   { get; private set; } = string.Empty;

    /// <summary>
    /// False while the callback is awaiting continuation creation (OC-08).
    /// Set to true in the same EF Core transaction that enqueues the continuation,
    /// satisfying domain invariant 5 (all writes atomic).
    /// Once true this flag is never reset.
    /// </summary>
    public bool           Processed     { get; private set; }

    // ── State transition ───────────────────────────────────────────────────────

    /// <summary>
    /// Marks the callback as processed. Must be called in the same transaction
    /// as the continuation enqueue so the commit is atomic (invariant 5).
    /// Idempotent: calling it a second time is a no-op (safe for retry paths).
    /// </summary>
    public void MarkProcessed()
    {
        Processed = true;
    }
}
