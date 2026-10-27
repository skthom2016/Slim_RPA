#nullable enable

using System;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

// Coordinates the validation → execution → outcome → retry loop for a single step.
// Does NOT invoke the actual action (that's the Runtime's job); manages the state around it.
public sealed class StepLifecycle
{
    private readonly StepStateMachine _stateMachine;
    private readonly RetryPolicy _retryPolicy;
    private readonly AttemptFence _attemptFence;
    private readonly TimeProvider _timeProvider;

    public StepLifecycle(
        StepStateMachine stateMachine,
        RetryPolicy retryPolicy,
        AttemptFence attemptFence,
        TimeProvider timeProvider)
    {
        _stateMachine = stateMachine;
        _retryPolicy = retryPolicy;
        _attemptFence = attemptFence;
        _timeProvider = timeProvider;
    }

    // Determines what happens after a step attempt completes with the given outcome.
    public StepLifecycleDecision Decide(
        StepState outcomeState,
        RetryClass retryClass,
        bool hasCommittedSideEffect,
        AttemptHistory history)
    {
        // UNKNOWN: fence immediately, no retry
        if (outcomeState == StepState.Unknown)
            return new StepLifecycleDecision(StepLifecycleOutcome.FenceRun, outcomeState, ShouldRetry: false);

        int maxAttempts = RetryPolicy.GetMaxAttempts(retryClass);
        bool canRetry = _retryPolicy.CanRetry(
            outcomeState, retryClass, hasCommittedSideEffect,
            history.AttemptCount, maxAttempts);

        if (canRetry)
            return new StepLifecycleDecision(StepLifecycleOutcome.Retry, outcomeState, ShouldRetry: true);

        if (outcomeState == StepState.Succeeded)
            return new StepLifecycleDecision(StepLifecycleOutcome.Proceed, outcomeState, ShouldRetry: false);

        return new StepLifecycleDecision(StepLifecycleOutcome.Fail, outcomeState, ShouldRetry: false);
    }

    // Creates a timestamped attempt record for the completed attempt.
    public RetryAttemptRecord CreateRecord(
        int attemptNumber,
        StepState outcomeState,
        ErrorCategory? errorCategory,
        string? errorDetail,
        DateTimeOffset startedAt,
        bool hasCommittedSideEffect,
        string? unknownReason)
    {
        return new RetryAttemptRecord(
            attemptNumber, outcomeState, errorCategory, errorDetail,
            startedAt, _timeProvider.GetUtcNow(),
            hasCommittedSideEffect, unknownReason);
    }
}

public enum StepLifecycleOutcome { Proceed, Retry, Fail, FenceRun }
public record StepLifecycleDecision(StepLifecycleOutcome Outcome, StepState FinalStepState, bool ShouldRetry);
