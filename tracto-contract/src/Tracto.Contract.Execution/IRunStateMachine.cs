#nullable enable

using System.Collections.Generic;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Execution;

public interface IRunStateMachine
{
    // Returns whether the transition From→To is allowed.
    TransitionResult CanTransition(RunState from, RunState to);

    // Applies transition — throws if not allowed.
    RunState Transition(RunState current, RunState next);

    // Returns all valid next states from the current state.
    IReadOnlyList<RunState> GetAllowedTransitions(RunState from);

    bool IsTerminal(RunState state);

    // C3 APPROVED (2026-10-23): TIMED_OUT handling — allowed only when non-commitment is safely confirmed
    // (retryClass=TRANSIENT_SAFE and no side effect could have committed). See decision C3.
    // This transition must NOT be used when commitment uncertainty exists — use UNKNOWN instead.

    // C3 APPROVED (2026-10-23): UNKNOWN handling — correct terminal state when commitment cannot be established.
    // A run terminating in UNKNOWN must set needsAttention=true and populate unknownReason.
    // No second attempt may be dispatched without affirmative reconciliation proof.
    // RunState.Unknown is terminal; transitions FROM Unknown are not permitted by the state machine.
}
