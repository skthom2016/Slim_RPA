using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TractoRuntime.Browser.Network;

/// <summary>
/// Installs Playwright route interception to enforce BRW-US-005 network policy:
/// origin allowlist, TLS integrity constraint, and redirect-chain visibility.
/// </summary>
/// <remarks>
/// <para><b>AC#1</b> — Every outgoing request is checked against the origin allowlist
/// before a network packet is sent.  Blocked requests are aborted with error code
/// "accessdenied" — the browser receives no response.</para>
/// <para><b>AC#2</b> — Redirect chains are covered because every fetch/document request,
/// including redirect-triggered secondary requests, is routed through the same handler.</para>
/// <para><b>AC#3</b> — Only the origin component (scheme + authority) is logged; path,
/// query, and credential components are never written to any log.</para>
/// <para><b>AC#5</b> — Enterprise proxy configuration is applied to the browser context
/// options by <see cref="BrowserContextManager"/> before context creation; this class
/// does not duplicate that responsibility.</para>
/// <para><b>AC#6</b> — <c>IgnoreHTTPSErrors</c> is never set to true; TLS errors surface
/// as exceptions from navigation calls and are classified by <see cref="Navigation.NavigationExecutor"/>.</para>
/// </remarks>
public sealed class NetworkPolicyEnforcer(
    NetworkPolicy policy,
    IOriginPolicyEnforcer originEnforcer,
    ILogger<NetworkPolicyEnforcer> logger)
    : INetworkPolicyEnforcer
{
    /// <inheritdoc/>
    public async Task InstallAsync(IBrowserContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Register an async route handler for ALL requests on this context.
        // This covers navigation, fetch, XHR, sub-resources — every outgoing request.
        await context.RouteAsync("**/*", async route =>
        {
            var url = route.Request.Url;
            var check = originEnforcer.Check(url);

            if (!check.IsAllowed)
            {
                // AC#3: log only the blocked origin — never the path, query, or credentials.
                logger.LogWarning(
                    "Request aborted by origin policy: blocked origin='{BlockedOrigin}', " +
                    "resourceType={ResourceType} (BRW-US-005 AC#1/AC#2).",
                    check.BlockedOrigin,
                    route.Request.ResourceType);

                // "accessdenied" causes Playwright to throw a PlaywrightException with a
                // message matching the access-denied pattern, allowing NavigationExecutor
                // to classify it as POLICY_DENIED / browser.originDenied.
                await route.AbortAsync("accessdenied").ConfigureAwait(false);
            }
            else
            {
                await route.ContinueAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        logger.LogInformation(
            "Network policy enforcer installed on browser context: {Count} allowed origin(s).",
            policy.OriginPolicy.AllowedOrigins.Count);
    }
}
