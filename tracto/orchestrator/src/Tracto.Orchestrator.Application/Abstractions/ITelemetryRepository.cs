#nullable enable
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Application-layer abstraction for <see cref="TelemetryEvent"/> persistence (OC-14).
///
/// This mirrors <see cref="Domain.Telemetry.ITelemetryRepository"/> so that handlers in
/// the Application layer depend on Application.Abstractions rather than on Domain directly.
/// The concrete implementation (<c>EfTelemetryRepository</c>) implements both interfaces.
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>
    /// Persists a new <see cref="TelemetryEvent"/> in the current Unit-of-Work.
    /// Not committed until <c>IUnitOfWork.CommitAsync</c> completes (invariant 5).
    /// </summary>
    Task AddAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all telemetry events for a run, ordered by OccurredAt ascending.
    /// </summary>
    Task<IReadOnlyList<TelemetryEvent>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
