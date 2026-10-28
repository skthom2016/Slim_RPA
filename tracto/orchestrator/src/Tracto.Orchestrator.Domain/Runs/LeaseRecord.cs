// LeaseRecord has been consolidated into Tracto.Orchestrator.Domain.Leases.
// This file is intentionally emptied to prevent duplicate-type compile errors.
// Use Domain.Leases.LeaseRecord everywhere.
namespace Tracto.Orchestrator.Domain.Runs;
/* canonical type lives in Domain.Leases — nothing to declare here */
#if false

/// <summary>
/// Represents a job offer extended to a worker agent (OC-03).
/// A lease is created in state <see cref="LeaseState.Offered"/> and transitions to
/// <see cref="LeaseState.Accepted"/> when the worker calls AcceptLease, or to
/// <see cref="LeaseState.Expired"/> when the offer window elapses.
///
/// Domain invariants enforced here:
///   - Accepted leases cannot be expired.
///   - AcceptedAt is set only when transitioning to Accepted.
///   - ArtifactHash is immutable after creation (pinned at offer time).
/// </summary>
public sealed class LeaseRecord
{
    // EF Core parameterless constructor.
    private LeaseRecord() { }

    public LeaseRecord(
        Guid leaseId,
        Guid runId,
        Guid attemptId,
        string workerId,
        string artifactId,
        string artifactVersion,
        string artifactHash,
        DateTimeOffset offeredAt,
        DateTimeOffset expiresAt,
        string? interactiveSessionId)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("ArtifactId is required.", nameof(artifactId));
        if (string.IsNullOrWhiteSpace(artifactHash))
            throw new ArgumentException("ArtifactHash is required.", nameof(artifactHash));

        LeaseId              = leaseId;
        RunId                = runId;
        AttemptId            = attemptId;
        WorkerId             = workerId;
        ArtifactId           = artifactId;
        ArtifactVersion      = artifactVersion;
        ArtifactHash         = artifactHash;
        OfferedAt            = offeredAt;
        ExpiresAt            = expiresAt;
        State                = LeaseState.Offered;
        InteractiveSessionId = interactiveSessionId;
    }

    /// <summary>Primary key for the lease record.</summary>
    public Guid LeaseId { get; private set; }

    /// <summary>The run this lease is for.</summary>
    public Guid RunId { get; private set; }

    /// <summary>
    /// Fences stage transitions — invariant 3.
    /// Must match the run's AttemptId at lease creation time.
    /// </summary>
    public Guid AttemptId { get; private set; }

    /// <summary>Stable identifier of the worker that received this offer.</summary>
    public string WorkerId { get; private set; } = string.Empty;

    /// <summary>Artifact identifier pinned at offer time.</summary>
    public string ArtifactId { get; private set; } = string.Empty;

    /// <summary>Artifact version pinned at offer time.</summary>
    public string ArtifactVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Content hash of the artifact pinned at offer time.
    /// Immutable after creation — invariant 7.
    /// </summary>
    public string ArtifactHash { get; private set; } = string.Empty;

    /// <summary>When the offer was created.</summary>
    public DateTimeOffset OfferedAt { get; private set; }

    /// <summary>When the offer expires if not accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the worker accepted the offer. Null until accepted.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>Current lifecycle state of the lease.</summary>
    public LeaseState State { get; private set; }

    /// <summary>
    /// The interactive session identifier from the worker that holds this lease.
    /// Null for headless (non-GUI) workers.
    /// Used to enforce the one-headed-worker-per-session invariant (invariant 6).
    /// </summary>
    public string? InteractiveSessionId { get; private set; }

    // ── State transitions ──────────────────────────────────────────────────────

    /// <summary>
    /// Transitions the lease from Offered → Accepted.
    /// Sets <see cref="AcceptedAt"/> to <paramref name="now"/>.
    /// Throws if the lease is not in <see cref="LeaseState.Offered"/> state.
    /// </summary>
    public void Accept(DateTimeOffset now)
    {
        if (State != LeaseState.Offered)
            throw new InvalidOperationException(
                $"Cannot accept lease {LeaseId}: current state is {State}, expected Offered.");

        State      = LeaseState.Accepted;
        AcceptedAt = now;
    }

    /// <summary>
    /// Transitions the lease from Offered → Expired.
    /// Accepted leases cannot be expired — they must be released or closed by the run completing.
    /// </summary>
    public void Expire()
    {
        if (State == LeaseState.Accepted)
            throw new InvalidOperationException(
                $"Cannot expire lease {LeaseId}: it has already been accepted.");
        if (State == LeaseState.Expired)
            return; // idempotent

        State = LeaseState.Expired;
    }

    /// <summary>Returns true when the lease is still active (not Expired).</summary>
    public bool IsActive() => State != LeaseState.Expired;
}
#endif
