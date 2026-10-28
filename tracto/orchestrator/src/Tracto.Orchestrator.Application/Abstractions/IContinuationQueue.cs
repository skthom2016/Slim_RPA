#nullable enable
namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Application-layer port for enqueuing continuation creation work items (OC-08).
///
/// Called by <c>IngestCallbackCommandHandler</c> (OC-07) inside the same
/// <see cref="IUnitOfWork"/> transaction so that the enqueue and the
/// <c>CallbackRecord.Processed</c> flag flip are committed atomically
/// (domain invariant 5).
///
/// The Infrastructure implementation may write to an in-process channel,
/// a transactional outbox table, or a durable queue — the Application layer
/// is insulated from the choice.
/// </summary>
public interface IContinuationQueue
{
    /// <summary>
    /// Enqueues a request to create a continuation run derived from
    /// <paramref name="runId"/> / <paramref name="correlationId"/>.
    ///
    /// Must be called inside an active <see cref="IUnitOfWork"/> transaction
    /// so the enqueue is rolled back atomically if the commit fails.
    /// </summary>
    /// <param name="runId">Primary key of the parent HANDED_OFF run.</param>
    /// <param name="correlationId">Correlation token shared by the parent run and the callback.</param>
    /// <param name="callbackPayload">Raw JSON payload forwarded opaquely from the callback body.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task EnqueueAsync(
        Guid              runId,
        Guid              correlationId,
        string            callbackPayload,
        CancellationToken cancellationToken = default);
}
