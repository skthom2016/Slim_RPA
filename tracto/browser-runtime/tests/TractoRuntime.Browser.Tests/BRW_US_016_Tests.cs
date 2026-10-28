using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TractoRuntime.Browser.Cancellation;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-016 — Cancellation and Resource Release.
/// Covers BRW-AT-15, BRW-AT-23.
/// Requirement refs: BRE-008, BRW-004.
/// </summary>
/// <remarks>
///   AC#1 — Cancellation signal is propagated through the run's CancellationToken;
///           in-flight downloads and navigations stop and partial files are cleaned up.
///   AC#2 — All browser contexts, pages, and processes are closed within the configured
///           bounded time; on timeout, forced release is performed.
///   AC#3 — Worker lease and profile lock are released (in the forced path via the
///           lease manager; in the normal path by the run executor before signalling
///           cleanup complete).
///   AC#4 — No orphaned context or exposed credentials remain after cancellation.
/// </remarks>
public sealed class BRW_US_016_Tests : BrowserTestBase
{
    // ── Helper factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="RunCancellationCoordinator"/> wired with the shared base-class
    /// mocks and the supplied <paramref name="cleanupTimeout"/>.
    /// </summary>
    private RunCancellationCoordinator CreateCoordinator(TimeSpan? cleanupTimeout = null)
    {
        var opts = new RunCancellationOptions
        {
            CleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(30)
        };

        // Wire all mocks with permissive setups so individual tests focus on specific behaviour.
        ContextManagerMock
            .Setup(m => m.ReleaseContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        LeaseManagerMock
            .Setup(m => m.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        JournalMock
            .Setup(j => j.CloseAsync(
                It.IsAny<string>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new RunCancellationCoordinator(
            ContextManagerMock.Object,
            LeaseManagerMock.Object,
            JournalMock.Object,
            opts,
            NullLogger<RunCancellationCoordinator>.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — CancellationToken propagation: CancelRunAsync signals the run's CTS
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_RegisteredRun_SignalsCancellationToken()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var runId = "run-signal-001";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts);

        // Act — cancel and then immediately signal cleanup so the wait completes quickly
        var cancelTask = coordinator.CancelRunAsync(runId, CancellationToken.None);
        coordinator.SignalCleanupComplete(runId);

        var result = await cancelTask;

        // Assert
        result.CancellationSignalled.Should().BeTrue("the run CTS was registered and must be cancelled");
        runCts.IsCancellationRequested.Should().BeTrue("the run token must fire to stop in-flight operations");
    }

    [Fact]
    public async Task CancelRunAsync_TokenIsPassedThroughToOperations()
    {
        // Arrange — verifies that the token obtained from a registered CTS is the one that fires.
        var coordinator = CreateCoordinator();
        var runId = "run-signal-002";
        using var runCts = new CancellationTokenSource();
        var runToken = runCts.Token;    // captured before registration
        coordinator.RegisterRun(runId, runCts);

        // A simulated in-flight operation awaiting the token
        var operationCancelled = false;
        var opTask = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, runToken); }
            catch (OperationCanceledException) { operationCancelled = true; }
        });

        // Act
        var cancelTask = coordinator.CancelRunAsync(runId, CancellationToken.None);
        coordinator.SignalCleanupComplete(runId);
        await cancelTask;

        // Wait for the simulated operation to react
        await opTask;

        // Assert — the in-flight operation received the cancellation signal (AC#1)
        operationCancelled.Should().BeTrue(
            "the run's CancellationToken must propagate to every in-flight operation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — CancelRunAsync for an unknown run returns CancellationSignalled=false
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_UnknownRun_ReturnsCancellationSignalledFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();

        // Act — no run has been registered with this ID
        var result = await coordinator.CancelRunAsync("run-unknown-001", CancellationToken.None);

        // Assert — no-op, nothing to cancel
        result.CancellationSignalled.Should().BeFalse(
            "a run that was never registered has no CTS to cancel");
        result.CleanupCompletedInTime.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#2 — Cleanup completed in time: CleanupCompletedInTime=true
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_WhenCleanupCompletesInTime_ReturnsCleanupCompletedTrue()
    {
        // Arrange
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromSeconds(5));
        var runId = "run-cleanup-001";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts);

        // Act — signal cleanup complete before the timeout expires
        var cancelTask = coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Simulate the run executor finishing cleanup shortly after cancellation fires
        await Task.Delay(20);   // small artificial delay; well within 5s timeout
        coordinator.SignalCleanupComplete(runId);

        var result = await cancelTask;

        // Assert
        result.CleanupCompletedInTime.Should().BeTrue(
            "cleanup was signalled before the timeout expired");
        result.CancellationSignalled.Should().BeTrue();

        // In the normal path the coordinator must NOT force-release resources
        ContextManagerMock.Verify(
            m => m.ReleaseContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "forced release must not be called when cleanup completes in time");

        LeaseManagerMock.Verify(
            m => m.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "lease release must not be forced when cleanup completes in time");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#2 — Forced release on timeout: context is closed, CleanupCompletedInTime=false
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_WhenCleanupTimesOut_ForceReleasesContext()
    {
        // Arrange — very short timeout so the test does not block
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-timeout-001";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts, leaseToken: null);

        // Act — do NOT call SignalCleanupComplete → timeout fires
        var result = await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — forced path was taken
        result.CleanupCompletedInTime.Should().BeFalse(
            "cleanup was not signalled within the configured timeout");
        result.CancellationSignalled.Should().BeTrue();

        // AC#2: Context must have been force-released
        ContextManagerMock.Verify(
            m => m.ReleaseContextAsync(runId, CancellationToken.None),
            Times.Once,
            "the browser context must be force-released when the bounded cleanup time expires");
    }

    [Fact]
    public async Task CancelRunAsync_WhenCleanupTimesOut_ClosesJournalWithCancelledState()
    {
        // Arrange
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-timeout-002";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts);

        // Act
        await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — journal must be closed with CANCELLED state for ReconciliationService
        JournalMock.Verify(
            j => j.CloseAsync(runId, RunState.CANCELLED, CancellationToken.None),
            Times.Once,
            "the run journal must be closed with CANCELLED so reconciliation has a record");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#3 — Lease release in forced path
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_WhenCleanupTimesOut_ReleasesLeaseWhenTokenProvided()
    {
        // Arrange — register run with a lease token
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-lease-001";
        const string leaseToken = "lease-token-abc123";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts, leaseToken: leaseToken);

        // Act
        var result = await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — AC#3: lease released during forced cleanup
        result.CleanupCompletedInTime.Should().BeFalse();
        LeaseManagerMock.Verify(
            m => m.ReleaseAsync(leaseToken, CancellationToken.None),
            Times.Once,
            "the Orchestrator lease must be released so the worker is available for new jobs");
    }

    [Fact]
    public async Task CancelRunAsync_WhenCleanupTimesOut_DoesNotReleaseLeaseWhenNoToken()
    {
        // Arrange — no lease token: run may have been cancelled before lease acquisition
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-lease-002";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts, leaseToken: null);

        // Act
        await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — no lease release call when no token was registered
        LeaseManagerMock.Verify(
            m => m.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no lease release should be attempted when no lease token was registered");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IsRunActive: reflects registration state correctly
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsRunActive_ReflectsRegistrationAndCancellationLifecycle()
    {
        // Arrange
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromSeconds(5));
        var runId = "run-active-001";
        using var runCts = new CancellationTokenSource();

        // Before registration
        coordinator.IsRunActive(runId).Should().BeFalse("run has not been registered yet");

        // After registration
        coordinator.RegisterRun(runId, runCts);
        coordinator.IsRunActive(runId).Should().BeTrue("run is now registered");

        // After normal cancellation (cleanup signalled in time)
        var cancelTask = coordinator.CancelRunAsync(runId, CancellationToken.None);
        coordinator.SignalCleanupComplete(runId);
        await cancelTask;

        coordinator.IsRunActive(runId).Should().BeFalse(
            "run entry must be removed after CancelRunAsync returns");
    }

    [Fact]
    public async Task IsRunActive_FalseAfterForcedCleanup()
    {
        // Arrange
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-active-002";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts);

        coordinator.IsRunActive(runId).Should().BeTrue();

        // Act — let timeout expire
        await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — forced cleanup removes the entry
        coordinator.IsRunActive(runId).Should().BeFalse(
            "run entry must be removed even after a forced cleanup");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RegisterRun: duplicate registration throws
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterRun_DuplicateRunId_ThrowsInvalidOperationException()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var runId = "run-duplicate-001";
        using var runCts1 = new CancellationTokenSource();
        using var runCts2 = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts1);

        // Act
        var act = () => coordinator.RegisterRun(runId, runCts2);

        // Assert — second registration is refused
        act.Should().Throw<InvalidOperationException>(
            "registering the same runId twice indicates a lifecycle bug and must be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SignalCleanupComplete: idempotent and safe when no run is registered
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SignalCleanupComplete_UnknownRun_DoesNotThrow()
    {
        // Arrange
        var coordinator = CreateCoordinator();

        // Act — safe to call for a run that was never registered (e.g. after forced cleanup)
        var act = () => coordinator.SignalCleanupComplete("run-unknown-signal-001");

        // Assert
        act.Should().NotThrow(
            "SignalCleanupComplete must be safe to call for any runId without preconditions");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#4 — No orphaned context: forced release errors do not suppress each other
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelRunAsync_WhenContextReleaseThrows_StillReleasesLeaseAndClosesJournal()
    {
        // Arrange — context release fails (e.g. browser already crashed)
        ContextManagerMock
            .Setup(m => m.ReleaseContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Browser process already terminated."));

        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromMilliseconds(40));
        var runId = "run-resilience-001";
        const string leaseToken = "lease-resilience-abc";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts, leaseToken: leaseToken);

        // Act — timeout fires, forced release encounters a context error
        var result = await coordinator.CancelRunAsync(runId, CancellationToken.None);

        // Assert — lease and journal steps still executed despite context release failure (AC#4)
        result.CleanupCompletedInTime.Should().BeFalse();

        LeaseManagerMock.Verify(
            m => m.ReleaseAsync(leaseToken, CancellationToken.None),
            Times.Once,
            "lease must be released even when context.ReleaseContextAsync throws");

        JournalMock.Verify(
            j => j.CloseAsync(runId, RunState.CANCELLED, CancellationToken.None),
            Times.Once,
            "journal must be closed even when context.ReleaseContextAsync throws");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CEC contract: RunCancellationResult data contract
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunCancellationResult_CleanupDuration_IsNonNegative()
    {
        // Arrange
        var coordinator = CreateCoordinator(cleanupTimeout: TimeSpan.FromSeconds(5));
        var runId = "run-duration-001";
        using var runCts = new CancellationTokenSource();
        coordinator.RegisterRun(runId, runCts);

        var cancelTask = coordinator.CancelRunAsync(runId, CancellationToken.None);
        coordinator.SignalCleanupComplete(runId);
        var result = await cancelTask;

        // Assert
        result.CleanupDuration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero,
            "wall-clock elapsed time is always non-negative");
    }

    [Fact]
    public void RunCancellationOptions_DefaultCleanupTimeout_IsThirtySeconds()
    {
        // This test documents the default timeout value so it is visible in the test report.
        var opts = new RunCancellationOptions();

        opts.CleanupTimeout.Should().Be(TimeSpan.FromSeconds(30),
            "the default bounded cleanup window must be 30 seconds per BRW-US-016 AC#2");
    }
}
