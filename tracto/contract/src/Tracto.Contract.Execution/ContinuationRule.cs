#nullable enable

// Represents a static continuation rule evaluated after a run reaches a terminal state.
// REF-04: Continuation rules are statically configured and applied only when explicitly present.

using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

// Represents a static continuation rule: on state S, jump to step X or hand off.
public record ContinuationRule(
    RunState TriggerState,
    ContinuationAction Action,
    string? TargetStepId = null,   // for GoTo action
    string? HandoffTarget = null); // for HandOff action

public enum ContinuationAction { GoTo, HandOff, Terminate }
