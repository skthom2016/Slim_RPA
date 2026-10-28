#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Api.Admin;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /admin/jobs/{runId}/resolve request body (OC-12 Operator Resolution).
/// </summary>
/// <param name="Type">
/// The operator decision: "INVESTIGATED_NO_RERUN" or "APPROVED_NEW_RUN".
/// </param>
/// <param name="Justification">
/// Mandatory free-text rationale. An empty value returns 400.
/// </param>
/// <param name="OperatorId">
/// Identity of the operator performing the resolution (user id, service account, etc.).
/// </param>
public sealed record ResolveUnknownRunRequest(
    string Type,
    string Justification,
    string OperatorId);

/// <summary>
/// Successful response from POST /admin/jobs/{runId}/resolve.
/// NewRunId is non-null only for APPROVED_NEW_RUN resolutions.
/// </summary>
public sealed record ResolveUnknownRunResponse(
    Guid  ResolutionId,
    Guid  RunId,
    Guid? NewRunId);

/// <summary>
/// Response from GET /admin/jobs/{runId}/unknown-detail.
/// </summary>
public sealed record UnknownRunDetailResponse(
    Guid                         RunId,
    string                       State,
    string?                      UnknownReason,
    bool                         NeedsAttention,
    string?                      LastJournalEntry,
    IReadOnlyList<string>        EvidenceRefs,
    IReadOnlyList<ResolutionDto> Resolutions);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-12: UNKNOWN Runs and Operator Resolution.
///   GET  /admin/jobs/{runId}/unknown-detail — full detail for an UNKNOWN run.
///   POST /admin/jobs/{runId}/resolve        — record an operator resolution.
/// </summary>
public static class UnknownRunEndpoints
{
    /// <summary>
    /// Maps all OC-12 admin endpoints onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapUnknownRunEndpoints(this WebApplication app)
    {
        // ── GET /admin/jobs/{runId}/unknown-detail ────────────────────────────
        // Returns full UNKNOWN-state detail: reason, journal evidence, resolutions.
        // 404 when the run does not exist.
        // 422 when the run is not in UNKNOWN state.
        app.MapGet("/admin/jobs/{runId:guid}/unknown-detail", async (
            Guid runId,
            [FromServices] GetUnknownRunDetailQueryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetUnknownRunDetailQuery(runId), ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"         => StatusCodes.Status404NotFound,
                    "NOT_UNKNOWN_STATE" => StatusCodes.Status422UnprocessableEntity,
                    _                  => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            var detail = result.Value;
            return Results.Ok(new UnknownRunDetailResponse(
                RunId:            detail.RunId,
                State:            detail.State,
                UnknownReason:    detail.UnknownReason,
                NeedsAttention:   detail.NeedsAttention,
                LastJournalEntry: detail.LastJournalEntry,
                EvidenceRefs:     detail.EvidenceRefs,
                Resolutions:      detail.Resolutions));
        })
        .WithName("GetUnknownRunDetail")
        .WithTags("Admin")
        .Produces<UnknownRunDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ── POST /admin/jobs/{runId}/resolve ──────────────────────────────────
        // Records an operator resolution for an UNKNOWN run.
        // 400 when justification is empty or ResolutionType is invalid.
        // 404 when the run does not exist.
        // 422 when the run is not in UNKNOWN state.
        //
        // INVARIANT: the original RunRecord is NEVER transitioned from UNKNOWN.
        // APPROVED_NEW_RUN creates a brand-new RunRecord (different RunId).
        app.MapPost("/admin/jobs/{runId:guid}/resolve", async (
            Guid runId,
            [FromBody]   ResolveUnknownRunRequest          request,
            [FromServices] ResolveUnknownRunCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new ResolveUnknownRunCommand(
                RunId:          runId,
                ResolutionType: request.Type,
                Justification:  request.Justification,
                OperatorId:     request.OperatorId);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "Validation"           => StatusCodes.Status400BadRequest,
                    "NotFound"             => StatusCodes.Status404NotFound,
                    "NOT_UNKNOWN_STATE"    => StatusCodes.Status422UnprocessableEntity,
                    "NEW_RUN_CREATION_FAILED" => StatusCodes.Status422UnprocessableEntity,
                    _                      => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new ResolveUnknownRunResponse(
                ResolutionId: result.Value.ResolutionId,
                RunId:        result.Value.RunId,
                NewRunId:     result.Value.NewRunId));
        })
        .WithName("ResolveUnknownRun")
        .WithTags("Admin")
        .Accepts<ResolveUnknownRunRequest>("application/json")
        .Produces<ResolveUnknownRunResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
