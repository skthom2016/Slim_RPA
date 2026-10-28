#nullable enable
using System.Security.Cryptography;
using System.Text;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 signer for telemetry export bundles (OC-13).
///
/// Uses the first key from <see cref="OrchestratorSettings.HmacKeysOrEmpty"/> as the
/// admin signing key. When no keys are configured, returns the sentinel string
/// <c>"NOT_CONFIGURED"</c> — not a valid hex HMAC — so callers can detect misconfiguration
/// without revealing key material (domain invariant 4).
///
/// The canonical payload is signed as-is: callers are responsible for deterministic
/// serialisation (see <c>ExportTelemetryBundleQueryHandler</c>).
/// </summary>
public sealed class TelemetryBundleSigner : ITelemetryBundleSigner
{
    private const string NotConfiguredSentinel = "NOT_CONFIGURED";

    private readonly OrchestratorSettings _settings;

    public TelemetryBundleSigner(OrchestratorSettings settings)
    {
        _settings = settings;
    }

    /// <inheritdoc/>
    public string Sign(string canonicalPayload)
    {
        // Use the first configured key as the admin telemetry signing key.
        var keyEntry = _settings.HmacKeysOrEmpty.FirstOrDefault();
        if (keyEntry.Equals(default(KeyValuePair<string, string>)) || keyEntry.Value is null)
            return NotConfiguredSentinel;

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(keyEntry.Value);
        }
        catch (FormatException)
        {
            // Misconfigured key — fail closed without exposing key material.
            return NotConfiguredSentinel;
        }

        var data = Encoding.UTF8.GetBytes(canonicalPayload);
        var hash = HMACSHA256.HashData(keyBytes, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
