#nullable enable

using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Testing;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class RepeatBoundedPolicyTests
{
    // ── Test 1: iterations <= DefaultMaxIterations → no error ─────────────────

    [Fact]
    public void Validate_WithinDefault_ReturnsNull()
    {
        var sut = new RepeatBoundedPolicy();

        var error = sut.Validate(RepeatBoundedPolicy.DefaultMaxIterations);

        error.Should().BeNull();
    }

    // ── Test 2: iterations > DefaultMaxIterations → error containing "policy cap" ─────────────────

    [Fact]
    public void Validate_ExceedsDefault_ReturnsError_ContainingPolicyCap()
    {
        var sut = new RepeatBoundedPolicy();

        var error = sut.Validate(RepeatBoundedPolicy.DefaultMaxIterations + 1);

        error.Should().NotBeNull();
        error.Should().Contain("policy cap");
    }

    // ── Test 3: iterations = 0 → error (must be >= 1) ────────────────────────

    [Fact]
    public void Validate_ZeroIterations_ReturnsError()
    {
        var sut = new RepeatBoundedPolicy();

        var error = sut.Validate(0);

        error.Should().NotBeNull();
    }

    // ── Test 4: IsWithinBound(100) = true, IsWithinBound(101) = false (default) ─

    [Fact]
    public void IsWithinBound_AtDefaultMax_ReturnsTrue()
    {
        var sut = new RepeatBoundedPolicy();

        sut.IsWithinBound(RepeatBoundedPolicy.DefaultMaxIterations).Should().BeTrue();
    }

    [Fact]
    public void IsWithinBound_ExceedsDefaultMax_ReturnsFalse()
    {
        var sut = new RepeatBoundedPolicy();

        sut.IsWithinBound(RepeatBoundedPolicy.DefaultMaxIterations + 1).Should().BeFalse();
    }

    // ── Test 5: Custom workerAdvertisedMax = 50 → iterations 51 fails ─────────

    [Fact]
    public void Validate_CustomMax50_Iterations51_ReturnsError()
    {
        var sut = new RepeatBoundedPolicy(workerAdvertisedMax: 50);

        var error = sut.Validate(51);

        error.Should().NotBeNull();
        error.Should().Contain("policy cap");
    }
}
