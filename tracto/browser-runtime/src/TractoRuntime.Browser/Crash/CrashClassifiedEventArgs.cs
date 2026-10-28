using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Crash;

/// <summary>
/// Event arguments raised when a browser crash has been detected, the run journal has been
/// updated, and the terminal run state has been classified (BRW-US-004 AC#2–AC#4).
/// </summary>
public sealed class CrashClassifiedEventArgs(
    string runId,
    RunState terminalState,
    string reason) : EventArgs
{
    /// <summary>The run that experienced the browser crash.</summary>
    public string RunId { get; } = runId;

    /// <summary>
    /// The CEC run state that the crashed run should be recorded with.
    /// Always <see cref="RunState.FAILED"/> or <see cref="RunState.UNKNOWN"/> —
    /// never SUCCEEDED, CANCELLED, TIMED_OUT, or HANDED_OFF.
    /// </summary>
    public RunState TerminalState { get; } = terminalState;

    /// <summary>Human-readable reason for the classification (for journal/log use only).</summary>
    public string Reason { get; } = reason;
}
