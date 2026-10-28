#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Api.Jobs;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /jobs/{runId}/result request body (OC-06 Final Result Processing).
/// </summary>
/// <param name="AttemptId">Must match the run's current AttemptId fence (invariant 3).</param>
/// <param name="TerminalState">Terminal RunState: Succeeded, Failed, HandedOff, Cancelled, TimedOut, or Unknown.</param>
/// <param name="ErrorCategory">Optional error category (e.g. "TIMEOUT"). Null for non-error outcomes.</param>
/// <param name="BusinessOutcome">Optional caller-defined business label (e.g. "ORDER_SUBMITTED").</param>
/// <param name="EvidenceRefs">References to output artifacts / evidence blobs. Defaults to empty.</param>
/// <param name="BusinessJobId">Optional caller-supplied correlation id for external systems.</param>
/// <param name="UnknownReason">Required when TerminalState is Unknown (domain invariant 2).</param>
/// <param name="Payload">Optional opaque payload. Scanned for secret patterns; rejected with 422 on a match.</param>
public sealed record CommitResultRequest(
    Guid      AttemptId,
    string    TerminalState,
    string?   ErrorCategory   = null,
    string?   BusinessOutcome = null,
    string[]? EvidenceRefs    = null,
    string?   BusinessJobId   = null,
    string?   UnknownReason   = null,
    string?   Payload         = null);

/// <summary>
/// Successful response from POST /jobs/{runId}/result.
/// </summary>
/// <param name="RunId">The run that was finalised.</param>
/// <param name="AttemptId">The AttemptId echoed back for correlation.</param>
/// <param name="TerminalState">The terminal state the run is now in.</param>
/// <param name="SlotReleased">True when the worker slot was released as part of this call.</param>
public sealed record CommitResultResponse(
    Guid   RunId,
    Guid   AttemptId,
    string TerminalState,
    bool   SlotReleased);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-06: Final Result Processing and AttemptId Commit.
///   POST /jobs/{runId}/result — commits a terminal result for a running job.
///
/// Status codes:
///   200 — result committed (or idempotent re-delivery of an already-terminal run).
///   404 — run not found.
///   409 — AttemptId mismatch (WRONG_ATTEMPT).
///   422 — validation failure (UNKNOWN_REASON_REQUIRED or SENSITIVE_PAYLOAD).
/// </summary>
public static class ResultEndpoints
{
    /// <summary>
    /// Maps the POST /jobs/{runId}/result endpoint onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapResultEndpoints(this WebApplication app)
    {
        // ── POST /jobs/{runId}/result ──────────────────────────────────────────
        // Commits the final terminal result of a running job.
        // Idempotent: a second call for an already-terminal run returns 200 without
        // writing a duplicate FinalResultRecord or releasing the worker slot again.
        app.MapPost("/jobs/{runId:guid}/result", async (
            Guid runId,
            [FromBody]   CommitResultRequest                      request,
            [FromServices] CommitFinalResultCommandHandler         handler,
            [FromServices] StaticSucceededContinuationService      continuationService,
            CancellationToken ct) =>
        {
            // ── Parse TerminalState from the string body field ─────────────────
            if (!Enum.TryParse<RunState>(request.TerminalState, ignoreCase: true, out var terminalState))
            {
                return Results.Problem(
                    title:      "INVALID_TERMINAL_STATE",
                    detail:     $"'{request.TerminalState}' is not a recognised RunState. " +
                                "Valid terminal values: Succeeded, Failed, HandedOff, Cancelled, TimedOut, Unknown.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    type:       "https://tracto.io/errors/INVALID_TERMINAL_STATE");
            }

            var command = new CommitFinalResultCommand(
                RunId:           runId,
                AttemptId:       request.AttemptId,
                TerminalState:   terminalState,
                ErrorCategory:   request.ErrorCategory,
                BusinessOutcome: request.BusinessOutcome,
                EvidenceRefs:    request.EvidenceRefs,
                BusinessJobId:   request.BusinessJobId,
                UnknownReason:   request.UnknownReason,
                Payload:         request.Payload);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "WRONG_ATTEMPT"           => StatusCodes.Status409Conflict,
                    "UNKNOWN_REASON_REQUIRED" => StatusCodes.Status422UnprocessableEntity,
                    "SENSITIVE_PAYLOAD"       => StatusCodes.Status422UnprocessableEntity,
                    "Validation"              => StatusCodes.Status422UnprocessableEntity,
                    "NotFound"                => StatusCodes.Status404NotFound,
                    _                         => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            var v = result.Value;

            // OC-08: Fire static SUCCEEDED continuation after a successful commit.
            // Runs outside the commit transaction — any continuation failure is logged
            // and swallowed by StaticSucceededContinuationService (by design).
            if (terminalState == RunState.Succeeded)
            {
                await continuationService.OnSucceededAsync(runId, request.BusinessJobId, ct);
            }

            return Results.Ok(new CommitResultResponse(
                RunId:         runId,
                AttemptId:     v.AttemptId,
                TerminalState: v.TerminalState,
                SlotReleased:  v.SlotReleased));
        })
        .WithName("CommitFinalResult")
        .WithTags("Jobs")
        .Accepts<CommitResultRequest>("application/json")
        .Produces<CommitResultResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
