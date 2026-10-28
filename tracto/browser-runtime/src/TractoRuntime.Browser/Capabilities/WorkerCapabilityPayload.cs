namespace TractoRuntime.Browser.Capabilities;

/// <summary>
/// Capability advertisement payload sent to the Orchestrator before the first step.
/// Contains all metadata required by BRW-US-002 AC#4 and BRW-006:
/// mode, interactiveSessionId (headed only), browser engine/version, and capability digest.
/// The capability digest is an integrity hint, not an authentication token.
/// </summary>
public sealed record WorkerCapabilityPayload
{
    public required string WorkerId { get; init; }

    /// <summary>Always "browser" for this runtime.</summary>
    public required string RuntimeType { get; init; }

    /// <summary>"headless" or "headed" — BRW-US-002 AC#4.</summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Windows interactive session ID, present only when Mode is "headed".
    /// BRW-US-002 AC#4 — reported to the Orchestrator for session coordination.
    /// </summary>
    public string? InteractiveSessionId { get; init; }

    /// <summary>Playwright browser engine name (e.g., "chromium").</summary>
    public string? BrowserEngine { get; init; }

    /// <summary>Playwright package version (e.g., "1.48.0").</summary>
    public string? BrowserVersion { get; init; }

    /// <summary>Sorted list of capability tokens this worker advertises.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// Integrity hint over the capability tokens, engine, and version.
    /// Not an authentication credential. BRW-006 capabilityDigest.
    /// </summary>
    public string? CapabilityDigest { get; init; }
}
