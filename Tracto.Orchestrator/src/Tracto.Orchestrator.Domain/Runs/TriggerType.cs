#nullable enable
namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// Discriminates how a job was initiated.
/// Manual       — direct API call.
/// Scheduled    — triggered by a cron schedule.
/// Callback     — triggered by an external event (EventId dedup applies).
/// Continuation — triggered automatically by a ContinuationMapping (OC-08).
/// </summary>
public enum TriggerType
{
    Manual = 0,
    Scheduled = 1,
    Callback = 2,
    Continuation = 3
}
