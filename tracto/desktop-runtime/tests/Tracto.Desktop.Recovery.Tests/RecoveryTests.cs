using FluentAssertions;
using Tracto.Desktop.Recovery;
using Tracto.Desktop.Session;
using Xunit;

namespace Tracto.Desktop.Recovery.Tests;

/// <summary>
/// Unit tests for DT-16: crash detection, session-lock monitoring,
/// focus-theft detection, and attempt fencing.
/// </summary>
public sealed class RecoveryTests
{
    // =========================================================================
    // DT-16-AC01 — Process.HasExited = true → CrashDetector returns IsCrashed = true
    // =========================================================================

    [Fact]
    public void DT16_AC01_ProcessHasExited_IsCrashedTrue()
    {
        // Arrange: inject a fake process inspector that reports the process has exited.
        var detector = new CrashDetector(
            inspectProcess: _ => (hasExited: true, exitCode: 1),
            hasWerDialog: () => false);

        // Act
        var result = detector.DetectCrash(processId: 9999);

        // Assert
        result.IsCrashed.Should().BeTrue();
        result.ExitCode.Should().Be(1);
        result.WerDialogDetected.Should().BeFalse();
    }

    [Fact]
    public void DT16_AC01_ProcessStillRunning_IsCrashedFalse()
    {
        // Arrange: process is alive.
        var detector = new CrashDetector(
            inspectProcess: _ => (hasExited: false, exitCode: null),
            hasWerDialog: () => false);

        // Act
        var result = detector.DetectCrash(processId: 9999);

        // Assert
        result.IsCrashed.Should().BeFalse();
        result.ExitCode.Should().BeNull();
    }

    [Fact]
    public void DT16_AC01_WerDialogPresent_WerDialogDetectedTrue()
    {
        // Arrange: process has exited AND a WER dialog exists.
        var detector = new CrashDetector(
            inspectProcess: _ => (hasExited: true, exitCode: -1),
            hasWerDialog: () => true);

        // Act
        var result = detector.DetectCrash(processId: 9999);

        // Assert
        result.IsCrashed.Should().BeTrue();
        result.WerDialogDetected.Should().BeTrue();
    }

    // =========================================================================
    // DT-16-AC04 — FakeTimer past policyLimit; session stays Locked
    //              → LockMonitorResult.Expired = true; heartbeats sent
    // =========================================================================

    [Fact]
    public async Task DT16_AC04_SessionLockDeadlineExpires_ExpiredTrueAndHeartbeatsSent()
    {
        // Arrange
        var provider = new MockSessionStateProvider { State = SessionState.Locked };
        var hb = new MockHeartbeatSender();
        var timer = new FakeTimer();

        // policyLimit = 2 500 ms; HeartbeatIntervalMs = 1 000 ms
        // After 3 advances of 1 000 ms → elapsed = 3 000 ms ≥ 2 500 ms → Expired
        var policyLimit = TimeSpan.FromMilliseconds(2_500);
        var monitor = new SessionLockMonitor();

        // Act: start the monitor on the thread pool, then drive the FakeTimer.
        var monitorTask = monitor.MonitorAsync(
            provider, hb, policyLimit, timer, leaseId: "lease-001", ct: CancellationToken.None);

        // Advance past the policy limit in 1 000 ms steps, yielding after each
        // advance so the async state machine can progress on the thread pool.
        for (int i = 0; i < 4; i++)
        {
            timer.AdvanceMs(1_000);
            await Task.Yield();
        }

        var result = await monitorTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        result.Expired.Should().BeTrue("the lock outlasted the policy limit");
        result.Resumed.Should().BeFalse();
        result.ErrorCategory.Should().Be("TIMEOUT");
        hb.CallCount.Should().BeGreaterThan(0, "heartbeats must be sent during the lock period");
    }

    [Fact]
    public async Task DT16_AC04_SessionUnlocksBeforeDeadline_ResumedTrue()
    {
        // Arrange: session is initially Locked but unlocks after the first advance.
        var provider = new MockSessionStateProvider { State = SessionState.Locked };
        var hb = new MockHeartbeatSender();
        var timer = new FakeTimer();
        var policyLimit = TimeSpan.FromMilliseconds(30_000);
        var monitor = new SessionLockMonitor();

        var monitorTask = monitor.MonitorAsync(
            provider, hb, policyLimit, timer, leaseId: "lease-002", ct: CancellationToken.None);

        // Unlock the session before the deadline.
        timer.AdvanceMs(1_000);
        await Task.Yield();
        provider.State = SessionState.Interactive; // session unlocked

        timer.AdvanceMs(1_000);
        await Task.Yield();

        var result = await monitorTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        result.Resumed.Should().BeTrue();
        result.Expired.Should().BeFalse();
    }

    // =========================================================================
    // DT-16-AC05 — GetForegroundWindow != expected → FocusTheftResult.IsStolen = true
    // =========================================================================

    [Fact]
    public void DT16_AC05_ForegroundWindowDiffers_IsStolenTrue()
    {
        // Arrange: inject a fake that returns a different handle than the expected one.
        var expectedHandle = new IntPtr(0x1000);
        var stealingHandle = new IntPtr(0x2000);

        var detector = new FocusTheftDetector(
            getForegroundWindow: () => stealingHandle,
            getWindowTitle: _ => "Unexpected Notification");

        // Act
        var result = detector.Check(expectedHandle);

        // Assert
        result.IsStolen.Should().BeTrue();
        result.StealingWindowTitle.Should().Be("Unexpected Notification");
    }

    [Fact]
    public void DT16_AC05_ForegroundWindowMatches_IsStolenFalse()
    {
        // Arrange: foreground window is the expected one — no theft.
        var expectedHandle = new IntPtr(0x1000);

        var detector = new FocusTheftDetector(
            getForegroundWindow: () => expectedHandle,
            getWindowTitle: _ => null);

        // Act
        var result = detector.Check(expectedHandle);

        // Assert
        result.IsStolen.Should().BeFalse();
        result.StealingWindowTitle.Should().BeNull();
    }

    // =========================================================================
    // DT-16-AC06 — AttemptFenceState.TryFence behaviour
    //              First call → true; subsequent calls → false;
    //              ThrowIfFenced throws AttemptFencedException for ALL subsequent actions
    //              (including read-only queries — there is no bypass path).
    // =========================================================================

    [Fact]
    public void DT16_AC06_TryFence_FirstCallReturnsTrue_SubsequentReturnsFalse()
    {
        // Arrange
        var fence = new AttemptFenceState();

        // Act
        bool first = fence.TryFence("attempt-abc");
        bool second = fence.TryFence("attempt-abc");
        bool third = fence.TryFence("attempt-xyz"); // different ID, same fence object

        // Assert
        first.Should().BeTrue("the first call sets the fence");
        second.Should().BeFalse("the fence is already set");
        third.Should().BeFalse("the fence is still set regardless of attempt ID");
    }

    [Fact]
    public void DT16_AC06_ThrowIfFenced_AfterTryFence_ThrowsAttemptFencedException()
    {
        // Arrange
        var fence = new AttemptFenceState();
        fence.TryFence("attempt-abc");

        // Act & Assert: any subsequent action call must throw.
        var act = () => fence.ThrowIfFenced();
        act.Should().Throw<AttemptFencedException>()
            .Which.AttemptId.Should().Be("attempt-abc");
    }

    [Fact]
    public void DT16_AC06_ReadOnlyQuery_DoesNotSkipFenceCheck_ThrowsAttemptFencedException()
    {
        // Arrange: even a "read-only" action (simulated here by a second ThrowIfFenced call)
        // must not bypass the fence.
        var fence = new AttemptFenceState();
        fence.TryFence("attempt-def");

        // Act & Assert: first action throws.
        var firstQuery = () => fence.ThrowIfFenced();
        firstQuery.Should().Throw<AttemptFencedException>();

        // Act & Assert: second action also throws — no bypass for read-only queries.
        var secondQuery = () => fence.ThrowIfFenced();
        secondQuery.Should().Throw<AttemptFencedException>()
            .Which.AttemptId.Should().Be("attempt-def");
    }

    [Fact]
    public void DT16_AC06_UnfencedState_DoesNotThrow()
    {
        // Arrange: fence not yet set.
        var fence = new AttemptFenceState();

        // Act & Assert: no exception before the fence is raised.
        var act = () => fence.ThrowIfFenced();
        act.Should().NotThrow();
    }

    // =========================================================================
    // Supplementary — AttemptFencedException and UnknownOutcomeException carry their fields
    // =========================================================================

    [Fact]
    public void AttemptFencedException_CarriesAttemptId()
    {
        var ex = new AttemptFencedException("attempt-999");
        ex.AttemptId.Should().Be("attempt-999");
        ex.Message.Should().Contain("attempt-999");
    }

    [Fact]
    public void UnknownOutcomeException_CarriesUnknownReason()
    {
        var ex = new UnknownOutcomeException("action.commitUncertain");
        ex.UnknownReason.Should().Be("action.commitUncertain");
        ex.Message.Should().Contain("action.commitUncertain");
    }
}
