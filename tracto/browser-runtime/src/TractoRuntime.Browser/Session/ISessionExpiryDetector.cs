using Microsoft.Playwright;

namespace TractoRuntime.Browser.Session;

/// <summary>
/// Detects whether an active browser page shows signs of session expiry (BRW-US-014 AC#6, WEB-009).
///
/// <para><b>AC#6 obligation:</b> When a session expires mid-run, the runtime must stop the run
/// with <c>needsAttention=true</c> or a classified FAILED outcome.  No silent re-authentication
/// is attempted.  The caller is responsible for reporting the resulting
/// <see cref="SessionExpiryException"/> as a FAILED or NEEDS_ATTENTION terminal state.</para>
///
/// <para>Detection strategies (in priority order):</para>
/// <list type="number">
///   <item>URL-path pattern matching — fast O(n) check against known login/expired redirect paths.</item>
///   <item>Page-text matching — <c>&lt;title&gt;</c> and visible body text scanned for known
///         expiry strings (e.g. "session expired", "please log in again").</item>
/// </list>
///
/// <para>The opaque <c>sessionRef</c> parameter is used only for logging — the raw token value
/// is never passed here or logged.</para>
/// </summary>
public interface ISessionExpiryDetector
{
    /// <summary>
    /// Examines the current page URL and visible text for well-known session-expiry signals.
    /// </summary>
    /// <param name="page">The active Playwright page to inspect.</param>
    /// <param name="sessionRef">
    /// Opaque reference to the active session (for log correlation only; never the raw value).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the page shows a session-expired indicator;
    /// <see langword="false"/> when the session appears to be active.
    /// </returns>
    Task<bool> IsExpiredAsync(IPage page, string? sessionRef, CancellationToken ct);

    /// <summary>
    /// Throws <see cref="SessionExpiryException"/> when the session is detected as expired.
    /// No-op when the session appears active.
    ///
    /// <para>The thrown exception carries <c>NeedsAttention=true</c> to signal that the
    /// operator must provision a fresh credential before the run is retried.</para>
    /// </summary>
    /// <exception cref="SessionExpiryException">
    /// Thrown when <see cref="IsExpiredAsync"/> returns <see langword="true"/>.
    /// </exception>
    Task ThrowIfExpiredAsync(IPage page, string? sessionRef, CancellationToken ct);
}
