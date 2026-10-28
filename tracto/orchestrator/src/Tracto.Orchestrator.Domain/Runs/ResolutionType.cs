#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Discriminates the two legal operator resolution actions for an UNKNOWN run (OC-12).
/// </summary>
public enum ResolutionType
{
    /// <summary>
    /// Operator has investigated and determined no new run is needed.
    /// The original RunRecord stays in state UNKNOWN permanently.
    /// </summary>
    InvestigatedNoRerun = 0,

    /// <summary>
    /// Operator has approved creation of a new run for the same script.
    /// A new RunRecord is created (CREATED→QUEUED); the original stays UNKNOWN.
    /// <see cref="ResolutionRecord.NewRunId"/> will be populated with the new run's Id.
    /// </summary>
    ApprovedNewRun = 1
}
