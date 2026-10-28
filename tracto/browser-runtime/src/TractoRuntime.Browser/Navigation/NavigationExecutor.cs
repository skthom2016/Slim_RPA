using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Network;

namespace TractoRuntime.Browser.Navigation;

/// <summary>
/// Executes navigation operations using Playwright's IPage API.
/// Classifies TLS, proxy, and origin-denied failures into CEC error categories.
/// </summary>
/// <remarks>
/// <para><b>AC#1/AC#2</b> — Origin policy is checked before sending any request.
/// The pre-check here guards against Navigate actions; the
/// <see cref="INetworkPolicyEnforcer"/> route handler covers redirect chains and
/// sub-resource requests at the context level.</para>
///
/// <para><b>AC#6</b> — When Playwright throws an SSL/certificate exception (because
/// <c>IgnoreHTTPSErrors</c> is always false), it is classified as
/// <c>TRANSPORT / browser.tlsCertificateError</c>.  Certificate details are
/// recorded in evidence; no bypass is attempted.</para>
///
/// <para><b>AC#7</b> — Proxy connection failures are classified as
/// <c>TRANSPORT / browser.proxyError</c>.  No retry through an alternate route
/// is attempted.</para>
///
/// <para><b>AC#8</b> — Navigate, Reload, Back, and Forward are all supported.
/// Each history operation asserts the resulting URL or a declared DOM state
/// before the run proceeds to the next step.</para>
/// </remarks>
public sealed class NavigationExecutor(
    IOriginPolicyEnforcer originEnforcer,
    ILogger<NavigationExecutor> logger)
    : INavigationExecutor
{
    /// <inheritdoc/>
    public async Task<NavigationResult> ExecuteAsync(
        IPage page,
        NavigationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(request);

        // AC#1: For Navigate actions, check origin BEFORE sending any request.
        if (request.Action == NavigationHistoryAction.Navigate)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return NavigationResult.Failure(
                    ErrorCategory.VALIDATION,
                    "browser.selectorRefInvalid",
                    "Navigate action requires a non-empty absolute URL.");
            }

            var policyCheck = originEnforcer.Check(request.Url);
            if (!policyCheck.IsAllowed)
            {
                logger.LogWarning(
                    "Navigate blocked: origin '{BlockedOrigin}' is not in the allowlist (AC#1).",
                    policyCheck.BlockedOrigin);

                return NavigationResult.Failure(
                    ErrorCategory.POLICY_DENIED,
                    "browser.originDenied",
                    $"Navigation to origin '{policyCheck.BlockedOrigin}' is denied by the " +
                    "deployment origin policy. No request was sent.");
            }
        }

        try
        {
            var timeoutMs = (float)request.Timeout.TotalMilliseconds;

            // AC#8: all four history operations are supported.
            _ = request.Action switch
            {
                NavigationHistoryAction.Navigate =>
                    await page.GotoAsync(
                        request.Url!,
                        new PageGotoOptions { Timeout = timeoutMs })
                    .ConfigureAwait(false),

                NavigationHistoryAction.Reload =>
                    await page.ReloadAsync(
                        new PageReloadOptions { Timeout = timeoutMs })
                    .ConfigureAwait(false),

                NavigationHistoryAction.Back =>
                    await page.GoBackAsync(
                        new PageGoBackOptions { Timeout = timeoutMs })
                    .ConfigureAwait(false),

                NavigationHistoryAction.Forward =>
                    await page.GoForwardAsync(
                        new PageGoForwardOptions { Timeout = timeoutMs })
                    .ConfigureAwait(false),

                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    $"Unsupported navigation action: {request.Action}")
            };

            var finalUrl = page.Url;

            // AC#8: assert resulting URL if a declaration was provided.
            if (request.ExpectedUrl is not null &&
                !string.Equals(finalUrl, request.ExpectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Navigation completed but URL assertion failed: " +
                    "expected='{Expected}' (redacted path), actual=(recorded in evidence).",
                    MaskUrl(request.ExpectedUrl));

                return NavigationResult.Failure(
                    ErrorCategory.VALIDATION,
                    "browser.navigationAssertionFailed",
                    "Navigation completed but the resulting URL did not match the declared expectation. " +
                    "The expected URL and actual URL are recorded in evidence.");
            }

            // AC#8: assert DOM state (selector visibility) if a declaration was provided.
            if (request.ExpectedSelector is not null)
            {
                try
                {
                    await page.WaitForSelectorAsync(
                        request.ExpectedSelector,
                        new PageWaitForSelectorOptions { Timeout = timeoutMs })
                        .ConfigureAwait(false);

                    logger.LogDebug(
                        "DOM state assertion passed: selector visible after {Action}.",
                        request.Action);
                }
                catch (TimeoutException)
                {
                    logger.LogWarning(
                        "DOM state assertion timed out: declared selector did not become visible " +
                        "within the configured timeout after {Action} (BRW-US-005 AC#8).",
                        request.Action);

                    return NavigationResult.Failure(
                        ErrorCategory.TIMEOUT,
                        "browser.domAssertionTimeout",
                        "Navigation completed but the declared DOM selector did not become visible " +
                        "within the configured timeout. The run cannot proceed to the next step.");
                }
            }

            logger.LogInformation(
                "Navigation succeeded: action={Action}, finalUrl recorded in evidence.",
                request.Action);

            return NavigationResult.Success(finalUrl);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex,
                "Navigation timed out: action={Action}, timeout={Timeout}.",
                request.Action, request.Timeout);

            return NavigationResult.Failure(
                ErrorCategory.TIMEOUT,
                "browser.navigationTimeout",
                $"Navigation action '{request.Action}' did not complete within the configured timeout.");
        }
        catch (PlaywrightException ex) when (IsTlsError(ex.Message))
        {
            // AC#6: TLS certificate error — never bypassed silently.
            // Certificate details are recorded in evidence (the exception message); no bypass.
            logger.LogWarning(
                "Navigation failed: TLS certificate error (BRW-US-005 AC#6). " +
                "Certificate details recorded in evidence; no TLS bypass attempted.");

            return NavigationResult.Failure(
                ErrorCategory.TRANSPORT,
                "browser.tlsCertificateError",
                "Navigation failed due to a TLS/SSL certificate error. " +
                "The certificate was not trusted and cannot be bypassed per deployment policy.");
        }
        catch (PlaywrightException ex) when (IsProxyError(ex.Message))
        {
            // AC#7: Proxy connection failure — categorized TRANSPORT, no alternate route.
            logger.LogWarning(
                "Navigation failed: proxy connection error (BRW-US-005 AC#7). " +
                "No alternate routing attempted.");

            return NavigationResult.Failure(
                ErrorCategory.TRANSPORT,
                "browser.proxyError",
                "Navigation failed due to a proxy connection error. " +
                "The run is stopped; no retry through an alternate route is permitted.");
        }
        catch (PlaywrightException ex) when (IsOriginDenied(ex.Message))
        {
            // AC#1/AC#2: request aborted by the NetworkPolicyEnforcer route handler.
            logger.LogWarning(
                "Navigation failed: request aborted by origin policy route handler (BRW-US-005 AC#1/AC#2).");

            return NavigationResult.Failure(
                ErrorCategory.POLICY_DENIED,
                "browser.originDenied",
                "Navigation was blocked because the target origin is not in the deployment allowlist.");
        }
        catch (PlaywrightException ex)
        {
            logger.LogWarning(ex,
                "Navigation failed with a Playwright exception: action={Action}.",
                request.Action);

            return NavigationResult.Failure(
                ErrorCategory.TRANSPORT,
                "browser.networkInterrupted",
                "Navigation failed due to a network or browser error. Details in evidence.");
        }
    }

    // ── Error classification helpers ──────────────────────────────────────────

    /// <summary>
    /// Returns true when the Playwright exception message indicates a TLS/SSL certificate error.
    /// Covers Chromium's ERR_CERT_* and ERR_SSL_* error codes.
    /// </summary>
    private static bool IsTlsError(string message) =>
        message.Contains("ssl", StringComparison.OrdinalIgnoreCase)
        || message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ERR_CERT_", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ERR_SSL_", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NET::ERR_CERT", StringComparison.OrdinalIgnoreCase)
        || message.Contains("CERT_AUTHORITY_INVALID", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the Playwright exception message indicates a proxy connection failure.
    /// </summary>
    private static bool IsProxyError(string message) =>
        message.Contains("proxy", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ERR_PROXY_", StringComparison.OrdinalIgnoreCase)
        || message.Contains("PROXY_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the Playwright exception message indicates that the request was
    /// aborted with "accessdenied" by the NetworkPolicyEnforcer route handler.
    /// </summary>
    private static bool IsOriginDenied(string message) =>
        message.Contains("accessdenied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ERR_ACCESS_DENIED", StringComparison.OrdinalIgnoreCase)
        || message.Contains("net::ERR_BLOCKED", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Request intercepted", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the URL scheme and host only — no path, query, fragment, or credentials.
    /// Used in log messages to avoid PII leakage.
    /// </summary>
    private static string MaskUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Authority}/(path-redacted)";
        return "(invalid-url)";
    }
}
