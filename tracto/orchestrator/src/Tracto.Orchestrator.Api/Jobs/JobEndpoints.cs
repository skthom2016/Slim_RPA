#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Api.Jobs;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /jobs request body (OC-02 Job Intake).
/// </summary>
/// <param name="ScriptArtifactId">Artifact identifier of the script to run.</param>
/// <param name="ScriptVersion">Pinned version of the artifact.</param>
/// <param name="SchemaVersion">Must be "1". Any other value is rejected with 422.</param>
/// <param name="InputRefs">Zero or more opaque references to input blobs.</param>
/// <param name="SecretRefs">Zero or more secret references. No actual secret values stored.</param>
/// <param name="Deadline">Optional hard deadline (UTC). Must be in the future.</param>
/// <param name="EventId">Optional external event id for 5-minute deduplication (Callback triggers).</param>
/// <param name="TriggerType">Manual | Scheduled | Callback.</param>
/// <param name="ContinuationScriptRef">Must be null/absent at intake; reserved for future use.</param>
/// <param name="BusinessJobId">Optional caller-supplied correlation id for external systems.</param>
public sealed record CreateJobRequest(
    string    ScriptArtifactId,
    string    ScriptVersion,
    string    SchemaVersion,
    string[]? InputRefs,
    string[]? SecretRefs,
    DateTime? Deadline,
    string?   EventId,
    TriggerType TriggerType,
    string?   ContinuationScriptRef = null,
    string?   BusinessJobId = null);

/// <summary>
/// Successful response from POST /jobs and GET /jobs/{runId}.
/// </summary>
public sealed record JobResponse(Guid RunId, string State);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

// ── OC-03: PullJob DTOs ───────────────────────────────────────────────────────

/// <summary>POST /jobs/pull request body.</summary>
public sealed record PullJobRequest(
    string    WorkerId,
    string    RuntimeType,
    string[]? CapabilityNames,
    string?   InteractiveSessionId = null);

/// <summary>
/// Returned when a job was successfully offered to the worker.
/// The worker must present <see cref="LeaseId"/> and <see cref="AttemptId"/> when
/// calling AcceptLease within the 30-second offer window.
/// </summary>
public sealed record JobOfferedResponse(
    string   Result,
    Guid     RunId,
    Guid     AttemptId,
    Guid     LeaseId,
    string   ArtifactId,
    string   ArtifactVersion,
    string   ArtifactHash,
    string[] InputRefs,
    string[] SecretRefs);

/// <summary>
/// Returned when no matching job is available, or the queue is full.
/// </summary>
public sealed record NoPendingJobResponse(string Result);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-02: Job Intake endpoints.
///   POST /jobs          — create a job run (Manual, Scheduled, or Callback trigger).
///   GET  /jobs/{runId}  — retrieve a run summary.
/// OC-03: Worker dispatch.
///   POST /jobs/pull     — worker pulls the next matching queued job.
/// OC-09: Cancellation.
///   POST /jobs/{runId}/cancel — cancel a queued, leased, or running job.
/// </summary>
public static class JobEndpoints
{
    /// <summary>
    /// Maps all job-related endpoints onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapJobEndpoints(this WebApplication app)
    {
        // ── POST /jobs ────────────────────────────────────────────────────────
        // Creates a new job run. Returns 200 {runId, state} on success.
        // 200 is also returned on a duplicate Callback EventId (idempotent).
        // 400 when ContinuationScriptRef is supplied.
        // 422 when SchemaVersion != "1", artifact not found, or deadline in past.
        app.MapPost("/jobs", async (
            [FromBody]   CreateJobRequest       request,
            [FromServices] CreateJobCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new CreateJobCommand(
                ScriptArtifactId:     request.ScriptArtifactId,
                ScriptVersion:        request.ScriptVersion,
                SchemaVersion:        request.SchemaVersion,
                InputRefs:            request.InputRefs  ?? [],
                SecretRefs:           request.SecretRefs ?? [],
                Deadline:             request.Deadline,
                EventId:              request.EventId,
                TriggerType:          request.TriggerType,
                ContinuationScriptRef: request.ContinuationScriptRef,
                BusinessJobId:        request.BusinessJobId);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "CONTINUATION_SCRIPT_REF_FORBIDDEN" => StatusCodes.Status400BadRequest,
                    "UNSUPPORTED_SCHEMA"                => StatusCodes.Status422UnprocessableEntity,
                    "ARTIFACT_NOT_FOUND"                => StatusCodes.Status422UnprocessableEntity,
                    "DEADLINE_IN_PAST"                  => StatusCodes.Status422UnprocessableEntity,
                    _                                   => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new JobResponse(result.Value.RunId, result.Value.State));
        })
        .WithName("CreateJob")
        .WithTags("Jobs")
        .Accepts<CreateJobRequest>("application/json")
        .Produces<JobResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ── POST /jobs/pull ───────────────────────────────────────────────────
        // Worker pulls the next matching queued job.
        // Returns 200 {result:"JobOffered", ...} or 200 {result:"NoPendingJob"}.
        app.MapPost("/jobs/pull", async (
            [FromBody]   PullJobRequest        request,
            [FromServices] PullJobCommandHandler handler,
            CancellationToken ct) =>
        {
            var command = new PullJobCommand(
                WorkerId:             request.WorkerId,
                RuntimeType:          request.RuntimeType,
                CapabilityNames:      request.CapabilityNames ?? [],
                InteractiveSessionId: request.InteractiveSessionId);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"   => StatusCodes.Status404NotFound,
                    "Validation" => StatusCodes.Status400BadRequest,
                    _            => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return result.Value switch
            {
                MatchResult.JobOffered offered => Results.Ok(new JobOfferedResponse(
                    Result:          "JobOffered",
                    RunId:           offered.RunId,
                    AttemptId:       offered.AttemptId,
                    LeaseId:         offered.LeaseId,
                    ArtifactId:      offered.ArtifactId,
                    ArtifactVersion: offered.ArtifactVersion,
                    ArtifactHash:    offered.ArtifactHash,
                    InputRefs:       offered.InputRefs,
                    SecretRefs:      offered.SecretRefs)),

                MatchResult.QueueFull => Results.Ok(
                    new NoPendingJobResponse("QueueFull")),

                _ => Results.Ok(new NoPendingJobResponse("NoPendingJob"))
            };
        })
        .WithName("PullJob")
        .WithTags("Jobs")
        .Accepts<PullJobRequest>("application/json")
        .Produces<JobOfferedResponse>(StatusCodes.Status200OK)
        .Produces<NoPendingJobResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ── GET /jobs/{runId} ─────────────────────────────────────────────────
        // Returns a summary of the run. 404 when the run is not found.
        app.MapGet("/jobs/{runId:guid}", async (
            Guid runId,
            [FromServices] GetJobQueryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetJobQuery(runId), ct);

            return result.IsSuccess
                ? Results.Ok(new JobResponse(result.Value.RunId, result.Value.State))
                : Results.Problem(
                    title:      "Run not found",
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    type:       "https://tracto.io/errors/NotFound");
        })
        .WithName("GetJob")
        .WithTags("Jobs")
        .Produces<JobResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

    }
}
