#nullable enable

using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Implements the job-pull loop: requests a job from the Orchestrator, accepts the lease,
/// and returns a populated <see cref="JobContext"/>. Handles NoPendingJob with a configurable delay.
/// </summary>
public sealed class JobPullLoop
{
    private readonly string _workerId;
    private readonly int _pollIntervalMs;

    /// <summary>
    /// Creates a new pull loop.
    /// </summary>
    /// <param name="workerId">Worker identifier used in pull requests.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait when no job is available before retrying.</param>
    public JobPullLoop(string workerId, int pollIntervalMs = 5_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        _workerId       = workerId;
        _pollIntervalMs = pollIntervalMs;
    }

    /// <summary>
    /// Pulls the next available job. Retries with a delay on NoPendingJob.
    /// Calls AcceptLease once a job is offered and records leaseId + attemptId in the returned <see cref="JobContext"/>.
    /// </summary>
    public async Task<JobContext> PullJobAsync(IOrchestratorServer orch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orch);
        string? lastAttemptId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var request = new PullJobRequest(
                WorkerId:          _workerId,
                LastSeenAttemptId: lastAttemptId,
                RequestedAt:       DateTimeOffset.UtcNow);

            var response = await orch.PullJobAsync(request, ct).ConfigureAwait(false);

            switch (response)
            {
                case JobOffered offered:
                {
                    var lease = offered.Lease;
                    lastAttemptId = lease.AttemptId;

                    var accepted = new LeaseAccepted(
                        LeaseId:    lease.LeaseId,
                        WorkerId:   _workerId,
                        AcceptedAt: DateTimeOffset.UtcNow);

                    await orch.AcceptLeaseAsync(accepted, ct).ConfigureAwait(false);

                    return new JobContext
                    {
                        LeaseId       = lease.LeaseId,
                        AttemptId     = lease.AttemptId,
                        RunId         = lease.RunId,
                        ArtifactUrl   = lease.ArtifactUrl,
                        ArtifactDigest = lease.ArtifactDigest,
                        LeaseDurationMs = lease.LeaseDurationMs,
                    };
                }

                case NoPendingJob noPending:
                {
                    int delay = noPending.Message.RetryAfterMs > 0
                        ? noPending.Message.RetryAfterMs
                        : _pollIntervalMs;

                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    break;
                }

                default:
                    // Unknown discriminant — treat as no-job to avoid tight loop.
                    await Task.Delay(_pollIntervalMs, ct).ConfigureAwait(false);
                    break;
            }
        }
    }
}
