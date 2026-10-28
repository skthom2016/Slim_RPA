#nullable enable
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="ContinuationMapping"/> aggregates (OC-08).
/// All mutating methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IContinuationMappingRepository
{
    /// <summary>Returns a mapping by its primary key; null when not found.</summary>
    Task<ContinuationMapping?> GetByIdAsync(Guid mappingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the most-specific mapping for a given source run and trigger state:
    /// 1. First looks for a mapping whose TriggerRunId == sourceRunId AND TriggerState == state.
    /// 2. Falls back to a global mapping (TriggerRunId == null) for TriggerState == state.
    /// Returns null when neither exists.
    /// </summary>
    Task<ContinuationMapping?> FindBestMatchAsync(
        Guid     sourceRunId,
        RunState triggerState,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new mapping to the store.</summary>
    Task AddAsync(ContinuationMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing mapping as modified for EF Core change tracking.</summary>
    void Update(ContinuationMapping mapping);
}

/// <summary>
/// Repository contract for <see cref="ContinuationRunRecord"/> dedup records (OC-08).
/// All mutating methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IContinuationRunRepository
{
    /// <summary>
    /// Returns the existing Run B record for (sourceRunId, triggerState), or null.
    /// Used for the idempotent dedup guard.
    /// </summary>
    Task<ContinuationRunRecord?> FindAsync(
        Guid     sourceRunId,
        RunState triggerState,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new continuation run record.</summary>
    Task AddAsync(ContinuationRunRecord record, CancellationToken cancellationToken = default);
}
