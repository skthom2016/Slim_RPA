namespace Tracto.Orchestrator.Domain.Security;

/// <summary>
/// Domain port for appending and querying security events.
/// Implementations live in Infrastructure; the Application layer depends on this interface.
/// </summary>
public interface ISecurityEventLog
{
    /// <summary>Persists a <see cref="SecurityEvent"/> to the durable log.</summary>
    Task AppendAsync(SecurityEvent securityEvent, CancellationToken ct = default);
}
