#nullable enable
namespace Tracto.Orchestrator.Domain.Admin;

/// <summary>
/// Value object representing a signed, redacted telemetry export for a single run.
/// Returned by <c>ExportTelemetryBundleQuery</c> (OC-13).
///
/// Construction rules (invariants):
///   - All event payload strings have been redacted by <see cref="SecretPattern"/> before
///     being passed into this type. The constructor itself does NOT re-run redaction so
///     that the caller controls the redaction pass (testable independently).
///   - <see cref="Signature"/> is an HMAC-SHA256 hex string computed by the Infrastructure
///     layer over a canonical representation of the bundle contents, keyed with the admin key.
///   - The bundle is immutable once created; no mutation methods are exposed.
/// </summary>
public sealed record TelemetryBundle
{
    /// <param name="runId">The run whose telemetry is exported.</param>
    /// <param name="exportedAt">UTC timestamp of bundle creation.</param>
    /// <param name="events">
    ///   Redacted journal-entry payloads. Each item is a self-contained string
    ///   (typically JSON) with secret values already replaced by [REDACTED].
    /// </param>
    /// <param name="securityEvents">
    ///   Redacted security-event details for the same run.
    /// </param>
    /// <param name="signature">
    ///   Hex-encoded HMAC-SHA256 over the canonical bundle, computed by Infrastructure
    ///   with the <c>AdminHmacKey</c> from <see cref="Tracto.Orchestrator.Domain.Settings.OrchestratorSettings"/>.
    /// </param>
    public TelemetryBundle(
        Guid                        runId,
        DateTimeOffset              exportedAt,
        IReadOnlyList<RedactedEvent> events,
        IReadOnlyList<RedactedSecurityEvent> securityEvents,
        string                      signature)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(securityEvents);

        if (string.IsNullOrWhiteSpace(signature))
            throw new ArgumentException("Signature must not be empty.", nameof(signature));

        RunId          = runId;
        ExportedAt     = exportedAt;
        Events         = events;
        SecurityEvents = securityEvents;
        Signature      = signature;
    }

    public Guid                              RunId          { get; }
    public DateTimeOffset                    ExportedAt     { get; }

    /// <summary>Redacted journal entries in chronological order.</summary>
    public IReadOnlyList<RedactedEvent>      Events         { get; }

    /// <summary>Redacted security events in chronological order.</summary>
    public IReadOnlyList<RedactedSecurityEvent> SecurityEvents { get; }

    /// <summary>
    /// Hex-encoded HMAC-SHA256 signature over the bundle.
    /// Recipients may verify bundle integrity by recomputing the HMAC with the shared admin key.
    /// </summary>
    public string                            Signature      { get; }
}

/// <summary>
/// A single redacted journal entry inside a <see cref="TelemetryBundle"/>.
/// The <see cref="Payload"/> is safe to return to callers; secret values have been replaced.
/// </summary>
public sealed record RedactedEvent(
    Guid           Id,
    Guid           RunId,
    Guid?          AttemptId,
    string         EventType,
    DateTimeOffset OccurredAt,
    string?        Payload         // already redacted by SecretPattern
);

/// <summary>
/// A single redacted security event inside a <see cref="TelemetryBundle"/>.
/// </summary>
public sealed record RedactedSecurityEvent(
    Guid           Id,
    string         EventType,
    string?        SourceIp,
    DateTimeOffset OccurredAt,
    string         Details          // already redacted by SecretPattern
);
