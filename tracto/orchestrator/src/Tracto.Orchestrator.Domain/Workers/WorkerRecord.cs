#nullable enable
namespace Tracto.Orchestrator.Domain.Workers;

/// <summary>
/// Aggregate root representing a registered automation worker agent.
/// Domain layer — zero EF/ASP dependencies; EF mapping lives in OrchestratorDbContext.
/// </summary>
public sealed class WorkerRecord
{
    // EF Core parameterless constructor.
    private WorkerRecord() { }

    public WorkerRecord(
        string workerId,
        string runtimeType,
        int absenceThresholdSeconds,
        DateTimeOffset registeredAt,
        WorkerCapability? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (string.IsNullOrWhiteSpace(runtimeType))
            throw new ArgumentException("RuntimeType is required.", nameof(runtimeType));

        WorkerId                = workerId;
        RuntimeType             = runtimeType;
        AbsenceThresholdSeconds = absenceThresholdSeconds;
        RegisteredAt            = registeredAt;
        LastHeartbeatAt         = registeredAt;
        LastRegisteredAt        = registeredAt;
        State                   = WorkerState.Available;
        IsOnline                = true;
        Capabilities            = capabilities;
    }

    /// <summary>Unique identifier for the worker (e.g. "worker-abc123").</summary>
    public string WorkerId { get; private set; } = string.Empty;

    /// <summary>Runtime type tag (e.g. "dotnet", "python", "browser").</summary>
    public string RuntimeType { get; private set; } = string.Empty;

    public WorkerState State { get; private set; }

    public DateTimeOffset  RegisteredAt     { get; private set; }
    public DateTimeOffset  LastHeartbeatAt  { get; private set; }

    /// <summary>
    /// Updated on every successful registration (including re-registrations).
    /// Null only for records created before OC-01 was deployed (legacy rows).
    /// </summary>
    public DateTimeOffset? LastRegisteredAt { get; private set; }

    /// <summary>
    /// Seconds of missed heartbeats before the orchestrator considers this worker absent.
    /// </summary>
    public int AbsenceThresholdSeconds { get; private set; }

    /// <summary>
    /// True while heartbeats arrive within <see cref="AbsenceThresholdSeconds"/>.
    /// Set false by restart-recovery when the worker is absent at startup.
    /// </summary>
    public bool IsOnline { get; private set; }

    /// <summary>
    /// What the worker can execute. Stored as JSON by EF Core.
    /// Null for workers that did not supply capability information at registration time.
    /// </summary>
    public WorkerCapability? Capabilities { get; private set; }

    // ── State mutations ────────────────────────────────────────────────────────

    /// <summary>
    /// Update mutable fields on re-registration.
    /// Per invariant: does NOT release any LEASED/RUNNING RunRecords.
    /// Re-registration also brings the worker back online — if it was OFFLINE it returns
    /// to AVAILABLE so the scheduler can assign new work.
    /// </summary>
    public void UpdateRegistration(
        int absenceThresholdSeconds,
        WorkerCapability? capabilities,
        DateTimeOffset now)
    {
        AbsenceThresholdSeconds = absenceThresholdSeconds;
        Capabilities            = capabilities;
        LastRegisteredAt        = now;
        // RuntimeType is intentionally NOT updated — it is set at first registration
        // and must remain stable so that existing leases and run-records stay coherent.

        // Re-registration signals the worker is alive.  Restore online visibility so the
        // scheduler can dispatch new work to it.  Active leases/runs are not touched.
        IsOnline = true;
        if (State == WorkerState.Offline)
            State = WorkerState.Available;
    }

    public void RecordHeartbeat(DateTimeOffset now)
    {
        LastHeartbeatAt = now;
        IsOnline        = true;
        if (State == WorkerState.Offline)
            State = WorkerState.Available;
    }

    public void MarkOffline(DateTimeOffset now)
    {
        // Do NOT update LastHeartbeatAt here.  That field records the last real heartbeat
        // received from the worker; overwriting it with the offline-detection timestamp
        // would destroy the diagnostic information about when the worker was last seen.
        _ = now; // parameter reserved for future use (e.g. OfflineSince audit column).
        IsOnline = false;
        State    = WorkerState.Offline;
    }

    public void MarkLeased()    => State = WorkerState.Leased;
    public void MarkRunning()   => State = WorkerState.Running;
    public void MarkAvailable() => State = WorkerState.Available;
}
