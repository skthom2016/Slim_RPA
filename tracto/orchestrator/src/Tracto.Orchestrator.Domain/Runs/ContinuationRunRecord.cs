#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Join / dedup record that tracks a Run B created for a given (SourceRunId, TriggerState) pair.
/// Enforces the uniqueness invariant from OC-08: only one Run B per (SourceRunId, TriggerState).
/// A DB unique index on (SourceRunId, TriggerState) provides the authoritative dedup guard.
/// </summary>
public sealed class ContinuationRunRecord
{
    // EF Core private constructor.
    private ContinuationRunRecord() { }

    public ContinuationRunRecord(
        Guid        id,
        Guid        sourceRunId,
        RunState    triggerState,
        Guid        runBId,
        Guid        mappingId,
        int         mappingVersionSnapshot,
        DateTimeOffset createdAt)
    {
        Id                    = id;
        SourceRunId           = sourceRunId;
        TriggerState          = triggerState;
        RunBId                = runBId;
        MappingId             = mappingId;
        MappingVersionSnapshot = mappingVersionSnapshot;
        CreatedAt             = createdAt;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The run whose terminal state triggered continuation.</summary>
    public Guid SourceRunId { get; private set; }

    /// <summary>The terminal state that was matched.</summary>
    public RunState TriggerState { get; private set; }

    /// <summary>The newly created continuation run (Run B).</summary>
    public Guid RunBId { get; private set; }

    /// <summary>The mapping that was used to create Run B.</summary>
    public Guid MappingId { get; private set; }

    /// <summary>
    /// Snapshot of <see cref="ContinuationMapping.Version"/> at the time Run B was created.
    /// Once Run B is RUNNING the mapping version is frozen for it, even if the mapping changes.
    /// </summary>
    public int MappingVersionSnapshot { get; private set; }

    /// <summary>When this record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
