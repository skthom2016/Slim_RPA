#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Artifacts;

/// <summary>
/// Command: soft-delete an artifact version.
/// Blocked (409) when any LEASED or RUNNING run references this artifact — invariant 7.
/// Returns 404 when the artifact version does not exist or is already deleted.
/// </summary>
public sealed record DeleteArtifactCommand(string ArtifactId, string Version);

public sealed class DeleteArtifactHandler
{
    private readonly IArtifactRepository _artifacts;

    public DeleteArtifactHandler(IArtifactRepository artifacts)
    {
        _artifacts = artifacts;
    }

    public async Task<Result<bool, Error>> HandleAsync(
        DeleteArtifactCommand cmd,
        CancellationToken cancellationToken = default)
    {
        var record = await _artifacts.GetRecordAsync(cmd.ArtifactId, cmd.Version, cancellationToken);
        if (record is null || record.IsDeleted)
            return Result<bool, Error>.Fail(
                Error.NotFound($"Artifact {cmd.ArtifactId}/{cmd.Version} not found."));

        var inUse = await _artifacts.IsReferencedByActiveRunAsync(
            cmd.ArtifactId, cmd.Version, cancellationToken);
        if (inUse)
            return Result<bool, Error>.Fail(
                Error.Conflict("ARTIFACT_IN_USE"));

        record.SoftDelete();
        _artifacts.Update(record);

        return Result<bool, Error>.Ok(true);
    }
}
