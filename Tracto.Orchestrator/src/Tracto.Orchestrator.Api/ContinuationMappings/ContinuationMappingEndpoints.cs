#nullable enable
using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Api.ContinuationMappings;

// ── Request / Response records ────────────────────────────────────────────────

/// <summary>
/// POST /continuation-mappings request body.
/// Creates a new continuation mapping (OC-08).
/// </summary>
internal sealed record CreateContinuationMappingRequest(
    /// <summary>
    /// The specific source run whose terminal state triggers Run B.
    /// Null means a global (state-only) mapping that applies to any run.
    /// </summary>
    Guid?   TriggerRunId,

    /// <summary>
    /// Terminal state that triggers Run B creation.
    /// Must be one of: SUCCEEDED, FAILED, HANDED_OFF, CANCELLED, TIMED_OUT, UNKNOWN.
    /// </summary>
    string  TriggerState,

    /// <summary>ArtifactId of the script to execute as Run B.</summary>
    string  TargetArtifactId,

    /// <summary>Version of the target script artifact.</summary>
    string  TargetArtifactVersion,

    /// <summary>Optional opaque JSON template for Run B's input references.</summary>
    string? InputTemplate = null);

/// <summary>Response body on 201 Created.</summary>
internal sealed record CreateContinuationMappingResponse(
    Guid     MappingId,
    Guid?    TriggerRunId,
    string   TriggerState,
    string   TargetArtifactId,
    string   TargetArtifactVersion,
    string?  InputTemplate,
    DateTimeOffset CreatedAt,
    int      Version);

/// <summary>Response body on 200 OK (GET).</summary>
internal sealed record GetContinuationMappingResponse(
    Guid     MappingId,
    Guid?    TriggerRunId,
    string   TriggerState,
    string   TargetArtifactId,
    string   TargetArtifactVersion,
    string?  InputTemplate,
    DateTimeOffset CreatedAt,
    int      Version);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-08: Continuation Mapping and Run B Creation endpoints.
///   POST /continuation-mappings          — create a mapping
///   GET  /continuation-mappings/{id}     — retrieve a mapping by id
/// </summary>
public static class ContinuationMappingEndpoints
{
    public static WebApplication MapContinuationMappingEndpoints(this WebApplication app)
    {
        // POST — create a new continuation mapping
        app.MapPost("/continuation-mappings", CreateAsync)
           .WithName("CreateContinuationMapping")
           .WithTags("ContinuationMappings")
           .Produces<CreateContinuationMappingResponse>(StatusCodes.Status201Created)
           .ProducesProblem(StatusCodes.Status400BadRequest)
           .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET — retrieve a mapping by primary key
        app.MapGet("/continuation-mappings/{id:guid}", GetAsync)
           .WithName("GetContinuationMapping")
           .WithTags("ContinuationMappings")
           .Produces<GetContinuationMappingResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // ── POST ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateContinuationMappingRequest request,
        CreateContinuationMappingCommandHandler     handler,
        IUnitOfWork                                 unitOfWork,
        CancellationToken                           cancellationToken)
    {
        // Parse TriggerState string → RunState enum
        if (!Enum.TryParse<RunState>(request.TriggerState, ignoreCase: true, out var triggerState))
        {
            return Results.Problem(
                detail: $"'{request.TriggerState}' is not a recognised RunState value. " +
                        "Valid terminal states: SUCCEEDED, FAILED, HANDED_OFF, CANCELLED, TIMED_OUT, UNKNOWN.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid TriggerState");
        }

        if (string.IsNullOrWhiteSpace(request.TargetArtifactId))
        {
            return Results.Problem(
                detail: "TargetArtifactId must not be empty.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing TargetArtifactId");
        }

        if (string.IsNullOrWhiteSpace(request.TargetArtifactVersion))
        {
            return Results.Problem(
                detail: "TargetArtifactVersion must not be empty.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing TargetArtifactVersion");
        }

        var cmd = new CreateContinuationMappingCommand(
            TriggerRunId:          request.TriggerRunId,
            TriggerState:          triggerState,
            TargetArtifactId:      request.TargetArtifactId,
            TargetArtifactVersion: request.TargetArtifactVersion,
            InputTemplate:         request.InputTemplate);

        var result = await handler.HandleAsync(cmd, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "Validation" => Results.Problem(
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title:      "Validation error"),
                _ => Results.Problem(
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      result.Error.Code)
            };
        }

        var v = result.Value;
        return Results.Created(
            $"/continuation-mappings/{v.MappingId}",
            new CreateContinuationMappingResponse(
                MappingId:             v.MappingId,
                TriggerRunId:          v.TriggerRunId,
                TriggerState:          v.TriggerState,
                TargetArtifactId:      v.TargetArtifactId,
                TargetArtifactVersion: v.TargetArtifactVersion,
                InputTemplate:         v.InputTemplate,
                CreatedAt:             v.CreatedAt,
                Version:               v.Version));
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAsync(
        Guid                              id,
        GetContinuationMappingQueryHandler handler,
        CancellationToken                 cancellationToken)
    {
        var query  = new GetContinuationMappingQuery(id);
        var result = await handler.HandleAsync(query, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "NotFound" => Results.Problem(
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title:      "Continuation mapping not found"),
                _ => Results.Problem(
                    detail:     result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      result.Error.Code)
            };
        }

        var v = result.Value;
        return Results.Ok(new GetContinuationMappingResponse(
            MappingId:             v.MappingId,
            TriggerRunId:          v.TriggerRunId,
            TriggerState:          v.TriggerState,
            TargetArtifactId:      v.TargetArtifactId,
            TargetArtifactVersion: v.TargetArtifactVersion,
            InputTemplate:         v.InputTemplate,
            CreatedAt:             v.CreatedAt,
            Version:               v.Version));
    }
}
