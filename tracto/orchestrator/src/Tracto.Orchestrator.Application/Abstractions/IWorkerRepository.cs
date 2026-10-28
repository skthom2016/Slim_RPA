using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="WorkerRecord"/> aggregates.
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IWorkerRepository
{
    /// <summary>Retrieves a worker by its stable WorkerId. Returns null when not found.</summary>
    Task<WorkerRecord?> GetByIdAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns workers where IsOnline == false.
    /// Used by RestartRecoveryService to identify absent workers at startup.
    /// </summary>
    Task<IReadOnlyList<WorkerRecord>> FindOfflineAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all registered workers.</summary>
    Task<IReadOnlyList<WorkerRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new worker registration. Throws when the WorkerId already exists.</summary>
    Task AddAsync(WorkerRecord worker, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing worker as modified for EF Core change tracking.</summary>
    void Update(WorkerRecord worker);
}
