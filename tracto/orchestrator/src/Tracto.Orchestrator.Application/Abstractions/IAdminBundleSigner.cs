#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Signs telemetry bundle payloads with HMAC-SHA256 using the admin signing key (OC-13).
/// Domain invariant 4: no plaintext key material is ever returned through this interface.
/// </summary>
public interface IAdminBundleSigner
{
    /// <summary>
    /// Computes an HMAC-SHA256 signature over <paramref name="canonicalPayload"/> and
    /// returns it as a lowercase hex string (64 chars).
    /// Returns "NOT_CONFIGURED" when no admin key is configured.
    /// </summary>
    string Sign(string canonicalPayload);
}