#nullable enable
namespace Tracto.Orchestrator.Domain.Continuations;

using Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Records that Run B (identified by <see cref="ChildRunId"/>) was created as a
/// continuation of Run A (<see cref="SourceRunId"/>) when it reached
/// <see cref="TriggerState"/>, using mapping version <see cref="MappingVersionAtCapture"/>.
///
/// The DB unique constraint on (SourceRunId, TriggerState) is the authoritative
/// deduplication guard — only one Run B may ever exist for a given (source, state) pair.
///
/// The mapping version is frozen at the moment Run B is created, so subsequent
/// updates to the ContinuationMapping do not affect an already-created Run B.
/// </summary>
public sealed class ContinuationRunLink
{
    // EF Core parameterless constructor.
    private ContinuationRunLink() { }

    public ContinuationRunLink(
        Guid linkId,
        Guid mappingId,
        Guid sourceRunId,
        RunState triggerState,
        Guid childRunId,
        int mappingVersionAtCapture,
        DateTimeOffset createdAt)
    {
        LinkId                  = linkId;
        MappingId               = mappingId;
        SourceRunId             = sourceRunId;
        TriggerState            = triggerState;
        ChildRunId              = childRunId;
        MappingVersionAtCapture = mappingVersionAtCapture;
        CreatedAt               = createdAt;
    }

    /// <summary>Stable identifier for this link record.</summary>
    public Guid LinkId { get; private set; }

    /// <summary>The mapping that triggered Run B creation.</summary>
    public Guid MappingId { get; private set; }

    /// <summary>Run A — the source run whose state transition triggered Run B.</summary>
    public Guid SourceRunId { get; private set; }

    /// <summary>The terminal state of Run A that activated the mapping.</summary>
    public RunState TriggerState { get; private set; }

    /// <summary>Run B — the continuation run that was created.</summary>
    public Guid ChildRunId { get; private set; }

    /// <summary>
    /// The <see cref="ContinuationMapping.Version"/> at the time Run B was created.
    /// Run B is executed against the artifact version captured here, even if the
    /// mapping is subsequently updated.
    /// </summary>
    public int MappingVersionAtCapture { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
