namespace TractoRuntime.Browser.Mode;

/// <summary>
/// Manages exclusive foreground-input ownership for headed browser and desktop workers.
/// Only one worker (Browser or Desktop) may hold foreground-input ownership for a given
/// Windows session at a time.
/// BRW-US-002 AC#2, AC#3 — acquisition must be atomic; a second attempt for the same
/// session is rejected.
/// AC#5 — in isolated-host deployments the host-level supervisor also enforces this rule
/// cross-process; this interface handles in-process enforcement.
/// </summary>
public interface IForegroundInputOwner : IAsyncDisposable
{
    /// <summary>Returns the runId of the current foreground-input owner for the session,
    /// or null when the session has no owner.</summary>
    string? GetCurrentOwner(string sessionId);

    /// <summary>
    /// Attempts to acquire exclusive foreground-input ownership for <paramref name="sessionId"/>
    /// on behalf of <paramref name="runId"/>. Non-blocking: returns immediately with
    /// <see cref="ForegroundOwnershipResult.Acquired"/> false if the session is already owned.
    /// </summary>
    Task<ForegroundOwnershipResult> TryAcquireAsync(string runId, string sessionId, CancellationToken ct);

    /// <summary>
    /// Releases foreground-input ownership previously acquired by <paramref name="runId"/>
    /// for <paramref name="sessionId"/>. No-op if the run does not own the session.
    /// </summary>
    Task ReleaseAsync(string runId, string sessionId, CancellationToken ct);
}
