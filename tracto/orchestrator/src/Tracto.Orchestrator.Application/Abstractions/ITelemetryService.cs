#nullable enable
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Application-layer contract for emitting telemetry events.
///
/// Every state transition in the orchestrator emits a <see cref="TelemetryEvent"/>
/// via this interface. Implementations persist the event atomically in the same
/// EF Core transaction as the business change (domain invariant 5).
///
/// No plaintext secrets are ever passed to this interface — callers must replace
/// any secret value with the literal string "REDACTED" before constructing the event.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Persists <paramref name="telemetryEvent"/> to the telemetry store.
    /// Implementations must write the row inside the current ambient transaction
    /// (UnitOfWork) so the emit and the state mutation commit together.
    /// </summary>
    Task EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}
