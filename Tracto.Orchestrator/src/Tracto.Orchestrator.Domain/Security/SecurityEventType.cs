#nullable enable
namespace Tracto.Orchestrator.Domain.Security;

/// <summary>
/// Discriminated set of security-relevant event types recorded by the orchestrator.
/// Each variant is self-documenting so audit consumers need no separate code table.
/// </summary>
public enum SecurityEventType
{
    /// <summary>An unauthenticated request reached a protected route (/jobs/*, /workers/*).</summary>
    UnauthenticatedAccess = 1,

    /// <summary>A request arrived from an IP address outside the configured admin CIDR allowlist.</summary>
    AdminIpDenied = 2,

    /// <summary>A secret pattern (AWS key, PEM block, bare password) was detected in a payload.</summary>
    SecretDetected = 3,

    /// <summary>
    /// A JWT token was presented but failed cryptographic or claims validation
    /// (expired, bad signature, wrong audience/issuer).
    /// </summary>
    InvalidToken = 4,

    /// <summary>A request body exceeded the configured size limit and was rejected.</summary>
    OversizedPayload = 5,

    /// <summary>
    /// General security audit entry — used when none of the specific variants fit.
    /// Details field carries the discriminating description.
    /// </summary>
    GeneralAudit = 99,

    // ── OC-07: Async Callback Intake ──────────────────────────────────────────

    /// <summary>
    /// An inbound callback presented an HMAC-SHA256 signature that did not verify
    /// against the canonical string (keyId+"|"+timestamp+"|"+nonce+"|"+bodyJson).
    /// The callback is rejected with HTTP 401 and the event is recorded here.
    /// KeyId and EventId are captured in the Details field; the key material is never stored.
    /// </summary>
    InvalidHmac = 100,

    /// <summary>
    /// An inbound callback carried a timestamp that falls outside the ±5-minute replay window.
    /// The callback is rejected with HTTP 401 and the event is recorded here.
    /// Replay protection prevents an attacker from reusing a previously captured HMAC.
    /// </summary>
    ReplayDetected = 101,
}
