#nullable enable
namespace Tracto.Orchestrator.Domain.Leases;

/// <summary>
/// Aggregate root representing a job-offer lease (OC-03).
/// Created by PullJobCommandHandler (State=Offered, ExpiresAt=now+30s).
/// Transitions to Accepted when the worker calls AcceptLease;
/// marked Expired by the lease-expiry background job.
///
/// Domain invariants:
///   - AttemptId is immutable once set.
///   - ArtifactHash is immutable (pinned at offer time) — invariant 7.
///   - InteractiveSessionId exclusivity is enforced at the Application layer.
/// </summary>
public sealed class LeaseRecord
{
    // EF Core parameterless constructor.
    private LeaseRecord() { }

    public LeaseRecord(
        Guid   leaseId,
        Guid   runId,
        Guid   attemptId,
        string workerId,
        string artifactId,
        string artifactVersion,
        string artifactHash,
        DateTimeOffset offeredAt,
        DateTimeOffset expiresAt,
        string? interactiveSessionId = null)
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

    public Guid   LeaseId         { get; private set; }
    public Guid   RunId           { get; private set; }
    public Guid   AttemptId       { get; private set; }
    public string WorkerId        { get; private set; } = string.Empty;
    public string ArtifactId      { get; private set; } = string.Empty;
    public string ArtifactVersion { get; private set; } = string.Empty;

    /// <summary>Content hash pinned at offer time. Immutable — invariant 7.</summary>
    public string ArtifactHash    { get; private set; } = string.Empty;

    public DateTimeOffset  OfferedAt   { get; private set; }
    public DateTimeOffset  ExpiresAt   { get; private set; }
    public DateTimeOffset? AcceptedAt  { get; private set; }
    public LeaseState      State       { get; private set; }

    /// <summary>
    /// Populated only for headed (interactive) jobs.
    /// Enforces single-headed-worker-per-session invariant (invariant 6).
    /// </summary>
    public string? InteractiveSessionId { get; private set; }

    // ── OC-05: Heartbeat fields ────────────────────────────────────────────────

    /// <summary>UTC timestamp of the most recent accepted heartbeat. Null until the first heartbeat.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    /// <summary>
    /// Monotonically-increasing sequence number of the most recent accepted heartbeat.
    /// Any incoming heartbeat with SeqNo &lt;= LastSeqNo is rejected as stale.
    /// </summary>
    public long LastSeqNo { get; private set; }

    /// <summary>
    /// When true the orchestrator has signalled that the worker should cancel the run.
    /// Returned to the worker on each heartbeat response so they act on it promptly.
    /// </summary>
    public bool CancellationRequested { get; private set; }

    // ── Transitions ────────────────────────────────────────────────────────────

    public void Accept(DateTimeOffset now)
    {
        if (State != LeaseState.Offered)
            throw new InvalidOperationException(
                $"Cannot accept lease {LeaseId}: current state is {State}, expected Offered.");
        State      = LeaseState.Accepted;
        AcceptedAt = now;
    }

    /// <summary>
    /// Expires an Offered lease that has not been accepted within its offer window.
    /// Throws when called on an Accepted lease — use <see cref="ExpireAccepted"/> for that.
    /// Idempotent when the lease is already Expired.
    /// </summary>
    public void Expire()
    {
        if (State == LeaseState.Accepted)
            throw new InvalidOperationException(
                $"Cannot expire lease {LeaseId}: it has already been accepted.");
        if (State == LeaseState.Expired) return; // idempotent
        State = LeaseState.Expired;
    }

    /// <summary>
    /// Forces an Accepted lease to Expired state.
    /// Called by the LeaseExpiryMonitorService when the heartbeat window lapses.
    /// Idempotent when already Expired.
    /// Throws when called on an Offered lease — use <see cref="Expire"/> for that.
    /// </summary>
    public void ExpireAccepted()
    {
        if (State == LeaseState.Expired) return; // idempotent

        if (State != LeaseState.Accepted)
            throw new InvalidOperationException(
                $"ExpireAccepted called on lease {LeaseId} in state {State}; " +
                "only Accepted leases may be expired via this path.");

        State = LeaseState.Expired;
    }

    /// <summary>
    /// Records a valid heartbeat: updates LastHeartbeatAt and LastSeqNo.
    /// Preconditions (caller must verify before calling):
    ///   - Lease state is Accepted (not Expired or Offered).
    ///   - seqNo &gt; LastSeqNo (monotonic progression).
    /// </summary>
    /// <param name="now">Current UTC time from IClock.</param>
    /// <param name="seqNo">Sequence number supplied by the worker.</param>
    /// <exception cref="InvalidOperationException">Thrown when preconditions are violated.</exception>
    public void RecordHeartbeat(DateTimeOffset now, long seqNo)
    {
        if (State == LeaseState.Expired)
            throw new InvalidOperationException(
                $"Cannot record heartbeat for lease {LeaseId}: lease is Expired.");

        if (State != LeaseState.Accepted)
            throw new InvalidOperationException(
                $"Cannot record heartbeat for lease {LeaseId}: current state is {State}, expected Accepted.");

        if (seqNo <= LastSeqNo)
            throw new InvalidOperationException(
                $"Stale sequence number {seqNo} for lease {LeaseId}: current LastSeqNo is {LastSeqNo}.");

        LastHeartbeatAt = now;
        LastSeqNo       = seqNo;
    }

    /// <summary>
    /// Signals that the orchestrator wants the worker to cancel this run.
    /// The flag is delivered to the worker on the next heartbeat response.
    /// </summary>
    public void RequestCancellation()
    {
        CancellationRequested = true;
    }

    /// <summary>
    /// Computes the live heartbeat-health state of the lease given the current time
    /// and the configured expiry window.
    /// </summary>
    /// <param name="now">Current UTC time.</param>
    /// <param name="expiryWindow">How long without a heartbeat before the lease is considered Expired.</param>
    /// <param name="suspectWindow">
    /// How long without a heartbeat before the lease is considered Suspect
    /// (must be less than <paramref name="expiryWindow"/>).
    /// </param>
    public LeaseExpiryState ComputeExpiryState(
        DateTimeOffset now,
        TimeSpan       expiryWindow,
        TimeSpan       suspectWindow)
    {
        if (State == LeaseState.Expired)
            return LeaseExpiryState.Expired;

        if (LastHeartbeatAt is null)
            return LeaseExpiryState.Suspect; // never heartbeated

        var elapsed = now - LastHeartbeatAt.Value;

        if (elapsed >= expiryWindow)
            return LeaseExpiryState.Expired;

        if (elapsed >= suspectWindow)
            return LeaseExpiryState.Suspect;

        return LeaseExpiryState.Active;
    }

    /// <summary>Returns true when the lease has not yet expired (Offered or Accepted).</summary>
    public bool IsNonExpired() => State != LeaseState.Expired;

    /// <summary>
    /// Transitions the lease to <see cref="LeaseState.Expired"/> regardless of its current
    /// Offered-or-Accepted state. Idempotent when already Expired.
    ///
    /// Use this instead of <see cref="Expire"/> or <see cref="ExpireAccepted"/> whenever the
    /// caller does not know (or cannot guarantee) the current lease state — for example when
    /// cancelling a run whose AcceptLease may have partially completed (lease Accepted but run
    /// still LEASED due to a crash). Calling the state-specific overloads in that scenario
    /// would throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public void ForceExpire()
    {
        State = LeaseState.Expired;
    }
}
