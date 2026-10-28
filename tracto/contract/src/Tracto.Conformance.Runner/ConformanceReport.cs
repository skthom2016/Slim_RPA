#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Tracto.Conformance.Runner;

/// <summary>Aggregate result of a conformance runner execution covering all scenarios.</summary>
public sealed record ConformanceReport(
    string ContractVersion,
    string FixtureVersion,
    DateTimeOffset RunAt,
    IReadOnlyList<FixtureResult> Results)
{
    public bool AllPassed => Results.All(r => r.Passed);
    public int  PassCount => Results.Count(r => r.Passed);
    public int  FailCount => Results.Count(r => !r.Passed);
}
