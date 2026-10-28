#nullable enable

using Tracto.Contract.Models;

namespace Tracto.Mock.Orchestrator;

/// <summary>Tracks the full state of one active lease within the mock orchestrator.</summary>
internal sealed class LeaseRecord
{
    public string LeaseId       { get; init; } = default!;
    public string RunId         { get; init; } = default!;
    public string AttemptId     { get; init; } = default!;
    public string WorkerId      { get; init; } = default!;
    public bool   IsInteractive { get; init; }
    public string? SessionId    { get; init; }

    // Monotonic heartbeat sequence guard.
    public int LastHeartbeatSequence { get; private set; } = -1;

    public bool TryAdvanceSequence(int incoming)
    {
        if (incoming <= LastHeartbeatSequence) return false;
        LastHeartbeatSequence = incoming;
        return true;
    }

    public bool CancellationRequested { get; set; }

    // Terminal outcome (null = still active).
    public RunState? TerminalState { get; private set; }
    public bool      IsTerminal    => TerminalState.HasValue;

    public void MarkTerminal(RunState state)
    {
        TerminalState = state;
    }
}
