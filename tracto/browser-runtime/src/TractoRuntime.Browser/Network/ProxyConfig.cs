namespace TractoRuntime.Browser.Network;

/// <summary>
/// Enterprise proxy settings declared in the deployment policy (BRW-US-005 AC#5, BRW-008).
/// </summary>
/// <remarks>
/// The proxy password, when required, is stored as a vault secret reference
/// (<see cref="PasswordSecretRef"/>).  The resolved plaintext value is used
/// only at context-creation time and must never be logged, traced, or placed
/// in any Orchestrator payload.
/// </remarks>
public sealed record ProxyConfig
{
    /// <summary>
    /// Proxy server URL including scheme and port, e.g. "http://proxy.corp.example.com:8080".
    /// </summary>
    public required string Server { get; init; }

    /// <summary>Optional proxy authentication username.</summary>
    public string? Username { get; init; }

    /// <summary>
    /// Vault secret reference for the proxy password (e.g. "vault://proxy/corp-password").
    /// Resolved at context-creation time via <c>ISecretResolver</c>.
    /// This field must never hold a raw password value.
    /// </summary>
    public string? PasswordSecretRef { get; init; }

    /// <summary>
    /// Optional comma-separated list of hosts that bypass the proxy.
    /// </summary>
    public string? Bypass { get; init; }
}
