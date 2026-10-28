#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Validates HMAC-SHA256 signatures on inbound async callbacks (OC-07).
///
/// The canonical string signed by the caller is:
///   <c>{keyId}|{timestamp}|{nonce}|{eventId}|{correlationId}|{bodyJson}</c>
///
/// Including <c>eventId</c> and <c>correlationId</c> in the canonical string
/// cryptographically binds those routing/dedup fields to the signature, preventing
/// an attacker from replaying a valid body to a different run or bypassing the
/// duplicate-EventId guard.
///
/// The Infrastructure implementation (<c>HmacValidator</c>) resolves the raw key
/// bytes from <c>OrchestratorSettings.HmacKeys[keyId]</c> and performs a constant-time
/// comparison to prevent timing attacks. No plaintext key material is ever returned
/// through this interface (domain invariant 4).
/// </summary>
public interface IHmacValidator
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="signature"/> is a valid HMAC-SHA256
    /// over the canonical string for the supplied parameters; <c>false</c> otherwise.
    ///
    /// Returns <c>false</c> immediately (without throwing) when <paramref name="keyId"/>
    /// is not present in the configured key dictionary — no key enumeration or error
    /// detail is disclosed to the caller.
    /// </summary>
    /// <param name="keyId">Signing key identifier header value (X-KeyId).</param>
    /// <param name="timestamp">Timestamp header value (X-Timestamp), ISO-8601 or Unix epoch seconds.</param>
    /// <param name="nonce">Nonce header value (X-Nonce).</param>
    /// <param name="eventId">Caller-supplied event identifier (deduplication key).</param>
    /// <param name="correlationId">Correlation token string (canonical form of the Guid).</param>
    /// <param name="bodyJson">Raw UTF-8 request body as received.</param>
    /// <param name="signature">HMAC-SHA256 hex or Base64 value from the X-Hmac-Signature header.</param>
    bool Validate(string keyId, string timestamp, string nonce, string eventId, string correlationId, string bodyJson, string signature);
}
