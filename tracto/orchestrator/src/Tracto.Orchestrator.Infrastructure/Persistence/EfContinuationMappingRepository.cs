#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IContinuationMappingRepository"/> (OC-08).
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfContinuationMappingRepository : IContinuationMappingRepository
{
    private readonly OrchestratorDbContext _db;

    public EfContinuationMappingRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<ContinuationMapping?> GetByIdAsync(
        Guid              mappingId,
        CancellationToken cancellationToken = default)
        => await _db.ContinuationMappings.FindAsync([mappingId], cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Resolution order (most-specific wins):
    ///   1. Run-specific mapping  — TriggerRunId == sourceRunId AND TriggerState == triggerState
    ///   2. Global/fallback mapping — TriggerRunId IS NULL       AND TriggerState == triggerState
    /// Returns null when neither is found.
    /// </remarks>
    public async Task<ContinuationMapping?> FindBestMatchAsync(
        Guid              sourceRunId,
        RunState          triggerState,
        CancellationToken cancellationToken = default)
    {
        // Try the run-specific mapping first.
        var specific = await _db.ContinuationMappings
            .FirstOrDefaultAsync(
                m => m.TriggerRunId == sourceRunId && m.TriggerState == triggerState,
                cancellationToken);

        if (specific is not null)
            return specific;

        // Fall back to the global (state-level) mapping.
        return await _db.ContinuationMappings
            .FirstOrDefaultAsync(
                m => m.TriggerRunId == null && m.TriggerState == triggerState,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(
        ContinuationMapping mapping,
        CancellationToken   cancellationToken = default)
        => await _db.ContinuationMappings.AddAsync(mapping, cancellationToken);

    /// <inheritdoc/>
    public void Update(ContinuationMapping mapping)
        => _db.ContinuationMappings.Update(mapping);
}

/// <summary>
/// EF Core implementation of <see cref="IContinuationRunRepository"/> (OC-08).
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfContinuationRunRepository : IContinuationRunRepository
{
    private readonly OrchestratorDbContext _db;

    public EfContinuationRunRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<ContinuationRunRecord?> FindAsync(
        Guid              sourceRunId,
        RunState          triggerState,
        CancellationToken cancellationToken = default)
        => await _db.ContinuationRunRecords
            .FirstOrDefaultAsync(
                r => r.SourceRunId == sourceRunId && r.TriggerState == triggerState,
                cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(
        ContinuationRunRecord record,
        CancellationToken     cancellationToken = default)
        => await _db.ContinuationRunRecords.AddAsync(record, cancellationToken);
}
