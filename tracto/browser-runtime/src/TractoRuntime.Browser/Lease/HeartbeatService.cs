using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Lease;

/// <summary>
/// CEC-HOST-002: Sends periodic heartbeats to the Orchestrator while a lease is active.
/// Runs on a background loop; stops when <paramref name="ct"/> is cancelled or the lease is released.
/// Heartbeat failures are logged but do not crash the caller — the run engine decides when to act.
/// </summary>
public sealed class HeartbeatService(ILeaseManager leaseManager, ILogger<HeartbeatService> logger)
{
    /// <summary>
    /// Runs the heartbeat loop until <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="leaseToken">The active lease token to renew.</param>
    /// <param name="runId">The current run identifier (for logging).</param>
    /// <param name="interval">Time between consecutive heartbeats.</param>
    /// <param name="ct">Cancelled when the run ends or the lease is explicitly released.</param>
    public async Task RunAsync(
        string leaseToken,
        string runId,
        TimeSpan interval,
        CancellationToken ct)
    {
        int sequenceNumber = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await leaseManager.RenewAsync(
                    new HeartbeatRequest { LeaseToken = leaseToken, RunId = runId },
                    ct).ConfigureAwait(false);

                sequenceNumber++;
                logger.LogDebug(
                    "Heartbeat {Seq} sent — runId={RunId}",
                    sequenceNumber, runId);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log but do not rethrow — the lease fence and run engine handle expiry.
                // leaseToken is intentionally omitted from the log (secret token per DoD).
                logger.LogWarning(ex,
                    "Heartbeat failed — runId={RunId}",
                    runId);
            }
        }

        logger.LogDebug("Heartbeat loop stopped after {Seq} heartbeats — runId={RunId}",
            sequenceNumber, runId);
    }
}
