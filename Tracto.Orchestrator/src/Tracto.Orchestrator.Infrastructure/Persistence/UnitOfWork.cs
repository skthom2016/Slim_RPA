using Microsoft.EntityFrameworkCore.Storage;
using Tracto.Orchestrator.Application.Abstractions;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
/// Wraps the DbContext in an explicit transaction so that business changes
/// and journal entries are always flushed atomically (domain invariant 5).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork, IAsyncDisposable, IDisposable
{
    private readonly OrchestratorDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(OrchestratorDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Explicitly opens the underlying database transaction (idempotent).
    /// </summary>
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        _transaction ??= await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Opens a transaction on the first call, then flushes all changes in one round-trip.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _transaction ??= await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <summary>Rolls back the open transaction without persisting any changes.</summary>
    public async Task RollbackAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
