#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Leases;

namespace Tracto.Orchestrator.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ILeaseRepository"/>.
/// All mutations participate in the ambient UnitOfWork transaction.
/// Queries are aligned with the indexes declared in OrchestratorDbContext:
///   IX_LeaseRecords_WorkerId_State, IX_LeaseRecords_RunId_State, IX_LeaseRecords_InteractiveSessionId.
/// </summary>
public sealed class EfLeaseRepository : ILeaseRepository
{
    private readonly OrchestratorDbContext _db;

    public EfLeaseRepository(OrchestratorDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<LeaseRecord?> GetByIdAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords.FindAsync([leaseId], cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Uses IX_LeaseRecords_WorkerId_State.</remarks>
    public async Task<LeaseRecord?> FindActiveForWorkerAsync(
        string workerId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.WorkerId == workerId && l.State != LeaseState.Expired)
                    .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Uses IX_LeaseRecords_RunId_State.</remarks>
    public async Task<bool> HasActiveOfferForRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .AnyAsync(l => l.RunId == runId && l.State != LeaseState.Expired,
                              cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Uses IX_LeaseRecords_InteractiveSessionId.</remarks>
    public async Task<bool> HasActiveSessionLeaseAsync(
        string interactiveSessionId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .AnyAsync(l => l.InteractiveSessionId == interactiveSessionId
                                   && l.State != LeaseState.Expired,
                              cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Uses IX_LeaseRecords_RunId_State.</remarks>
    public async Task<LeaseRecord?> FindActiveLeaseForRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.RunId == runId && l.State != LeaseState.Expired)
                    .OrderByDescending(l => l.OfferedAt)
                    .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the most recent lease for the run regardless of state (Offered, Accepted, or Expired).
    /// Used by AcceptLeaseCommandHandler to distinguish "no lease ever" (404) from "lease expired" (409).
    /// </remarks>
    public async Task<LeaseRecord?> FindByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.RunId == runId)
                    .OrderByDescending(l => l.OfferedAt)
                    .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LeaseRecord>> FindExpiredOffersAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.State == LeaseState.Offered && l.ExpiresAt < asOf)
                    .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Finds Accepted leases that are overdue for a heartbeat.
    /// <list type="bullet">
    ///   <item>If the lease HAS sent at least one heartbeat: it is stale when
    ///         <c>LastHeartbeatAt &lt; heartbeatThreshold</c>.</item>
    ///   <item>If the lease has NEVER sent a heartbeat (<c>LastHeartbeatAt == null</c>):
    ///         the grace period starts from <c>AcceptedAt</c>, so it is stale only when
    ///         <c>AcceptedAt &lt; heartbeatThreshold</c>.  This prevents newly-accepted
    ///         leases from being expired on the first scan tick before the worker has had
    ///         a chance to send its first heartbeat.</item>
    /// </list>
    /// The threshold is computed by the caller as: <c>now - ExpiryWindow</c>.
    /// </remarks>
    public async Task<IReadOnlyList<LeaseRecord>> FindAcceptedWithExpiredHeartbeatAsync(
        DateTimeOffset heartbeatThreshold,
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.State == LeaseState.Accepted
                                && ((l.LastHeartbeatAt == null && l.AcceptedAt < heartbeatThreshold)
                                    || l.LastHeartbeatAt < heartbeatThreshold))
                    .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns every lease whose State is <see cref="LeaseState.Expired"/>, covering both
    /// leases that expired during the Offered window and those expired by OC-05 after the
    /// heartbeat window lapsed.  The ReconciliationService (OC-10) filters further to find
    /// runs that still need state reconciliation.
    /// </remarks>
    public async Task<IReadOnlyList<LeaseRecord>> FindExpiredAsync(
        CancellationToken cancellationToken = default)
        => await _db.LeaseRecords
                    .Where(l => l.State == LeaseState.Expired)
                    .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(LeaseRecord lease, CancellationToken cancellationToken = default)
        => await _db.LeaseRecords.AddAsync(lease, cancellationToken);

    /// <inheritdoc/>
    public void Update(LeaseRecord lease)
        => _db.LeaseRecords.Update(lease);
}
