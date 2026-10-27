using FluentAssertions;
using Tracto.Desktop.Session;
using Xunit;

namespace Tracto.Desktop.Session.Tests;

public sealed class SessionEligibilityTests
{
    private static readonly DisplayProfile BaselineProfile = new(1920, 1080, 96, 96);

    // ── Helper factory ────────────────────────────────────────────────────────

    private static (SessionEligibilityChecker checker, MockSessionStateProvider sessionMock, MockDisplayProfileProvider displayMock)
        BuildChecker(SessionState state = SessionState.Interactive, DisplayProfile? display = null)
    {
        var sessionMock = new MockSessionStateProvider { State = state };
        var displayMock = new MockDisplayProfileProvider { Profile = display ?? BaselineProfile };
        var checker = new SessionEligibilityChecker(sessionMock, displayMock, BaselineProfile);
        return (checker, sessionMock, displayMock);
    }

    // ── DT-01-AC02: Locked session → SESSION_UNAVAILABLE ─────────────────────

    [Fact]
    public void LockedSession_ReturnsIneligible_WithSessionUnavailableDetailCode()
    {
        var (checker, _, _) = BuildChecker(SessionState.Locked);

        var result = checker.CheckEligibility();

        result.IsEligible.Should().BeFalse();
        result.ErrorCategory.Should().Be("SESSION_UNAVAILABLE");
        result.DetailCode.Should().Be("desktop.sessionUnavailable");
        result.State.Should().Be(SessionState.Locked);
    }

    // ── DT-01-AC02: Disconnected session → SESSION_UNAVAILABLE ───────────────

    [Fact]
    public void DisconnectedSession_ReturnsIneligible_WithSessionUnavailableDetailCode()
    {
        var (checker, _, _) = BuildChecker(SessionState.Disconnected);

        var result = checker.CheckEligibility();

        result.IsEligible.Should().BeFalse();
        result.ErrorCategory.Should().Be("SESSION_UNAVAILABLE");
        result.DetailCode.Should().Be("desktop.sessionUnavailable");
        result.State.Should().Be(SessionState.Disconnected);
    }

    // ── DT-01-AC02: NonInteractive session → SESSION_UNAVAILABLE ─────────────

    [Fact]
    public void NonInteractiveSession_ReturnsIneligible_WithSessionUnavailableDetailCode()
    {
        var (checker, _, _) = BuildChecker(SessionState.NonInteractive);

        var result = checker.CheckEligibility();

        result.IsEligible.Should().BeFalse();
        result.ErrorCategory.Should().Be("SESSION_UNAVAILABLE");
        result.DetailCode.Should().Be("desktop.sessionUnavailable");
    }

    // ── DT-01-AC03: Display mismatch → ENVIRONMENT ───────────────────────────

    [Fact]
    public void DisplayProfileMismatch_ReturnsIneligible_WithDisplayProfileChangedDetailCode()
    {
        var mismatch = new DisplayProfile(1280, 720, 96, 96);
        var (checker, _, _) = BuildChecker(SessionState.Interactive, mismatch);

        var result = checker.CheckEligibility();

        result.IsEligible.Should().BeFalse();
        result.ErrorCategory.Should().Be("ENVIRONMENT");
        result.DetailCode.Should().Be("desktop.displayProfileChanged");
        result.State.Should().Be(SessionState.Interactive);
    }

    // ── DT-01-AC01: Interactive session with matching display → eligible ──────

    [Fact]
    public void InteractiveSession_MatchingDisplay_ReturnsEligible()
    {
        var (checker, _, _) = BuildChecker(SessionState.Interactive, BaselineProfile);

        var result = checker.CheckEligibility();

        result.IsEligible.Should().BeTrue();
        result.ErrorCategory.Should().BeNull();
        result.DetailCode.Should().BeNull();
        result.State.Should().Be(SessionState.Interactive);
    }

    // ── DT-01-AC04: Two concurrent TryAcquireLease → only first returns true ─

    [Fact]
    public async Task TwoConcurrentAcquire_OnlyFirstSucceeds()
    {
        using var manager = new InputLeaseManager();

        bool first = false, second = false;

        var t1 = Task.Run(() => { first = manager.TryAcquireLease("job-1"); });
        var t2 = Task.Run(() => { second = manager.TryAcquireLease("job-2"); });

        await Task.WhenAll(t1, t2);

        // Exactly one of the two should have acquired the lease.
        (first ^ second).Should().BeTrue("exactly one of two concurrent TryAcquireLease calls must succeed");
        manager.IsLeaseHeld.Should().BeTrue();
    }

    // ── DT-01-AC04: Sequential acquire succeeds; second is rejected ──────────

    [Fact]
    public void SequentialAcquire_SecondRejected()
    {
        using var manager = new InputLeaseManager();

        var first = manager.TryAcquireLease("job-1");
        var second = manager.TryAcquireLease("job-2");

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    // ── DT-01-AC08: Release then re-acquire succeeds ─────────────────────────

    [Fact]
    public void ReleaseThenReacquire_Succeeds()
    {
        using var manager = new InputLeaseManager();

        var acquired = manager.TryAcquireLease("job-1");
        acquired.Should().BeTrue();

        manager.IsLeaseHeld.Should().BeTrue();
        manager.ReleaseLease("job-1");
        manager.IsLeaseHeld.Should().BeFalse();

        var reacquired = manager.TryAcquireLease("job-2");
        reacquired.Should().BeTrue();
        manager.IsLeaseHeld.Should().BeTrue();
    }

    // ── IsLeaseHeld reflects state correctly ─────────────────────────────────

    [Fact]
    public void IsLeaseHeld_FalseInitially_TrueAfterAcquire_FalseAfterRelease()
    {
        using var manager = new InputLeaseManager();

        manager.IsLeaseHeld.Should().BeFalse();

        manager.TryAcquireLease("job-X");
        manager.IsLeaseHeld.Should().BeTrue();

        manager.ReleaseLease("job-X");
        manager.IsLeaseHeld.Should().BeFalse();
    }
}
