#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Domain.Callbacks;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ICallbackRepository"/> (OC-07).
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfCallbackRepository : ICallbackRepository
{
    private readonly OrchestratorDbContext _db;

    public EfCallbackRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<CallbackRecord?> FindByEventIdAsync(
        string eventId,
        CancellationToken ct = default)
        => await _db.CallbackRecords
                    .FirstOrDefaultAsync(c => c.EventId == eventId, ct);

    /// <inheritdoc/>
    public async Task AddAsync(
        CallbackRecord record,
        CancellationToken ct = default)
        => await _db.CallbackRecords.AddAsync(record, ct);
}
