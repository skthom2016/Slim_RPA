#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tracto.Contract.Orchestrator;
using Tracto.Mock.Orchestrator;

namespace Tracto.Conformance.Runner;

/// <summary>
/// Executes a list of <see cref="ConformanceScenario"/> instances and returns a
/// <see cref="ConformanceReport"/> summarising the outcome.
/// </summary>
public sealed class ConformanceRunner
{
    private readonly TimeProvider _clock;

    public ConformanceRunner(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ConformanceReport> RunAsync(
        IReadOnlyList<ConformanceScenario> scenarios,
        IOrchestratorServer? server = null,
        CancellationToken ct = default)
    {
        var results = new List<FixtureResult>();
        foreach (var scenario in scenarios)
        {
            var sut = server ?? new MockOrchestrator(clock: _clock);
            results.Add(await scenario.ExecuteAsync(sut, ct));
        }

        return new ConformanceReport(
            ContractVersion: "1.0.1",
            FixtureVersion:  "1.0.0",
            RunAt:           _clock.GetUtcNow(),
            Results:         results);
    }
}
