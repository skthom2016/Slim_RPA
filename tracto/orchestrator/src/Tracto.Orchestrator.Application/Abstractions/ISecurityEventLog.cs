#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Application-layer contract for appending security audit events.
///
/// Implementations (Infrastructure) persist events to the
/// <c>SecurityEvents</c> table via EF Core.
/// The Application layer depends only on this interface — never on EF directly.
/// </summary>
public interface ISecurityEventLog
{
    /// <summary>
    /// Appends a security event record. Implementations must not include secret
    /// values in <paramref name="details"/>.
    /// </summary>
    /// <param name="eventType">
    ///   Machine-readable event classification, e.g. "UNAUTHENTICATED_REQUEST",
    ///   "ADMIN_IP_BLOCKED", "SENSITIVE_PAYLOAD_REJECTED".
    /// </param>
    /// <param name="sourceIp">
    ///   IP address of the request origin, or null when not available.
    /// </param>
    /// <param name="details">
    ///   Human-readable context. Must be sanitised — never include secret values,
    ///   credentials, or Authorization header content.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task AppendAsync(
        string            eventType,
        string?           sourceIp,
        string            details,
        CancellationToken cancellationToken = default);
}
