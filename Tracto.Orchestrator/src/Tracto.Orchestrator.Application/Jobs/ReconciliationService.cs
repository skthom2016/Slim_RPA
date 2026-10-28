#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;

namespace Tracto.Orchestrator.Application.Jobs;

/// <summary>
/// Application-layer service that performs one reconciliation scan cycle (OC-10).
///
/// Every scan tick:
///   1. Finds all Expired lease records (set by OC-05 LeaseExpiryMonitorService).
///   2. For each Expired lease whose run is still non-terminal and whose AttemptId
///      matches the run's current fence, loads the persisted journal entries.
///   3. Dispatches <see cref="ReconcileRunCommandHandler"/> to transition the run to
///      SUCCEEDED (journal confirms it) or UNKNOWN (journal is empty/uncertain).
///
/// NEVER auto-replays: reconciliation must NOT set State=QUEUED or CREATED (invariant 2).
///
/// This is a plain, framework-free service — it intentionally does NOT implement
/// IHostedService so the Application layer stays free of Microsoft.Extensions.Hosting
/// references (coding rule: Application layer has no framework/EF deps).
/// Production hosting is provided by Infrastructure.Recovery.ReconciliationWorker,
/// which creates a per-tick DI scope and resolves this class.
/// </summary>
public sealed class ReconciliationService
{
    /// <summary>How frequently the background worker should poll for leases needing reconciliation.</summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

    private readonly ILeaseRepository              _leaseRepo;
    private readonly IRunRepository                _runRepo;
    private readonly IJournalRepository            _journalRepo;
    private readonly ReconcileRunCommandHandler    _reconcileHandler;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        ILeaseRepository               leaseRepo,
        IRunRepository                 runRepo,
        IJournalRepository             journalRepo,
        ReconcileRunCommandHandler     reconcileHandler,
        ILogger<ReconciliationService> logger)
    {
        _leaseRepo        = leaseRepo;
        _runRepo          = runRepo;
        _journalRepo      = journalRepo;
        _reconcileHandler = reconcileHandler;
        _logger           = logger;
    }

    /// <summary>
    /// Performs one reconciliation scan cycle.
    ///
    /// Finds all Expired leases and reconciles any run that is still non-terminal.
    /// Each run is processed in isolation so a single failure does not abort the rest.
    /// </summary>
    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var expiredLeases = await _leaseRepo.FindExpiredAsync(cancellationToken);

        if (expiredLeases.Count == 0)
            return;

        _logger.LogInformation(
            "ReconciliationService: {Count} expired lease(s) found; evaluating for reconciliation.",
            expiredLeases.Count);

        foreach (var lease in expiredLeases)
        {
            try
            {
                // ── Load run ──────────────────────────────────────────────────
                var run = await _runRepo.GetByIdAsync(lease.RunId, cancellationToken);
                if (run is null)
                {
                    _logger.LogWarning(
                        "ReconciliationService: run {RunId} not found for expired lease {LeaseId} — skipping.",
                        lease.RunId, lease.LeaseId);
                    continue;
                }

                // ── Skip already-terminal runs ─────────────────────────────────
                if (run.IsTerminal())
                {
                    _logger.LogDebug(
                        "ReconciliationService: run {RunId} is already terminal ({State}) — skipping.",
                        run.Id, run.State);
                    continue;
                }

                // ── Skip stale leases (AttemptId mismatch) ─────────────────────
                if (run.AttemptId != lease.AttemptId)
                {
                    _logger.LogDebug(
                        "ReconciliationService: stale lease {LeaseId} (leaseAttempt={LeaseAttempt}) " +
                        "does not match run {RunId} current attempt {RunAttempt} — skipping.",
                        lease.LeaseId, lease.AttemptId, run.Id, run.AttemptId);
                    continue;
                }

                // ── Load persisted journal entries for this run+attempt ─────────
                var allEntries     = await _journalRepo.GetByRunIdAsync(lease.RunId, cancellationToken);
                var matchedEntries = allEntries
                    .Where(e => e.AttemptId == lease.AttemptId)
                    .Select(e => new ReconciliationJournalEntry(
                        EventType:  e.EventType,
                        AttemptId:  e.AttemptId,
                        OccurredAt: e.OccurredAt))
                    .ToArray();

                // ── Dispatch reconciliation ────────────────────────────────────
                var command = new ReconcileRunCommand(
                    RunId:          lease.RunId,
                    AttemptId:      lease.AttemptId,
                    JournalEntries: matchedEntries);

                var result = await _reconcileHandler.HandleAsync(command, cancellationToken);

                if (result.IsSuccess)
                {
                    _logger.LogInformation(
                        "ReconciliationService: run {RunId} → {State} " +
                        "(lease={LeaseId}, alreadyTerminal={Already}).",
                        lease.RunId, result.Value.ReconciledState,
                        lease.LeaseId, result.Value.WasAlreadyTerminal);
                }
                else
                {
                    _logger.LogWarning(
                        "ReconciliationService: reconcile failed for run {RunId} " +
                        "(lease={LeaseId}): [{Code}] {Message}.",
                        lease.RunId, lease.LeaseId,
                        result.Error.Code, result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ReconciliationService: unhandled exception processing lease {LeaseId} " +
                    "for run {RunId} — will retry on next tick.",
                    lease.LeaseId, lease.RunId);
            }
        }
    }
}
