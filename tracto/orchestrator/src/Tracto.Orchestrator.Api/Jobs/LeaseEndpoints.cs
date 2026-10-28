#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;

namespace Tracto.Orchestrator.Api.Jobs;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /jobs/{runId}/lease/accept request body (OC-04).
/// </summary>
/// <param name="AttemptId">The attemptId echoed back from the PullJob response.</param>
/// <param name="WorkerId">The stable worker identifier that received the offer.</param>
public sealed record AcceptLeaseRequest(Guid AttemptId, string WorkerId);

/// <summary>
/// 200 OK response from POST /jobs/{runId}/lease/accept.
/// Contains the lease payload with artifact references and input/secret refs.
/// No resolved secret values are ever included — only opaque references.
/// </summary>
/// <param name="RunId">The run that is now in RUNNING state.</param>
/// <param name="AttemptId">The fencing token for all subsequent stage-transition calls.</param>
/// <param name="ArtifactId">Script artifact identifier the worker should execute.</param>
/// <param name="ArtifactVersion">Pinned artifact version.</param>
/// <param name="ArtifactHash">SHA-256 content hash for integrity verification.</param>
/// <param name="InputRefs">Opaque input blob references.</param>
/// <param name="SecretRefs">Opaque secret references — no actual secret values.</param>
public sealed record AcceptLeaseResponse(
    Guid     RunId,
    Guid     AttemptId,
    string   ArtifactId,
    string   ArtifactVersion,
    string   ArtifactHash,
    string[] InputRefs,
    string[] SecretRefs);

// ── Heartbeat DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// POST /jobs/{runId}/heartbeat request body (OC-05).
/// </summary>
/// <param name="AttemptId">AttemptId echoed from AcceptLease response — fences stale workers.</param>
/// <param name="WorkerId">Stable identifier of the worker sending the heartbeat.</param>
/// <param name="SeqNo">Monotonically increasing sequence number; stale or equal values are rejected.</param>
public sealed record HeartbeatRequest(Guid AttemptId, string WorkerId, long SeqNo);

/// <summary>
/// 200 OK response from POST /jobs/{runId}/heartbeat.
/// </summary>
/// <param name="CancellationRequested">
/// True when the orchestrator has flagged this run for cancellation.
/// The worker should stop processing and call the completion endpoint with Cancelled state.
/// </param>
public sealed record HeartbeatResponse(bool CancellationRequested);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-04: Lease Acceptance and AttemptId Fencing.
///   POST /jobs/{runId}/lease/accept — worker accepts the lease offer; run moves to RUNNING.
///
/// OC-05: Heartbeat Processing.
///   POST /jobs/{runId}/heartbeat — worker keeps the lease alive; delivers cancellation flag.
///
/// Success (200): returns lease payload / heartbeat result.
/// Conflict (409): WRONG_WORKER | WRONG_ATTEMPT | LEASE_EXPIRED | STALE_SEQ_NO | WRONG_STATE.
/// Not Found (404): no active lease for the given runId.
/// </summary>
public static class LeaseEndpoints
{
    /// <summary>
    /// Maps lease-related endpoints (OC-04 + OC-05) onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapLeaseEndpoints(this WebApplication app)
    {
        // ── POST /jobs/{runId}/lease/accept (OC-04) ───────────────────────────
        // Worker accepts a lease offer. Transitions LeaseRecord→Accepted and
        // RunRecord LEASED→RUNNING in a single atomic transaction.
        // Returns the lease payload (artifact coordinates + refs) on success.
        // 409 when the workerId or attemptId does not match, or the lease has expired.
        app.MapPost("/jobs/{runId:guid}/lease/accept", async (
            Guid                         runId,
            [FromBody]   AcceptLeaseRequest       request,
            [FromServices] AcceptLeaseCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new AcceptLeaseCommand(
                RunId:     runId,
                AttemptId: request.AttemptId,
                WorkerId:  request.WorkerId);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"      => StatusCodes.Status404NotFound,
                    "WRONG_WORKER"  => StatusCodes.Status409Conflict,
                    "WRONG_ATTEMPT" => StatusCodes.Status409Conflict,
                    "LEASE_EXPIRED" => StatusCodes.Status409Conflict,
                    _               => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            var payload = result.Value;
            return Results.Ok(new AcceptLeaseResponse(
                RunId:          payload.RunId,
                AttemptId:      payload.AttemptId,
                ArtifactId:     payload.ArtifactId,
                ArtifactVersion: payload.ArtifactVersion,
                ArtifactHash:   payload.ArtifactHash,
                InputRefs:      payload.InputRefs,
                SecretRefs:     payload.SecretRefs));
        })
        .WithName("AcceptLease")
        .WithTags("Jobs")
        .Accepts<AcceptLeaseRequest>("application/json")
        .Produces<AcceptLeaseResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /jobs/{runId}/heartbeat (OC-05) ──────────────────────────────
        // Worker heartbeats to keep its lease alive.  Returns the cancellation flag
        // so the worker knows to stop processing and call the completion endpoint.
        // 409 on AttemptId/WorkerId mismatch, stale SeqNo, lease expired, or wrong state.
        app.MapPost("/jobs/{runId:guid}/heartbeat", async (
            Guid                             runId,
            [FromBody]   HeartbeatRequest             request,
            [FromServices] SendHeartbeatCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new SendHeartbeatCommand(
                RunId:     runId,
                AttemptId: request.AttemptId,
                WorkerId:  request.WorkerId,
                SeqNo:     request.SeqNo);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"      => StatusCodes.Status404NotFound,
                    "LEASE_EXPIRED" => StatusCodes.Status409Conflict,
                    "WRONG_WORKER"  => StatusCodes.Status409Conflict,
                    "WRONG_ATTEMPT" => StatusCodes.Status409Conflict,
                    "STALE_SEQ_NO"  => StatusCodes.Status409Conflict,
                    "WRONG_STATE"   => StatusCodes.Status409Conflict,
                    _               => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new HeartbeatResponse(result.Value.CancellationRequested));
        })
        .WithName("SendHeartbeat")
        .WithTags("Jobs")
        .Accepts<HeartbeatRequest>("application/json")
        .Produces<HeartbeatResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
