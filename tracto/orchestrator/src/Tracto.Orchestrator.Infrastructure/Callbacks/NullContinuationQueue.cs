#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;

namespace Tracto.Orchestrator.Infrastructure.Callbacks;

/// <summary>
/// No-op implementation of <see cref="IContinuationQueue"/> for use in production
/// deployments where the async callback continuation is handled by an external worker
/// process or a background job (not the in-process synchronous path).
///
/// Logs the enqueue request at Debug level so the operator can confirm the callback
/// was received by the orchestrator.  The continuation work item is intentionally
/// not persisted by this implementation — use a durable queue implementation in
/// high-availability deployments.
///
/// For integration tests, replace this registration with
/// <c>TestSynchronousContinuationQueue</c> (defined in the test project) so that
/// Run B is created synchronously within the same HTTP request, making assertions
/// straightforward.
/// </summary>
public sealed class NullContinuationQueue : IContinuationQueue
{
    private readonly ILogger<NullContinuationQueue> _logger;

    public NullContinuationQueue(ILogger<NullContinuationQueue> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This implementation logs the item and returns immediately without creating Run B.
    /// The continuation must be driven by an external process or background service.
    /// </remarks>
    public Task EnqueueAsync(
        Guid              runId,
        Guid              correlationId,
        string            callbackPayload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullContinuationQueue: enqueued continuation for RunId={RunId} " +
            "CorrelationId={CorrelationId}. No synchronous Run B creation in this deployment.",
            runId, correlationId);

        return Task.CompletedTask;
    }
}
