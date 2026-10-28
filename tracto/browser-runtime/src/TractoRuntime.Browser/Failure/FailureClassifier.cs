using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Network;

namespace TractoRuntime.Browser.Failure;

/// <summary>
/// Maps Playwright and .NET exceptions to CEC v1.0.1 <see cref="CecError"/> values.
///
/// <para><b>Classification rules (BRW-US-009, BRE-001, BRE-003, BRW-US-015):</b></para>
/// <list type="bullet">
///   <item><see cref="NetworkTransportException"/> → <c>TRANSPORT / {DetailCode}</c>
///         (BRE-003 / BRW-US-015 AC#1).</item>
///   <item><see cref="PlaywrightException"/> whose message matches a Chromium network error
///         code (<c>net::ERR_NAME_NOT_RESOLVED</c>, <c>net::ERR_CERT_*</c>, etc.) →
///         <c>TRANSPORT / browser.*</c> (BRE-003 / BRW-US-015 AC#1).</item>
///   <item><see cref="Microsoft.Playwright.TimeoutException"/> with a navigation method prefix
///         (<c>page.goto</c>, <c>page.waitForNavigation</c>, <c>page.waitForURL</c>) →
///         <c>TIMEOUT / browser.navigationTimeout</c> (DOM-005).</item>
///   <item><see cref="Microsoft.Playwright.TimeoutException"/> without a navigation prefix →
///         <c>TIMEOUT / browser.actionabilityTimeout</c> (DOM-007, BRE-001).</item>
///   <item><see cref="PlaywrightException"/> whose message indicates a strict-mode violation →
///         <c>TARGET_AMBIGUOUS / browser.multipleLocatorMatches</c>.</item>
///   <item><see cref="PlaywrightException"/> whose message indicates no element found →
///         <c>TARGET_NOT_FOUND / browser.locatorNotFound</c>.</item>
///   <item><see cref="OperationCanceledException"/> → <c>CANCELLED</c>.</item>
///   <item>All other exceptions → <c>INTERNAL</c>.</item>
/// </list>
///
/// <para>The <paramref name="stateKnown"/> flag in <see cref="Classify"/> is logged so
/// callers can decide between the FAILED and UNKNOWN run states — the flag does not alter
/// the returned <see cref="CecError"/> itself.  When <paramref name="stateKnown"/> is
/// <see langword="false"/> and the returned <see cref="CecError"/> category is
/// <see cref="ErrorCategory.TRANSPORT"/>, the caller must treat the attempt as UNKNOWN
/// if the interrupted action was non-idempotent (BRW-US-015 AC#2).</para>
/// </summary>
public sealed class FailureClassifier(ILogger<FailureClassifier> logger) : IFailureClassifier
{
    // Playwright prefixes emitted in TimeoutException messages for navigation-scoped timeouts
    private static readonly string[] NavigationPrefixes =
    [
        "page.goto:",
        "page.waitForNavigation:",
        "page.waitForURL:",
        "frame.goto:"
    ];

    // Playwright message tokens that signal a strict-mode (multiple-match) violation
    private static readonly string[] AmbiguousTokens =
    [
        "strict mode violation",
        "resolved to",        // "locator(...) resolved to 3 elements"
        "multiple elements"
    ];

    // Playwright message tokens that signal an element-not-found condition
    private static readonly string[] NotFoundTokens =
    [
        "no element",
        "element is not attached",
        "element handle is not attached"
    ];

    // ── BRE-003 / BRW-US-015 AC#1: Transport failure token tables ──────────────

    /// <summary>
    /// Chromium net-error codes that indicate a DNS resolution failure.
    /// Mapped to <c>TRANSPORT / browser.dnsFailure</c>.
    /// </summary>
    private static readonly string[] DnsFailureTokens =
    [
        "net::ERR_NAME_NOT_RESOLVED",
        "net::ERR_DNS_"
    ];

    /// <summary>
    /// Chromium net-error codes that indicate a TLS or certificate error.
    /// Mapped to <c>TRANSPORT / browser.tlsCertificateError</c>.
    /// </summary>
    private static readonly string[] TlsErrorTokens =
    [
        "net::ERR_CERT_",
        "net::ERR_SSL_",
        "ERR_CERT_AUTHORITY_INVALID",
        "ERR_CERT_DATE_INVALID",
        "ERR_CERT_COMMON_NAME_INVALID",
        "certificate"
    ];

    /// <summary>
    /// Chromium net-error codes that indicate a proxy failure.
    /// Mapped to <c>TRANSPORT / browser.proxyFailure</c>.
    /// </summary>
    private static readonly string[] ProxyFailureTokens =
    [
        "net::ERR_PROXY_",
        "net::ERR_TUNNEL_",
        "net::ERR_SOCKS_"
    ];

    /// <summary>
    /// Chromium net-error codes that indicate a connection or network-offline condition.
    /// Mapped to <c>TRANSPORT / browser.networkOffline</c>.
    /// Connection-level failures during a non-idempotent action must cause the run
    /// to be terminated as UNKNOWN by the caller (BRW-US-015 AC#2).
    /// </summary>
    private static readonly string[] NetworkOfflineTokens =
    [
        "net::ERR_CONNECTION_REFUSED",
        "net::ERR_CONNECTION_RESET",
        "net::ERR_CONNECTION_TIMED_OUT",
        "net::ERR_CONNECTION_CLOSED",
        "net::ERR_CONNECTION_FAILED",
        "net::ERR_INTERNET_DISCONNECTED",
        "net::ERR_NETWORK_CHANGED",
        "net::ERR_EMPTY_RESPONSE",
        "net::ERR_SOCKET_NOT_CONNECTED"
    ];

    /// <inheritdoc/>
    public CecError Classify(Exception ex, bool stateKnown)
    {
        var cecError = ClassifyCore(ex);

        logger.LogDebug(
            "Exception {ExceptionType} classified → errorCategory={Category} " +
            "detailCode={DetailCode} stateKnown={StateKnown}.",
            ex.GetType().Name, cecError.Category, cecError.DetailCode ?? "(none)", stateKnown);

        return cecError;
    }

    // -------------------------------------------------------------------------
    // Core classification — no logging here, purely deterministic
    // -------------------------------------------------------------------------

    private static CecError ClassifyCore(Exception ex)
    {
        // 1. Cancellation — always CANCELLED; stateKnown is irrelevant
        if (ex is OperationCanceledException)
        {
            return new CecError
            {
                Category   = ErrorCategory.CANCELLED,
                Message    = "The run was cancelled.",
                DetailCode = null
            };
        }

        // 2. NetworkTransportException — pre-classified by the caller (BRE-003 / BRW-US-015 AC#1).
        //    The DetailCode was set by the throwing site; use it directly.
        if (ex is NetworkTransportException transport)
        {
            return new CecError
            {
                Category   = ErrorCategory.TRANSPORT,
                Message    = transport.Message,
                DetailCode = transport.DetailCode
            };
        }

        // 3. PlaywrightException — covers timeout, transport, ambiguity and not-found conditions.
        //    Playwright .NET surfaces all browser-side errors as PlaywrightException; the
        //    message text is used to distinguish the sub-type.
        if (ex is PlaywrightException pw)
        {
            var msg = pw.Message;

            // ── BRE-003 / BRW-US-015 AC#1: Network-level transport failures ──────────
            // Chromium reports these as net::ERR_* codes in the exception message.
            // Classification must happen BEFORE the generic Timeout check because some
            // ERR_CONNECTION_TIMED_OUT messages also contain the word "Timeout".

            if (ContainsAny(msg, DnsFailureTokens))
            {
                return new CecError
                {
                    Category   = ErrorCategory.TRANSPORT,
                    Message    = pw.Message,
                    DetailCode = "browser.dnsFailure"
                };
            }

            if (ContainsAny(msg, TlsErrorTokens))
            {
                return new CecError
                {
                    Category   = ErrorCategory.TRANSPORT,
                    Message    = pw.Message,
                    DetailCode = "browser.tlsCertificateError"
                };
            }

            if (ContainsAny(msg, ProxyFailureTokens))
            {
                return new CecError
                {
                    Category   = ErrorCategory.TRANSPORT,
                    Message    = pw.Message,
                    DetailCode = "browser.proxyFailure"
                };
            }

            if (ContainsAny(msg, NetworkOfflineTokens))
            {
                // Connection-level failure — caller must decide FAILED vs UNKNOWN based on
                // whether the interrupted action was non-idempotent (BRW-US-015 AC#2).
                return new CecError
                {
                    Category   = ErrorCategory.TRANSPORT,
                    Message    = pw.Message,
                    DetailCode = "browser.networkOffline"
                };
            }

            // ── Playwright-specific structural errors ─────────────────────────────────

            // Timeout — further classify as navigation vs. actionability
            if (IsTimeout(msg))
            {
                return IsNavigationTimeout(msg)
                    ? new CecError
                    {
                        Category   = ErrorCategory.TIMEOUT,
                        Message    = pw.Message,
                        DetailCode = "browser.navigationTimeout"
                    }
                    : new CecError
                    {
                        Category   = ErrorCategory.TIMEOUT,
                        Message    = pw.Message,
                        DetailCode = "browser.actionabilityTimeout"
                    };
            }

            if (ContainsAny(msg, AmbiguousTokens))
            {
                return new CecError
                {
                    Category   = ErrorCategory.TARGET_AMBIGUOUS,
                    Message    = pw.Message,
                    DetailCode = "browser.multipleLocatorMatches"
                };
            }

            if (ContainsAny(msg, NotFoundTokens))
            {
                return new CecError
                {
                    Category   = ErrorCategory.TARGET_NOT_FOUND,
                    Message    = pw.Message,
                    DetailCode = "browser.locatorNotFound"
                };
            }

            return new CecError
            {
                Category   = ErrorCategory.INTERNAL,
                Message    = pw.Message,
                DetailCode = null
            };
        }

        // 4. Everything else
        return new CecError
        {
            Category   = ErrorCategory.INTERNAL,
            Message    = ex.Message,
            DetailCode = null
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsTimeout(string message)
        => message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    private static bool IsNavigationTimeout(string message)
        => NavigationPrefixes.Any(p =>
            message.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string source, IEnumerable<string> tokens)
        => tokens.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
}
