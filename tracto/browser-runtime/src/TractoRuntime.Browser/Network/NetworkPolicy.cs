namespace TractoRuntime.Browser.Network;

/// <summary>
/// Combines the origin allowlist policy and proxy/TLS configuration for a deployment.
/// Applied per browser context to enforce BRW-005 and BRW-008.
/// </summary>
/// <remarks>
/// AC#5: When <see cref="ProxyConfig"/> is set, ALL browser traffic is routed
///       through the configured proxy — no alternate route is permitted.
/// AC#6: <see cref="IgnoreHttpsErrors"/> is always false by design; TLS certificate
///       errors are never bypassed silently.
/// AC#7: Proxy connection failures surface as categorized TRANSPORT errors.
/// </remarks>
public sealed record NetworkPolicy
{
    /// <summary>
    /// Origin allowlist. When null, the deny-all default (<see cref="OriginPolicy.DenyAll"/>)
    /// is applied — no origin is reachable without an explicit allowlist.
    /// </summary>
    public required OriginPolicy OriginPolicy { get; init; }

    /// <summary>
    /// Enterprise proxy configuration.  When null, the system default routing is used.
    /// When non-null, all browser context traffic is routed through this proxy (AC#5).
    /// </summary>
    public ProxyConfig? ProxyConfig { get; init; }

    /// <summary>
    /// Whether HTTPS errors should be ignored.
    /// This property is intentionally read-only and always returns <see langword="false"/>.
    /// TLS certificate errors are NEVER bypassed by the Browser Runtime (BRW-008, BRW-US-005 AC#6).
    /// </summary>
    public bool IgnoreHttpsErrors => false;
}
