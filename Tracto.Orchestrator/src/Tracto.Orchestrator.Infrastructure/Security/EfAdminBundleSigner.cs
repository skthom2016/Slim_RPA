#nullable enable
using System.Security.Cryptography;
using System.Text;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 signer for admin telemetry export bundles (OC-13).
///
/// Implements both <see cref="IAdminBundleSigner"/> and <see cref="ITelemetryBundleSigner"/>
/// so a single class satisfies both signing contracts.
///
/// Uses the first key from <see cref="OrchestratorSettings.HmacKeysOrEmpty"/> as the admin
/// signing key. When no keys are configured returns "NOT_CONFIGURED" — not a valid HMAC —
/// so callers can detect the misconfiguration without key material being disclosed (invariant 4).
/// </summary>
public sealed class EfAdminBundleSigner : IAdminBundleSigner, ITelemetryBundleSigner
{
    private const string NotConfiguredSentinel = "NOT_CONFIGURED";

    private readonly OrchestratorSettings _settings;

    public EfAdminBundleSigner(OrchestratorSettings settings)
    {
        _settings = settings;
    }

    /// <inheritdoc cref="IAdminBundleSigner.Sign"/>
    public string Sign(string payload)
    {
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
            return NotConfiguredSentinel;
        }

        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
