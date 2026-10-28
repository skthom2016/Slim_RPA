#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Immutable audit record appended when an operator resolves an UNKNOWN run (OC-12).
///
/// Invariants:
///   - The originating RunRecord is NEVER modified; it stays in UNKNOWN with NeedsAttention=true.
///   - UNKNOWN→QUEUED auto-replay is forbidden: only an explicit ResolutionRecord with
///     Type=ApprovedNewRun triggers creation of a NEW RunRecord (different Id).
///   - NewRunId is non-null if and only if Type=ApprovedNewRun (enforced in the constructor).
///   - Justification must be non-empty.
///   - RunId must not be Guid.Empty.
///   - This entity is append-only: all setters are private and there are no mutation methods.
/// </summary>
public sealed class ResolutionRecord
{
    // EF Core requires a parameterless constructor; kept private to enforce factory usage.
    private ResolutionRecord() { }

    /// <summary>
    /// Creates a new resolution for an UNKNOWN run.
    /// </summary>
    /// <param name="runId">The RunRecord that is in UNKNOWN state. Must not be Guid.Empty.</param>
    /// <param name="type">The operator's decision.</param>
    /// <param name="justification">Mandatory free-text rationale (must be non-empty).</param>
    /// <param name="operatorId">Identity of the human or service account making the decision.</param>
    /// <param name="resolvedAt">Current UTC time from IClock.</param>
    /// <param name="newRunId">
    ///   Must be non-null when <paramref name="type"/> == <see cref="ResolutionType.ApprovedNewRun"/>.
    ///   Must be null when <paramref name="type"/> == <see cref="ResolutionType.InvestigatedNoRerun"/>.
    /// </param>
    public ResolutionRecord(
        Guid runId,
        ResolutionType type,
        string justification,
        string operatorId,
        DateTimeOffset resolvedAt,
        Guid? newRunId = null)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId must not be empty.", nameof(runId));

        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException(
                "Justification is required for a resolution record.", nameof(justification));

        if (string.IsNullOrWhiteSpace(operatorId))
            throw new ArgumentException(
                "OperatorId is required for a resolution record.", nameof(operatorId));

        if (type == ResolutionType.ApprovedNewRun && newRunId is null)
            throw new ArgumentException(
                "NewRunId must be provided when ResolutionType is ApprovedNewRun.",
                nameof(newRunId));

        if (type == ResolutionType.InvestigatedNoRerun && newRunId is not null)
            throw new ArgumentException(
                "NewRunId must be null when ResolutionType is InvestigatedNoRerun.",
                nameof(newRunId));

        Id            = Guid.NewGuid();
        RunId         = runId;
        Type          = type;
        Justification = justification;
        OperatorId    = operatorId;
        ResolvedAt    = resolvedAt;
        NewRunId      = newRunId;
    }

    /// <summary>Primary key (auto-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>The UNKNOWN RunRecord this resolution is linked to.</summary>
    public Guid RunId { get; private set; }

    /// <summary>The operator's decision: investigate-only or approve a new run.</summary>
    public ResolutionType Type { get; private set; }

    /// <summary>Free-text justification supplied by the operator. Must be non-empty.</summary>
    public string Justification { get; private set; } = string.Empty;

    /// <summary>Identifies the operator who performed the resolution.</summary>
    public string OperatorId { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when the resolution was recorded.</summary>
    public DateTimeOffset ResolvedAt { get; private set; }

    /// <summary>
    /// Non-null only when <see cref="Type"/> is <see cref="ResolutionType.ApprovedNewRun"/>.
    /// References the Id of the newly created RunRecord; the original run is never modified.
    /// </summary>
    public Guid? NewRunId { get; private set; }
    // NOTE: No mutation methods. This entity is append-only; callers must supply NewRunId
    // in the constructor for ApprovedNewRun resolutions.
}
