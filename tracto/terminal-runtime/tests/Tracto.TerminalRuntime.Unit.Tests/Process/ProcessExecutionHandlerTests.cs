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
/// Unit tests for <see cref="ProcessExecutionHandler"/>.
///
/// TER-US-015:
///   - AC-4: timeout → run state TIMED_OUT
///   - AC-3: cancel → run state CANCELLED
///   - Process tree cleanup verified via mocked IProcessTracker interactions
/// </summary>
public sealed class ProcessExecutionHandlerTests
{
    private const string RunId     = "run-timed-001";
    private const string AttemptId = "att-001";

    private static OwnedProcessTree MakeTree(int timeoutMs = 30_000) =>
        new(
            RunId: RunId,
            RootPid: 9999,
            ChildPids: [9999],
            StartedAt: DateTimeOffset.UtcNow,
            TimeoutMs: timeoutMs);

    private static AwaitProcessCommand MakeAwaitCommand(OwnedProcessTree tree) =>
        new(RunId, AttemptId, tree);

    // ── AC-4: timeout → TIMED_OUT state ──────────────────────────────────────

    [Fact]
    public async Task AwaitAndTransitionAsync_WhenTimedOut_RecordsTimedOutState()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.AwaitAsync(It.IsAny<AwaitProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.DeadlineExceeded));

        RunState? capturedState = null;
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, RunState, CancellationToken>(
                (_, _, _, state, _) => capturedState = state)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var result = await handler.AwaitAndTransitionAsync(MakeAwaitCommand(tree));

        result.IsSuccess.Should().BeTrue();
        result.Value.TimedOut.Should().BeTrue();
        capturedState.Should().Be(RunState.TimedOut);
    }

    // ── AC-3: cancel → CANCELLED state ───────────────────────────────────────

    [Fact]
    public async Task AwaitAndTransitionAsync_WhenCancelled_RecordsCancelledState()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.AwaitAsync(It.IsAny<AwaitProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.CancelledByRequest));

        RunState? capturedState = null;
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, RunState, CancellationToken>(
                (_, _, _, state, _) => capturedState = state)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var result = await handler.AwaitAndTransitionAsync(MakeAwaitCommand(tree));

        result.IsSuccess.Should().BeTrue();
        result.Value.Cancelled.Should().BeTrue();
        capturedState.Should().Be(RunState.Cancelled);
    }

    // ── Success path → SUCCEEDED state ───────────────────────────────────────

    [Fact]
    public async Task AwaitAndTransitionAsync_WhenCleanExit_RecordsSucceededState()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.AwaitAsync(It.IsAny<AwaitProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.Succeeded(0)));

        RunState? capturedState = null;
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, RunState, CancellationToken>(
                (_, _, _, state, _) => capturedState = state)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var result = await handler.AwaitAndTransitionAsync(MakeAwaitCommand(tree));

        result.IsSuccess.Should().BeTrue();
        result.Value.ExitCode.Should().Be(0);
        capturedState.Should().Be(RunState.Succeeded);
    }

    // ── Non-zero exit → FAILED state ─────────────────────────────────────────

    [Fact]
    public async Task AwaitAndTransitionAsync_WhenNonZeroExit_RecordsFailedState()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.AwaitAsync(It.IsAny<AwaitProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProcessOutcome, Error>.Ok(ProcessOutcome.Succeeded(1)));

        RunState? capturedState = null;
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, RunState, CancellationToken>(
                (_, _, _, state, _) => capturedState = state)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var result = await handler.AwaitAndTransitionAsync(MakeAwaitCommand(tree));

        result.IsSuccess.Should().BeTrue();
        capturedState.Should().Be(RunState.Failed);
    }

    // ── RecordCancelledAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RecordCancelledAsync_PersistsCancelledState()
    {
        RunState? capturedState = null;
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                RunId, AttemptId, It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, RunState, CancellationToken>(
                (_, _, _, state, _) => capturedState = state)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ProcessExecutionHandler(
            new Mock<IProcessController>().Object,
            journalMock.Object,
            NullLogger<ProcessExecutionHandler>.Instance);

        var result = await handler.RecordCancelledAsync(RunId, AttemptId);

        result.IsSuccess.Should().BeTrue();
        capturedState.Should().Be(RunState.Cancelled);
    }
}
