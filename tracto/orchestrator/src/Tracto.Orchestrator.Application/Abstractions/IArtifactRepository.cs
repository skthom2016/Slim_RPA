#nullable enable
using Tracto.Orchestrator.Domain.Artifacts;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="ArtifactRecord"/> and <see cref="ArtifactContent"/> aggregates.
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IArtifactRepository
{
    /// <summary>Returns the artifact record for a given id+version, or null if not found.</summary>
    Task<ArtifactRecord?> GetRecordAsync(string artifactId, string version, CancellationToken cancellationToken = default);

    /// <summary>Returns the artifact content for a given id+version, or null if not found.</summary>
    Task<ArtifactContent?> GetContentAsync(string artifactId, string version, CancellationToken cancellationToken = default);

    /// <summary>Adds a new artifact record and its content atomically.</summary>
    Task AddAsync(ArtifactRecord record, ArtifactContent content, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing artifact record as modified for change tracking.</summary>
    void Update(ArtifactRecord record);

    /// <summary>
    /// Returns true when any run in LEASED or RUNNING state references this artifact version.
    /// Used by the delete handler to enforce invariant 7.
    /// </summary>
    Task<bool> IsReferencedByActiveRunAsync(string artifactId, string version, CancellationToken cancellationToken = default);
}
