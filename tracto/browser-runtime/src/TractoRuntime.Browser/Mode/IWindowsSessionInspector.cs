namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Detects Windows interactive session availability for headed browser execution.
/// BRW-002 — headed execution requires an authorized, active Windows interactive session.
/// Headless workers must not require a session and run without a desktop logon (BRW-US-002 AC#1).
/// </summary>
public interface IWindowsSessionInspector
{
    /// <summary>
    /// True when the runtime host has an interactive Windows session available.
    /// Headless workers ignore this; headed workers require it.
    /// </summary>
    bool HasInteractiveSession { get; }

    /// <summary>
    /// Returns the Windows interactive session ID string, or null when no active session
    /// is available. Called during mode validation (BRW-US-002 AC#2) and capability
    /// advertisement construction (BRW-US-002 AC#4, BRW-006).
    /// </summary>
    Task<string?> GetActiveInteractiveSessionIdAsync(CancellationToken ct);
}
