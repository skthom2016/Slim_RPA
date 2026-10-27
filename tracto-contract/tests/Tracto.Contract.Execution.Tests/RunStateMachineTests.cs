#nullable enable

using FluentAssertions;
using Tracto.Contract.Execution;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Xunit;

namespace Tracto.Contract.Execution.Tests;

public sealed class RunStateMachineTests
{
    private readonly RunStateMachine _sut = new();

    // ── Allowed transitions ──────────────────────────────────────────────────

    [Fact]
    public void Created_To_Queued_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Created, RunState.Queued);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Queued_To_Leased_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Queued, RunState.Leased);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Leased_To_Running_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Leased, RunState.Running);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Running_To_Succeeded_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Running, RunState.Succeeded);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Running_To_Failed_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Running, RunState.Failed);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Running_To_Unknown_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Running, RunState.Unknown);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Running_To_TimedOut_IsAllowed()
    {
        var result = _sut.CanTransition(RunState.Running, RunState.TimedOut);
        result.IsAllowed.Should().BeTrue();
    }

    // ── Illegal transitions ──────────────────────────────────────────────────

    [Fact]
    public void Succeeded_To_Running_IsRejected_CannotLeaveTerminalState()
    {
        var result = _sut.CanTransition(RunState.Succeeded, RunState.Running);
        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Unknown_To_Queued_IsRejected_FencingRule()
    {
        var result = _sut.CanTransition(RunState.Unknown, RunState.Queued);
        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Transition_Throws_WhenNotAllowed()
    {
        Action act = () => _sut.Transition(RunState.Succeeded, RunState.Running);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── IsTerminal ───────────────────────────────────────────────────────────

    [Fact]
    public void IsTerminal_Succeeded_ReturnsTrue()
    {
        _sut.IsTerminal(RunState.Succeeded).Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_Running_ReturnsFalse()
    {
        _sut.IsTerminal(RunState.Running).Should().BeFalse();
    }

    // ── GetAllowedTransitions ────────────────────────────────────────────────

    [Fact]
    public void GetAllowedTransitions_Created_ContainsQueued_And_Cancelled()
    {
        var allowed = _sut.GetAllowedTransitions(RunState.Created);
        allowed.Should().Contain(RunState.Queued);
        allowed.Should().Contain(RunState.Cancelled);
    }

    // ── AttemptFence ─────────────────────────────────────────────────────────

    [Fact]
    public void AttemptFence_IsNewAttemptBlocked_Unknown_ReturnsTrue()
    {
        var fence = new AttemptFence();
        fence.IsNewAttemptBlocked(RunState.Unknown).Should().BeTrue();
    }

    [Fact]
    public void AttemptFence_IsNewAttemptBlocked_Failed_ReturnsFalse()
    {
        var fence = new AttemptFence();
        fence.IsNewAttemptBlocked(RunState.Failed).Should().BeFalse();
    }

    [Fact]
    public void AttemptFence_IsNewAttemptBlocked_Succeeded_ReturnsFalse()
    {
        var fence = new AttemptFence();
        fence.IsNewAttemptBlocked(RunState.Succeeded).Should().BeFalse();
    }

    [Fact]
    public void AttemptFence_GetRepublishDiagnostic_ContainsRepublish_And_UnsupportedVersion()
    {
        var unsupportedVersion = "99.0";
        var supportedMajors = new List<int> { 1, 2 };
        var diagnostic = AttemptFence.GetRepublishDiagnostic(unsupportedVersion, supportedMajors);
        diagnostic.Should().Contain("Republish");
        diagnostic.Should().Contain(unsupportedVersion);
    }
}
