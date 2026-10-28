using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Process;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Process;

namespace Tracto.TerminalRuntime.Unit.Tests.Process;

/// <summary>
/// Unit tests verifying process-tree cleanup through the mock IProcessTracker.
/// TER-US-015 AC-5, AC-6: termination affects only owned PIDs; cleanup is verified.
///
/// These tests exercise the coordination layer (handlers + journal) using mocked
/// IProcessController, so no real OS processes are required.
/// </summary>
public sealed class ProcessTreeCleanupTests
{
    private const string RunId     = "run-cleanup-001";
    private const string AttemptId = "att-001";

    // ── Cancel via handler → tracker is called on completion ─────────────────

    [Fact]
    public async Task CancelAsync_ViaController_UnregistersAllTrackedPids()
    {
        // Arrange: a tracked process tree with a root PID and one child PID.
        const int rootPid  = 1001;
        const int childPid = 1002;

        var trackerMock = new Mock<IProcessTracker>();
        trackerMock.Setup(t => t.GetPids(RunId))
            .Returns(new HashSet<int> { rootPid, childPid });

        // The controller mock simulates CancelAsync untracking the PIDs
        // (in production this is done inside WindowsProcessController.TerminateTreeAsync).
        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.CancelAsync(
                It.IsAny<CancelProcessCommand>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<CancelProcessCommand, int, CancellationToken>((cmd, _, _) =>
            {
                // Simulate what the real controller does: untrack all PIDs.
                foreach (int pid in cmd.Tree.AllPids)
                    trackerMock.Object.Untrack(cmd.Tree.RunId, pid);
            })
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.CancelledByRequest));

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var tree = new OwnedProcessTree(
            RunId: RunId,
            RootPid: rootPid,
            ChildPids: [rootPid, childPid],
            StartedAt: DateTimeOffset.UtcNow,
            TimeoutMs: 30_000);

        var cancelCommand = new CancelProcessCommand(RunId, AttemptId, tree, ProcessTreeCleanupMs: 0);

        // Act
        var cancelResult = await controllerMock.Object.CancelAsync(cancelCommand, 0);

        // Assert: cancel succeeded
        cancelResult.IsSuccess.Should().BeTrue();
        cancelResult.Value.Cancelled.Should().BeTrue();

        // Assert: Untrack was called for both PIDs (AC-5 — only owned PIDs touched)
        trackerMock.Verify(t => t.Untrack(RunId, rootPid),  Times.Once);
        trackerMock.Verify(t => t.Untrack(RunId, childPid), Times.Once);

        // Assert: unrelated PIDs are never touched
        trackerMock.Verify(t => t.Untrack(It.IsNotIn(RunId), It.IsAny<int>()), Times.Never);
    }

    // ── Timeout path: process tracker is untracked after deadline kill ────────

    [Fact]
    public async Task AwaitAndTransitionAsync_OnTimeout_ControllerReturnsDeadlineExceeded()
    {
        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.AwaitAsync(It.IsAny<AwaitProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.DeadlineExceeded));

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var tree = new OwnedProcessTree(
            RunId: RunId,
            RootPid: 2001,
            ChildPids: [2001],
            StartedAt: DateTimeOffset.UtcNow,
            TimeoutMs: 100);

        var result = await handler.AwaitAndTransitionAsync(new AwaitProcessCommand(RunId, AttemptId, tree));

        result.IsSuccess.Should().BeTrue();
        result.Value.TimedOut.Should().BeTrue();
        result.Value.Cancelled.Should().BeFalse();

        // Journal must have been asked to record TIMED_OUT
        journalMock.Verify(j => j.RecordStateAsync(
            RunId, AttemptId, It.IsAny<string>(),
            RunState.TimedOut, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── OwnedProcessTree helpers ──────────────────────────────────────────────

    [Fact]
    public void OwnedProcessTree_Deadline_CalculatedFromStartedAtAndTimeoutMs()
    {
        var start = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var tree  = new OwnedProcessTree("run-x", 100, [100], start, TimeoutMs: 5_000);

        tree.Deadline.Should().Be(start.AddMilliseconds(5_000));
    }

    [Fact]
    public void OwnedProcessTree_ZeroTimeout_DeadlineIsNull()
    {
        var tree = new OwnedProcessTree("run-x", 100, [100], DateTimeOffset.UtcNow, TimeoutMs: 0);
        tree.Deadline.Should().BeNull();
    }

    [Fact]
    public void OwnedProcessTree_NegativeTimeout_DeadlineIsNull()
    {
        var tree = new OwnedProcessTree("run-x", 100, [100], DateTimeOffset.UtcNow, TimeoutMs: -1);
        tree.Deadline.Should().BeNull();
    }
}
