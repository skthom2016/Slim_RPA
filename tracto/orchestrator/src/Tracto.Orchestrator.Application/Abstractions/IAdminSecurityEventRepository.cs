#nullable enable
using Tracto.Orchestrator.Domain.Security;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Read-only contract for querying <see cref="SecurityEvent"/> records for admin telemetry export (OC-13).
/// Separated from the append-only <see cref="ISecurityEventLog"/>.
/// </summary>
public interface IAdminSecurityEventRepository
{
    /// <summary>
    /// Returns all <see cref="SecurityEvent"/> records associated with the given run.
    /// Events are associated via Details text search (runId string embedded in Details JSON).
    /// Ordered by OccurredAt ascending.
    /// </summary>
    Task<IReadOnlyList<SecurityEvent>> FindByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}