#nullable enable

// Implements the run state machine from Contract v1.0.1 §5.

using System;
using System.Collections.Generic;
using System.Linq;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Execution;

public sealed class RunStateMachine : IRunStateMachine
{
    // Allowed transitions — keyed by current state
    private static readonly Dictionary<RunState, HashSet<RunState>> AllowedTransitions = new()
    {
        [RunState.Created]   = [RunState.Queued, RunState.Cancelled],
        [RunState.Queued]    = [RunState.Leased, RunState.Cancelled],
        [RunState.Leased]    = [RunState.Running, RunState.Queued, RunState.Cancelled], // Queued on lease expiry before work begins
        [RunState.Running]   = [RunState.Succeeded, RunState.Failed, RunState.TimedOut, RunState.Unknown, RunState.Cancelled, RunState.HandedOff],
        // Terminal states — no outbound transitions
        [RunState.Succeeded] = [],
        [RunState.Failed]    = [],
        [RunState.TimedOut]  = [],
        [RunState.Unknown]   = [],
        [RunState.Cancelled] = [],
        [RunState.HandedOff] = [],
    };

    public TransitionResult CanTransition(RunState from, RunState to)
    {
        if (AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to))
            return new TransitionResult(true);
        return new TransitionResult(false, $"Transition {from}→{to} is not permitted by the contract state machine.");
    }

    public RunState Transition(RunState current, RunState next)
    {
        var result = CanTransition(current, next);
        if (!result.IsAllowed)
            throw new InvalidOperationException(result.RejectionReason ?? $"Illegal state transition: {current} → {next}");
        return next;
    }

    public IReadOnlyList<RunState> GetAllowedTransitions(RunState from)
        => AllowedTransitions.TryGetValue(from, out var set) ? set.ToList() : Array.Empty<RunState>();

    public bool IsTerminal(RunState state)
        => AllowedTransitions.TryGetValue(state, out var allowed) && allowed.Count == 0;
}
