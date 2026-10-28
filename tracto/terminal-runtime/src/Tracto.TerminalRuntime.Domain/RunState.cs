namespace Tracto.TerminalRuntime.Domain;

/// <summary>CEC v1.0.1 run states.</summary>
public enum RunState
{
    Created,
    Queued,
    Leased,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Unknown,
    HandedOff
}
