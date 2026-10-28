#nullable enable
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Jobs;

/// <summary>Query: retrieve a run summary by RunId (OC-02).</summary>
public sealed record GetJobQuery(Guid RunId);

/// <summary>Successful result for <see cref="GetJobQueryHandler"/>.</summary>
public sealed record GetJobResult(Guid RunId, string State);

/// <summary>
/// Handles <see cref="GetJobQuery"/> by loading the RunRecord from the repository
/// and projecting it into a <see cref="GetJobResult"/> summary.
/// </summary>
public sealed class GetJobQueryHandler
{
    private readonly IRunRepository _runs;

    public GetJobQueryHandler(IRunRepository runs)
    {
        _runs = runs;
    }

    public async Task<Result<GetJobResult, Error>> HandleAsync(
        GetJobQuery query,
        CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetByIdAsync(query.RunId, cancellationToken);

        if (run is null)
            return Result<GetJobResult, Error>.Fail(
                Error.NotFound($"Run '{query.RunId}' was not found."));

        return Result<GetJobResult, Error>.Ok(
            new GetJobResult(run.Id, run.State.ToString().ToUpperInvariant()));
    }
}
