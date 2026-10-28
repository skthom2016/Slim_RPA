#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Signs a telemetry bundle's canonical payload with HMAC-SHA256 (OC-14).
/// The concrete implementation lives in Infrastructure and uses a pre-configured signing key.
/// No plaintext key material is ever returned — only the hex signature.
/// </summary>
public interface ITelemetryBundleSigner
{
    /// <summary>
    /// Computes an HMAC-SHA256 signature over <paramref name="canonicalPayload"/>
    /// and returns it as a lowercase hex string.
    /// </summary>
    string Sign(string canonicalPayload);
}
