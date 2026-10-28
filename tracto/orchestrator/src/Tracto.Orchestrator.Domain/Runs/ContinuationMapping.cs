#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Domain entity representing a continuation mapping for OC-08.
/// A mapping declares: when run <see cref="TriggerRunId"/> (or ANY run, when null)
/// reaches <see cref="TriggerState"/>, create a new Run B from
/// <see cref="TargetArtifactId"/>/<see cref="TargetArtifactVersion"/>.
///
/// Domain invariants:
///   - TriggerRunId null means a global (state-only) mapping.
///   - TriggerState must be a terminal state.
///   - Version is incremented on each update but frozen once a Run B references it.
///   - Immutable fields: MappingId, TriggerRunId, CreatedAt.
/// </summary>
public sealed class ContinuationMapping
{
    // EF Core private constructor for materialisation.
    private ContinuationMapping() { }

    public ContinuationMapping(
        Guid        mappingId,
        Guid?       triggerRunId,
        RunState    triggerState,
        string      targetArtifactId,
        string      targetArtifactVersion,
        string?     inputTemplate,
        DateTimeOffset createdAt)
    {
        MappingId            = mappingId;
        TriggerRunId         = triggerRunId;
        TriggerState         = triggerState;
        TargetArtifactId     = targetArtifactId;
        TargetArtifactVersion = targetArtifactVersion;
        InputTemplate        = inputTemplate;
        CreatedAt            = createdAt;
        Version              = 1;
    }

    /// <summary>Primary key.</summary>
    public Guid MappingId { get; private set; }

    /// <summary>
    /// The specific source run that triggers continuation.
    /// Null means the mapping applies to ANY run reaching <see cref="TriggerState"/>.
    /// </summary>
    public Guid? TriggerRunId { get; private set; }

    /// <summary>The terminal state that triggers Run B creation.</summary>
    public RunState TriggerState { get; private set; }

    /// <summary>ArtifactId of the script to run as Run B.</summary>
    public string TargetArtifactId { get; private set; } = string.Empty;

    /// <summary>Version of the artifact to use. Frozen once Run B is RUNNING.</summary>
    public string TargetArtifactVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Optional JSON template for Run B's input references.
    /// Null means Run B is started with no additional inputs beyond BusinessJobId.
    /// </summary>
    public string? InputTemplate { get; private set; }

    /// <summary>When this mapping was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Optimistic-concurrency version counter.
    /// Incremented on each update; EF Core uses it as a row-version.
    /// </summary>
    public int Version { get; private set; }

    // ── Mutation methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Updates mutable fields of the mapping.
    /// Version is incremented automatically.
    /// </summary>
    public void Update(
        string  targetArtifactId,
        string  targetArtifactVersion,
        string? inputTemplate)
    {
        TargetArtifactId      = targetArtifactId;
        TargetArtifactVersion = targetArtifactVersion;
        InputTemplate         = inputTemplate;
        Version++;
    }
}
