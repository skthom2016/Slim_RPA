namespace TractoRuntime.Browser.Network;

/// <summary>
/// Result of an origin-policy check performed by <see cref="IOriginPolicyEnforcer"/>
/// (BRW-US-005 AC#1, AC#2).
/// </summary>
public sealed record OriginCheckResult
{
    /// <summary>True when the URL's origin is in the deployment policy allowlist.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// The blocked origin (scheme + "://" + authority) when <see cref="IsAllowed"/> is false.
    /// Contains the origin component only — never the URL path, query, or credentials (AC#3).
    /// Null when <see cref="IsAllowed"/> is true.
    /// </summary>
    public string? BlockedOrigin { get; init; }

    /// <summary>Creates an allowed result.</summary>
    public static OriginCheckResult Allow() => new() { IsAllowed = true };

    /// <summary>
    /// Creates a denied result recording the blocked origin for evidence.
    /// The caller must ensure <paramref name="blockedOrigin"/> contains only
    /// scheme + authority — no path, query, or credentials.
    /// </summary>
    public static OriginCheckResult Deny(string blockedOrigin)
        => new() { IsAllowed = false, BlockedOrigin = blockedOrigin };
}
