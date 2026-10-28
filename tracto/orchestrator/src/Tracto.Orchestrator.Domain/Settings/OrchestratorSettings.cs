namespace Tracto.Orchestrator.Domain.Settings;

/// <summary>
/// Immutable configuration snapshot read at startup.
/// Pure domain record — no framework dependencies.
/// </summary>
/// <param name="Mode">Connected or Isolated binding mode.</param>
/// <param name="ApiPort">TCP port the HTTP listener binds on (default 8080).</param>
/// <param name="DbPath">Filesystem path to the SQLite database file.</param>
/// <param name="TlsCertPath">Optional path to a PEM/PFX certificate for TLS termination.</param>
/// <param name="AllowedCidr">Optional CIDR block restricting inbound connections (Connected mode only).</param>
/// <param name="AllowedAdminCidr">
///   Optional CIDR block that limits which IPs may reach /admin/* routes in Connected mode.
///   When null or empty the admin routes are accessible from any source allowed by <see cref="AllowedCidr"/>.
///   Evaluated by <c>AdminIpAllowlistMiddleware</c> (OC-17).
/// </param>
/// <param name="JwtSecret">
///   Shared secret (Base64-encoded HMAC key) used to validate JWT Bearer tokens on
///   /jobs/* and /workers/* routes. Must be set in Connected mode. Treat as sensitive;
///   never log or return in API responses.
/// </param>
/// <param name="HmacKeys">
///   OC-07: Named HMAC-SHA256 signing keys used to validate inbound async callbacks.
///   Dictionary maps KeyId (a short, non-secret identifier) → raw signing key (Base64).
///   The orchestrator never logs or returns key values; only KeyId identifiers are stored
///   in audit records (invariant 4 — no plaintext secrets stored or returned).
///   Keys may be rotated by adding a new entry and retiring the old KeyId at leisure.
/// </param>
/// <param name="AdminHmacKey">
///   OC-13: Base64-encoded HMAC-SHA256 key used to sign telemetry bundles returned by
///   GET /admin/telemetry-bundle/{runId}. Must be configured in Connected mode when
///   the admin API is exposed. Treat as sensitive — never log or return in responses.
///   When null the Infrastructure layer falls back to a deterministic per-process
///   ephemeral key (suitable for isolated/test deployments only).
/// </param>
public sealed record OrchestratorSettings(
    DeploymentMode Mode,
    int ApiPort = 8080,
    string DbPath = "tracto-orchestrator.db",
    string? TlsCertPath = null,
    string? AllowedCidr = null,
    string? AllowedAdminCidr = null,
    string? JwtSecret = null,
    IReadOnlyDictionary<string, string>? HmacKeys = null,
    string? AdminHmacKey = null
)
{
    /// <summary>
    /// The IP address the API listener should bind to given the deployment mode.
    /// </summary>
    public string ListenAddress => Mode == DeploymentMode.Connected ? "0.0.0.0" : "127.0.0.1";

    /// <summary>
    /// Full listen URL composed from <see cref="ListenAddress"/> and <see cref="ApiPort"/>.
    /// </summary>
    public string ListenUrl => $"http://{ListenAddress}:{ApiPort}";

    /// <summary>
    /// OC-07: Returns <see cref="HmacKeys"/> or an empty dictionary when not configured.
    /// Always safe to call without a null guard.
    /// The empty sentinel is cached as a static field to avoid per-call allocation.
    /// </summary>
    public IReadOnlyDictionary<string, string> HmacKeysOrEmpty =>
        HmacKeys ?? EmptyHmacKeys;

    /// <summary>Singleton empty dictionary returned by <see cref="HmacKeysOrEmpty"/> when no keys are configured.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyHmacKeys =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>());
}
