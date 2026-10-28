#nullable enable
namespace Tracto.Orchestrator.Domain.Leases;

/// <summary>
/// Lifecycle states for a <see cref="LeaseRecord"/>.
/// Offered → Accepted (worker accepted the job, RunRecord transitions to LEASED).
/// Offered → Expired  (30-second offer window elapsed without acknowledgement).
/// </summary>
public enum LeaseState
{
    Offered,
    Accepted,
    Expired
}
