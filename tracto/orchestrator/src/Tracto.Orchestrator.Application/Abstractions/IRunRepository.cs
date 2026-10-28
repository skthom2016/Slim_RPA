using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="RunRecord"/> aggregates.
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IRunRepository
{
    /// <summary>Retrieves a run by its primary key. Returns null when not found.</summary>
    Task<RunRecord?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns all runs currently in <paramref name="state"/>.</summary>
    Task<IReadOnlyList<RunRecord>> FindByStateAsync(RunState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all runs whose WorkerId matches <paramref name="workerId"/> and whose
    /// state matches <paramref name="state"/>.
    /// Used by RestartRecoveryService to find orphaned RUNNING runs.
    /// </summary>
    Task<IReadOnlyList<RunRecord>> FindByWorkerAndStateAsync(
        string workerId,
        RunState state,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new run. Throws when a run with the same Id already exists.</summary>
    Task AddAsync(RunRecord run, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing run as modified for EF Core change tracking.</summary>
    void Update(RunRecord run);

    /// <summary>
    /// Returns the most recent run with the given EventId whose CreatedAt is after
    /// <paramref name="notBefore"/>, or null when no matching run exists within the window.
    /// Used for the 5-minute EventId deduplication window (OC-02).
    /// </summary>
    Task<RunRecord?> FindByEventIdAsync(
        string eventId,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the run whose <see cref="RunRecord.CorrelationId"/> matches
    /// <paramref name="correlationId"/>, or <c>null</c> when not found.
    /// Used by IngestCallbackCommandHandler (OC-07) to locate the originating
    /// HANDED_OFF run for continuation wiring.
    /// </summary>
    Task<RunRecord?> FindByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);
}
