namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Encapsulates a single atomic unit of work.
/// The Infrastructure layer provides an EF Core implementation that wraps a DbContext
/// transaction. All run-state mutations, worker-record updates and journal entries
/// must be committed through one CommitAsync call (Domain Invariant 5).
///
/// Extends <see cref="IAsyncDisposable"/> so that callers can use <c>await using</c>
/// to guarantee the underlying transaction is rolled back if CommitAsync is never called
/// (e.g. when an exception escapes before CommitAsync is reached).
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Explicitly begins the underlying database transaction.
    /// Safe to call multiple times; a transaction is opened only once.
    /// Useful when callers need to stage SaveChanges calls before a final CommitAsync.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes all pending changes and commits the underlying database transaction.
    /// Throws on failure; the caller must not retry without re-creating the unit of work.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the underlying database transaction, discarding all pending changes.
    /// Safe to call even when no transaction is active.
    /// </summary>
    Task RollbackAsync();
}
