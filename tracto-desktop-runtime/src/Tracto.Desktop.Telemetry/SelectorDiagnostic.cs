namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Diagnostic snapshot of the selector resolution attempt for a step.
/// All fields are pre-sanitised; no secret values are present.
/// </summary>
public sealed record SelectorDiagnostic
{
    /// <summary>The selector strategy that was applied (e.g. "AutomationId", "Name").</summary>
    public required string Strategy { get; init; }

    /// <summary>The search scope (e.g. "Desktop", "Window", "Subtree").</summary>
    public required string Scope { get; init; }

    /// <summary>Number of candidate elements found before further filtering.</summary>
    public required int CandidateCount { get; init; }

    /// <summary>
    /// The window title after sanitisation. Null when the title was omitted because
    /// it could not be safely sanitised (e.g. a secret was present and stripping would
    /// have revealed partial information).
    /// </summary>
    public string? SanitizedWindowTitle { get; init; }
}
