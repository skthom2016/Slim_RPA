using Microsoft.Extensions.Logging;

namespace TractoRuntime.Browser.Network;

/// <summary>
/// Enforces the deployment origin allowlist for BRW-US-005.
/// </summary>
/// <remarks>
/// AC#1 — Navigation to a URL not in the allowlist is blocked before any request is sent.
/// AC#2 — A redirect chain that leads to a non-allowlisted origin is blocked at the redirect step.
/// AC#3 — The blocked origin (scheme + authority) is logged for evidence; URL path, query string,
///         and credential components are never logged to prevent PII leakage.
/// </remarks>
public sealed class OriginPolicyEnforcer(
    OriginPolicy policy,
    ILogger<OriginPolicyEnforcer> logger)
    : IOriginPolicyEnforcer
{
    /// <inheritdoc/>
    public OriginCheckResult Check(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning(
                "Origin policy check failed: URL is null or empty (BRW-US-005 AC#1).");
            return OriginCheckResult.Deny("(empty)");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            logger.LogWarning(
                "Origin policy check failed: URL is not a valid absolute URI (BRW-US-005 AC#1).");
            return OriginCheckResult.Deny("(invalid-uri)");
        }

        // Extract the origin: scheme + "://" + authority (host[:port]).
        // Per AC#3: only the origin is captured — never the path, query, or user-info.
        var requestOrigin = $"{uri.Scheme}://{uri.Authority}";

        foreach (var entry in policy.AllowedOrigins)
        {
            if (OriginMatches(requestOrigin, entry))
            {
                logger.LogDebug(
                    "Origin policy: allowed. Origin='{Origin}' matched policy entry '{Entry}'.",
                    requestOrigin, entry);
                return OriginCheckResult.Allow();
            }
        }

        // AC#3: log the blocked origin only — never path, query, fragment, or credentials.
        logger.LogWarning(
            "Navigation blocked: origin '{Origin}' is not in the deployment policy allowlist. " +
            "No request will be sent (BRW-US-005 AC#1).",
            requestOrigin);

        return OriginCheckResult.Deny(requestOrigin);
    }

    /// <summary>
    /// Returns true when <paramref name="requestOrigin"/> satisfies <paramref name="policyEntry"/>.
    /// </summary>
    /// <remarks>
    /// Exact match (case-insensitive): "https://example.com" == "https://example.com".
    /// Wildcard subdomain match: "*.example.com" matches any scheme + host that ends in ".example.com".
    /// The wildcard applies to the host portion only; the scheme is not constrained by it.
    /// </remarks>
    private static bool OriginMatches(string requestOrigin, string policyEntry)
    {
        // 1. Exact case-insensitive match
        if (string.Equals(requestOrigin, policyEntry, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Wildcard subdomain: "*.example.com" → host must end with ".example.com"
        if (policyEntry.StartsWith("*.", StringComparison.Ordinal))
        {
            var domainSuffix = policyEntry[1..]; // ".example.com"

            if (!Uri.TryCreate(requestOrigin, UriKind.Absolute, out var requestUri))
                return false;

            // authority = host or host:port; we want to match the host suffix
            return requestUri.Authority.EndsWith(domainSuffix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
