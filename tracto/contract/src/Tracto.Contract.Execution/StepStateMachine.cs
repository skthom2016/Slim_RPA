#nullable enable

using System.Collections.Generic;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public sealed class StepStateMachine
{
    private static readonly Dictionary<StepState, HashSet<StepState>> AllowedTransitions = new()
    {
        [StepState.Pending]   = [StepState.Running, StepState.Skipped, StepState.Cancelled],
        [StepState.Running]   = [StepState.Succeeded, StepState.Failed, StepState.TimedOut, StepState.Unknown, StepState.Cancelled],
        // Terminal step states
        [StepState.Succeeded] = [],
        [StepState.Failed]    = [],
        [StepState.TimedOut]  = [],
        [StepState.Unknown]   = [],
        [StepState.Skipped]   = [],
        [StepState.Cancelled] = [],
    };

    public bool CanTransition(StepState from, StepState to)
        => AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public bool IsTerminal(StepState state)
        => AllowedTransitions.TryGetValue(state, out var allowed) && allowed.Count == 0;
}
