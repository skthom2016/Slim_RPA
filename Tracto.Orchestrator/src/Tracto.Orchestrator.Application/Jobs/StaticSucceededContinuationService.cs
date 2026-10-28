#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Application.Jobs;

/// <summary>
/// Called by <see cref="CommitFinalResultCommandHandler"/> immediately after a
/// SUCCEEDED state is committed, to check for and trigger any matching
/// continuation mapping (OC-08 StaticSucceededContinuationService).
///
/// Design:
///   - Idempotent: if Run B already exists for this (SourceRunId, SUCCEEDED) pair, skip.
///   - Does not throw: any failure is logged and swallowed so the caller's commit is unaffected.
///   - The source run state is never modified by this service.
/// </summary>
public sealed class StaticSucceededContinuationService
{
    private readonly IContinuationMappingRepository         _mappingRepo;
    private readonly IContinuationRunRepository             _continuationRunRepo;
    private readonly CreateContinuationRunCommandHandler    _continuationHandler;
    private readonly ILogger<StaticSucceededContinuationService> _logger;

    public StaticSucceededContinuationService(
        IContinuationMappingRepository         mappingRepo,
        IContinuationRunRepository             continuationRunRepo,
        CreateContinuationRunCommandHandler    continuationHandler,
        ILogger<StaticSucceededContinuationService> logger)
    {
        _mappingRepo         = mappingRepo;
        _continuationRunRepo = continuationRunRepo;
        _continuationHandler = continuationHandler;
        _logger              = logger;
    }

    /// <summary>
    /// Checks whether a continuation mapping exists for (sourceRunId, SUCCEEDED).
    /// If found and not already dedup'd, creates Run B.
    /// This method never throws; all errors are logged.
    /// </summary>
    public async Task OnSucceededAsync(
        Guid              sourceRunId,
        string?           businessJobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Fast path: if Run B already recorded, no work needed.
            var existing = await _continuationRunRepo.FindAsync(
                sourceRunId, RunState.Succeeded, cancellationToken);

            if (existing is not null)
            {
                _logger.LogDebug(
                    "StaticSucceededContinuationService: Run B already exists for " +
                    "SourceRunId={SourceRunId}. Skipping (idempotent).", sourceRunId);
                return;
            }

            // Check whether a mapping exists at all before invoking the full handler.
            var mapping = await _mappingRepo.FindBestMatchAsync(
                sourceRunId, RunState.Succeeded, cancellationToken);

            if (mapping is null)
            {
                _logger.LogDebug(
                    "StaticSucceededContinuationService: no SUCCEEDED mapping for " +
                    "SourceRunId={SourceRunId}. No Run B required.", sourceRunId);
                return;
            }

            // Delegate to the full handler (includes dedup guard, Run B creation, journal).
            var cmd    = new CreateContinuationRunCommand(sourceRunId, RunState.Succeeded, businessJobId);
            var result = await _continuationHandler.HandleAsync(cmd, cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogError(
                    "StaticSucceededContinuationService: Run B creation failed for " +
                    "SourceRunId={SourceRunId}: {Code} — {Message}.",
                    sourceRunId, result.Error.Code, result.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "StaticSucceededContinuationService: outcome={Outcome} RunBId={RunBId} " +
                    "for SourceRunId={SourceRunId}.",
                    result.Value.Outcome, result.Value.RunBId, sourceRunId);
            }
        }
        catch (Exception ex)
        {
            // Swallow: the caller's commit must not be rolled back due to continuation failures.
            _logger.LogError(ex,
                "StaticSucceededContinuationService: unexpected error for SourceRunId={SourceRunId}.",
                sourceRunId);
        }
    }
}
