#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Api.Jobs;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /jobs/{runId}/cancel request body (OC-09).
/// </summary>
/// <param name="RequestedBy">
/// Stable identifier of the principal requesting cancellation
/// (operator username, scheduler service-account, etc.).
/// Null or absent values are rejected with 400 Validation by the handler.
/// </param>
public sealed record CancelRunRequest(string? RequestedBy);

/// <summary>
/// 200 OK response from POST /jobs/{runId}/cancel.
/// </summary>
/// <param name="RunId">
/// The run that was cancelled or had cancellation requested.
/// For RUNNING runs the run stays RUNNING until the worker picks up the flag on its next heartbeat.
/// </param>
public sealed record CancelRunResponse(Guid RunId);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-09: Cancellation endpoint.
///   POST /jobs/{runId}/cancel — cancel or signal cancellation of a run.
///
/// State-machine outcomes:
///   QUEUED   → run immediately CANCELLED; 200 {runId}
///   LEASED   → run immediately CANCELLED, outstanding lease expired; 200 {runId}
///   RUNNING  → CancellationRequested set on lease; worker picks it up on next heartbeat; 200 {runId}
///   CANCELLED → 200 idempotent {runId}
///   HANDED_OFF | SUCCEEDED | FAILED | TIMED_OUT | UNKNOWN → 409 TERMINAL_STATE
///   Not found → 404
/// </summary>
public static class CancelEndpoints
{
    /// <summary>
    /// Maps the cancellation endpoint (OC-09) onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapCancelEndpoints(this WebApplication app)
    {
        // ── POST /jobs/{runId}/cancel (OC-09) ─────────────────────────────────
        // Cancel a run or signal cancellation to its worker.
        // 200 on success (including idempotent double-cancel).
        // 404 when the run does not exist.
        // 409 when the run is in a terminal state other than CANCELLED.
        app.MapPost("/jobs/{runId:guid}/cancel", async (
            Guid                             runId,
            [FromBody]   CancelRunRequest             request,
            [FromServices] CancelRunCommandHandler    handler,
            CancellationToken ct) =>
        {
            var command = new CancelRunCommand(
                RunId:       runId,
                RequestedBy: request.RequestedBy);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"       => StatusCodes.Status404NotFound,
                    "TERMINAL_STATE" => StatusCodes.Status409Conflict,
                    "Conflict"       => StatusCodes.Status409Conflict,
                    "Validation"     => StatusCodes.Status400BadRequest,
                    _                => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new CancelRunResponse(RunId: result.Value));
        })
        .WithName("CancelRun")
        .WithTags("Jobs")
        .Accepts<CancelRunRequest>("application/json")
        .Produces<CancelRunResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
