#nullable enable
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Telemetry;
using Tracto.Orchestrator.Infrastructure.Telemetry;

namespace Tracto.Orchestrator.Api.Telemetry;

/// <summary>
/// OC-14 — Telemetry and Observability export endpoints.
///
/// Endpoints:
///   GET /telemetry/{runId} — export all telemetry events for a run, ordered by OccurredAt.
///
/// All endpoints set OpenTelemetry Activity tags (run.id) on the current span per OC-14.
/// No plaintext secrets are ever present in TelemetryEvent.Metadata; the emitting handlers
/// are responsible for replacing secret values with "REDACTED" before writing.
/// </summary>
public static class TelemetryEndpoints
{
    /// <summary>
    /// Maps all telemetry export endpoints onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        // ── GET /telemetry/{runId} ─────────────────────────────────────────────
        // Returns all telemetry events for the given run in chronological order.
        // Returns an empty array when the run exists but has no telemetry events.
        // Returns 400 ProblemDetails when runId is not a valid GUID.
        app.MapGet("/telemetry/{runId:guid}", async (
            Guid runId,
            [FromServices] GetTelemetryByRunIdQueryHandler handler,
            CancellationToken ct) =>
        {
            // OC-14: tag the current OTel span with run.id.
            var activity = Activity.Current;
            activity?.SetTag("run.id", runId.ToString());

            using var span = OrchestratorActivitySource.StartActivity("GetTelemetryByRunId");
            span?.SetTag("run.id", runId.ToString());

            var query  = new GetTelemetryByRunIdQuery(runId);
            var result = await handler.HandleAsync(query, ct);

            if (result.IsFailure)
            {
                var error = result.Error;
                var status = error.Code switch
                {
                    "Validation" => StatusCodes.Status400BadRequest,
                    _            => StatusCodes.Status500InternalServerError
                };

                // Mark the OTEL span as failed so traces accurately reflect the error.
                span?.SetStatus(ActivityStatusCode.Error, error.Message);

                return Results.Problem(
                    title:      "Telemetry query failed",
                    detail:     error.Message,
                    statusCode: status,
                    type:       $"https://tracto.io/errors/{error.Code}");
            }

            return Results.Ok(new TelemetryExportResponse(
                RunId:  runId,
                Count:  result.Value.Events.Count,
                Events: result.Value.Events));
        })
        .WithName("GetTelemetryByRunId")
        .WithTags("Telemetry")
        .Produces<TelemetryExportResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Top-level response envelope for the GET /telemetry/{runId} endpoint.
/// </summary>
public sealed record TelemetryExportResponse(
    Guid                                RunId,
    int                                 Count,
    IReadOnlyList<TelemetryEventDto>    Events);
