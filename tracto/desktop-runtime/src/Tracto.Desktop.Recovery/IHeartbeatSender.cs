namespace Tracto.Desktop.Recovery;

/// <summary>
/// Sends periodic heartbeat signals to the Orchestrator to keep the job lease alive.
/// </summary>
public interface IHeartbeatSender
{
    /// <summary>
    /// Sends a single heartbeat for the given <paramref name="leaseId"/>.
    /// </summary>
    /// <param name="leaseId">The active job lease identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendHeartbeatAsync(string leaseId, CancellationToken ct);
}
