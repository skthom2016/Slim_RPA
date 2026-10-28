#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Callbacks;

namespace Tracto.Orchestrator.Api.Callbacks;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /callbacks request body (OC-07 Async Callback Ingestion).
/// HMAC validation fields are supplied via HTTP headers extracted before the handler runs.
/// </summary>
internal sealed record IngestCallbackRequest(
    string EventId,
    Guid   CorrelationId,
    string Payload);

/// <summary>
/// Response from POST /callbacks.
/// Outcome discriminates Accepted / Duplicate / CorrelationNotFound.
/// </summary>
internal sealed record IngestCallbackResponse(
    string EventId,
    Guid   CorrelationId,
    string Outcome);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-07: Async Callback Ingestion.
///   POST /callbacks — ingest a signed inbound callback; enqueue continuation when applicable.
///
/// Status codes:
///   200 — accepted, duplicate, or CorrelationId not found (all return 200 to avoid disclosure).
///   400 — forbidden payload field detected.
///   401 — invalid HMAC or stale timestamp.
///   422 — run not in HANDED_OFF state.
/// </summary>
public static class CallbackEndpoints
{
    public static WebApplication MapCallbackEndpoints(this WebApplication app)
    {
        app.MapPost("/callbacks", async (
            HttpContext          httpContext,
            [FromBody] IngestCallbackRequest request,
            [FromServices] IngestCallbackCommandHandler handler,
            CancellationToken ct) =>
        {
            // Extract HMAC validation headers
            var keyId     = httpContext.Request.Headers["X-KeyId"].FirstOrDefault()          ?? string.Empty;
            var timestamp = httpContext.Request.Headers["X-Timestamp"].FirstOrDefault()      ?? string.Empty;
            var nonce     = httpContext.Request.Headers["X-Nonce"].FirstOrDefault()          ?? string.Empty;
            var signature = httpContext.Request.Headers["X-Hmac-Signature"].FirstOrDefault() ?? string.Empty;
            var sourceIp  = httpContext.Connection.RemoteIpAddress?.ToString();

            var command = new IngestCallbackCommand(
                EventId:       request.EventId,
                CorrelationId: request.CorrelationId,
                KeyId:         keyId,
                Timestamp:     timestamp,
                Nonce:         nonce,
                HmacSignature: signature,
                Payload:       request.Payload,
                SourceIp:      sourceIp);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "INVALID_HMAC"    => StatusCodes.Status401Unauthorized,
                    "REPLAY_DETECTED" => StatusCodes.Status401Unauthorized,
                    "NOT_HANDED_OFF"  => StatusCodes.Status422UnprocessableEntity,
                    _                 => StatusCodes.Status400BadRequest
                };
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            // Map CorrelationNotFound → "UNKNOWN_CORRELATION" per the spec.
            // All other enum values serialise by their default name.
            var outcomeString = result.Value.Outcome == IngestCallbackOutcome.CorrelationNotFound
                ? "UNKNOWN_CORRELATION"
                : result.Value.Outcome.ToString();

            return Results.Ok(new IngestCallbackResponse(
                EventId:       result.Value.EventId,
                CorrelationId: result.Value.CorrelationId,
                Outcome:       outcomeString));
        })
        .WithName("IngestCallback")
        .WithTags("Callbacks")
        .Produces<IngestCallbackResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
