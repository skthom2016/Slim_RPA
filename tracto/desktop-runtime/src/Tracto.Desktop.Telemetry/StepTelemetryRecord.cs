namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Immutable record of telemetry data for a single step execution.
/// No field may contain secret values.
/// </summary>
public sealed record StepTelemetryRecord
{
    /// <summary>Identifier of the step (from the script definition).</summary>
    public required string StepId { get; init; }

    /// <summary>The action type executed (e.g. "Click", "TypeText", "GetValue").</summary>
    public required string ActionType { get; init; }

    /// <summary>The selector strategy used (e.g. "AutomationId", "Name", "ClassName").</summary>
    public required string SelectorStrategy { get; init; }

    /// <summary>Elapsed milliseconds from step start to completion (or failure).</summary>
    public required long DurationMs { get; init; }

    /// <summary>High-level outcome: "Succeeded", "Failed", "Unknown".</summary>
    public required string ResultCategory { get; init; }

    /// <summary>CEC v1.0.1 errorCategory code when the step failed; null on success or unknown.</summary>
    public string? ErrorCategory { get; init; }

    /// <summary>Implementation-level detail code (maps to Spec Appendix A); null when not applicable.</summary>
    public string? DetailCode { get; init; }

    /// <summary>CEC-approved unknownReason code when ResultCategory is "Unknown"; null otherwise.</summary>
    public string? UnknownReason { get; init; }

    /// <summary>Optional reference to captured and possibly masked screenshot evidence.</summary>
    public EvidenceRef? EvidenceRef { get; init; }
}
