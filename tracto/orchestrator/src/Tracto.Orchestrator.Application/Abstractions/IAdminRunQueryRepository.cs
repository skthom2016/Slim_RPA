#nullable enable
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Read-only interface for direct RunRecord lookups by admin handlers (OC-13).
/// DTO records (AdminJobSummary, AdminJobPage, AdminJobDetail, etc.) are in IAdminJobRepository.cs.
/// </summary>
public interface IAdminRunQueryRepository
{
    Task<RunRecord?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunRecord>> FindByStateAsync(RunState state, CancellationToken cancellationToken = default);
}