using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Environment;

/// <summary>
/// Complete, secret-free environment snapshot reported to the Orchestrator before the first
/// step (BRW-006 / BRW-US-006 AC-1, AC-3).
/// <para>
/// All fields are non-sensitive identifiers or technical parameters.
/// TLS and proxy configuration are represented as opaque reference strings — never raw
/// certificate material, private keys, proxy credentials, or session tokens.
/// </para>
/// </summary>
public sealed record EnvironmentMetadata
{
    /// <summary>Run identifier that owns this environment snapshot.</summary>
    public required string RunId { get; init; }

    /// <summary>Playwright browser engine name, e.g. "chromium", "firefox", "webkit".</summary>
    public required string BrowserEngine { get; init; }

    /// <summary>Reported browser version string from Playwright (e.g. "120.0.6099.71").</summary>
    public required string BrowserVersion { get; init; }

    /// <summary>Execution mode declared in the deployment policy for this worker.</summary>
    public required BrowserMode ExecutionMode { get; init; }

    /// <summary>Declared viewport — width, height, and device-scale-factor.</summary>
    public required ViewportSettings Viewport { get; init; }

    /// <summary>IETF BCP 47 locale tag applied to the browser context (e.g. "en-US").</summary>
    public required string Locale { get; init; }

    /// <summary>IANA timezone identifier applied to the browser context (e.g. "UTC", "America/New_York").</summary>
    public required string Timezone { get; init; }

    /// <summary>
    /// Opaque reference to the deployment-policy TLS configuration entry.
    /// Contains no certificate material or private keys.
    /// </summary>
    public string? TlsConfigRef { get; init; }

    /// <summary>
    /// Opaque reference to the deployment-policy proxy configuration entry.
    /// Contains no proxy username, password, or authentication header.
    /// </summary>
    public string? ProxyConfigRef { get; init; }

    /// <summary>
    /// Integrity hint for the capability advertisement sent to the Orchestrator.
    /// Not an authentication credential — see CEC v1.0.1 §4.2.
    /// </summary>
    public string? CapabilityDigest { get; init; }

    /// <summary>UTC timestamp at which this snapshot was collected.</summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
}
