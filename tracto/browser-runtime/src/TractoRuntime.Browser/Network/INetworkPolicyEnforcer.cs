using Microsoft.Playwright;

namespace TractoRuntime.Browser.Network;

/// <summary>
/// Applies network policy (origin allowlist, proxy, TLS constraints) to a browser context
/// by installing Playwright route interception.
/// </summary>
/// <remarks>
/// Must be called immediately after context creation and before any navigation begins
/// so that no request escapes enforcement (BRW-US-005 AC#1).
/// </remarks>
public interface INetworkPolicyEnforcer
{
    /// <summary>
    /// Installs route-level origin enforcement on the context.
    /// After this call, any request to a non-allowlisted origin is aborted
    /// before a network packet is sent (BRW-US-005 AC#1, AC#2).
    /// </summary>
    Task InstallAsync(IBrowserContext context, CancellationToken ct);
}
