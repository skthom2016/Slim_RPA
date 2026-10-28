#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Runs;

namespace Tracto.Orchestrator.Infrastructure.Services;

/// <summary>
/// In-process <see cref="IContinuationQueue"/> implementation that immediately
/// invokes <see cref="CreateContinuationRunCommandHandler"/> inside a fresh
/// DI scope (OC-07 / OC-08).
///
/// Using a fresh scope avoids nested-transaction conflicts: the callback handler's
/// outer transaction manages the <see cref="CallbackRecord"/>; this queue creates
/// Run B in its own, independently committed transaction via a new DbContext.
///
/// Because the source run is always committed (HANDED_OFF) before the callback
/// arrives, the new scope can reliably read it even while the callback transaction
/// is still open (read-committed isolation with SQLite in-memory shared cache).
/// </summary>
public sealed class ScopedContinuationQueue : IContinuationQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScopedContinuationQueue> _logger;

    public ScopedContinuationQueue(
        IServiceScopeFactory            scopeFactory,
        ILogger<ScopedContinuationQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        Guid              runId,
        Guid              correlationId,
        string            callbackPayload,
        CancellationToken cancellationToken = default)
    {
        using var scope   = _scopeFactory.CreateScope();
        var       handler = scope.ServiceProvider
                                 .GetRequiredService<CreateContinuationRunCommandHandler>();

        var command = new CreateContinuationRunCommand(
            SourceRunId:  runId,
            TriggerState: RunState.HandedOff);

        var result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError(
                "ScopedContinuationQueue: continuation failed for RunId={RunId} " +
                "CorrelationId={CorrelationId}: [{Code}] {Message}",
                runId, correlationId, result.Error.Code, result.Error.Message);
        }
        else
        {
            _logger.LogInformation(
                "ScopedContinuationQueue: continuation enqueued for RunId={RunId} " +
                "CorrelationId={CorrelationId} → Outcome={Outcome} RunBId={RunBId}",
                runId, correlationId, result.Value.Outcome, result.Value.RunBId);
        }
    }
}
