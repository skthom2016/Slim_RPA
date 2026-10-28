#nullable enable
namespace Tracto.Orchestrator.Domain.Callbacks;

/// <summary>
/// Persistence contract for <see cref="CallbackRecord"/>.
/// Implemented in Infrastructure; consumed by Application handlers.
/// </summary>
public interface ICallbackRepository
{
    /// <summary>
    /// Returns the stored callback with the given <paramref name="eventId"/>,
    /// or <c>null</c> when no such record exists.
    /// Used for deduplication: if a record is found, return 200 DUPLICATE.
    /// </summary>
    Task<CallbackRecord?> FindByEventIdAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Persists a new <see cref="CallbackRecord"/>.
    /// Must be called inside the same <c>IUnitOfWork</c> transaction as the
    /// continuation enqueue and the journal entry (invariant 5).
    /// </summary>
    Task AddAsync(CallbackRecord record, CancellationToken ct = default);
}
