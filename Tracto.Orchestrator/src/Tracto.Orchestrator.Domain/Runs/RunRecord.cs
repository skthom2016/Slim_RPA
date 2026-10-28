#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Aggregate root representing a single automation run.
/// Domain layer — no EF annotations; mapping configured in OrchestratorDbContext.
/// Invariants enforced here:
///   - AttemptId must match for all stage-transition calls (enforced by handlers).
///   - UNKNOWN state requires NeedsAttention=true and a non-null UnknownReason.
///   - Artifact versions are immutable once published (enforced by handlers).
///   - Deadline (if set) must be in the future at creation time — validated in Application layer.
///   - EventId deduplication window is 5 minutes — validated in Application layer.
/// </summary>
public sealed class RunRecord
{
    // EF Core needs a parameterless constructor or a constructor it can map.
    private RunRecord() { }

    public RunRecord(
        Guid id,
        string workflowId,
        DateTimeOffset createdAt)
    {
        Id = id;
        WorkflowId = workflowId;
        CreatedAt = createdAt;
        State = RunState.Created;
        AttemptId = Guid.NewGuid();
        NeedsAttention = false;
        CorrelationId = Guid.NewGuid();
        TriggerType = TriggerType.Manual;
    }

    /// <summary>Full constructor used by the Application layer for OC-02/OC-03 job intake.</summary>
    public RunRecord(
        Guid id,
        string workflowId,
        DateTimeOffset createdAt,
        Guid correlationId,
        string scriptArtifactId,
        string scriptVersion,
        string schemaVersion,
        string[] inputRefs,
        string[] secretRefs,
        DateTime? deadline,
        string? eventId,
        TriggerType triggerType,
        string? businessJobId,
        string? interactiveSessionId = null,
        // OC-03: worker matching fields (optional; empty / null = no restriction)
        string? runtimeType = null,
        string[]? requiredCapabilities = null)
    {
        Id = id;
        WorkflowId = workflowId;
        CreatedAt = createdAt;
        State = RunState.Created;
        AttemptId = Guid.NewGuid();
        NeedsAttention = false;

        CorrelationId = correlationId;
        ScriptArtifactId = scriptArtifactId;
        ScriptVersion = scriptVersion;
        SchemaVersion = schemaVersion;
        InputRefs = inputRefs;
        SecretRefs = secretRefs;
        Deadline = deadline;
        EventId = eventId;
        TriggerType = triggerType;
        BusinessJobId = businessJobId;
        InteractiveSessionId = interactiveSessionId;
        RuntimeType          = runtimeType ?? string.Empty;
        RequiredCapabilities = requiredCapabilities ?? [];
    }

    public Guid Id { get; private set; }
    public string WorkflowId { get; private set; } = string.Empty;
    public RunState State { get; private set; }

    // ── OC-02: Job Intake fields ───────────────────────────────────────────────

    /// <summary>Correlation token propagated to all child runs and journal entries.</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>Artifact identifier for the script to execute.</summary>
    public string ScriptArtifactId { get; private set; } = string.Empty;

    /// <summary>Pinned version of the script artifact.</summary>
    public string ScriptVersion { get; private set; } = string.Empty;

    /// <summary>Schema version of the job payload. Only "1" is currently supported.</summary>
    public string SchemaVersion { get; private set; } = string.Empty;

    /// <summary>Opaque references to input blobs/files. Never stored with secret values.</summary>
    public string[] InputRefs { get; private set; } = [];

    /// <summary>References to secrets needed by the script. The orchestrator never stores actual secret values.</summary>
    public string[] SecretRefs { get; private set; } = [];

    /// <summary>Optional hard deadline for the run. Runs not completed by this time will be timed out.</summary>
    public DateTime? Deadline { get; private set; }

    /// <summary>
    /// External event identifier for callback-triggered jobs.
    /// Used within the 5-minute deduplication window to return the same RunId for duplicate events.
    /// </summary>
    public string? EventId { get; private set; }

    /// <summary>How this run was initiated.</summary>
    public TriggerType TriggerType { get; private set; }

    /// <summary>Caller-supplied business-level job identifier for correlation in external systems.</summary>
    public string? BusinessJobId { get; private set; }

    // ── OC-03: Queue Management and Worker Capability Matching fields ─────────

    /// <summary>
    /// Runtime type required to execute this run (e.g. "dotnet", "python", "browser").
    /// Stored from the artifact's RuntimeType at job creation time.
    /// Empty string means no restriction — any worker runtime type can pick it up.
    /// </summary>
    public string RuntimeType { get; private set; } = string.Empty;

    /// <summary>
    /// Capability names required by this run (a subset of WorkerCapability.ActionNames).
    /// A worker must advertise ALL of these capabilities to be eligible.
    /// Empty array means no specific capabilities are required.
    /// </summary>
    public string[] RequiredCapabilities { get; private set; } = [];

    /// <summary>
    /// When set, this run requires a headed (interactive) GUI session.
    /// Only one non-Expired LeaseRecord may exist for a given InteractiveSessionId
    /// at any point in time — enforced by PullJobCommandHandler (invariant 6).
    /// Null for headless runs that have no foreground-input requirement.
    /// </summary>
    public string? InteractiveSessionId { get; private set; }

    /// <summary>
    /// The worker currently holding a lease or running this job.
    /// Null when the run has not been assigned to any worker yet, or after the worker released it.
    /// Required for restart-recovery: identifies which worker was absent.
    /// </summary>
    public string? WorkerId { get; private set; }

    /// <summary>
    /// Fences all stage transitions. Any message carrying a different attemptId
    /// must be rejected with 409 Conflict — invariant 3.
    /// </summary>
    public Guid AttemptId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// True when the run is in UNKNOWN state or otherwise requires human review.
    /// Must be set whenever State == Unknown — invariant 2.
    /// </summary>
    public bool NeedsAttention { get; private set; }

    /// <summary>Required when State == Unknown — invariant 2.</summary>
    public string? UnknownReason { get; private set; }

    // ── State transitions ──────────────────────────────────────────────────────

    public void Enqueue(DateTimeOffset now)
    {
        EnsureState(RunState.Created);
        State = RunState.Queued;
        UpdatedAt = now;
    }

    public void Lease(Guid incomingAttemptId, string workerId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Queued);
        WorkerId = workerId;
        State = RunState.Leased;
        UpdatedAt = now;
    }

    /// <summary>Overload kept for callers that do not yet supply a workerId.</summary>
    public void Lease(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Queued);
        State = RunState.Leased;
        UpdatedAt = now;
    }

    /// <summary>
    /// OC-04: Adopts <paramref name="leaseAttemptId"/> as the new AttemptId fence and
    /// transitions the run QUEUED → LEASED without validating the previous AttemptId
    /// (which was generated at creation time, before any lease existed).
    /// Must be called exactly once per AcceptLease attempt, before either
    /// <see cref="Start"/> (success path) or <see cref="ForceRequeue"/>/<see cref="MarkUnknown"/>
    /// (expired-lease path).
    /// </summary>
    public void AdoptLease(Guid leaseAttemptId, string workerId, DateTimeOffset now)
    {
        EnsureState(RunState.Queued);
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("WorkerId is required.", nameof(workerId));

        AttemptId = leaseAttemptId; // fence all future stage calls against this ID
        WorkerId  = workerId;
        State     = RunState.Leased;
        UpdatedAt = now;
    }

    public void Start(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Leased);
        State = RunState.Running;
        UpdatedAt = now;
    }

    public void Succeed(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Running);
        State = RunState.Succeeded;
        UpdatedAt = now;
    }

    public void Fail(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Running);
        State = RunState.Failed;
        UpdatedAt = now;
    }

    public void HandOff(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        EnsureState(RunState.Running);
        State = RunState.HandedOff;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions a <see cref="RunState.Leased"/> run back to <see cref="RunState.Queued"/>
    /// when the lease expires before the worker accepts it and the journal confirms the run
    /// never started (no RUN_RUNNING entries). Issues a fresh <see cref="AttemptId"/> so
    /// any in-flight messages from the previous attempt are fenced off — domain invariant 3.
    ///
    /// Used by AcceptLeaseCommandHandler (OC-04) on the "late accept / safe to retry" path.
    /// </summary>
    public void Requeue(DateTimeOffset now)
    {
        if (State != RunState.Leased)
            throw new InvalidOperationException(
                $"Run {Id} is in state {State}; Requeue requires {RunState.Leased}.");

        State     = RunState.Queued;
        WorkerId  = null;
        AttemptId = Guid.NewGuid(); // new fence — stale attempt messages are now rejected
        UpdatedAt = now;
    }

    /// <summary>
    /// Forces a run back to <see cref="RunState.Queued"/> from any non-terminal state.
    /// Issues a fresh <see cref="AttemptId"/> so stale in-flight messages are fenced off
    /// (domain invariant 3). Unlike <see cref="Requeue"/>, this overload does not require
    /// the run to currently be in <see cref="RunState.Leased"/>.
    ///
    /// Used by AcceptLeaseCommandHandler (OC-04) when a lease expires and the journal
    /// confirms the run had previously started (RUN_RUNNING exists), meaning the start-phase
    /// completed in a prior attempt and it is safe to retry from the queue.
    /// </summary>
    public void ForceRequeue(DateTimeOffset now)
    {
        if (IsTerminal())
            throw new InvalidOperationException(
                $"Cannot requeue run {Id}: it is in terminal state {State}.");

        State     = RunState.Queued;
        WorkerId  = null;
        AttemptId = Guid.NewGuid();
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions a run to <see cref="RunState.Succeeded"/> via the reconciliation path (OC-10).
    ///
    /// Unlike <see cref="Succeed"/>, this transition is valid from ANY non-terminal state
    /// (e.g. LEASED, RUNNING) because a worker disconnect may occur before the RUNNING
    /// state was formally recorded by the orchestrator.
    ///
    /// Validates the AttemptId fence (invariant 3) but does NOT issue a new AttemptId —
    /// reconciliation retains the original AttemptId so attempt count is unchanged.
    /// </summary>
    /// <param name="incomingAttemptId">Must match the current <see cref="AttemptId"/> fence.</param>
    /// <param name="now">Current UTC time from IClock.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the AttemptId mismatches or the run is already in a terminal state.
    /// </exception>
    public void ReconcileSucceeded(Guid incomingAttemptId, DateTimeOffset now)
    {
        if (incomingAttemptId != AttemptId)
            throw new InvalidOperationException(
                $"AttemptId mismatch for run {Id}. Expected {AttemptId}, received {incomingAttemptId}. Respond 409.");

        if (IsTerminal())
            throw new InvalidOperationException(
                $"Run {Id} is already in terminal state {State}; cannot reconcile to SUCCEEDED.");

        State     = RunState.Succeeded;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        // Cancel is allowed from any non-terminal state.
        if (IsTerminal())
            throw new InvalidOperationException($"Cannot cancel a run in terminal state {State}.");
        State = RunState.Cancelled;
        UpdatedAt = now;
    }

    public void MarkTimedOut(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        State = RunState.TimedOut;
        UpdatedAt = now;
    }

    /// <summary>
    /// Mark as UNKNOWN — always sets NeedsAttention=true; never auto-replayed.
    /// Invariant 2: UNKNOWN requires unknownReason.
    /// </summary>
    public void MarkUnknown(string unknownReason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(unknownReason))
            throw new ArgumentException("unknownReason is required when marking a run Unknown.", nameof(unknownReason));

        State = RunState.Unknown;
        NeedsAttention = true;
        UnknownReason = unknownReason;
        UpdatedAt = now;
    }

    /// <summary>
    /// Forces the run to SUCCEEDED state from any non-terminal state during reconciliation.
    /// Used by <c>ReconcileRunCommandHandler</c> when the worker's submitted journal confirms
    /// a SUCCEEDED outcome after a disconnect.
    ///
    /// AttemptId must match (invariant 3). Does NOT issue a new AttemptId — the original
    /// AttemptId is retained (OC-10: reconcile does not increment the attempt count).
    /// Unlike <see cref="Succeed"/>, this method does not require the run to be in RUNNING
    /// state; a worker may have crashed between LEASED and RUNNING while still completing
    /// the automation work and emitting a final journal entry.
    /// </summary>
    public void ForceSucceed(Guid incomingAttemptId, DateTimeOffset now)
    {
        EnsureAttemptId(incomingAttemptId);
        if (IsTerminal())
            throw new InvalidOperationException(
                $"Run {Id} is already in terminal state {State}; cannot force-succeed.");
        State = RunState.Succeeded;
        UpdatedAt = now;
    }

    /// <summary>
    /// Administrative override that transitions a run from <see cref="RunState.Unknown"/>
    /// to a definitive terminal state (SUCCEEDED, FAILED, or CANCELLED). OC-13.
    ///
    /// This is the only path that accepts UNKNOWN as a source state for a forward transition
    /// and is deliberately restricted to operator/admin use. It resets NeedsAttention and
    /// UnknownReason on the new terminal state.
    /// </summary>
    /// <param name="resolution">
    /// The target terminal state. Must be SUCCEEDED, FAILED, or CANCELLED.
    /// </param>
    /// <param name="now">UTC timestamp from IClock.</param>
    public void AdminResolve(RunState resolution, DateTimeOffset now)
    {
        if (State != RunState.Unknown)
            throw new InvalidOperationException(
                $"AdminResolve requires state {RunState.Unknown}; run {Id} is in state {State}.");

        if (resolution is not (RunState.Succeeded or RunState.Failed or RunState.Cancelled))
            throw new ArgumentException(
                $"Resolution '{resolution}' is not allowed. " +
                "Permitted values: Succeeded, Failed, Cancelled.", nameof(resolution));

        State          = resolution;
        NeedsAttention = false;       // cleared — human reviewed and resolved
        UnknownReason  = null;        // cleared — no longer unknown
        UpdatedAt      = now;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    public bool IsTerminal() =>
        State is RunState.Succeeded or RunState.Failed or RunState.HandedOff
               or RunState.Cancelled or RunState.TimedOut or RunState.Unknown;

    private void EnsureState(RunState expected)
    {
        if (State != expected)
            throw new InvalidOperationException(
                $"Run {Id} is in state {State}; expected {expected} for this transition.");
    }

    private void EnsureAttemptId(Guid incoming)
    {
        if (incoming != AttemptId)
            throw new InvalidOperationException(
                $"AttemptId mismatch for run {Id}. Expected {AttemptId}, received {incoming}. Respond 409.");
    }
}
