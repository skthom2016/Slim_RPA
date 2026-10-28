#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Leases;

namespace Tracto.Orchestrator.Application.Jobs;

/// <summary>
/// Application-layer service that performs one heartbeat-expiry scan cycle (OC-05).
///
/// Encapsulates the scan algorithm so it can be unit-tested without a background loop.
/// Production hosting is provided by <c>Infrastructure.Recovery.LeaseExpiryMonitorWorker</c>,
/// which creates a per-tick DI scope and resolves the Infrastructure-layer counterpart
/// of this class that wraps the same algorithm via <c>IServiceScopeFactory</c>.
///
/// This class is a plain, framework-free service — it intentionally does NOT implement
/// <c>IHostedService</c> so the Application layer stays free of <c>Microsoft.Extensions.Hosting</c>
/// references (coding rule: Application layer has no framework/EF deps).
/// </summary>
public sealed class LeaseExpiryMonitorService
{
    /// <summary>How long without a heartbeat before an Accepted lease is expired.</summary>
    public static readonly TimeSpan ExpiryWindow = TimeSpan.FromSeconds(60);

    /// <summary>How frequently the background worker should poll for stale leases.</summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);

    private readonly ILeaseRepository _leaseRepo;
    private readonly IUnitOfWork      _unitOfWork;
    private readonly IClock           _clock;
    private readonly ILogger<LeaseExpiryMonitorService> _logger;

    public LeaseExpiryMonitorService(
        ILeaseRepository leaseRepo,
        IUnitOfWork      unitOfWork,
        IClock           clock,
        ILogger<LeaseExpiryMonitorService> logger)
    {
        _leaseRepo  = leaseRepo;
        _unitOfWork = unitOfWork;
        _clock      = clock;
        _logger     = logger;
    }

    // ── Core scan ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs one scan: finds all Accepted leases whose heartbeat is overdue and expires them.
    /// Each lease expiry is committed in its own atomic transaction so a single failure does
    /// not prevent the remainder of the batch from being processed.
    /// </summary>
    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var now       = _clock.UtcNow;
        var threshold = now - ExpiryWindow;

        var stale = await _leaseRepo.FindAcceptedWithExpiredHeartbeatAsync(
            threshold, cancellationToken);

        if (stale.Count == 0)
            return;

        _logger.LogWarning(
            "LeaseExpiryMonitorService: found {Count} accepted lease(s) with expired heartbeat " +
            "(threshold={Threshold:O}).",
            stale.Count, threshold);

        foreach (var lease in stale)
        {
            var committed = false;
            try
            {
                await _unitOfWork.BeginAsync(cancellationToken);

                lease.ExpireAccepted();
                _leaseRepo.Update(lease);

                await _unitOfWork.CommitAsync(cancellationToken);
                committed = true;

                _logger.LogWarning(
                    "LeaseExpiryMonitorService: expired lease {LeaseId} for run {RunId} " +
                    "(lastHeartbeat={LastHeartbeat:O}, worker={WorkerId}).",
                    lease.LeaseId, lease.RunId,
                    lease.LastHeartbeatAt, lease.WorkerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "LeaseExpiryMonitorService: failed to expire lease {LeaseId} for run {RunId}.",
                    lease.LeaseId, lease.RunId);
            }
            finally
            {
                if (!committed)
                    await _unitOfWork.RollbackAsync();
            }
        }
    }
}
