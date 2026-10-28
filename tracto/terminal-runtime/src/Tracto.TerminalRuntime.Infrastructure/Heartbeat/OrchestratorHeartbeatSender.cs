using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Heartbeat;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Infrastructure.Heartbeat;

/// <summary>
/// HTTP implementation of <see cref="IHeartbeatSender"/> that POSTs to
/// <c>POST {OrchestratorBaseUrl}/jobs/{runId}/heartbeat</c> (TER-US-018).
///
/// CEC v1.0.1 invariants:
/// <list type="bullet">
///   <item>#4 — no plaintext secret values appear in any request payload.</item>
///   <item>#5 — only the HTTP gateway is called; no direct third-party API, DB, or socket.</item>
/// </list>
///
/// Failure modes handled:
/// <list type="bullet">
///   <item>HTTP non-2xx — returns <c>Fail(Heartbeat.NonSuccessResponse)</c>.</item>
///   <item>Per-request timeout — returns <c>Fail(Heartbeat.Timeout)</c>.</item>
///   <item>Transport error (<see cref="HttpRequestException"/>) — returns <c>Fail(Heartbeat.HttpError)</c>.</item>
///   <item>Command-level <see cref="OperationCanceledException"/> — re-thrown so the loop exits.</item>
/// </list>
/// </summary>
public sealed class OrchestratorHeartbeatSender(
    HttpClient                             httpClient,
    IOptions<HeartbeatSettings>            settings,
    ILogger<OrchestratorHeartbeatSender>   logger) : IHeartbeatSender
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented              = false
    };

    /// <inheritdoc/>
    public async Task<Result<bool, Error>> SendAsync(HeartbeatRecord record, CancellationToken ct = default)
    {
        var baseUrl = settings.Value.OrchestratorBaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result.Fail<bool>(Error.General(
                "Heartbeat.NoBaseUrl",
                "OrchestratorBaseUrl is not configured. Cannot deliver heartbeat."));

        // CEC invariant #4: the payload carries no secret-bearing fields.
        var payload = new HeartbeatPayload(
            AttemptId:       record.AttemptId,
            SeqNo:           record.SeqNo,
            SentAtUtc:       record.SentAt,
            ProgressSummary: record.ProgressSummary);

        var json    = JsonSerializer.Serialize(payload, SerializerOptions);
        var runIdEncoded = Uri.EscapeDataString(record.RunId);
        var url     = $"{baseUrl.TrimEnd('/')}/jobs/{runIdEncoded}/heartbeat";

        try
        {
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestCts.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));

            using var content  = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(url, content, requestCts.Token)
                                                 .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return Result.Ok<bool>(true);

            // A 409 Conflict can occur when the Orchestrator fences a stale attempt.
            // Treat it as a warning rather than a hard error so the caller can decide.
            logger.LogWarning(
                "Orchestrator returned HTTP {Status} for heartbeat on run {RunId} seq {SeqNo}.",
                (int)response.StatusCode, record.RunId, record.SeqNo);

            return Result.Fail<bool>(Error.General(
                "Heartbeat.NonSuccessResponse",
                $"Orchestrator returned HTTP {(int)response.StatusCode}."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Command-level cancellation — propagate so the caller's loop exits.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Per-request timeout (requestCts fired, not ct).
            logger.LogWarning(
                "Heartbeat POST for run {RunId} seq {SeqNo} timed out after {Seconds}s.",
                record.RunId, record.SeqNo, settings.Value.TimeoutSeconds);

            return Result.Fail<bool>(Error.Timeout(
                "Heartbeat.Timeout",
                $"Heartbeat for run '{record.RunId}' did not complete within {settings.Value.TimeoutSeconds}s."));
        }
        catch (HttpRequestException ex)
        {
            // Transport-level error (DNS, connection refused, TLS).
            logger.LogWarning(ex,
                "HTTP transport error sending heartbeat for run {RunId} seq {SeqNo}.",
                record.RunId, record.SeqNo);

            return Result.Fail<bool>(Error.General(
                "Heartbeat.HttpError",
                "HTTP transport error sending heartbeat."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error sending heartbeat for run {RunId}.", record.RunId);

            return Result.Fail<bool>(Error.General(
                "Heartbeat.UnexpectedError",
                "Unexpected error sending heartbeat."));
        }
    }

    // ── Payload type ──────────────────────────────────────────────────────────

    /// <summary>
    /// JSON payload sent to <c>POST /jobs/{runId}/heartbeat</c>.
    /// CEC invariant #4: no secret fields.
    /// </summary>
    private sealed record HeartbeatPayload(
        string         AttemptId,
        long           SeqNo,
        DateTimeOffset SentAtUtc,
        string         ProgressSummary);
}
