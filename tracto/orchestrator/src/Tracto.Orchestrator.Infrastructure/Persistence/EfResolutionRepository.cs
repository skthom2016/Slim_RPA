#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IResolutionRepository"/> (OC-12).
/// Records are append-only; all mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfResolutionRepository : IResolutionRepository
{
    private readonly OrchestratorDbContext _db;

    public EfResolutionRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task AddAsync(
        ResolutionRecord record,
        CancellationToken cancellationToken = default)
        => await _db.ResolutionRecords.AddAsync(record, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResolutionRecord>> GetByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot translate DateTimeOffset in ORDER BY; pull rows then sort in memory.
        var rows = await _db.ResolutionRecords
                            .Where(r => r.RunId == runId)
                            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.ResolvedAt).ToList();
    }
}
