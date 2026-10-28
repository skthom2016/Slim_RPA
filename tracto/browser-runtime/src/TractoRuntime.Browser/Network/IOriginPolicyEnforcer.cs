namespace TractoRuntime.Browser.Network;

/// <summary>
/// Checks whether a URL is authorized for navigation by the deployment origin policy.
/// </summary>
/// <remarks>
/// Implementations must be pure: no network request is made and no browser interaction
/// occurs.  This is a policy check only (BRW-US-005 AC#1, AC#2).
/// </remarks>
public interface IOriginPolicyEnforcer
{
    /// <summary>
    /// Returns an allowed or denied result for the given absolute URL.
    /// Only the URL's origin component (scheme + authority) is evaluated.
    /// No request is sent.
    /// </summary>
    /// <param name="url">Absolute URL to check.</param>
    OriginCheckResult Check(string url);
}
