#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ILeaseRepository = Tracto.Orchestrator.Application.Abstractions.ILeaseRepository;
using IUnitOfWork      = Tracto.Orchestrator.Application.Abstractions.IUnitOfWork;
using IClock           = Tracto.Orchestrator.Application.Abstractions.IClock;
using LeaseRecord      = Tracto.Orchestrator.Domain.Leases.LeaseRecord;

namespace Tracto.Orchestrator.Infrastructure.Recovery;

/// <summary>
/// Domain service that expires Accepted <c>LeaseRecord</c>s whose heartbeat has
/// not been updated within the configured <see cref="ExpiryWindow"/> (OC-05).
///
/// Each stale lease is expired and committed in its own atomic transaction so that
/// a failure for one lease does not block expiry of the others.
///
/// No run-state changes are made here — that is handled by OC-10 reconciliation.
///
/// This class is a plain scoped service registered by <see cref="PersistenceServiceExtensions"/>.
/// The background polling loop lives in <see cref="LeaseExpiryMonitorWorker"/>.
/// </summary>
public sealed class LeaseExpiryMonitorService
{
    /// <summary>
    /// How long without a heartbeat before an Accepted lease is considered expired.
    /// Must match <see cref="Application.Jobs.LeaseExpiryMonitorService.ExpiryWindow"/> exactly;
    /// the unit tests validate against the Application-layer constant.
    /// </summary>
    public static readonly TimeSpan ExpiryWindow = TimeSpan.FromSeconds(60);

    /// <summary>How often the background worker polls for stale leases.</summary>
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);

    private readonly ILeaseRepository _leaseRepo;
    private readonly IUnitOfWork      _unitOfWork;
    private readonly IClock           _clock;
    private readonly ILogger<LeaseExpiryMonitorService> _logger;

    public LeaseExpiryMonitorService(
        ILeaseRepository                     leaseRepo,
        IUnitOfWork                          unitOfWork,
        IClock                               clock,
        ILogger<LeaseExpiryMonitorService>   logger)
    {
        _leaseRepo  = leaseRepo;
        _unitOfWork = unitOfWork;
        _clock      = clock;
        _logger     = logger;
    }

    /// <summary>
    /// Performs one scan cycle: finds all Accepted leases with an expired heartbeat and
    /// marks each one Expired in its own atomic transaction.
    ///
    /// Heartbeat threshold: <c>now - <see cref="ExpiryWindow"/></c>.
    /// A lease whose <c>LastHeartbeatAt</c> is null (never heartbeated) is also included.
    /// </summary>
    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var now       = _clock.UtcNow;
        var threshold = now - ExpiryWindow;

        var staleLeases = await _leaseRepo.FindAcceptedWithExpiredHeartbeatAsync(
            threshold, cancellationToken);

        if (staleLeases.Count == 0)
            return;

        // Expire each lease in its own transaction so a failure for one does not
        // prevent expiry of the rest.
        foreach (var lease in staleLeases)
        {
            await ExpireLeaseAsync(lease, cancellationToken);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task ExpireLeaseAsync(LeaseRecord lease, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginAsync(cancellationToken);
        var committed = false;

        try
        {
            lease.ExpireAccepted();
            _leaseRepo.Update(lease);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogWarning(
                "LeaseExpiryMonitorService: expired stale lease {LeaseId} for run {RunId} " +
                "(workerId={WorkerId}, lastHeartbeat={LastHeartbeat:O}).",
                lease.LeaseId,
                lease.RunId,
                lease.WorkerId,
                lease.LastHeartbeatAt);
        }
        catch (Exception ex)
        {
            // Log and swallow so a single DB fault does not abort the rest of the batch.
            // The lease will be retried on the next scan tick.
            _logger.LogError(ex,
                "LeaseExpiryMonitorService: failed to expire lease {LeaseId} — will retry on next tick.",
                lease.LeaseId);
        }
        finally
        {
            // Always roll back when the commit did not succeed.  Running rollback after a
            // successful commit is a no-op in SQLite, but guard with the flag anyway for
            // clarity and to avoid any provider-specific side-effects.
            if (!committed)
            {
                try { await _unitOfWork.RollbackAsync(); }
                catch (Exception rbEx)
                {
                    _logger.LogError(rbEx,
                        "LeaseExpiryMonitorService: rollback failed for lease {LeaseId}.",
                        lease.LeaseId);
                }
            }
        }
    }
}

// ── Background worker ─────────────────────────────────────────────────────────

/// <summary>
/// Singleton <see cref="BackgroundService"/> that drives the <see cref="LeaseExpiryMonitorService"/>
/// heartbeat-expiry scan on a fixed <see cref="LeaseExpiryMonitorService.PollingInterval"/>.
///
/// IHostedService instances live in the singleton root container.
/// All Scoped dependencies are resolved through a per-tick <see cref="IServiceScope"/>
/// so repositories and the UnitOfWork share a single <c>DbContext</c> per scan cycle.
/// </summary>
public sealed class LeaseExpiryMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaseExpiryMonitorWorker> _logger;

    public LeaseExpiryMonitorWorker(
        IServiceScopeFactory              scopeFactory,
        ILogger<LeaseExpiryMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LeaseExpiryMonitorWorker started (pollingInterval={Interval}s, expiryWindow={Window}s).",
            LeaseExpiryMonitorService.PollingInterval.TotalSeconds,
            LeaseExpiryMonitorService.ExpiryWindow.TotalSeconds);

        using var timer = new PeriodicTimer(LeaseExpiryMonitorService.PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope   = _scopeFactory.CreateAsyncScope();
                var scanService         = scope.ServiceProvider
                                              .GetRequiredService<LeaseExpiryMonitorService>();
                await scanService.ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "LeaseExpiryMonitorWorker: unhandled exception during scan tick — will retry next interval.");
            }
        }

        _logger.LogInformation("LeaseExpiryMonitorWorker stopped.");
    }
}
