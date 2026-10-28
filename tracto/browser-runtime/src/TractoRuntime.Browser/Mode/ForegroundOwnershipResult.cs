namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Result of attempting to acquire exclusive foreground-input ownership for a session.
/// BRW-US-002 AC#2, AC#3 — one headed worker (Browser or Desktop) may hold
/// foreground-input ownership per Windows session at any time.
/// </summary>
public sealed record ForegroundOwnershipResult
{
    /// <summary>True when the caller successfully acquired foreground-input ownership.</summary>
    public required bool Acquired { get; init; }

    /// <summary>
    /// RunId of the existing owner when <see cref="Acquired"/> is false.
    /// Used for diagnostics; not surfaced to untrusted output.
    /// </summary>
    public string? ConflictingRunId { get; init; }

    /// <summary>Session ID for which ownership was acquired or contested.</summary>
    public string? SessionId { get; init; }
}
