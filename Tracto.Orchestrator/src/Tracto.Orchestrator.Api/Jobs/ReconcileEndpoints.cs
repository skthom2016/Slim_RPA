#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Api.Jobs;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// Journal entry as supplied by the worker in the reconcile request body (OC-10).
/// Carries the event type and attempt fencing token needed to determine the run outcome.
/// No full journal record is required — EventType and AttemptId are sufficient.
/// </summary>
/// <param name="EventType">E.g. "RUN_SUCCEEDED", "RUN_FAILED". Case-insensitive comparison.</param>
/// <param name="AttemptId">AttemptId that generated this event. Null entries are treated as non-matching.</param>
/// <param name="OccurredAt">UTC wall-clock time when the event was recorded on the worker.</param>
/// <param name="Payload">Optional opaque payload. Not used for reconciliation decisions.</param>
public sealed record ReconcileJournalEntryRequest(
    string         EventType,
    Guid?          AttemptId,
    DateTimeOffset OccurredAt,
    string?        Payload = null);

/// <summary>
/// POST /jobs/{runId}/reconcile request body (OC-10).
/// </summary>
/// <param name="AttemptId">
/// The fencing token that must match the run's current AttemptId.
/// A mismatch is rejected with 409 WRONG_ATTEMPT.
/// </param>
/// <param name="JournalEntries">
/// Worker-side journal snapshot. May be empty — an empty array causes the run to
/// transition to UNKNOWN (no evidence of completion).
/// </param>
public sealed record ReconcileRequest(
    Guid                         AttemptId,
    ReconcileJournalEntryRequest[] JournalEntries);

/// <summary>
/// 200 OK response from POST /jobs/{runId}/reconcile.
/// </summary>
/// <param name="RunId">The run that was reconciled.</param>
/// <param name="AttemptId">The AttemptId fence that was used for reconciliation.</param>
/// <param name="ReconciledState">Terminal state the run was moved into: SUCCEEDED or UNKNOWN.</param>
public sealed record ReconcileResponse(
    Guid   RunId,
    Guid   AttemptId,
    string ReconciledState);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-10: Reconciliation After Disconnect or Restart.
///   POST /jobs/{runId}/reconcile — resolves a run's final state from a worker-supplied journal.
///
/// Success (200): run transitioned to SUCCEEDED or UNKNOWN, or already terminal (idempotent).
/// Conflict (409): AttemptId supplied in the body does not match the run's current fence.
/// Not Found (404): no run with the given runId.
/// </summary>
public static class ReconcileEndpoints
{
    /// <summary>
    /// Maps the reconcile endpoint onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapReconcileEndpoints(this WebApplication app)
    {
        // ── POST /jobs/{runId}/reconcile ──────────────────────────────────────
        // Worker (or an operator tool) submits its local journal so the orchestrator
        // can determine the run's true outcome after a disconnect or restart.
        //
        // Invariants enforced by the handler:
        //   - AttemptId mismatch          → 409 WRONG_ATTEMPT
        //   - Run already terminal        → 200 (idempotent, WasAlreadyTerminal=true)
        //   - Journal has RUN_SUCCEEDED   → State=SUCCEEDED, FinalResultRecord written
        //   - Uncertain / empty journal   → State=UNKNOWN, NeedsAttention=true
        //   - NEVER transitions to QUEUED or CREATED (no auto-replay, invariant 2)
        app.MapPost("/jobs/{runId:guid}/reconcile", async (
            Guid                          runId,
            [FromBody]   ReconcileRequest           request,
            [FromServices] ReconcileRunCommandHandler handler,
            CancellationToken ct) =>
        {
            // Map API DTOs → Application-layer lightweight journal entries.
            // The handler inspects EventType and AttemptId to determine the run outcome.
            var entries = (request.JournalEntries ?? [])
                .Select(e => new ReconciliationJournalEntry(
                    EventType:  e.EventType,
                    AttemptId:  e.AttemptId,
                    OccurredAt: e.OccurredAt))
                .ToArray();

            var command = new ReconcileRunCommand(
                RunId:          runId,
                AttemptId:      request.AttemptId,
                JournalEntries: entries);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "WRONG_ATTEMPT" => StatusCodes.Status409Conflict,
                    "Conflict"      => StatusCodes.Status409Conflict,
                    "NotFound"      => StatusCodes.Status404NotFound,
                    _               => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new ReconcileResponse(
                RunId:           result.Value.RunId,
                AttemptId:       result.Value.AttemptId,
                ReconciledState: result.Value.ReconciledState));
        })
        .WithName("ReconcileJob")
        .WithTags("Jobs")
        .Accepts<ReconcileRequest>("application/json")
        .Produces<ReconcileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
