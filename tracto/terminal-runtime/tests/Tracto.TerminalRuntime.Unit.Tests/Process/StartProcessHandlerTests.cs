using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Process;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Arguments;
using Tracto.TerminalRuntime.Domain.Process;
using Tracto.TerminalRuntime.Domain.Shells;

namespace Tracto.TerminalRuntime.Unit.Tests.Process;

/// <summary>
/// Unit tests for <see cref="StartProcessHandler"/>.
/// TER-US-015 AC-7: background process without approval must be rejected.
/// </summary>
public sealed class StartProcessHandlerTests
{
    private static StartProcessCommand MakeCommand(
        bool allowsBackground,
        IReadOnlyList<string>? argList = null) =>
        new(
            RunId: "run-001",
            AttemptId: "att-001",
            Spec: new ProcessStartSpec(
                Shell: ShellType.PowerShell,
                ExecutablePath: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                ArgumentList: argList ?? ["-Command", "Write-Host 'hello'"],
                WorkingDirectory: @"C:\Temp",
                EnvironmentVars: new Dictionary<string, string>()),
            TimeoutMs: 30_000,
            AllowsBackground: allowsBackground);

    private static OwnedProcessTree MakeTree(string runId = "run-001") =>
        new(
            RunId: runId,
            RootPid: 1234,
            ChildPids: [1234],
            StartedAt: DateTimeOffset.UtcNow,
            TimeoutMs: 30_000);

    // ── AC-7: background rejection ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_BackgroundPatternAndNotAllowed_ReturnsPolicyDeniedError()
    {
        var controllerMock = new Mock<IProcessController>();
        var journalMock    = new Mock<IRunJournal>();
        var handler = new StartProcessHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<StartProcessHandler>.Instance);

        var command = MakeCommand(
            allowsBackground: false,
            argList: ["-Command", "Start-Job { Write-Host 'bg' }"]);

        Result<OwnedProcessTree, Error> result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
        result.Error.Code.Should().Be("Process.BackgroundNotAllowed");

        // Controller must NOT have been called (rejection before any I/O).
        controllerMock.Verify(c => c.StartAsync(It.IsAny<StartProcessCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_BackgroundPatternAndAllowed_DelegatesToController()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.StartAsync(It.IsAny<StartProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OwnedProcessTree, Error>.Ok(tree));

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new StartProcessHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<StartProcessHandler>.Instance);

        var command = MakeCommand(
            allowsBackground: true,
            argList: ["-Command", "Start-Job { Write-Host 'bg' }"]);

        Result<OwnedProcessTree, Error> result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootPid.Should().Be(1234);
    }

    [Fact]
    public async Task HandleAsync_CleanCommandNotAllowsBackground_DelegatesToController()
    {
        var tree = MakeTree();

        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.StartAsync(It.IsAny<StartProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OwnedProcessTree, Error>.Ok(tree));

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new StartProcessHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<StartProcessHandler>.Instance);

        var command = MakeCommand(allowsBackground: false);

        Result<OwnedProcessTree, Error> result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ControllerFailure_PropagatesError()
    {
        var controllerMock = new Mock<IProcessController>();
        controllerMock
            .Setup(c => c.StartAsync(It.IsAny<StartProcessCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OwnedProcessTree, Error>.Fail(
                Error.General("Process.LaunchFailed", "Could not launch.")));

        var journalMock = new Mock<IRunJournal>();

        var handler = new StartProcessHandler(
            controllerMock.Object,
            journalMock.Object,
            NullLogger<StartProcessHandler>.Instance);

        var command = MakeCommand(allowsBackground: false);

        Result<OwnedProcessTree, Error> result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Process.LaunchFailed");
    }
}
