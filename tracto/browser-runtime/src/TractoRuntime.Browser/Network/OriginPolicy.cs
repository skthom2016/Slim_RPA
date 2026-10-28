namespace TractoRuntime.Browser.Network;

/// <summary>
/// Deployment-policy origin allowlist for BRW-US-005.
/// Only origins explicitly listed here are approved for navigation.
/// </summary>
/// <remarks>
/// An "origin" is the scheme + host + optional port component of a URL,
/// e.g. "https://example.com" or "https://app.internal:8443".
/// A leading "*." wildcard entry matches any immediate subdomain:
/// "*.example.com" allows "https://sub.example.com" for any scheme.
/// Logging of redirect chains (AC#3) records only the origin portion —
/// URL paths, query strings, and credential components are never logged.
/// </remarks>
public sealed record OriginPolicy
{
    /// <summary>A policy that denies all navigation (empty allowlist).</summary>
    public static readonly OriginPolicy DenyAll = new() { AllowedOrigins = [] };

    /// <summary>
    /// Normalized origins approved for navigation.
    /// Each entry is either an exact origin ("https://example.com")
    /// or a wildcard subdomain pattern ("*.example.com").
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}
