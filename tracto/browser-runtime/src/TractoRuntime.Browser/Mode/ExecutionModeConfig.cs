using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Resolved execution mode configuration for a browser worker run.
/// Populated during mode selection (BRW-002) and used for foreground-input
/// ownership acquisition and capability advertisement (BRW-006).
/// BRW-US-002 AC#1–AC#4.
/// </summary>
public sealed record ExecutionModeConfig
{
    public required BrowserMode Mode { get; init; }

    /// <summary>
    /// Windows interactive session ID, populated only for headed mode (AC#4).
    /// Reported to the Orchestrator in the capability advertisement.
    /// Null for headless workers — no desktop session is required (AC#1).
    /// </summary>
    public string? InteractiveSessionId { get; init; }

    /// <summary>True when an active Windows interactive session is required.</summary>
    public bool RequiresActiveSession => Mode is BrowserMode.Headed;
}
