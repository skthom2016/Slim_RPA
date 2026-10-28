#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence.Artifacts;

/// <summary>
/// EF Core implementation of <see cref="IArtifactRepository"/>.
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfArtifactRepository : IArtifactRepository
{
    private readonly OrchestratorDbContext _db;

    public EfArtifactRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc />
    /// <remarks>
    /// Returns the record regardless of its <c>IsDeleted</c> flag so that Application-layer
    /// handlers can apply domain logic (e.g. return 404 for soft-deleted, allow re-publish of
    /// a deleted version).  Filtering at the repository level would make the handlers'
    /// <c>record.IsDeleted</c> checks unreachable in production while the unit-test mocks
    /// would still exercise them — a dangerous test/production behaviour gap.
    /// </remarks>
    public async Task<ArtifactRecord?> GetRecordAsync(
        string artifactId,
        string version,
        CancellationToken cancellationToken = default)
        => await _db.ArtifactRecords
                    .FirstOrDefaultAsync(
                        a => a.ArtifactId == artifactId && a.Version == version,
                        cancellationToken);

    /// <inheritdoc />
    public async Task<ArtifactContent?> GetContentAsync(
        string artifactId,
        string version,
        CancellationToken cancellationToken = default)
        => await _db.ArtifactContents
                    .FirstOrDefaultAsync(
                        c => c.ArtifactId == artifactId && c.Version == version,
                        cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(
        ArtifactRecord record,
        ArtifactContent content,
        CancellationToken cancellationToken = default)
    {
        await _db.ArtifactRecords.AddAsync(record, cancellationToken);
        await _db.ArtifactContents.AddAsync(content, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(ArtifactRecord record)
        => _db.ArtifactRecords.Update(record);

    /// <inheritdoc />
    /// <remarks>
    /// A run "references" an artifact when its WorkflowId matches the artifactId
    /// and the run is currently LEASED or RUNNING (domain invariant 7).
    /// </remarks>
    public async Task<bool> IsReferencedByActiveRunAsync(
        string artifactId,
        string version,
        CancellationToken cancellationToken = default)
        => await _db.RunRecords
                    .AnyAsync(
                        r => r.WorkflowId == artifactId &&
                             (r.State == RunState.Leased || r.State == RunState.Running),
                        cancellationToken);
}
