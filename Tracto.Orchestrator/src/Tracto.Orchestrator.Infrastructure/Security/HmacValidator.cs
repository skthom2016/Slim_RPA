#nullable enable
using System.Security.Cryptography;
using System.Text;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 callback signature validator (OC-07).
///
/// Implements <see cref="IHmacValidator"/> (which extends Application.Abstractions.IHmacValidator)
/// so the Application handler remains framework-free and testable with a simple mock.
///
/// Canonical string:  {keyId}|{timestamp}|{nonce}|{eventId}|{correlationId}|{bodyJson}
///
/// Rules enforced:
///   1. KeyId must be present in <c>OrchestratorSettings.HmacKeysOrEmpty</c>.
///   2. Computed HMAC must equal supplied signature (constant-time compare).
///
/// Replay-window enforcement (|now − timestamp| ≤ 5 min) is intentionally delegated
/// to the Application handler (<see cref="IngestCallbackCommandHandler"/>), which emits
/// the correctly typed security event (<c>REPLAY_DETECTED</c> vs <c>INVALID_HMAC</c>).
/// This validator concerns itself only with cryptographic correctness.
///
/// No plaintext key material is ever returned or logged (domain invariant 4).
/// </summary>
public sealed class HmacValidator : IHmacValidator
{
    private readonly OrchestratorSettings _settings;

    public HmacValidator(OrchestratorSettings settings)
    {
        _settings = settings;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>false</c> on any validation failure: unknown KeyId or bad signature.
    /// Never throws for expected failure paths.
    /// </remarks>
    public bool Validate(
        string keyId,
        string timestamp,
        string nonce,
        string eventId,
        string correlationId,
        string bodyJson,
        string signature)
    {
        // 1. Key must be configured.
        if (!_settings.HmacKeysOrEmpty.TryGetValue(keyId, out var keyBase64))
            return false;

        // 2. Decode key material.
        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException)
        {
            // Misconfigured key — fail closed; do not expose internal error.
            return false;
        }

        // 3. Compute expected HMAC over canonical string.
        // eventId and correlationId are included to cryptographically bind those
        // routing/dedup fields to the signature (prevents replay to a different run
        // or bypass of the duplicate-EventId guard).
        var canonical = $"{keyId}|{timestamp}|{nonce}|{eventId}|{correlationId}|{bodyJson}";
        var data      = Encoding.UTF8.GetBytes(canonical);
        var expected  = HMACSHA256.HashData(keyBytes, data);

        // 4. Decode supplied signature — accept lowercase/uppercase hex or Base-64.
        byte[] supplied;
        try
        {
            supplied = signature.Length == 64
                ? Convert.FromHexString(signature)
                : Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        // 5. Constant-time compare to prevent timing side-channel attacks.
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
