#nullable enable
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="ScheduleRecord"/> aggregates.
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>Returns the schedule with the given id, or null if not found.</summary>
    Task<ScheduleRecord?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Returns all enabled schedules. Used by CronTriggerService.</summary>
    Task<IReadOnlyList<ScheduleRecord>> GetAllEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new schedule record.</summary>
    Task AddAsync(ScheduleRecord schedule, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing schedule as modified for EF Core change tracking.</summary>
    void Update(ScheduleRecord schedule);
}
