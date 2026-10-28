#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using DomainTelemetry = Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Infrastructure.Telemetry;

/// <summary>
/// EF Core implementation of <see cref="ITelemetryService"/> (OC-14).
///
/// Delegates persistence to <see cref="DomainTelemetry.ITelemetryRepository.AddAsync"/> which tracks
/// the event against the ambient OrchestratorDbContext (shared with UnitOfWork).
/// The event row is committed atomically in the same transaction as the state mutation
/// that triggered it (domain invariant 5).
///
/// Secret-safety contract: this class never inspects TelemetryEvent.Metadata.
/// Callers are responsible for replacing any secret value with "REDACTED" before
/// constructing the entity.
/// </summary>
public sealed class EfTelemetryService : ITelemetryService
{
    private readonly DomainTelemetry.ITelemetryRepository _repository;

    public EfTelemetryService(DomainTelemetry.ITelemetryRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public Task EmitAsync(DomainTelemetry.TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
        => _repository.AddAsync(telemetryEvent, cancellationToken);
}