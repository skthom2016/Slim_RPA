namespace Tracto.Orchestrator.Domain.Runs;

/// <summary>
/// State machine for a Tracto run.
/// Legal transitions:
///   CREATED -> QUEUED -> LEASED -> RUNNING -> SUCCEEDED | FAILED | HANDED_OFF | CANCELLED | TIMED_OUT | UNKNOWN
/// UNKNOWN is a run *state*, not an error category.
/// UNKNOWN always requires needsAttention=true and an unknownReason; it is never auto-replayed.
/// </summary>
public enum RunState
{
    Created = 0,
    Queued = 1,
    Leased = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    HandedOff = 6,
    Cancelled = 7,
    TimedOut = 8,
    Unknown = 9
}
