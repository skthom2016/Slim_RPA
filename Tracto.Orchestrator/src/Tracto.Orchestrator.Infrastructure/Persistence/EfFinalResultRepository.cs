#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IFinalResultRepository"/> (OC-06).
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfFinalResultRepository : IFinalResultRepository
{
    private readonly OrchestratorDbContext _db;

    public EfFinalResultRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<FinalResultRecord?> FindByRunIdAsync(
        Guid              runId,
        CancellationToken cancellationToken = default)
        => await _db.FinalResultRecords
                    .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(
        FinalResultRecord record,
        CancellationToken cancellationToken = default)
        => await _db.FinalResultRecords.AddAsync(record, cancellationToken);
}
