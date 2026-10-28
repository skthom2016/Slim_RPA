using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TractoRuntime.Browser.Session;

/// <summary>
/// Production implementation of <see cref="ISessionExpiryDetector"/>.
///
/// Satisfies BRW-US-014 AC#6, WEB-009 and BRE-006:
/// <list type="bullet">
///   <item>Checks for well-known session-expiry URL path patterns (login, sign-in,
///         session-expired) — cheap O(n) scan with no network round-trip.</item>
///   <item>Checks for well-known page-text patterns in <c>&lt;title&gt;</c> and the
///         visible <c>&lt;body&gt;</c> text — guards against sites that return HTTP 200
///         with an expiry message rather than a redirect.</item>
///   <item>When expiry is detected, raises <see cref="SessionExpiryException"/> with
///         <c>NeedsAttention=true</c> so the run stops without silent re-authentication.</item>
///   <item>The opaque <paramref name="sessionRef"/> parameter (in each method) is used for
///         log correlation only — the raw token value is never logged or accessed here.</item>
///   <item>URL query strings and fragments are stripped before logging to avoid leaking any
///         session tokens that sites may embed in redirect parameters.</item>
/// </list>
/// </summary>
public sealed class SessionExpiryDetector(ILogger<SessionExpiryDetector> logger)
    : ISessionExpiryDetector
{
    // URL absolute-path fragments that indicate a session-expired redirect.
    // Matched case-insensitively against the path component of the current URL.
    private static readonly string[] ExpiryUrlFragments =
    [
        "/login",
        "/signin",
        "/sign-in",
        "/logon",
        "/log-in",
        "/auth/login",
        "/auth/signin",
        "/account/login",
        "/session-expired",
        "/session/expired",
        "/sso/login",
        "/oauth/authorize",
        "/saml/login",
        "/oidc/login"
    ];

    // Visible page-text patterns (matched case-insensitively) that indicate expiry.
    // Checked against both <title> and visible <body> text.
    private static readonly string[] ExpiryTextPatterns =
    [
        "session expired",
        "session has expired",
        "your session has expired",
        "session timed out",
        "your session timed out",
        "please log in again",
        "please sign in again",
        "you have been logged out",
        "you have been signed out",
        "you were logged out",
        "login required",
        "sign in required",
        "authentication required",
        "token expired",
        "access token expired",
        "token has expired",
        "credential expired",
        "please re-authenticate"
    ];

    // Short timeout for the body-text locator probe (we are detecting, not waiting for load)
    private const float TextProbeTimeoutMs = 3_000f;

    // -------------------------------------------------------------------------
    // ISessionExpiryDetector
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<bool> IsExpiredAsync(IPage page, string? sessionRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Gate 1: URL path — fast, no I/O required
        if (IsExpiryUrl(page.Url))
        {
            logger.LogWarning(
                "Session expiry detected via URL pattern for ref={SessionRef}: " +
                "url={SanitizedUrl}. Run will stop with needsAttention=true (AC#6 / WEB-009).",
                sessionRef, SanitizeUrl(page.Url));
            return true;
        }

        // Gate 2: Page title — lightweight string match against the <title> element
        try
        {
            string title = await page.TitleAsync().ConfigureAwait(false);
            if (ContainsAnyPattern(title))
            {
                logger.LogWarning(
                    "Session expiry detected via page title for ref={SessionRef}: " +
                    "url={SanitizedUrl}. Run will stop with needsAttention=true (AC#6 / WEB-009).",
                    sessionRef, SanitizeUrl(page.Url));
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(
                "Could not read page title during expiry check (ref={SessionRef}): {Msg}. " +
                "Continuing to body-text check.",
                sessionRef, ex.Message);
        }

        // Gate 3: Body text — more expensive but catches non-redirect expiry pages
        if (await ContainsExpiryBodyTextAsync(page, sessionRef, ct).ConfigureAwait(false))
        {
            logger.LogWarning(
                "Session expiry detected via body text for ref={SessionRef}: " +
                "url={SanitizedUrl}. Run will stop with needsAttention=true (AC#6 / WEB-009).",
                sessionRef, SanitizeUrl(page.Url));
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task ThrowIfExpiredAsync(IPage page, string? sessionRef, CancellationToken ct)
    {
        bool expired = await IsExpiredAsync(page, sessionRef, ct).ConfigureAwait(false);

        if (expired)
        {
            // AC#6: No silent re-authentication — raise SessionExpiryException with
            // NeedsAttention=true so the calling worker can stop the run and signal the
            // operator to provision a fresh authenticated session.
            throw new SessionExpiryException(
                message:
                    "The browser session has expired mid-run. " +
                    "No automatic re-authentication will be attempted. " +
                    "Operator attention is required to provision a fresh authenticated session " +
                    "(BRW-US-014 AC#6 / WEB-009).",
                sessionRef: sessionRef,
                needsAttention: true);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsExpiryUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        string pathLower = uri.AbsolutePath.ToLowerInvariant();
        return ExpiryUrlFragments.Any(frag =>
            pathLower.StartsWith(frag, StringComparison.Ordinal)
            || pathLower.Contains(frag, StringComparison.Ordinal));
    }

    private async Task<bool> ContainsExpiryBodyTextAsync(
        IPage page,
        string? sessionRef,
        CancellationToken ct)
    {
        try
        {
            // Use InnerTextAsync on <body> — relatively cheap (no rendering pipeline wait)
            string bodyText = await page
                .Locator("body")
                .InnerTextAsync(new LocatorInnerTextOptions
                {
                    Timeout = TextProbeTimeoutMs
                })
                .ConfigureAwait(false);

            return ContainsAnyPattern(bodyText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cannot read body text — cannot confirm expiry via this gate
            logger.LogDebug(
                "Could not read body text during expiry check (ref={SessionRef}): {Msg}.",
                sessionRef, ex.Message);
            return false;
        }
    }

    private static bool ContainsAnyPattern(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ExpiryTextPatterns.Any(p =>
            text.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the URL with the query string and fragment stripped to avoid leaking session
    /// tokens that some sites embed in redirect URL parameters.
    /// </summary>
    private static string SanitizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "[empty-url]";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "[invalid-url]";
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }
}
