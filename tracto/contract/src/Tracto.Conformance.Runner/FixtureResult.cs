#nullable enable

using System.Collections.Generic;

namespace Tracto.Conformance.Runner;

/// <summary>Outcome of running a single conformance fixture scenario.</summary>
public sealed record FixtureResult(
    string FixtureId,
    string Title,
    bool   Passed,
    string? FailureReason = null,
    IReadOnlyList<string>? AcIds = null);
