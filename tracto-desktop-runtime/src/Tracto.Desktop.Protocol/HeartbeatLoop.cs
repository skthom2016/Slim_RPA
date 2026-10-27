#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Sends periodic heartbeats to the Orchestrator for an active lease.
/// A gap greater than the configured interval causes the Orchestrator to expire the lease.
/// </summary>
public sealed class HeartbeatLoop
{
    /// <summary>
    /// Runs the heartbeat loop, sending ProcessHeartbeat at the given interval until cancellation.
    /// The loop exits cleanly when <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="orch">The Orchestrator server.</param>
    /// <param name="workerId">Worker identifier included in heartbeat messages.</param>
    /// <param name="leaseId">The active lease identifier to renew.</param>
    /// <param name="intervalMs">Interval between heartbeats in milliseconds.</param>
    /// <param name="ct">Cancellation token; cancellation stops the loop.</param>
    public static async Task RunAsync(
        IOrchestratorServer orch,
        string workerId,
        string leaseId,
        int intervalMs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orch);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        int sequence = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                    break;

                var heartbeat = new Heartbeat(
                    LeaseId:          leaseId,
                    WorkerId:         workerId,
                    SequenceNumber:   sequence++,
                    Timestamp:        DateTimeOffset.UtcNow,
                    CurrentStepId:    null,
                    CurrentRunState:  RunState.Running);

                await orch.ProcessHeartbeatAsync(heartbeat, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — exit cleanly.
        }
    }
}
