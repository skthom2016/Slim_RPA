#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Artifacts;

/// <summary>
/// Query: retrieve the payload and content hash (ETag) for a specific artifact version.
/// Returns 404 NotFound when the artifact version does not exist or has been soft-deleted.
/// </summary>
public sealed record GetArtifactQuery(string ArtifactId, string Version);

public sealed record GetArtifactResult(byte[] Payload, string ContentHash);

public sealed class GetArtifactHandler
{
    private readonly IArtifactRepository _artifacts;

    public GetArtifactHandler(IArtifactRepository artifacts)
    {
        _artifacts = artifacts;
    }

    public async Task<Result<GetArtifactResult, Error>> HandleAsync(
        GetArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        var record = await _artifacts.GetRecordAsync(query.ArtifactId, query.Version, cancellationToken);
        if (record is null || record.IsDeleted)
            return Result<GetArtifactResult, Error>.Fail(
                Error.NotFound($"Artifact {query.ArtifactId}/{query.Version} not found."));

        var content = await _artifacts.GetContentAsync(query.ArtifactId, query.Version, cancellationToken);
        if (content is null)
            return Result<GetArtifactResult, Error>.Fail(
                Error.NotFound($"Artifact content for {query.ArtifactId}/{query.Version} not found."));

        return Result<GetArtifactResult, Error>.Ok(
            new GetArtifactResult(content.Payload, record.ContentHash));
    }
}
