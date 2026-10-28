#nullable enable
namespace Tracto.Orchestrator.Domain.Telemetry;

/// <summary>
/// Repository contract for <see cref="TelemetryEvent"/> persistence.
///
/// The concrete implementation lives in Tracto.Orchestrator.Infrastructure.
/// Application command handlers depend on this abstraction via DI; they never
/// reference EF Core or any other persistence framework directly.
///
/// All writes must participate in the ambient Unit-of-Work transaction so that
/// telemetry events and the triggering state change are committed atomically
/// (domain invariant 5).
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>
    /// Persists a new <see cref="TelemetryEvent"/> in the current Unit-of-Work.
    /// The event is not durably committed until <c>IUnitOfWork.SaveChangesAsync</c> completes.
    /// </summary>
    /// <param name="telemetryEvent">The event to add. Must not be null.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task AddAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all telemetry events for a given run, ordered by <see cref="TelemetryEvent.OccurredAt"/> ascending.
    /// Used by the export endpoint (OC-14) and by unit tests that verify event emission.
    /// </summary>
    /// <param name="runId">The run whose events are retrieved. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task<IReadOnlyList<TelemetryEvent>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
