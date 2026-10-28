using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Viewport;

namespace TractoRuntime.Browser.Environment;

/// <summary>
/// Default implementation of <see cref="IEnvironmentReporter"/> (BRW-006 / BRW-007).
/// <para>
/// Collects environment metadata from deployment-policy settings and the Playwright browser
/// context, then exposes a sanitized evidence dictionary for diagnostic artifacts.
/// Credential values — TLS certificates, proxy passwords, session cookies — never appear
/// in the collected metadata or the evidence output.
/// </para>
/// </summary>
public sealed class EnvironmentReporter : IEnvironmentReporter
{
    private readonly string _locale;
    private readonly string _timezone;

    /// <summary>
    /// Opaque reference to the TLS configuration policy entry.
    /// Contains no certificate material or private key.
    /// </summary>
    private readonly string? _tlsConfigRef;

    /// <summary>
    /// Opaque reference to the proxy configuration policy entry.
    /// Contains no proxy username, password, or auth header.
    /// </summary>
    private readonly string? _proxyConfigRef;

    /// <summary>
    /// Integrity hint for the capability digest reported to the Orchestrator.
    /// Not an authentication credential.
    /// </summary>
    private readonly string? _capabilityDigest;

    /// <param name="locale">IETF BCP 47 locale tag (e.g. "en-US").</param>
    /// <param name="timezone">IANA timezone identifier (e.g. "UTC").</param>
    /// <param name="tlsConfigRef">Opaque TLS config reference — no cert/key values.</param>
    /// <param name="proxyConfigRef">Opaque proxy config reference — no credentials.</param>
    /// <param name="capabilityDigest">Integrity hint for the capability advertisement.</param>
    public EnvironmentReporter(
        string locale = "en-US",
        string timezone = "UTC",
        string? tlsConfigRef = null,
        string? proxyConfigRef = null,
        string? capabilityDigest = null)
    {
        _locale = locale;
        _timezone = timezone;
        _tlsConfigRef = tlsConfigRef;
        _proxyConfigRef = proxyConfigRef;
        _capabilityDigest = capabilityDigest;
    }

    /// <inheritdoc/>
    public Task<EnvironmentMetadata> CollectAsync(
        string runId,
        string browserEngine,
        string browserVersion,
        BrowserMode mode,
        ViewportSettings viewport,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var metadata = new EnvironmentMetadata
        {
            RunId          = runId,
            BrowserEngine  = browserEngine,
            BrowserVersion = browserVersion,
            ExecutionMode  = mode,
            Viewport       = viewport,
            Locale         = _locale,
            Timezone       = _timezone,
            TlsConfigRef   = _tlsConfigRef,
            ProxyConfigRef = _proxyConfigRef,
            CapabilityDigest = _capabilityDigest,
            CollectedAt    = DateTimeOffset.UtcNow
        };

        return Task.FromResult(metadata);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The dictionary is safe to embed in Orchestrator payloads and run-result evidence.
    /// TLS and proxy configuration appear only as opaque reference identifiers.
    /// </remarks>
    public IReadOnlyDictionary<string, object?> BuildSanitizedEvidence(EnvironmentMetadata metadata)
    {
        return new Dictionary<string, object?>
        {
            ["runId"]             = metadata.RunId,
            ["browserEngine"]     = metadata.BrowserEngine,
            ["browserVersion"]    = metadata.BrowserVersion,
            ["executionMode"]     = metadata.ExecutionMode.ToString(),
            ["viewportWidth"]     = metadata.Viewport.Width,
            ["viewportHeight"]    = metadata.Viewport.Height,
            ["deviceScaleFactor"] = metadata.Viewport.DeviceScaleFactor,
            ["locale"]            = metadata.Locale,
            ["timezone"]          = metadata.Timezone,
            // Reference identifiers only — never raw cert, key, password, or token
            ["tlsConfigRef"]      = metadata.TlsConfigRef,
            ["proxyConfigRef"]    = metadata.ProxyConfigRef,
            ["capabilityDigest"]  = metadata.CapabilityDigest,
            ["collectedAt"]       = metadata.CollectedAt.ToString("O")
        };
    }
}
