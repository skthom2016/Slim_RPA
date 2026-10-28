using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRunRepository"/>.
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfRunRepository : IRunRepository
{
    private readonly OrchestratorDbContext _db;

    public EfRunRepository(OrchestratorDbContext db) => _db = db;

    public async Task<RunRecord?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
        => await _db.RunRecords.FindAsync([runId], cancellationToken);

    public async Task<IReadOnlyList<RunRecord>> FindByStateAsync(RunState state, CancellationToken cancellationToken = default)
        => await _db.RunRecords.Where(r => r.State == state).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RunRecord>> FindByWorkerAndStateAsync(
        string workerId,
        RunState state,
        CancellationToken cancellationToken = default)
        => await _db.RunRecords
                    .Where(r => r.State == state && r.WorkerId == workerId)
                    .ToListAsync(cancellationToken);

    public async Task AddAsync(RunRecord run, CancellationToken cancellationToken = default)
        => await _db.RunRecords.AddAsync(run, cancellationToken);

    public void Update(RunRecord run)
    {
        // Guard: DbSet.Update() unconditionally sets entity state to Modified.
        // When the entity was added in the same unit of work (state = Added), calling
        // Update() changes Added → Modified, causing EF to generate an UPDATE instead
        // of INSERT — which finds 0 rows and throws DbUpdateConcurrencyException.
        // For Added entities EF change-tracking already captures property mutations;
        // no explicit state override is needed.
        // For Detached or Unchanged entities we still need to attach/mark Modified.
        var entry = _db.Entry(run);
        if (entry.State is not EntityState.Added)
            _db.RunRecords.Update(run);
    }

    public async Task<RunRecord?> FindByEventIdAsync(
        string eventId,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
        => await _db.RunRecords
                    .Where(r => r.EventId == eventId && r.CreatedAt >= notBefore)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Returns the most-recent run whose CorrelationId matches <paramref name="correlationId"/>,
    /// or null when no matching run exists.
    /// Used by callback ingestion handlers (OC-08) to correlate a callback with its originating run.
    /// </summary>
    public async Task<RunRecord?> FindByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
        => await _db.RunRecords
                    .Where(r => r.CorrelationId == correlationId)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
}
