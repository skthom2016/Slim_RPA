#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Workers;

namespace Tracto.Orchestrator.Api.Workers;

/// <summary>
/// OC-01 — Worker registration and capability advertisement endpoints.
///
/// Endpoints:
///   POST /workers/register  — register a new worker or re-register an existing one.
///   POST /workers/{workerId}/offline — mark a worker offline; flags RUNNING runs as UNKNOWN.
/// </summary>
public static class WorkerEndpoints
{
    /// <summary>
    /// Maps all worker-related endpoints onto the <paramref name="app"/> route builder.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapWorkerEndpoints(this WebApplication app)
    {
        // ── POST /workers/register ────────────────────────────────────────────────
        // Registers a new worker or re-registers an existing one.
        // Re-registration is idempotent: existing LEASED/RUNNING run records are not touched.
        // Returns 200 {workerId} on success, 400 ProblemDetails on validation failure.
        app.MapPost("/workers/register", async (
            [FromBody]   RegisterWorkerRequest  request,
            [FromServices] RegisterWorkerCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new RegisterWorkerCommand(
                WorkerId:                request.WorkerId,
                RuntimeType:             request.RuntimeType,
                ActionNames:             request.ActionNames ?? [],
                CapabilityDigest:        request.CapabilityDigest,
                AbsenceThresholdSeconds: request.AbsenceThresholdSeconds);

            var result = await handler.HandleAsync(command, ct);

            return result.IsSuccess
                ? Results.Ok(new RegisterWorkerResponse(result.Value.WorkerId))
                : Results.Problem(
                    title:      "Worker registration failed",
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
        })
        .WithName("RegisterWorker")
        .WithTags("Workers")
        .Accepts<RegisterWorkerRequest>("application/json")
        .Produces<RegisterWorkerResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── POST /workers/{workerId}/offline ──────────────────────────────────────
        // Administratively marks a worker offline; any RUNNING runs become UNKNOWN.
        // Returns 200 on success, 404 when the worker is not found.
        app.MapPost("/workers/{workerId}/offline", async (
            string workerId,
            [FromServices] MarkWorkerOfflineCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new MarkWorkerOfflineCommand(workerId);
            var result  = await handler.HandleAsync(command, ct);

            return result.IsSuccess
                ? Results.Ok()
                : result.Error.Code == "NotFound"
                    ? Results.Problem(
                        title:      "Worker not found",
                        detail:     result.Error.Message,
                        statusCode: StatusCodes.Status404NotFound,
                        type:       "https://tracto.io/errors/NotFound")
                    : Results.Problem(
                        title:      "Mark offline failed",
                        detail:     result.Error.Message,
                        statusCode: StatusCodes.Status400BadRequest,
                        type:       $"https://tracto.io/errors/{result.Error.Code}");
        })
        .WithName("MarkWorkerOffline")
        .WithTags("Workers")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// Request body for POST /workers/register.
/// </summary>
/// <param name="WorkerId">Stable, caller-assigned identifier for this worker agent.</param>
/// <param name="RuntimeType">Runtime type (e.g. "dotnet", "python", "browser").</param>
/// <param name="ActionNames">Workflow-action types this worker can execute.</param>
/// <param name="CapabilityDigest">
/// Optional SHA-256 hex digest over the canonical JSON of {ActionNames, RuntimeType}.
/// Mismatch is logged as a warning — registration still succeeds (OC-01 spec).
/// </param>
/// <param name="AbsenceThresholdSeconds">
/// Seconds of missed heartbeats before the orchestrator considers this worker absent.
/// </param>
public sealed record RegisterWorkerRequest(
    string   WorkerId,
    string   RuntimeType,
    string[] ActionNames,
    string?  CapabilityDigest,
    int      AbsenceThresholdSeconds);

/// <summary>
/// Successful response body for POST /workers/register.
/// </summary>
public sealed record RegisterWorkerResponse(string WorkerId);
