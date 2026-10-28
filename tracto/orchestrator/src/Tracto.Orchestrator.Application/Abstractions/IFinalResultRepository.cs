#nullable enable
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="FinalResultRecord"/> entities.
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IFinalResultRepository
{
    /// <summary>
    /// Returns the existing final-result record for a run, or null when none has been committed.
    /// Used by <see cref="CommitFinalResultCommandHandler"/> to detect idempotent retries and
    /// enforce the "slot released exactly once" invariant.
    /// </summary>
    Task<FinalResultRecord?> FindByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new <see cref="FinalResultRecord"/>. Never updates or deletes.</summary>
    Task AddAsync(FinalResultRecord record, CancellationToken cancellationToken = default);
}
