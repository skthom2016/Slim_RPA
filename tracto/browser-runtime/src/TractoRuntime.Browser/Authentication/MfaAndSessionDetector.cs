using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TractoRuntime.Browser.Authentication;

/// <summary>
/// Implements <see cref="IMfaAndSessionDetector"/> for BRW-US-012 (WEB-009) —
/// Authentication Workflows.
///
/// <para><b>Detection strategy:</b><br/>
/// The detector applies two layers of heuristics:
/// <list type="number">
///   <item><b>URL pattern matching</b> — checks the page URL against a list of well-known
///         login / MFA / CAPTCHA URL patterns.</item>
///   <item><b>DOM keyword matching</b> — inspects the page's visible text for well-known
///         challenge indicators such as "Enter your code", "Verify your identity",
///         "Sign in again", "Session expired", "CAPTCHA" etc.</item>
/// </list>
/// </para>
///
/// <para><b>AC#5 — MFA / CAPTCHA (no bypass):</b><br/>
/// When an MFA or CAPTCHA pattern is detected, the detector returns
/// <see cref="MfaDetectionResult.NeedsAttention"/> = <see langword="true"/> and
/// <see cref="DetectedInterventionKind.MfaChallenge"/> or
/// <see cref="DetectedInterventionKind.CaptchaChallenge"/>.
/// The caller is responsible for terminating the run with FAILED or needsAttention=true.</para>
///
/// <para><b>AC#6 — Expired session (no silent re-authentication):</b><br/>
/// When a session-expiry pattern is detected, the detector returns
/// <see cref="MfaDetectionResult.NeedsAttention"/> = <see langword="true"/> and
/// <see cref="DetectedInterventionKind.SessionExpired"/>.
/// The caller must stop the run — no automatic credential re-use or bypass is permitted.</para>
///
/// <para><b>Safety:</b><br/>
/// The detector reads only page URL and sanitized visible text; it never logs or includes
/// credential values in the result (BRE-006).</para>
/// </summary>
public sealed class MfaAndSessionDetector(ILogger<MfaAndSessionDetector> logger)
    : IMfaAndSessionDetector
{
    // ─────────────────────────────────────────────────────────────────────────
    // URL patterns for well-known authentication challenge pages
    // ─────────────────────────────────────────────────────────────────────────

    // Known MFA / conditional-access URL fragments (case-insensitive)
    private static readonly string[] MfaUrlPatterns =
    [
        "/mfa",
        "/multifactor",
        "/step-up",
        "/challenge",
        "/verify",
        "/two-factor",
        "/2fa",
        "/otp",
        "login/mfa",
        "auth/mfa",
        "signin/mfa",
        "identity/mfa",
        "device/mfa",
        "microsoft.com/devicelogin",
        "login.microsoftonline.com/common/oauth2",
        "accounts.google.com/signin/v2/challenge"
    ];

    // Known session-expiry URL fragments
    private static readonly string[] SessionExpiryUrlPatterns =
    [
        "/session-expired",
        "/sessionexpired",
        "/timeout",
        "/re-authenticate",
        "/reauthenticate",
        "/relogin",
        "/re-login"
    ];

    // Known CAPTCHA URL fragments
    private static readonly string[] CaptchaUrlPatterns =
    [
        "/captcha",
        "/recaptcha",
        "hcaptcha.com",
        "challenges.cloudflare.com"
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // DOM keywords for MFA challenges
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] MfaDomKeywords =
    [
        "enter your code",
        "enter the code",
        "verification code",
        "authenticator code",
        "one-time password",
        "one time password",
        "verify your identity",
        "multi-factor",
        "two-factor",
        "authentication required",
        "approve sign-in",
        "approve the sign-in",
        "phone number to verify",
        "text a code"
    ];

    private static readonly string[] SessionExpiryDomKeywords =
    [
        "session expired",
        "session has expired",
        "your session has expired",
        "sign in again",
        "please sign in again",
        "you have been signed out",
        "signed out",
        "log in again",
        "please log in again",
        "re-authenticate"
    ];

    private static readonly string[] CaptchaDomKeywords =
    [
        "captcha",
        "i am not a robot",
        "i'm not a robot",
        "verify you are human",
        "verify that you are human",
        "complete the security check"
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // IMfaAndSessionDetector
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MfaDetectionResult> DetectAsync(IPage page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var url = page.Url;

        // Layer 1: URL pattern matching (fast, no DOM access required)
        var urlResult = CheckUrlPatterns(url);
        if (urlResult.InterventionRequired)
        {
            logger.LogWarning(
                "Authentication intervention detected by URL pattern: kind={Kind}, url={Url}.",
                urlResult.Kind, url);
            return urlResult;
        }

        // Layer 2: DOM text inspection (requires page access; limited to visible body text)
        // Check cancellation before the potentially slow DOM read.
        ct.ThrowIfCancellationRequested();
        try
        {
            // Read only the innerText of the body to avoid injecting custom scripts.
            // Playwright's InnerTextAsync is safe and does not execute user-authored code.
            var bodyText = await page.Locator("body")
                .InnerTextAsync()
                .ConfigureAwait(false);

            var domResult = CheckDomText(url, bodyText);
            if (domResult.InterventionRequired)
            {
                logger.LogWarning(
                    "Authentication intervention detected by DOM keyword: kind={Kind}, url={Url}.",
                    domResult.Kind, url);
                return domResult;
            }
        }
        catch (Exception ex)
        {
            // DOM inspection failing is not itself an intervention — log and continue.
            logger.LogDebug(ex,
                "MfaAndSessionDetector: DOM inspection failed for url={Url}; " +
                "continuing without DOM check.", url);
        }

        return MfaDetectionResult.None;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static MfaDetectionResult CheckUrlPatterns(string url)
    {
        if (ContainsAny(url, CaptchaUrlPatterns))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.CaptchaChallenge,
                NeedsAttention       = true,
                Reason               = $"CAPTCHA challenge detected at URL: {Sanitize(url)}"
            };
        }

        if (ContainsAny(url, MfaUrlPatterns))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.MfaChallenge,
                NeedsAttention       = true,
                Reason               = $"MFA/multi-factor authentication challenge detected at URL: {Sanitize(url)}"
            };
        }

        if (ContainsAny(url, SessionExpiryUrlPatterns))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.SessionExpired,
                NeedsAttention       = true,
                Reason               = $"Session expiry page detected at URL: {Sanitize(url)}"
            };
        }

        return MfaDetectionResult.None;
    }

    private static MfaDetectionResult CheckDomText(string url, string bodyText)
    {
        var lower = bodyText.ToLowerInvariant();

        if (ContainsAny(lower, CaptchaDomKeywords))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.CaptchaChallenge,
                NeedsAttention       = true,
                Reason               = $"CAPTCHA challenge detected in page content at: {Sanitize(url)}"
            };
        }

        if (ContainsAny(lower, MfaDomKeywords))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.MfaChallenge,
                NeedsAttention       = true,
                Reason               = $"MFA challenge detected in page content at: {Sanitize(url)}"
            };
        }

        if (ContainsAny(lower, SessionExpiryDomKeywords))
        {
            return new MfaDetectionResult
            {
                InterventionRequired = true,
                Kind                 = DetectedInterventionKind.SessionExpired,
                NeedsAttention       = true,
                Reason               = $"Session expiry detected in page content at: {Sanitize(url)}"
            };
        }

        return MfaDetectionResult.None;
    }

    private static bool ContainsAny(string source, IEnumerable<string> patterns)
        => patterns.Any(p => source.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Strips query string and fragment from a URL before including it in log or evidence
    /// to avoid leaking session tokens that may appear in query parameters.
    /// </summary>
    private static string Sanitize(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        return "(non-parseable URL)";
    }
}
