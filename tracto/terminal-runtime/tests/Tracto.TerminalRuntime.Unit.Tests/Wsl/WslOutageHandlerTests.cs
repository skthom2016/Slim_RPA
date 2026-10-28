using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Wsl;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Wsl;

namespace Tracto.TerminalRuntime.Unit.Tests.Wsl;

/// <summary>
/// Unit tests for TER-US-017: WSL Failure Detection and Reconciliation.
///
/// Coverage:
///   AC-1  Distro unavailable before command start → ENVIRONMENT error with
///         errorCategory=ENVIRONMENT, detailCode=terminal.wslUnavailable.
///   AC-2a Distro dies after side effect committed → run state UNKNOWN.
///   AC-2b Distro dies before side effect committed → run state FAILED.
///   AC-3  Outage event is persisted to the journal so Orchestrator can
///         reconcile without launching a duplicate command.
/// </summary>
public sealed class WslOutageHandlerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IClock FixedClock(DateTimeOffset at)
    {
        var mock = new Mock<IClock>();
        mock.SetupGet(c => c.UtcNow).Returns(at);
        return mock.Object;
    }

    private static Mock<IRunJournal> HappyJournal()
    {
        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordWslOutageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<WslOutageEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        journal
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        journal
            .Setup(j => j.RecordUnknownStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        return journal;
    }

    private static HandleWslOutageCommand MakeCommand(
        bool occurredBefore,
        bool? wasSideEffectCommitted,
        string runId    = "run-wsl-001",
        string distroName = "Ubuntu-22.04")
        => new(
            RunId:                   runId,
            AttemptId:               Guid.NewGuid().ToString("D"),
            LeaseId:                 Guid.NewGuid().ToString("D"),
            DistroName:              distroName,
            OccurredBeforeCommandStart: occurredBefore,
            WasSideEffectCommitted:  wasSideEffectCommitted);

    // -----------------------------------------------------------------------
    // AC-1: Distro unavailable before command start → ENVIRONMENT error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleWslOutage_WhenDistroUnavailableBeforeStart_ReturnsEnvironmentError()
    {
        // Arrange
        var journal = new Mock<IRunJournal>();   // strict-ish — no setup means no calls expected
        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = MakeCommand(occurredBefore: true, wasSideEffectCommitted: null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Environment);
        result.Error.Code.Should().Be("Wsl.DistroUnavailable");
        result.Error.DetailCode.Should().Be("terminal.wslUnavailable");

        // No journal writes should occur for a pre-start availability failure.
        journal.Verify(
            j => j.RecordWslOutageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<WslOutageEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // AC-2a: Distro dies AFTER side effect committed → UNKNOWN
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleWslOutage_WhenDistroDiesAfterSideEffectCommitted_ReturnsUnknownState()
    {
        // Arrange
        var journal = HappyJournal();
        var now     = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(now));
        var command = MakeCommand(
            occurredBefore: false,
            wasSideEffectCommitted: true,   // side effect IS committed
            runId: "run-wsl-sideeffect");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewState.Should().Be(RunState.Unknown,
            "when a side effect was committed the outcome cannot be determined — UNKNOWN");
        result.Value.OutageEvent.WasSideEffectCommitted.Should().BeTrue();
        result.Value.OutageEvent.OccurredAt.Should().Be(now);

        // CEC v1.0.1 invariant #2: UNKNOWN must set NeedsAttention=true and a non-null UnknownReason.
        result.Value.NeedsAttention.Should().BeTrue("UNKNOWN runs always require attention");
        result.Value.UnknownReason.Should().Be("WslOutage:SideEffectCommitted");

        // RecordUnknownStateAsync must have been called (not plain RecordStateAsync) for UNKNOWN.
        journal.Verify(
            j => j.RecordUnknownStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "WslOutage:SideEffectCommitted", It.IsAny<CancellationToken>()),
            Times.Once,
            "UNKNOWN state must be persisted via RecordUnknownStateAsync to set NeedsAttention");
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "RecordStateAsync must not be called for UNKNOWN transitions");
    }

    [Fact]
    public async Task HandleWslOutage_WhenDistroDiesAndSideEffectStatusUnknown_ReturnsUnknownState()
    {
        // Arrange — cannot determine whether side effect committed (null)
        var journal = HappyJournal();
        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = MakeCommand(
            occurredBefore: false,
            wasSideEffectCommitted: null,   // unknown
            runId: "run-wsl-unknown");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewState.Should().Be(RunState.Unknown,
            "when side-effect status is unknown the run must be flagged UNKNOWN");
        result.Value.OutageEvent.WasSideEffectCommitted.Should().BeNull();

        // CEC v1.0.1 invariant #2: UNKNOWN must set NeedsAttention=true and a non-null UnknownReason.
        result.Value.NeedsAttention.Should().BeTrue("UNKNOWN runs always require attention");
        result.Value.UnknownReason.Should().Be("WslOutage:SideEffectStatusIndeterminate");

        // RecordUnknownStateAsync must have been called (not plain RecordStateAsync) for UNKNOWN.
        journal.Verify(
            j => j.RecordUnknownStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "WslOutage:SideEffectStatusIndeterminate", It.IsAny<CancellationToken>()),
            Times.Once,
            "UNKNOWN state must be persisted via RecordUnknownStateAsync to set NeedsAttention");
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "RecordStateAsync must not be called for UNKNOWN transitions");
    }

    // -----------------------------------------------------------------------
    // AC-2b: Distro dies BEFORE side effect committed → FAILED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleWslOutage_WhenDistroDiesBeforeSideEffectCommitted_ReturnsFailed()
    {
        // Arrange
        var journal = HappyJournal();
        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = MakeCommand(
            occurredBefore: false,
            wasSideEffectCommitted: false,  // no side effect committed
            runId: "run-wsl-nosideeffect");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewState.Should().Be(RunState.Failed,
            "when no side effect was committed the run is a clean FAILED");
        result.Value.OutageEvent.WasSideEffectCommitted.Should().BeFalse();

        // FAILED transitions must NOT set NeedsAttention (invariant #2 applies to UNKNOWN only).
        result.Value.NeedsAttention.Should().BeFalse();
        result.Value.UnknownReason.Should().BeNull();

        // FAILED uses RecordStateAsync, not RecordUnknownStateAsync.
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                RunState.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
        journal.Verify(
            j => j.RecordUnknownStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // AC-3: Journal records outage event for Orchestrator reconciliation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleWslOutage_Always_RecordsOutageEventInJournal()
    {
        // Arrange
        const string runId     = "run-wsl-reconcile";
        const string attemptId = "attempt-r1";
        const string leaseId   = "lease-r1";
        var          now       = new DateTimeOffset(2026, 10, 24, 10, 30, 0, TimeSpan.Zero);

        WslOutageEvent? capturedEvent = null;
        string? capturedRunId          = null;

        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordWslOutageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<WslOutageEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, WslOutageEvent, CancellationToken>(
                (rid, _, _, evt, _) =>
                {
                    capturedRunId  = rid;
                    capturedEvent  = evt;
                })
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        journal
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(now));
        var command = new HandleWslOutageCommand(
            RunId:                     runId,
            AttemptId:                 attemptId,
            LeaseId:                   leaseId,
            DistroName:                "Ubuntu-22.04",
            OccurredBeforeCommandStart: false,
            WasSideEffectCommitted:     false);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue("outage handling should succeed");

        // AC-3: outage must have been written to the journal.
        journal.Verify(
            j => j.RecordWslOutageAsync(
                runId, attemptId, leaseId,
                It.IsAny<WslOutageEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "outage event must be persisted exactly once so Orchestrator can reconcile");

        capturedRunId.Should().Be(runId);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.OccurredAt.Should().Be(now);
        capturedEvent.WasSideEffectCommitted.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Error propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleWslOutage_WhenJournalRecordOutageFails_PropagatesFailure()
    {
        // Arrange
        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordWslOutageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<WslOutageEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(
                Error.General("Journal.WriteFailure", "Disk full.")));

        var handler = new HandleWslOutageHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = MakeCommand(occurredBefore: false, wasSideEffectCommitted: null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");

        // State must NOT have been changed when journal write failed.
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
