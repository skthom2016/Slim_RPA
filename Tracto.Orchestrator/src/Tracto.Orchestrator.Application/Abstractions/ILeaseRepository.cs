#nullable enable
using Tracto.Orchestrator.Domain.Leases;

namespace Tracto.Orchestrator.Application.Abstractions;

/// <summary>
/// Repository contract for <see cref="LeaseRecord"/> aggregates (OC-03).
/// All methods participate in the ambient <see cref="IUnitOfWork"/> transaction.
/// Implemented by <c>EfLeaseRepository</c> in the Infrastructure layer.
/// Method names and return types match the EfLeaseRepository implementation exactly
/// so that Moq-backed unit tests and the production EF implementation are consistent.
/// </summary>
public interface ILeaseRepository
{
    /// <summary>Retrieves a lease by its primary key. Returns null when not found.</summary>
    Task<LeaseRecord?> GetByIdAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the single non-Expired lease held by <paramref name="workerId"/>,
    /// or null when the worker holds no active lease.
    /// Used to enforce the single-lease-per-worker invariant.
    /// </summary>
    Task<LeaseRecord?> FindActiveForWorkerAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when any non-Expired LeaseRecord for <paramref name="runId"/> exists,
    /// meaning the run is already offered to another worker.
    /// </summary>
    Task<bool> HasActiveOfferForRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when any non-Expired LeaseRecord with <paramref name="interactiveSessionId"/>
    /// exists. Used to enforce headed-worker exclusivity per session (invariant 6).
    /// </summary>
    Task<bool> HasActiveSessionLeaseAsync(string interactiveSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent non-Expired lease for <paramref name="runId"/>, or null when none exists.
    /// Used by AcceptLeaseCommandHandler to load the active offer for a run.
    /// </summary>
    Task<LeaseRecord?> FindActiveLeaseForRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent lease for <paramref name="runId"/> in any state (Offered, Accepted, or Expired).
    /// Returns null when no lease record exists for the run at all.
    /// Used by AcceptLeaseCommandHandler (OC-04) to distinguish "no lease ever" (404)
    /// from "lease expired" (409 LEASE_EXPIRED).
    /// </summary>
    Task<LeaseRecord?> FindByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns all Offered leases whose ExpiresAt is before <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<LeaseRecord>> FindExpiredOffersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all leases currently in <see cref="LeaseState.Expired"/> state,
    /// regardless of whether they were originally Offered or Accepted before expiry.
    /// Used by <see cref="ReconciliationService"/> (OC-10) to find runs that may need
    /// state reconciliation after a worker disconnect or orchestrator restart.
    /// </summary>
    Task<IReadOnlyList<LeaseRecord>> FindExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all Accepted leases whose LastHeartbeatAt is older than <paramref name="heartbeatThreshold"/>
    /// (or whose LastHeartbeatAt is null, indicating no heartbeat has ever been received).
    /// Used by <c>LeaseExpiryMonitorService</c> (OC-05) to detect stale worker connections.
    /// </summary>
    Task<IReadOnlyList<LeaseRecord>> FindAcceptedWithExpiredHeartbeatAsync(
        DateTimeOffset heartbeatThreshold,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new lease. Throws when a lease with the same LeaseId already exists.</summary>
    Task AddAsync(LeaseRecord lease, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing lease as modified for EF Core change tracking.</summary>
    void Update(LeaseRecord lease);
}
