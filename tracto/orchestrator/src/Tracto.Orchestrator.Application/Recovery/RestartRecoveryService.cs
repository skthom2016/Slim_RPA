using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// Explicit aliases prevent ambiguity when both Application.Abstractions and
// Domain namespaces define same-named types across different sessions.
using IWorkerRepository  = Tracto.Orchestrator.Application.Abstractions.IWorkerRepository;
using IRunRepository     = Tracto.Orchestrator.Application.Abstractions.IRunRepository;
using IJournalRepository = Tracto.Orchestrator.Application.Abstractions.IJournalRepository;
using IUnitOfWork        = Tracto.Orchestrator.Application.Abstractions.IUnitOfWork;
using IClock             = Tracto.Orchestrator.Application.Abstractions.IClock;
using JournalEntry       = Tracto.Orchestrator.Domain.Journal.JournalEntry;
using RunRecord          = Tracto.Orchestrator.Domain.Runs.RunRecord;
using RunState           = Tracto.Orchestrator.Domain.Runs.RunState;
using WorkerRecord       = Tracto.Orchestrator.Domain.Workers.WorkerRecord;

namespace Tracto.Orchestrator.Application.Recovery;

/// <summary>
/// Startup-phase background service that reconciles any RUNNING runs whose worker was
/// absent when the Orchestrator last shut down.
///
/// Algorithm (runs once in <see cref="StartAsync"/>):
///   1. Load all <see cref="WorkerRecord"/> rows where <c>IsOnline == false</c>.
///   2. For each offline worker, load all <see cref="RunRecord"/> rows in state RUNNING
///      where <c>RunRecord.WorkerId == WorkerRecord.WorkerId</c>.
///   3. For each such run: call <see cref="RunRecord.MarkUnknown"/> (sets NeedsAttention=true
///      and the unknownReason), then append a <see cref="JournalEntry"/>.
///   4. Commit the entire batch atomically via <see cref="IUnitOfWork.CommitAsync"/>.
///
/// Domain invariant 2: UNKNOWN always requires NeedsAttention=true + unknownReason; never
/// auto-replayed. This service satisfies that invariant through <see cref="RunRecord.MarkUnknown"/>.
///
/// IHostedService instances are resolved from the root (singleton) container.
/// Repositories and UnitOfWork are Scoped — they cannot be injected directly into a singleton.
/// Instead, this service injects <see cref="IServiceScopeFactory"/> and creates a short-lived
/// scope for its startup work so all scoped dependencies share a single DbContext instance.
/// </summary>
public sealed class RestartRecoveryService : IHostedService
{
    private const string UnknownReason = "Worker absent at restart";
    private const string EventType     = "WorkerAbsentAtRestart";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock               _clock;
    private readonly ILogger<RestartRecoveryService> _logger;

    public RestartRecoveryService(
        IServiceScopeFactory scopeFactory,
        IClock               clock,
        ILogger<RestartRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock        = clock;
        _logger       = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[RestartRecovery] Scanning for RUNNING runs whose worker is offline...");

        // Create a DI scope so all scoped dependencies (repositories, UnitOfWork)
        // share a single OrchestratorDbContext, satisfying the UoW pattern (invariant 5).
        await using var scope = _scopeFactory.CreateAsyncScope();
        var workers    = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
        var runs       = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var journal    = scope.ServiceProvider.GetRequiredService<IJournalRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        IReadOnlyList<WorkerRecord> offlineWorkers;
        try
        {
            offlineWorkers = await workers.FindOfflineAsync(cancellationToken);
        }
        catch (Exception ex) when (IsSchemaNotReadyException(ex))
        {
            // The database schema has not been created yet (e.g. first boot before migrations
            // complete, or an integration-test host started before EnsureCreated finished).
            // Nothing to recover; log and exit gracefully so the host can finish starting.
            _logger.LogWarning(
                ex,
                "[RestartRecovery] Database schema not ready at startup — recovery skipped.");
            return;
        }

        if (offlineWorkers.Count == 0)
        {
            _logger.LogInformation("[RestartRecovery] No offline workers found. Nothing to recover.");
            return;
        }

        _logger.LogWarning(
            "[RestartRecovery] Found {Count} offline worker(s). Checking for orphaned RUNNING runs.",
            offlineWorkers.Count);

        var now          = _clock.UtcNow;
        var recoveredIds = new List<Guid>();

        foreach (WorkerRecord worker in offlineWorkers)
        {
            IReadOnlyList<RunRecord> orphans =
                await runs.FindByWorkerAndStateAsync(
                    worker.WorkerId, RunState.Running, cancellationToken);

            foreach (RunRecord run in orphans)
            {
                run.MarkUnknown(UnknownReason, now);
                runs.Update(run);

                // Use JsonSerializer so WorkerId values containing '"' or '\' do not produce
                // malformed JSON (C1 fix: replace raw string interpolation with proper serialization).
                var payload = JsonSerializer.Serialize(new { workerId = worker.WorkerId });
                var entry = new JournalEntry(
                    runId:      run.Id,
                    attemptId:  run.AttemptId,
                    eventType:  EventType,
                    occurredAt: now,
                    payload:    payload);

                await journal.AppendAsync(entry, cancellationToken);
                recoveredIds.Add(run.Id);

                _logger.LogWarning(
                    "[RestartRecovery] Run {RunId} marked UNKNOWN -- worker '{WorkerId}' was absent.",
                    run.Id, worker.WorkerId);
            }
        }

        if (recoveredIds.Count == 0)
        {
            _logger.LogInformation(
                "[RestartRecovery] Offline workers present but no RUNNING runs were orphaned.");
            return;
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "[RestartRecovery] Committed {Count} recovery record(s): {RunIds}.",
                recoveredIds.Count,
                string.Join(", ", recoveredIds));
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "[RestartRecovery] Failed to commit recovery batch. Attempting rollback.");

            await unitOfWork.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Returns true when the exception (or any exception in its <see cref="Exception.InnerException"/>
    /// chain) indicates the database schema does not exist yet (e.g. migrations have not run, or an
    /// in-memory SQLite test DB was not initialised).
    /// Uses message-text matching instead of a direct SQLite dependency to keep the
    /// Application layer free of infrastructure references.
    /// Walks the full chain so that exceptions wrapped inside <see cref="AggregateException"/>
    /// or multi-level infrastructure wrappers are also detected (M2 fix).
    /// </summary>
    private static bool IsSchemaNotReadyException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Also check flattened AggregateException inner exceptions.
        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                if (IsSchemaNotReadyException(inner))
                    return true;
            }
        }
        return false;
    }
}
