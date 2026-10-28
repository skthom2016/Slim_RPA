#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Orchestrator;

namespace Tracto.Conformance.Runner;

/// <summary>
/// Wraps a named test function. The runner calls it, catches any exception (fail),
/// or passes when no exception is thrown.
/// </summary>
public sealed class ConformanceScenario
{
    public string FixtureId { get; }
    public string Title     { get; }
    public IReadOnlyList<string> AcIds { get; }

    private readonly Func<IOrchestratorServer, Task> _run;

    public ConformanceScenario(
        string fixtureId,
        string title,
        IReadOnlyList<string> acIds,
        Func<IOrchestratorServer, Task> run)
    {
        FixtureId = fixtureId;
        Title     = title;
        AcIds     = acIds;
        _run      = run;
    }

    public async Task<FixtureResult> ExecuteAsync(IOrchestratorServer server, CancellationToken ct = default)
    {
        try
        {
            await _run(server);
            return new FixtureResult(FixtureId, Title, Passed: true, AcIds: AcIds);
        }
        catch (Exception ex)
        {
            return new FixtureResult(FixtureId, Title, Passed: false,
                FailureReason: ex.Message, AcIds: AcIds);
        }
    }
}
