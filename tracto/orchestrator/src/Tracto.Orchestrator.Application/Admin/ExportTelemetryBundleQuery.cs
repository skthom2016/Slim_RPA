#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Admin;

// ── Query ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Exports a signed telemetry bundle for a run (OC-13).
/// GET /admin/telemetry-bundle/{runId}
///
/// The bundle contains:
///   • All JournalEntries for the run (payload fields redacted for secret patterns).
///   • SecurityEvents associated with the RunId (via Details text search).
///   • An HMAC-SHA256 signature over the canonical JSON payload.
///
/// Domain invariant 4: no field matching secret patterns is included in the bundle.
/// </summary>
public sealed record ExportTelemetryBundleQuery(Guid RunId);

// ── Result DTO ─────────────────────────────────────────────────────────────────

public sealed record TelemetryEvent(
    Guid            Id,
    string          EventType,
    DateTimeOffset  OccurredAt,
    string?         Payload);

public sealed record TelemetrySecurityEvent(
    Guid            Id,
    string          EventType,
    string?         SourceIp,
    DateTimeOffset  OccurredAt,
    string          Details);

public sealed record TelemetryBundle(
    Guid                                  RunId,
    DateTimeOffset                        ExportedAt,
    IReadOnlyList<TelemetryEvent>         JournalEntries,
    IReadOnlyList<TelemetryEvent>         TelemetryEvents,
    IReadOnlyList<TelemetrySecurityEvent> SecurityEvents,
    string                                Signature);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>Handles <see cref="ExportTelemetryBundleQuery"/> for OC-13.</summary>
public sealed class ExportTelemetryBundleQueryHandler
{
    /// <summary>
    /// Regex patterns used to detect secret-like values in event payloads and details.
    /// A match is replaced with the redaction sentinel before being included in the bundle.
    /// </summary>
    private static readonly Regex[] SecretPatterns =
    [
        new Regex(
            @"(?i)(password|passwd|secret|token|apikey|api_key|auth|credential|private_key)\s*[:=]\s*\S+",
            RegexOptions.Compiled),
        new Regex(
            @"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*",
            RegexOptions.Compiled),
        // Base64-encoded blobs ≥ 40 chars that contain at least one base64-specific
        // character ('+' or '/'), distinguishing them from hex strings (which use only
        // [0-9A-Fa-f] and would produce false positives on SHA-256 or HMAC digests).
        new Regex(
            @"[A-Za-z0-9+/]*[+/][A-Za-z0-9+/]{38,}={0,2}|[A-Za-z0-9+/]{38,}[+/][A-Za-z0-9+/]*={0,2}",
            RegexOptions.Compiled),
    ];

    private const string RedactionSentinel = "[REDACTED]";

    private readonly IJournalRepository           _journalRepo;
    private readonly ITelemetryRepository         _telemetryRepo;
    private readonly IAdminSecurityEventRepository _securityRepo;
    private readonly IAdminBundleSigner           _signer;
    private readonly IClock                       _clock;

    public ExportTelemetryBundleQueryHandler(
        IJournalRepository            journalRepo,
        ITelemetryRepository          telemetryRepo,
        IAdminSecurityEventRepository securityRepo,
        IAdminBundleSigner            signer,
        IClock                        clock)
    {
        _journalRepo   = journalRepo;
        _telemetryRepo = telemetryRepo;
        _securityRepo  = securityRepo;
        _signer        = signer;
        _clock         = clock;
    }

    public async Task<Result<TelemetryBundle, Error>> HandleAsync(
        ExportTelemetryBundleQuery query,
        CancellationToken          cancellationToken = default)
    {
        // ── Collect raw data ───────────────────────────────────────────────────
        var journalEntries   = await _journalRepo.GetByRunIdAsync(query.RunId, cancellationToken);
        var rawTelemetry     = await _telemetryRepo.GetByRunIdAsync(query.RunId, cancellationToken);
        var securityEvents   = await _securityRepo.FindByRunIdAsync(query.RunId, cancellationToken);

        var exportedAt = _clock.UtcNow;

        // ── Redact journal entry payloads ──────────────────────────────────────
        var redactedJournalEntries = journalEntries
            .Select(e => new TelemetryEvent(
                Id:         e.Id,
                EventType:  e.EventType,
                OccurredAt: e.OccurredAt,
                Payload:    RedactString(e.Payload)))
            .ToList();

        // ── Redact OC-14 telemetry event metadata ──────────────────────────────
        var redactedTelemetryEvents = rawTelemetry
            .Select(e => new TelemetryEvent(
                Id:         e.Id,
                EventType:  e.EventType,
                OccurredAt: e.OccurredAt,
                Payload:    RedactString(e.Metadata)))
            .ToList();

        // ── Redact security event details ──────────────────────────────────────
        var redactedSecurityEvents = securityEvents
            .Select(s => new TelemetrySecurityEvent(
                Id:         s.Id,
                EventType:  s.EventType,
                SourceIp:   s.SourceIp,
                OccurredAt: s.OccurredAt,
                Details:    RedactString(s.Details) ?? string.Empty))
            .ToList();

        // ── Build canonical string for HMAC ───────────────────────────────────
        var canonical = BuildCanonicalPayload(
            query.RunId, exportedAt, redactedJournalEntries, redactedTelemetryEvents, redactedSecurityEvents);

        var signature = _signer.Sign(canonical);

        return Result<TelemetryBundle, Error>.Ok(new TelemetryBundle(
            RunId:           query.RunId,
            ExportedAt:      exportedAt,
            JournalEntries:  redactedJournalEntries,
            TelemetryEvents: redactedTelemetryEvents,
            SecurityEvents:  redactedSecurityEvents,
            Signature:       signature));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Replaces secret-like patterns in <paramref name="value"/> with a redaction sentinel.</summary>
    internal static string? RedactString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        foreach (var pattern in SecretPatterns)
            value = pattern.Replace(value, RedactionSentinel);

        return value;
    }

    /// <summary>
    /// Deterministic JSON serialisation used as the HMAC canonical string.
    /// Fields are always written in the same order.
    /// </summary>
    private static string BuildCanonicalPayload(
        Guid                         runId,
        DateTimeOffset               exportedAt,
        List<TelemetryEvent>         journalEntries,
        List<TelemetryEvent>         telemetryEvents,
        List<TelemetrySecurityEvent> securityEvents)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        return JsonSerializer.Serialize(new
        {
            runId           = runId.ToString(),
            exportedAt      = exportedAt.ToString("o"),
            journalEntries,
            telemetryEvents,
            securityEvents,
        }, options);
    }
}
