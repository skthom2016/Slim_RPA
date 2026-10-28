#nullable enable

using System;
using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class StepLifecycleTests
{
    private static StepLifecycle BuildLifecycle()
    {
        var stateMachine = new StepStateMachine();
        var retryPolicy  = new RetryPolicy();
        var fence        = new AttemptFence();
        var clock        = new FakeTimeProvider();
        return new StepLifecycle(stateMachine, retryPolicy, fence, clock);
    }

    private static AttemptHistory OneAttempt(StepState state)
    {
        var h = new AttemptHistory();
        h.Record(new RetryAttemptRecord(
            AttemptNumber: 1,
            OutcomeState: state,
            ErrorCategory: null,
            ErrorDetail: null,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            HasCommittedSideEffect: false,
            UnknownReason: null));
        return h;
    }

    // ── Test 1: SUCCEEDED → Proceed, no retry ────────────────────────────────

    [Fact]
    public void Decide_Succeeded_ReturnsProceed_NoRetry()
    {
        var sut     = BuildLifecycle();
        var history = OneAttempt(StepState.Succeeded);

        var decision = sut.Decide(StepState.Succeeded, RetryClass.TransientSafe,
            hasCommittedSideEffect: false, history);

        decision.Outcome.Should().Be(StepLifecycleOutcome.Proceed);
        decision.ShouldRetry.Should().BeFalse();
    }

    // ── Test 2: FAILED + TRANSIENT_SAFE + no side effect + under max → Retry ─

    [Fact]
    public void Decide_Failed_TransientSafe_NoSideEffect_UnderMax_ReturnsRetry()
    {
        var sut     = BuildLifecycle();
        var history = OneAttempt(StepState.Failed); // 1 attempt, max is 3

        var decision = sut.Decide(StepState.Failed, RetryClass.TransientSafe,
            hasCommittedSideEffect: false, history);

        decision.Outcome.Should().Be(StepLifecycleOutcome.Retry);
        decision.ShouldRetry.Should().BeTrue();
    }

    // ── Test 3: FAILED + NEVER → Fail, no retry ──────────────────────────────

    [Fact]
    public void Decide_Failed_Never_ReturnsFail_NoRetry()
    {
        var sut     = BuildLifecycle();
        var history = OneAttempt(StepState.Failed);

        var decision = sut.Decide(StepState.Failed, RetryClass.Never,
            hasCommittedSideEffect: false, history);

        decision.Outcome.Should().Be(StepLifecycleOutcome.Fail);
        decision.ShouldRetry.Should().BeFalse();
    }

    // ── Test 4: UNKNOWN → FenceRun (C3 approved) ─────────────────────────────

    [Fact]
    public void Decide_Unknown_ReturnsFenceRun()
    {
        var sut     = BuildLifecycle();
        var history = OneAttempt(StepState.Unknown);

        var decision = sut.Decide(StepState.Unknown, RetryClass.TransientSafe,
            hasCommittedSideEffect: false, history);

        decision.Outcome.Should().Be(StepLifecycleOutcome.FenceRun);
        decision.ShouldRetry.Should().BeFalse();
    }

    // ── Test 5: TIMED_OUT + TRANSIENT_SAFE + no side effect → Retry ──────────

    [Fact]
    public void Decide_TimedOut_TransientSafe_NoSideEffect_ReturnsRetry()
    {
        var sut     = BuildLifecycle();
        var history = OneAttempt(StepState.TimedOut); // 1 attempt, max is 3

        var decision = sut.Decide(StepState.TimedOut, RetryClass.TransientSafe,
            hasCommittedSideEffect: false, history);

        decision.Outcome.Should().Be(StepLifecycleOutcome.Retry);
        decision.ShouldRetry.Should().BeTrue();
    }
}
