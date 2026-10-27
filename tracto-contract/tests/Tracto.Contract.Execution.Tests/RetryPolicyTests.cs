#nullable enable

using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class RetryPolicyTests
{
    private readonly RetryPolicy _sut = new();

    // ── Test 1: FAILED + TRANSIENT_SAFE + no side effect + attempt 1 of 3 → CanRetry = true

    [Fact]
    public void CanRetry_Failed_TransientSafe_NoSideEffect_UnderMax_ReturnsTrue()
    {
        var result = _sut.CanRetry(
            StepState.Failed,
            RetryClass.TransientSafe,
            hasCommittedSideEffect: false,
            attemptNumber: 1,
            maxAttempts: 3);

        result.Should().BeTrue();
    }

    // ── Test 2: FAILED + TRANSIENT_SAFE + hasCommittedSideEffect → CanRetry = false

    [Fact]
    public void CanRetry_Failed_TransientSafe_HasSideEffect_ReturnsFalse()
    {
        var result = _sut.CanRetry(
            StepState.Failed,
            RetryClass.TransientSafe,
            hasCommittedSideEffect: true,
            attemptNumber: 1,
            maxAttempts: 3);

        result.Should().BeFalse();
    }

    // ── Test 3: FAILED + NEVER → CanRetry = false

    [Fact]
    public void CanRetry_Failed_Never_ReturnsFalse()
    {
        var result = _sut.CanRetry(
            StepState.Failed,
            RetryClass.Never,
            hasCommittedSideEffect: false,
            attemptNumber: 1,
            maxAttempts: 1);

        result.Should().BeFalse();
    }

    // ── Test 4: UNKNOWN + any retryClass → CanRetry = false (C3: never retry blindly)

    [Fact]
    public void CanRetry_Unknown_AnyRetryClass_ReturnsFalse()
    {
        // Try with every RetryClass to confirm the rule is universal
        foreach (var retryClass in new[] { RetryClass.TransientSafe, RetryClass.Idempotent, RetryClass.Never, RetryClass.OnceOnly })
        {
            var result = _sut.CanRetry(
                StepState.Unknown,
                retryClass,
                hasCommittedSideEffect: false,
                attemptNumber: 1,
                maxAttempts: 3);

            result.Should().BeFalse($"UNKNOWN must never be retried (C3 rule), but {retryClass} returned true");
        }
    }

    // ── Test 5: TIMED_OUT + TRANSIENT_SAFE + no side effect + attempt 1 → CanRetry = true

    [Fact]
    public void CanRetry_TimedOut_TransientSafe_NoSideEffect_UnderMax_ReturnsTrue()
    {
        var result = _sut.CanRetry(
            StepState.TimedOut,
            RetryClass.TransientSafe,
            hasCommittedSideEffect: false,
            attemptNumber: 1,
            maxAttempts: 3);

        result.Should().BeTrue();
    }

    // ── Test 6: TIMED_OUT + TRANSIENT_SAFE + hasCommittedSideEffect → CanRetry = false

    [Fact]
    public void CanRetry_TimedOut_TransientSafe_HasSideEffect_ReturnsFalse()
    {
        var result = _sut.CanRetry(
            StepState.TimedOut,
            RetryClass.TransientSafe,
            hasCommittedSideEffect: true,
            attemptNumber: 1,
            maxAttempts: 3);

        result.Should().BeFalse();
    }

    // ── Test 7: FAILED + IDEMPOTENT + attempt 1 of 2 → CanRetry = true

    [Fact]
    public void CanRetry_Failed_Idempotent_UnderMax_ReturnsTrue()
    {
        var result = _sut.CanRetry(
            StepState.Failed,
            RetryClass.Idempotent,
            hasCommittedSideEffect: false,
            attemptNumber: 1,
            maxAttempts: 2);

        result.Should().BeTrue();
    }

    // ── Test 8: FAILED + IDEMPOTENT + at max attempts → CanRetry = false

    [Fact]
    public void CanRetry_Failed_Idempotent_AtMaxAttempts_ReturnsFalse()
    {
        var result = _sut.CanRetry(
            StepState.Failed,
            RetryClass.Idempotent,
            hasCommittedSideEffect: false,
            attemptNumber: 2,
            maxAttempts: 2);

        result.Should().BeFalse();
    }

    // ── Test 9: GetMaxAttempts(TransientSafe) = 3

    [Fact]
    public void GetMaxAttempts_TransientSafe_Returns3()
    {
        RetryPolicy.GetMaxAttempts(RetryClass.TransientSafe).Should().Be(3);
    }

    // ── Test 10: GetMaxAttempts(Never) = 1

    [Fact]
    public void GetMaxAttempts_Never_Returns1()
    {
        RetryPolicy.GetMaxAttempts(RetryClass.Never).Should().Be(1);
    }
}
