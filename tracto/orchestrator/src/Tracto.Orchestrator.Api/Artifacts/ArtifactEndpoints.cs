using Microsoft.AspNetCore.Mvc;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Artifacts;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Api.Artifacts;

// ── Request / Response records ────────────────────────────────────────────────

/// <summary>
/// PUT /artifacts/{id}/versions/{version} request body.
/// Payload is base64-encoded so binary content survives JSON serialisation.
/// </summary>
internal sealed record PublishArtifactRequest(
    string SchemaVersion,
    string Payload,          // base64-encoded bytes
    string? Signature,
    string? SelectorContent);

/// <summary>PUT response — 201 Created on success.</summary>
internal sealed record PublishArtifactResponse(
    string ArtifactId,
    string Version,
    string ContentHash);

/// <summary>GET /artifacts/{id}/versions/{version} response — 200 OK on success.</summary>
internal sealed record GetArtifactResponse(
    string Payload,          // base64-encoded bytes
    string ContentHash);

// ── Endpoint wiring ───────────────────────────────────────────────────────────

/// <summary>
/// OC-11: Script and Artifact Registry endpoints.
///   PUT    /artifacts/{id}/versions/{version}
///   GET    /artifacts/{id}/versions/{version}
///   DELETE /artifacts/{id}/versions/{version}
/// </summary>
public static class ArtifactEndpoints
{
    public static WebApplication MapArtifactEndpoints(this WebApplication app)
    {
        // PUT — publish a new artifact version
        app.MapPut("/artifacts/{id}/versions/{version}", PublishAsync)
           .WithName("PublishArtifact")
           .WithTags("Artifacts")
           .Produces<PublishArtifactResponse>(StatusCodes.Status201Created)
           .ProducesProblem(StatusCodes.Status409Conflict)
           .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
           .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET — retrieve artifact payload + ETag
        app.MapGet("/artifacts/{id}/versions/{version}", GetAsync)
           .WithName("GetArtifact")
           .WithTags("Artifacts")
           .Produces<GetArtifactResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status404NotFound);

        // DELETE — soft-delete an artifact version
        app.MapDelete("/artifacts/{id}/versions/{version}", DeleteAsync)
           .WithName("DeleteArtifact")
           .WithTags("Artifacts")
           .Produces(StatusCodes.Status204NoContent)
           .ProducesProblem(StatusCodes.Status409Conflict)
           .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // ── PUT ───────────────────────────────────────────────────────────────────

    private static async Task<IResult> PublishAsync(
        string id,
        string version,
        [FromBody] PublishArtifactRequest request,
        PublishArtifactHandler handler,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // Decode base64 payload
        byte[] payloadBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(request.Payload);
        }
        catch (FormatException)
        {
            return Results.Problem(
                detail: "The 'payload' field must be a valid base64-encoded string.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid payload encoding");
        }

        var cmd = new PublishArtifactCommand(
            ArtifactId: id,
            Version: version,
            SchemaVersion: request.SchemaVersion,
            Payload: payloadBytes,
            Signature: request.Signature,
            SelectorContent: request.SelectorContent);

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var result = await handler.HandleAsync(cmd, cancellationToken);

            if (result.IsFailure)
            {
                await unitOfWork.RollbackAsync();
                return result.Error.Code switch
                {
                    "Conflict" => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Version already exists"),
                    "UNSUPPORTED_SCHEMA" => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Unsupported schema version"),
                    "INVALID_SIGNATURE" => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Invalid signature"),
                    _ => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: result.Error.Code)
                };
            }

            await unitOfWork.CommitAsync(cancellationToken);

            var v = result.Value;
            return Results.Created(
                $"/artifacts/{v.ArtifactId}/versions/{v.Version}",
                new PublishArtifactResponse(v.ArtifactId, v.Version, v.ContentHash));
        }
        catch
        {
            // Unexpected exception — roll back the open transaction before re-throwing
            // so the DbContext is left in a clean state for the next request on this scope.
            await unitOfWork.RollbackAsync();
            throw;
        }
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAsync(
        string id,
        string version,
        GetArtifactHandler handler,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        var query = new GetArtifactQuery(id, version);
        var result = await handler.HandleAsync(query, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "NotFound" => Results.Problem(
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Artifact not found"),
                _ => Results.Problem(
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: result.Error.Code)
            };
        }

        var v = result.Value;

        // Set ETag header to ContentHash (domain invariant 7 — immutable once published).
        httpResponse.Headers.ETag = $"\"{v.ContentHash}\"";

        return Results.Ok(new GetArtifactResponse(
            Payload: Convert.ToBase64String(v.Payload),
            ContentHash: v.ContentHash));
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    private static async Task<IResult> DeleteAsync(
        string id,
        string version,
        DeleteArtifactHandler handler,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var cmd = new DeleteArtifactCommand(id, version);

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var result = await handler.HandleAsync(cmd, cancellationToken);

            if (result.IsFailure)
            {
                await unitOfWork.RollbackAsync();
                return result.Error.Code switch
                {
                    "NotFound" => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Artifact not found"),
                    "Conflict" => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Artifact in use"),
                    _ => Results.Problem(
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: result.Error.Code)
                };
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Results.NoContent();
        }
        catch
        {
            // Unexpected exception — roll back before re-throwing so the DbContext is clean.
            await unitOfWork.RollbackAsync();
            throw;
        }
    }
}
