#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Callbacks;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Callbacks;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Ingests an inbound async callback and wires it to the continuation creator (OC-07).
///
/// Header fields (X-KeyId, X-Timestamp, X-Nonce, X-Hmac-Signature) arrive pre-extracted
/// by the API layer and are passed as scalar command fields so the handler stays
/// framework-free.
///
/// Happy-path invariants (all must hold in order):
///   1. Payload must not contain automationId / scriptVersionRef / continuationScriptRef → 400.
///   2. HMAC-SHA256 signature must be valid → 401 INVALID_HMAC + SecurityEvent.
///   3. |now − Timestamp| ≤ 5 minutes → 401 REPLAY_DETECTED + SecurityEvent.
///   4. Duplicate EventId → 200 DUPLICATE (idempotent; no second continuation).
///   5. CorrelationId not found → sanitised audit log, no continuation, 200 OK.
///   6. Run for CorrelationId not in HANDED_OFF state → 422 NOT_HANDED_OFF.
///   7. Store CallbackRecord (Processed=false). Enqueue continuation (OC-08).
///      Mark Processed=true. Commit (all writes atomic — invariant 5).
/// </summary>
/// <param name="EventId">Caller-supplied event identifier; used as the deduplication key.</param>
/// <param name="CorrelationId">Token that maps this callback to an existing run.</param>
/// <param name="KeyId">HMAC signing key identifier (X-KeyId header).</param>
/// <param name="Timestamp">Raw timestamp string from the X-Timestamp header.</param>
/// <param name="Nonce">Replay-protection nonce from the X-Nonce header.</param>
/// <param name="HmacSignature">Signature from the X-Hmac-Signature header.</param>
/// <param name="Payload">Raw JSON body as received.</param>
/// <param name="SourceIp">Client IP extracted by the API layer for security event logging.</param>
public sealed record IngestCallbackCommand(
    string  EventId,
    Guid    CorrelationId,
    string  KeyId,
    string  Timestamp,
    string  Nonce,
    string  HmacSignature,
    string  Payload,
    string? SourceIp = null);

// ── Result DTO ─────────────────────────────────────────────────────────────────

/// <summary>Discriminator for the outcome of a callback ingestion attempt.</summary>
public enum IngestCallbackOutcome
{
    /// <summary>Callback accepted; continuation enqueued.</summary>
    Accepted,

    /// <summary>EventId already processed — idempotent acknowledgement.</summary>
    Duplicate,

    /// <summary>
    /// CorrelationId was not found in the run store.
    /// The callback is acknowledged (200) to avoid disclosing run existence.
    /// No continuation is created.
    /// </summary>
    CorrelationNotFound,
}

/// <summary>Returned to the API layer on the success branch.</summary>
public sealed record IngestCallbackResponse(
    string                EventId,
    Guid                  CorrelationId,
    IngestCallbackOutcome Outcome);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="IngestCallbackCommand"/> — story OC-07.
///
/// All writes (CallbackRecord + journal entry) are committed inside a single EF Core
/// transaction via <see cref="IUnitOfWork"/> (domain invariant 5).
///
/// Continuation creation (OC-08) is modelled through <see cref="IContinuationQueue"/>
/// so the handler has no direct dependency on the OC-08 implementation.
/// </summary>
public sealed class IngestCallbackCommandHandler
{
    // ── Forbidden payload field names ──────────────────────────────────────────

    /// <summary>
    /// Top-level JSON property names whose presence in the payload must cause a 400 rejection.
    /// Prevents callers from sneaking script references into the callback body.
    /// </summary>
    private static readonly string[] ForbiddenPayloadFields =
    [
        "continuationScriptRef",
        "automationId",
        "scriptVersionRef",
    ];

    // ── Dependencies ───────────────────────────────────────────────────────────

    private readonly ICallbackRepository  _callbackRepo;
    private readonly IRunRepository       _runRepo;
    private readonly IJournalRepository   _journalRepo;
    private readonly ISecurityEventLog    _securityLog;
    private readonly IHmacValidator       _hmacValidator;
    private readonly IContinuationQueue   _continuationQueue;
    private readonly IUnitOfWork          _unitOfWork;
    private readonly IClock               _clock;
    private readonly ITelemetryService    _telemetry;
    private readonly ILogger<IngestCallbackCommandHandler> _logger;

    // Sentinel RunId for telemetry events emitted before a run is identified.
    private static readonly Guid CallbackTelemetryRunId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public IngestCallbackCommandHandler(
        ICallbackRepository  callbackRepo,
        IRunRepository       runRepo,
        IJournalRepository   journalRepo,
        ISecurityEventLog    securityLog,
        IHmacValidator       hmacValidator,
        IContinuationQueue   continuationQueue,
        IUnitOfWork          unitOfWork,
        IClock               clock,
        ITelemetryService    telemetry,
        ILogger<IngestCallbackCommandHandler> logger)
    {
        _callbackRepo      = callbackRepo;
        _runRepo           = runRepo;
        _journalRepo       = journalRepo;
        _securityLog       = securityLog;
        _hmacValidator     = hmacValidator;
        _continuationQueue = continuationQueue;
        _unitOfWork        = unitOfWork;
        _clock             = clock;
        _telemetry         = telemetry;
        _logger            = logger;
    }

    // ── Entry point ────────────────────────────────────────────────────────────

    public async Task<Result<IngestCallbackResponse, Error>> HandleAsync(
        IngestCallbackCommand command,
        CancellationToken     cancellationToken = default)
    {
        // ── 1. Forbidden payload field check → 400 ─────────────────────────────
        var forbiddenCheck = CheckForbiddenFields(command.Payload);
        if (forbiddenCheck is not null)
            return Result<IngestCallbackResponse, Error>.Fail(forbiddenCheck);

        // ── 2. HMAC validation → 401 INVALID_HMAC ─────────────────────────────
        // EventId and CorrelationId are included in the canonical string so that
        // an attacker cannot replay a valid body against a different run or bypass
        // the duplicate-EventId guard by substituting a different EventId.
        var signatureValid = _hmacValidator.Validate(
            command.KeyId,
            command.Timestamp,
            command.Nonce,
            command.EventId,
            command.CorrelationId.ToString(),
            command.Payload,
            command.HmacSignature);

        if (!signatureValid)
        {
            _logger.LogWarning(
                "[SECURITY] IngestCallback: invalid HMAC for EventId={EventId}, KeyId={KeyId}.",
                command.EventId, command.KeyId);

            await _securityLog.AppendAsync(
                eventType: "CALLBACK_AUTH_FAILED",
                sourceIp:  command.SourceIp,
                details:   $"HMAC validation failed. EventId={Sanitise(command.EventId)}, " +
                           $"KeyId={Sanitise(command.KeyId)}.",
                cancellationToken: cancellationToken);

            // Telemetry: CALLBACK_AUTH_FAILED — include sourceIp, NEVER the callback body.
            await _telemetry.EmitAsync(new TelemetryEvent(
                id:        Guid.NewGuid(),
                runId:     CallbackTelemetryRunId,
                eventType: TelemetryEventType.CallbackAuthFailed,
                occurredAt: _clock.UtcNow,
                metadata:  JsonSerializer.Serialize(new
                {
                    sourceIp   = command.SourceIp,
                    keyId      = Sanitise(command.KeyId),
                    // callback body intentionally absent (security invariant 4)
                })),
                cancellationToken);

            return Result<IngestCallbackResponse, Error>.Fail(
                Error.General("INVALID_HMAC", "HMAC signature validation failed."));
        }

        // ── 3. Replay-window check → 401 REPLAY_DETECTED ──────────────────────
        if (!TryParseTimestamp(command.Timestamp, out var callbackTime))
        {
            _logger.LogWarning(
                "[SECURITY] IngestCallback: unparseable timestamp '{Timestamp}' for EventId={EventId}.",
                command.Timestamp, command.EventId);

            await _securityLog.AppendAsync(
                eventType: "REPLAY_DETECTED",
                sourceIp:  command.SourceIp,
                details:   $"Timestamp could not be parsed. EventId={Sanitise(command.EventId)}.",
                cancellationToken: cancellationToken);

            return Result<IngestCallbackResponse, Error>.Fail(
                Error.General("REPLAY_DETECTED", "Callback timestamp is invalid or outside the replay window."));
        }

        var now = _clock.UtcNow;
        var skew = (now - callbackTime).Duration(); // absolute value of the time difference

        if (skew > TimeSpan.FromMinutes(5))
        {
            _logger.LogWarning(
                "[SECURITY] IngestCallback: stale timestamp for EventId={EventId}. " +
                "Skew={SkewSeconds:F1}s.", command.EventId, skew.TotalSeconds);

            await _securityLog.AppendAsync(
                eventType: "REPLAY_DETECTED",
                sourceIp:  command.SourceIp,
                details:   $"Timestamp outside 5-minute replay window. " +
                           $"EventId={Sanitise(command.EventId)}, SkewSeconds={skew.TotalSeconds:F1}.",
                cancellationToken: cancellationToken);

            return Result<IngestCallbackResponse, Error>.Fail(
                Error.General("REPLAY_DETECTED", "Callback timestamp is outside the 5-minute replay window."));
        }

        // ── 4. Deduplication — Duplicate EventId → 200 DUPLICATE ──────────────
        var existing = await _callbackRepo.FindByEventIdAsync(command.EventId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "IngestCallback: duplicate EventId={EventId} received; returning idempotent 200.",
                command.EventId);

            return Result<IngestCallbackResponse, Error>.Ok(
                new IngestCallbackResponse(
                    EventId:       command.EventId,
                    CorrelationId: command.CorrelationId,
                    Outcome:       IngestCallbackOutcome.Duplicate));
        }

        // ── 5. CorrelationId lookup ────────────────────────────────────────────
        var run = await _runRepo.FindByCorrelationIdAsync(command.CorrelationId, cancellationToken);
        if (run is null)
        {
            // Sanitised audit log — do not disclose whether the correlation reference exists.
            // CorrelationId is deliberately omitted from the log to avoid leaking run existence.
            _logger.LogInformation(
                "IngestCallback: received callback for unrecognised correlation reference; " +
                "acknowledging without continuation. EventId={EventId}.",
                Sanitise(command.EventId));

            // Still acknowledge to the caller (200) to avoid leaking run existence.
            return Result<IngestCallbackResponse, Error>.Ok(
                new IngestCallbackResponse(
                    EventId:       command.EventId,
                    CorrelationId: command.CorrelationId,
                    Outcome:       IngestCallbackOutcome.CorrelationNotFound));
        }

        // ── 6. Run must be HANDED_OFF → 422 NOT_HANDED_OFF ────────────────────
        if (run.State != RunState.HandedOff)
        {
            _logger.LogWarning(
                "IngestCallback: run {RunId} (CorrelationId={CorrelationId}) is in state {State}; " +
                "expected HANDED_OFF. Rejecting with 422.",
                run.Id, command.CorrelationId, run.State);

            return Result<IngestCallbackResponse, Error>.Fail(
                Error.General("NOT_HANDED_OFF",
                    $"The run for this callback is in state '{run.State}' and is not awaiting a callback."));
        }

        // ── 7. Atomic write: CallbackRecord + continuation + journal ───────────
        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;
        try
        {
            var receivedAt = _clock.UtcNow; // re-read after potentially slow lookups above

            // 7a. Persist the CallbackRecord (Processed=false).
            var record = new CallbackRecord(
                id:            Guid.NewGuid(),
                eventId:       command.EventId,
                correlationId: command.CorrelationId,
                keyId:         command.KeyId,
                receivedAt:    receivedAt,
                payloadJson:   command.Payload);

            await _callbackRepo.AddAsync(record, cancellationToken);

            // 7b. Enqueue the continuation creation task (OC-08).
            //     IContinuationQueue is an application-layer port; Infrastructure wires the impl.
            await _continuationQueue.EnqueueAsync(
                runId:         run.Id,
                correlationId: command.CorrelationId,
                callbackPayload: command.Payload,
                cancellationToken: cancellationToken);

            // 7c. Mark the callback record as processed in the same transaction.
            record.MarkProcessed();

            // 7d. Journal entry (invariant 5: atomic with the rest of the writes).
            await _journalRepo.AppendAsync(
                new JournalEntry(
                    runId:      run.Id,
                    attemptId:  run.AttemptId,
                    eventType:  JournalEventTypes.CallbackIngested,
                    occurredAt: receivedAt,
                    payload:    BuildJournalPayload(command)),
                cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogInformation(
                "IngestCallback: EventId={EventId} accepted for run {RunId} (CorrelationId={CorrelationId}). " +
                "Continuation enqueued.",
                command.EventId, run.Id, command.CorrelationId);

            // Telemetry: CALLBACK_RECEIVED — eventId + correlationId only, no payload body.
            Activity.Current?.SetTag("run.id", run.Id.ToString());
            Activity.Current?.SetTag("attempt.id", run.AttemptId.ToString());
            await _telemetry.EmitAsync(new TelemetryEvent(
                id:        Guid.NewGuid(),
                runId:     run.Id,
                eventType: TelemetryEventType.CallbackReceived,
                occurredAt: receivedAt,
                attemptId: run.AttemptId,
                metadata:  JsonSerializer.Serialize(new
                {
                    eventId       = command.EventId,
                    correlationId = command.CorrelationId,
                    // callback payload body intentionally absent (security invariant 4)
                })),
                cancellationToken);

            return Result<IngestCallbackResponse, Error>.Ok(
                new IngestCallbackResponse(
                    EventId:       command.EventId,
                    CorrelationId: command.CorrelationId,
                    Outcome:       IngestCallbackOutcome.Accepted));
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await _unitOfWork.RollbackAsync();
                }
                catch (Exception rollbackEx)
                {
                    // Log and swallow rollback failures so the original exception is not masked.
                    _logger.LogError(rollbackEx,
                        "UnitOfWork rollback failed after an error in IngestCallbackCommandHandler. " +
                        "EventId={EventId}.", command.EventId);
                }
            }
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether any forbidden top-level JSON field name appears in the payload,
    /// and validates that the payload is non-empty valid JSON.
    /// Returns a domain-coded <see cref="Error"/> when a violation is found; <c>null</c> otherwise.
    ///
    /// Error codes per the spec:
    ///   CONTINUATION_SCRIPT_REF_FORBIDDEN — for <c>continuationScriptRef</c>.
    ///   SERVICE_ROUTING_FORBIDDEN         — for <c>automationId</c> or <c>scriptVersionRef</c>.
    /// </summary>
    private static Error? CheckForbiddenFields(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Error.Validation("Callback payload must not be empty.");

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "continuationScriptRef", StringComparison.OrdinalIgnoreCase))
                {
                    return Error.General(
                        "CONTINUATION_SCRIPT_REF_FORBIDDEN",
                        "The payload field 'continuationScriptRef' is not permitted in a callback body.");
                }

                if (string.Equals(prop.Name, "automationId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "scriptVersionRef", StringComparison.OrdinalIgnoreCase))
                {
                    return Error.General(
                        "SERVICE_ROUTING_FORBIDDEN",
                        $"The payload field '{prop.Name}' is not permitted in a callback body.");
                }
            }
        }
        catch (JsonException)
        {
            return Error.Validation("Callback payload is not valid JSON.");
        }

        return null;
    }

    /// <summary>
    /// Attempts to parse the timestamp header value.
    /// Accepts ISO-8601 (DateTimeOffset.Parse) and Unix epoch seconds (long).
    /// </summary>
    private static bool TryParseTimestamp(string raw, out DateTimeOffset result)
    {
        // Try ISO-8601 first (preferred by the spec).
        if (DateTimeOffset.TryParse(raw, out result))
            return true;

        // Fallback: Unix epoch seconds.
        if (long.TryParse(raw, out var epochSeconds))
        {
            result = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Returns a sanitised substring of <paramref name="value"/> safe for log output.
    /// Truncates to 16 characters to limit leakage while keeping the value useful for correlation.
    /// </summary>
    private static string Sanitise(string value) =>
        string.IsNullOrEmpty(value)
            ? "(empty)"
            : value.Length <= 16
                ? value
                : string.Concat(value.AsSpan(0, 16), "…");

    /// <summary>Builds a minimal, non-sensitive journal payload for the CALLBACK_RECEIVED entry.</summary>
    private static string BuildJournalPayload(IngestCallbackCommand cmd) =>
        JsonSerializer.Serialize(new
        {
            eventId       = cmd.EventId,
            correlationId = cmd.CorrelationId,
            keyId         = cmd.KeyId,
            // Payload bytes length only — contents may be sensitive.
            payloadBytes  = cmd.Payload?.Length ?? 0,
        });
}
