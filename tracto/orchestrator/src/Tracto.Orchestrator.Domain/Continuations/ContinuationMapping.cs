#nullable enable
namespace Tracto.Orchestrator.Domain.Continuations;

using Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Aggregate root that declares: "when run A reaches <see cref="TriggerState"/>,
/// create a new run (Run B) from <see cref="TargetArtifactId"/>/<see cref="TargetArtifactVersion"/>."
///
/// Domain invariants:
///   - (TriggerRunId, TriggerState) is unique at the DB level — only one Run B ever.
///   - Once Run B is RUNNING, the mapping version is frozen for it (caller tracks VersionAtCapture).
///   - Source run state is never mutated by this entity.
///   - FAILED / CANCELLED / TIMED_OUT / UNKNOWN without a mapping → no action (Application layer).
///   - HANDED_OFF without a mapping → operator attention record (Application layer).
/// </summary>
public sealed class ContinuationMapping
{
    // EF Core parameterless constructor.
    private ContinuationMapping() { }

    /// <summary>
    /// Creates a mapping that fires whenever the specified trigger run reaches
    /// <paramref name="triggerState"/>.
    /// </summary>
    /// <param name="mappingId">Stable, caller-supplied identifier.</param>
    /// <param name="triggerRunId">
    /// When non-null, the mapping fires only for this specific source run.
    /// When null, the mapping is a global fallback that fires for any run reaching the state.
    /// </param>
    /// <param name="triggerState">The terminal state that activates this mapping.</param>
    /// <param name="targetArtifactId">Artifact to use for Run B.</param>
    /// <param name="targetArtifactVersion">Pinned version of that artifact.</param>
    /// <param name="inputTemplate">
    /// Optional JSON template merged into Run B's InputRefs at creation time.
    /// Treated as opaque by the domain layer.
    /// </param>
    /// <param name="createdAt">Creation timestamp (from IClock).</param>
    public ContinuationMapping(
        Guid mappingId,
        Guid? triggerRunId,
        RunState triggerState,
        string targetArtifactId,
        string targetArtifactVersion,
        string? inputTemplate,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(targetArtifactId))
            throw new ArgumentException("TargetArtifactId is required.", nameof(targetArtifactId));
        if (string.IsNullOrWhiteSpace(targetArtifactVersion))
            throw new ArgumentException("TargetArtifactVersion is required.", nameof(targetArtifactVersion));
        if (!IsAllowedTriggerState(triggerState))
            throw new ArgumentException(
                $"TriggerState {triggerState} is not a valid continuation trigger. " +
                "Allowed values: SUCCEEDED, HANDED_OFF.",
                nameof(triggerState));

        MappingId             = mappingId;
        TriggerRunId          = triggerRunId;
        TriggerState          = triggerState;
        TargetArtifactId      = targetArtifactId;
        TargetArtifactVersion = targetArtifactVersion;
        InputTemplate         = inputTemplate;
        CreatedAt             = createdAt;
        Version               = 1;
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    public Guid MappingId { get; private set; }

    // ── Trigger ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The specific source run that must reach <see cref="TriggerState"/> to activate
    /// this mapping. Null means the mapping is a global default for that state.
    /// </summary>
    public Guid? TriggerRunId { get; private set; }

    /// <summary>
    /// The terminal run state that activates this mapping.
    /// Only SUCCEEDED and HANDED_OFF are legal trigger states.
    /// </summary>
    public RunState TriggerState { get; private set; }

    // ── Target ────────────────────────────────────────────────────────────────

    /// <summary>Artifact identifier for Run B.</summary>
    public string TargetArtifactId { get; private set; } = string.Empty;

    /// <summary>
    /// Pinned version of the target artifact. Immutable once Run B is RUNNING
    /// (the Application layer captures <see cref="Version"/> at that point).
    /// </summary>
    public string TargetArtifactVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Optional JSON template merged into Run B's InputRefs at creation time.
    /// The orchestrator treats this value as opaque.
    /// </summary>
    public string? InputTemplate { get; private set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency version. Incremented on each mutation so that a
    /// Run B that started at version N is fenced from subsequent mapping updates.
    /// </summary>
    public int Version { get; private set; }

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the target artifact reference. Increments <see cref="Version"/>.
    /// Callers must not call this while an active Run B that was created from this
    /// mapping is still RUNNING — the Application layer enforces that invariant.
    /// </summary>
    public void UpdateTarget(
        string targetArtifactId,
        string targetArtifactVersion,
        string? inputTemplate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(targetArtifactId))
            throw new ArgumentException("TargetArtifactId is required.", nameof(targetArtifactId));
        if (string.IsNullOrWhiteSpace(targetArtifactVersion))
            throw new ArgumentException("TargetArtifactVersion is required.", nameof(targetArtifactVersion));

        TargetArtifactId      = targetArtifactId;
        TargetArtifactVersion = targetArtifactVersion;
        InputTemplate         = inputTemplate;
        UpdatedAt             = now;
        Version++;
    }

    // ── Domain helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when this mapping should fire for a run that just reached
    /// <paramref name="state"/> with <paramref name="runId"/>.
    /// A mapping without a <see cref="TriggerRunId"/> (global default) matches
    /// any run that reaches the state.
    /// </summary>
    public bool Matches(Guid runId, RunState state) =>
        TriggerState == state &&
        (TriggerRunId is null || TriggerRunId.Value == runId);

    /// <summary>
    /// Only SUCCEEDED and HANDED_OFF are legal continuation trigger states.
    /// FAILED / CANCELLED / TIMED_OUT / UNKNOWN continuations are not supported
    /// by the mapping mechanism (they result in no-action or attention records).
    /// </summary>
    public static bool IsAllowedTriggerState(RunState state) =>
        state is RunState.Succeeded or RunState.HandedOff;
}
