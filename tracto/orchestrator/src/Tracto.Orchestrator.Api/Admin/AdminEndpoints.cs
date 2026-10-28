#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Admin;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Api.Admin;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>
/// POST /admin/continuation-mappings request body (OC-13 Admin API).
/// Creates a new continuation mapping or updates an existing one for the same
/// (TriggerRunId, TriggerState) key.  Version is always incremented on update.
/// </summary>
public sealed record AdminUpsertContinuationMappingRequest(
    Guid?   TriggerRunId,
    string  TriggerState,
    string  TargetArtifactId,
    string  TargetArtifactVersion,
    string? InputTemplate = null);

/// <summary>Successful response from POST /admin/continuation-mappings.</summary>
public sealed record AdminUpsertContinuationMappingResponse(
    Guid            MappingId,
    Guid?           TriggerRunId,
    string          TriggerState,
    string          TargetArtifactId,
    string          TargetArtifactVersion,
    string?         InputTemplate,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? UpdatedAt,
    int             Version,
    bool            WasCreated);

/// <summary>
/// POST /admin/jobs request body (OC-13 Admin API).
/// Manually triggers a new job run.  Delegates to CreateJobCommand.
/// </summary>
public sealed record AdminCreateJobRequest(
    string    ScriptArtifactId,
    string    ScriptVersion,
    string    SchemaVersion,
    string[]  InputRefs,
    string[]  SecretRefs,
    DateTime? Deadline        = null,
    string?   EventId         = null,
    string?   BusinessJobId   = null);

/// <summary>Successful response from POST /admin/jobs.</summary>
public sealed record AdminCreateJobResponse(Guid RunId, string State);

/// <summary>
/// POST /admin/jobs/{runId}/force-resolve request body (OC-13 Admin API).
/// Allows administrators to force-resolve a run stuck in UNKNOWN state.
/// </summary>
/// <param name="AttemptId">Must match the run's current AttemptId (fencing, invariant 3).</param>
/// <param name="Resolution">Target state: SUCCEEDED, FAILED, or CANCELLED.</param>
/// <param name="ResolutionNote">Optional free-text rationale recorded in the journal.</param>
public sealed record AdminForceResolveRequest(
    Guid    AttemptId,
    string  Resolution,
    string? ResolutionNote = null);

/// <summary>Successful response from POST /admin/jobs/{runId}/force-resolve.</summary>
public sealed record AdminForceResolveResponse(
    Guid   RunId,
    string ResolvedState,
    Guid   AttemptId);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-13: Admin and Operator API.
///   GET  /admin/jobs                          — paged, filtered job listing.
///   GET  /admin/jobs/{runId}                  — full job detail (no SecretRefs).
///   POST /admin/jobs/{runId}/resolve           — admin force-resolution of UNKNOWN runs.
///   GET  /admin/telemetry-bundle/{runId}      — signed telemetry + security events.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Maps all OC-13 admin endpoints onto <paramref name="app"/>.
    /// Call this from Program.cs after the middleware pipeline is configured.
    /// </summary>
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // ── POST /admin/continuation-mappings ─────────────────────────────────
        // Creates or updates a continuation mapping.  Version is incremented on update.
        // A JournalEntry audit record is written in the same atomic transaction.
        // 400 when TriggerState is invalid or required fields are missing.
        app.MapPost("/admin/continuation-mappings", async (
            [FromBody]     AdminUpsertContinuationMappingRequest     request,
            [FromServices] UpsertAdminContinuationMappingCommandHandler handler,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<RunState>(request.TriggerState, ignoreCase: true, out var triggerState))
                return Results.Problem(
                    title:      "Validation",
                    detail:     $"Unknown TriggerState value '{request.TriggerState}'. " +
                                "Valid terminal states: SUCCEEDED, FAILED, HANDED_OFF, CANCELLED, TIMED_OUT, UNKNOWN.",
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       "https://tracto.io/errors/Validation");

            var command = new UpsertAdminContinuationMappingCommand(
                TriggerRunId:          request.TriggerRunId,
                TriggerState:          triggerState,
                TargetArtifactId:      request.TargetArtifactId,
                TargetArtifactVersion: request.TargetArtifactVersion,
                InputTemplate:         request.InputTemplate);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");

            var v = result.Value;
            var response = new AdminUpsertContinuationMappingResponse(
                MappingId:             v.MappingId,
                TriggerRunId:          v.TriggerRunId,
                TriggerState:          v.TriggerState,
                TargetArtifactId:      v.TargetArtifactId,
                TargetArtifactVersion: v.TargetArtifactVersion,
                InputTemplate:         v.InputTemplate,
                CreatedAt:             v.CreatedAt,
                UpdatedAt:             v.UpdatedAt,
                Version:               v.Version,
                WasCreated:            v.WasCreated);

            return v.WasCreated
                ? Results.Created($"/admin/continuation-mappings/{v.MappingId}", response)
                : Results.Ok(response);
        })
        .WithName("AdminUpsertContinuationMapping")
        .WithTags("Admin")
        .Accepts<AdminUpsertContinuationMappingRequest>("application/json")
        .Produces<AdminUpsertContinuationMappingResponse>(StatusCodes.Status201Created)
        .Produces<AdminUpsertContinuationMappingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── POST /admin/jobs ──────────────────────────────────────────────────
        // Manually triggers a new job run.  Delegates to CreateJobCommandHandler.
        // Same semantics as POST /jobs but under the admin prefix (JWT + IP required).
        // 400 on validation errors; 200 on success (or idempotent EventId match).
        app.MapPost("/admin/jobs", async (
            [FromBody]     AdminCreateJobRequest    request,
            [FromServices] CreateJobCommandHandler  handler,
            CancellationToken ct) =>
        {
            var command = new CreateJobCommand(
                ScriptArtifactId:      request.ScriptArtifactId,
                ScriptVersion:         request.ScriptVersion,
                SchemaVersion:         request.SchemaVersion,
                InputRefs:             request.InputRefs,
                SecretRefs:            request.SecretRefs,
                Deadline:              request.Deadline,
                EventId:               request.EventId,
                TriggerType:           TriggerType.Manual,
                ContinuationScriptRef: null,
                BusinessJobId:         request.BusinessJobId);

            var result = await handler.HandleAsync(command, ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "Validation" => StatusCodes.Status400BadRequest,
                    "Conflict"   => StatusCodes.Status409Conflict,
                    "NotFound"   => StatusCodes.Status404NotFound,
                    _            => StatusCodes.Status400BadRequest,
                };
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(new AdminCreateJobResponse(result.Value.RunId, result.Value.State));
        })
        .WithName("AdminCreateJob")
        .WithTags("Admin")
        .Accepts<AdminCreateJobRequest>("application/json")
        .Produces<AdminCreateJobResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── GET /admin/jobs ───────────────────────────────────────────────────
        // Returns a paged list of runs.  All query parameters are optional.
        // ?status=UNKNOWN&from=2024-01-01T00:00:00Z&to=2024-12-31T23:59:59Z&page=1&pageSize=50
        app.MapGet("/admin/jobs", async (
            [FromQuery] string?  status   = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to   = null,
            [FromQuery] int      page     = 1,
            [FromQuery] int      pageSize = 50,
            [FromServices] ListAdminJobsQueryHandler handler = default!,
            CancellationToken ct = default) =>
        {
            // Parse the optional state filter; reject unrecognised values.
            RunState? parsedState = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<RunState>(status, ignoreCase: true, out var s))
                    return Results.Problem(
                        title:      "Validation",
                        detail:     $"Unknown status value '{status}'.",
                        statusCode: StatusCodes.Status400BadRequest,
                        type:       "https://tracto.io/errors/Validation");
                parsedState = s;
            }

            var query  = new ListAdminJobsQuery(parsedState, from, to, page, pageSize);
            var result = await handler.HandleAsync(query, ct);

            if (result.IsFailure)
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");

            return Results.Ok(result.Value);
        })
        .WithName("AdminListJobs")
        .WithTags("Admin")
        .Produces<AdminJobPage>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── GET /admin/jobs/{runId} ───────────────────────────────────────────
        // Returns the full run detail.  SecretRefs are never present (invariant 4).
        // 404 when the run does not exist.
        app.MapGet("/admin/jobs/{runId:guid}", async (
            Guid runId,
            [FromServices] GetAdminJobDetailQueryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetAdminJobDetailQuery(runId), ct);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "NotFound"   => StatusCodes.Status404NotFound,
                    "Validation" => StatusCodes.Status400BadRequest,
                    "Conflict"   => StatusCodes.Status409Conflict,
                    _            => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: statusCode,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");
            }

            return Results.Ok(result.Value);
        })
        .WithName("AdminGetJobDetail")
        .WithTags("Admin")
        .Produces<AdminJobDetailView>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ── GET /admin/telemetry-bundle/{runId} ───────────────────────────────
        // Exports a signed telemetry bundle for a run.
        // Bundle contains journal entries + security events, all secret-redacted.
        // Returns TelemetryBundle with an HMAC-SHA256 signature.
        // Always returns 200; if the run has no telemetry the bundle is empty (not 404).
        app.MapGet("/admin/telemetry-bundle/{runId:guid}", async (
            Guid runId,
            [FromServices] ExportTelemetryBundleQueryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ExportTelemetryBundleQuery(runId), ct);

            if (result.IsFailure)
                return Results.Problem(
                    title:      result.Error.Code,
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    type:       $"https://tracto.io/errors/{result.Error.Code}");

            return Results.Ok(result.Value);
        })
        .WithName("AdminExportTelemetryBundle")
        .WithTags("Admin")
        .Produces<TelemetryBundle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
