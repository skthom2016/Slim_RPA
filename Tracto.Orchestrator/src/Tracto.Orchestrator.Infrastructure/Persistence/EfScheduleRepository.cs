#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IScheduleRepository"/>.
/// All mutations participate in the ambient UnitOfWork transaction.
/// </summary>
public sealed class EfScheduleRepository : IScheduleRepository
{
    private readonly OrchestratorDbContext _db;

    public EfScheduleRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<ScheduleRecord?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
        => await _db.ScheduleRecords.FindAsync([scheduleId], cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRecord>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
        => await _db.ScheduleRecords
                    .Where(s => s.Enabled)
                    .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ScheduleRecord schedule, CancellationToken cancellationToken = default)
        => await _db.ScheduleRecords.AddAsync(schedule, cancellationToken);

    /// <inheritdoc />
    public void Update(ScheduleRecord schedule)
        => _db.ScheduleRecords.Update(schedule);
}
