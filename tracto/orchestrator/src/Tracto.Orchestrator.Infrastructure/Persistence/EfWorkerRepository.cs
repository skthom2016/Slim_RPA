using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Domain.Workers;

// Explicit alias to avoid CS0104 when Domain.Workers also declares IWorkerRepository.
using IWorkerRepository = Tracto.Orchestrator.Application.Abstractions.IWorkerRepository;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="Tracto.Orchestrator.Application.Abstractions.IWorkerRepository"/>.
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfWorkerRepository : IWorkerRepository
{
    private readonly OrchestratorDbContext _db;

    public EfWorkerRepository(OrchestratorDbContext db) => _db = db;

    public async Task<WorkerRecord?> GetByIdAsync(string workerId, CancellationToken cancellationToken = default)
        => await _db.WorkerRecords.FindAsync([workerId], cancellationToken);

    public async Task<IReadOnlyList<WorkerRecord>> FindOfflineAsync(CancellationToken cancellationToken = default)
        => await _db.WorkerRecords.Where(w => !w.IsOnline).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkerRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.WorkerRecords.ToListAsync(cancellationToken);

    public async Task AddAsync(WorkerRecord worker, CancellationToken cancellationToken = default)
        => await _db.WorkerRecords.AddAsync(worker, cancellationToken);

    public void Update(WorkerRecord worker)
        => _db.WorkerRecords.Update(worker);
}
