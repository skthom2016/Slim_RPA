#nullable enable
using Tracto.Orchestrator.Domain.Runs;
namespace Tracto.Orchestrator.Application.Abstractions;
public interface IResolutionRepository
{
    Task AddAsync(ResolutionRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResolutionRecord>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
