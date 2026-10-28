namespace TractoRuntime.Browser.Conformance;

/// <summary>
/// Result record for a single CEC v1.0.1 conformance fixture (CF-01 … CF-10).
/// BRW-US-020 AC#1 / CEC-007, CEC-010.
/// </summary>
public sealed record ConformanceFixtureResult
{
    /// <summary>Fixture identifier, e.g. "CF-01".</summary>
    public required string FixtureId { get; init; }

    /// <summary>Human-readable description of what the fixture verifies.</summary>
    public required string Description { get; init; }

    /// <summary><c>true</c> when the fixture passed all positive and negative assertions.</summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// When <see cref="Passed"/> is <see langword="false"/>, the structured reason for failure.
    /// <c>null</c> on a passing fixture.
    /// </summary>
    public string? FailureReason { get; init; }
}
