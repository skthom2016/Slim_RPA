using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// HMAC-SHA256-based implementation of <see cref="IArtifactValidator"/>.
///
/// Canonical payload (used for both signing and verification):
///   {RuntimeType}|{SchemaMajor}|{csv(ActionRefs,sorted)}|{csv(CommandRefs,sorted)}
///
/// The pre-shared key is loaded from <see cref="ArtifactSignatureSettings"/> and is
/// never written to any log, telemetry or evidence record (CEC v1.0.1 invariant #4).
/// </summary>
public sealed class ArtifactValidator : IArtifactValidator
{
    private readonly byte[] _hmacKey;
    private readonly ILogger<ArtifactValidator> _logger;

    public ArtifactValidator(
        IOptions<ArtifactSignatureSettings> settings,
        ILogger<ArtifactValidator> logger)
    {
        _logger = logger;

        var keyBase64 = settings.Value.HmacKeyBase64;

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            // Log the absence of key configuration — not the key value itself.
            _logger.LogWarning("ArtifactSignature:HmacKeyBase64 is not configured. All signature checks will fail.");
            _hmacKey = [];
        }
        else
        {
            _hmacKey = Convert.FromBase64String(keyBase64);
        }
    }

    /// <inheritdoc/>
    public bool IsSignatureValid(ArtifactManifest manifest)
    {
        if (_hmacKey.Length == 0)
        {
            return false;
        }

        var canonical = BuildCanonicalPayload(manifest);
        var payload   = Encoding.UTF8.GetBytes(canonical);

        using var hmac     = new HMACSHA256(_hmacKey);
        var       expected = hmac.ComputeHash(payload);
        var       expectedHex = Convert.ToHexString(expected).ToLowerInvariant();

        // Constant-time comparison prevents timing attacks.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHex),
            Encoding.UTF8.GetBytes(manifest.Signature.ToLowerInvariant()));
    }

    /// <summary>
    /// Builds a deterministic canonical string over the manifest fields that
    /// are covered by the signature.  The <c>Signature</c> field itself is excluded.
    /// </summary>
    internal static string BuildCanonicalPayload(ArtifactManifest manifest)
    {
        var sortedActions  = manifest.ActionRefs.Order(StringComparer.Ordinal);
        var sortedCommands = manifest.CommandRefs.Order(StringComparer.Ordinal);

        return string.Join("|",
            manifest.RuntimeType,
            manifest.SchemaMajor.ToString(),
            string.Join(",", sortedActions),
            string.Join(",", sortedCommands));
    }

    // -----------------------------------------------------------------------
    // Helper: compute the expected HMAC for a manifest (used by test helpers
    // and the signing tool — never called from the validation path).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes the HMAC-SHA256 signature for <paramref name="manifest"/> using
    /// <paramref name="keyBytes"/>.  Intended for test fixtures and the artifact
    /// signing tool.
    /// </summary>
    public static string ComputeSignature(ArtifactManifest manifest, byte[] keyBytes)
    {
        var canonical = BuildCanonicalPayload(manifest);
        var payload   = Encoding.UTF8.GetBytes(canonical);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
