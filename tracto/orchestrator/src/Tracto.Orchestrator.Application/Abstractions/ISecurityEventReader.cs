#nullable enable
using Tracto.Orchestrator.Domain.Security;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Read-only query contract for security events used by the telemetry bundle export (OC-14).
/// Separated from <see cref="ISecurityEventLog"/> (write side) to follow CQRS.
/// </summary>
public interface ISecurityEventReader
{
    /// <summary>
    /// Returns all <see cref="SecurityEvent"/> rows associated with the given run,
    /// ordered by <see cref="SecurityEvent.OccurredAt"/> ascending.
    /// Returns an empty list when no events exist.
    /// </summary>
    Task<IReadOnlyList<SecurityEvent>> FindByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
