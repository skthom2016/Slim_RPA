#nullable enable
namespace Tracto.Orchestrator.Domain.Leases;

/// <summary>
/// Computed heartbeat-health state for a <see cref="LeaseRecord"/>.
/// Derived at runtime by comparing LastHeartbeatAt to the current clock; not persisted.
///
///   Active  – heartbeat received within the expected window.
///   Suspect – no heartbeat yet or last heartbeat older than the warning threshold
///             but still within the expiry grace period.
///   Expired – heartbeat overdue beyond the expiry grace period; the lease is stale.
/// </summary>
public enum LeaseExpiryState
{
    Active,
    Suspect,
    Expired
}
