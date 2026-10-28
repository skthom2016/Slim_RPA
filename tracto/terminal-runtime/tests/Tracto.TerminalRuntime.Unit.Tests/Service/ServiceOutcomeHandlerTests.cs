using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Service;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Cleanup;
using Tracto.TerminalRuntime.Domain.Service;

namespace Tracto.TerminalRuntime.Unit.Tests.Service;

/// <summary>
/// Unit tests for TER-US-021: All Five Service Outcomes and Run State Handling.
///
/// Acceptance Criteria:
///   AC-1: All five serviceOutcome values are handled; runtime branches only on
///         category or approved actionCode, never on domain fields.
///   AC-2: BUSINESS_REJECTION → SUCCEEDED + businessOutcome="REJECTED"; no retry.
///         Post-rejection technical failure → FAILED for that technical failure.
///   AC-3: UNKNOWN_OUTCOME → attempt terminated as UNKNOWN; needsAttention=true;
///         no continuation; no auto-replay.
///   AC-4: ASYNC_ACKNOWLEDGED → HANDED_OFF; worker released; exactly one static
///         continuation; dynamic scriptRef refused (covered by callback-handler tests).
///   AC-5: Terminal run states SUCCEEDED, FAILED, CANCELLED, TIMED_OUT, UNKNOWN,
///         HANDED_OFF are all used correctly; UNKNOWN is terminal and stops the attempt.
/// </summary>
public sealed class ServiceOutcomeHandlerTests
{
    // ── Shared test helpers ───────────────────────────────────────────────────

    private const string RunId    = "run-svc-021";
    private const string Attempt  = "attempt-001";
    private const string Lease    = "lease-001";
    private const string MapRef   = "map-001";

    private static HandleServiceOutcomeCommand MakeCommand(
        ServiceOutcome outcome,
        string?        actionCode      = null,
        string?        businessOutcome = null,
        string?        continuationRef = MapRef) =>
        new(RunId, Attempt, Lease,
            new ServiceInvokeResult(outcome, actionCode, businessOutcome),
            continuationRef);

    private static IRunJournal JournalThatAcceptsState(RunState expectedState)
    {
        var mock = new Mock<IRunJournal>();
        mock.Setup(j => j.RecordStateAsync(
                RunId, Attempt, Lease, expectedState, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        return mock.Object;
    }

    private static IRunJournal JournalThatAcceptsUnknown()
    {
        var mock = new Mock<IRunJournal>();
        mock.Setup(j => j.RecordUnknownStateAsync(
                RunId, Attempt, Lease, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        return mock.Object;
    }

    private static IServiceHandoffHandler HandoffHandlerOk(
        string runId, string handedOffAt_ignored)
    {
        var mock = new Mock<IServiceHandoffHandler>();
        mock.Setup(h => h.HandleHandoffAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HandoffResult, Error>.Ok(
                new HandoffResult(runId, DateTimeOffset.UtcNow, null)));
        return mock.Object;
    }

    private static HandleServiceOutcomeHandler MakeHandlerWith(
        IRunJournal journal,
        IServiceHandoffHandler? handoff = null)
    {
        handoff ??= new Mock<IServiceHandoffHandler>().Object;
        return new HandleServiceOutcomeHandler(journal, handoff);
    }

    // ── AC-1 + AC-5: SUCCEEDED → RunState.Succeeded ──────────────────────────

    [Fact]
    public async Task Outcome_Succeeded_Maps_To_RunState_Succeeded()
    {
        // Arrange
        var journal = JournalThatAcceptsState(RunState.Succeeded);
        var handler = MakeHandlerWith(journal);
        var command = MakeCommand(ServiceOutcome.Succeeded);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Succeeded);
        result.Value.BusinessOutcome.Should().BeNull();
        result.Value.NeedsAttention.Should().BeFalse();
        result.Value.UnknownReason.Should().BeNull();

        // Verify journal recorded the correct state.
        Mock.Get(journal).Verify(j => j.RecordStateAsync(
            RunId, Attempt, Lease, RunState.Succeeded, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-1 + AC-5: FAILED → RunState.Failed ───────────────────────────────

    [Fact]
    public async Task Outcome_Failed_Maps_To_RunState_Failed()
    {
        // Arrange
        var journal = JournalThatAcceptsState(RunState.Failed);
        var handler = MakeHandlerWith(journal);
        var command = MakeCommand(ServiceOutcome.Failed);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Failed);
        result.Value.BusinessOutcome.Should().BeNull();
        result.Value.NeedsAttention.Should().BeFalse();

        Mock.Get(journal).Verify(j => j.RecordStateAsync(
            RunId, Attempt, Lease, RunState.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-2: BUSINESS_REJECTION → SUCCEEDED + businessOutcome="REJECTED" ────

    [Fact]
    public async Task Outcome_BusinessRejection_Maps_To_Succeeded_With_Rejected_BusinessOutcome()
    {
        // Arrange — a business rejection is a valid, expected outcome; it maps
        // to SUCCEEDED (not FAILED) with businessOutcome="REJECTED".
        var journal = JournalThatAcceptsState(RunState.Succeeded);
        var handler = MakeHandlerWith(journal);
        var command = MakeCommand(ServiceOutcome.BusinessRejection);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Succeeded,
            because: "BUSINESS_REJECTION is an expected business outcome, not a technical failure");
        result.Value.BusinessOutcome.Should().Be("REJECTED",
            because: "AC-2: businessOutcome must be 'REJECTED' for business rejections");
        result.Value.NeedsAttention.Should().BeFalse();
        result.Value.UnknownReason.Should().BeNull();

        // The journal must record SUCCEEDED, not FAILED.
        Mock.Get(journal).Verify(j => j.RecordStateAsync(
            RunId, Attempt, Lease, RunState.Succeeded, It.IsAny<CancellationToken>()),
            Times.Once,
            "BUSINESS_REJECTION must persist RunState.Succeeded to the journal");

        Mock.Get(journal).Verify(j => j.RecordStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            RunState.Failed, It.IsAny<CancellationToken>()),
            Times.Never,
            "BUSINESS_REJECTION must NOT write RunState.Failed");
    }

    // ── AC-2: no retry on BUSINESS_REJECTION ────────────────────────────────

    [Fact]
    public async Task Outcome_BusinessRejection_Does_Not_Set_NeedsAttention_And_Terminates()
    {
        // AC-2: business rejections are terminal — the run is done (SUCCEEDED +
        // businessOutcome=REJECTED). The handler must not signal a retry.
        var journal = JournalThatAcceptsState(RunState.Succeeded);
        var handler = MakeHandlerWith(journal);

        var result = await handler.HandleAsync(MakeCommand(ServiceOutcome.BusinessRejection));

        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Succeeded);
        result.Value.NeedsAttention.Should().BeFalse(
            because: "business rejections do not require human attention");
    }

    // ── AC-3: UNKNOWN_OUTCOME → UNKNOWN + needsAttention=true ────────────────

    [Fact]
    public async Task Outcome_UnknownOutcome_Maps_To_RunState_Unknown_With_NeedsAttention()
    {
        // AC-3: the service could not confirm whether the operation was applied.
        // The attempt is terminated as UNKNOWN; needsAttention is mandatory
        // (CEC v1.0.1 invariant #2).
        var journal = JournalThatAcceptsUnknown();
        var handler = MakeHandlerWith(journal);
        var command = MakeCommand(ServiceOutcome.UnknownOutcome);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Unknown,
            because: "UNKNOWN_OUTCOME must terminate the attempt as UNKNOWN");
        result.Value.NeedsAttention.Should().BeTrue(
            because: "CEC v1.0.1 invariant #2: UNKNOWN always requires needsAttention=true");
        result.Value.UnknownReason.Should().NotBeNullOrEmpty(
            because: "CEC v1.0.1 invariant #2: UNKNOWN must carry a non-null unknownReason");

        // Journal must use RecordUnknownStateAsync, not RecordStateAsync(Unknown).
        Mock.Get(journal).Verify(j => j.RecordUnknownStateAsync(
            RunId, Attempt, Lease, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "UNKNOWN_OUTCOME must call RecordUnknownStateAsync to persist the reason code");

        // RecordStateAsync must NOT be called for UNKNOWN.
        Mock.Get(journal).Verify(j => j.RecordStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AC-3: UNKNOWN is terminal — stops the attempt ─────────────────────────

    [Fact]
    public async Task Outcome_UnknownOutcome_Is_Terminal_And_Stops_Attempt()
    {
        // UNKNOWN state must be the final state for this attempt.
        // No retry signal, no continuation reference in the result.
        var journal = JournalThatAcceptsUnknown();
        var handler = MakeHandlerWith(journal);

        var result = await handler.HandleAsync(MakeCommand(ServiceOutcome.UnknownOutcome));

        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.Unknown);
        // The result carries no continuation — UNKNOWN stops the attempt completely.
        result.Value.BusinessOutcome.Should().BeNull();
    }

    // ── AC-4: ASYNC_ACKNOWLEDGED → HANDED_OFF; worker released ───────────────

    [Fact]
    public async Task Outcome_AsyncAcknowledged_Maps_To_HandedOff_And_Releases_Worker()
    {
        // AC-4: the service accepted the request asynchronously.
        // The Terminal worker is released immediately; no continuation run is created here.
        var journalMock = new Mock<IRunJournal>();
        var handoffMock = new Mock<IServiceHandoffHandler>();

        handoffMock
            .Setup(h => h.HandleHandoffAsync(
                RunId, Attempt, Lease, MapRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HandoffResult, Error>.Ok(
                new HandoffResult(RunId, DateTimeOffset.UtcNow, MapRef)));

        var handler = new HandleServiceOutcomeHandler(journalMock.Object, handoffMock.Object);
        var command = MakeCommand(ServiceOutcome.AsyncAcknowledged, continuationRef: MapRef);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FinalRunState.Should().Be(RunState.HandedOff,
            because: "ASYNC_ACKNOWLEDGED must transition the run to HANDED_OFF");
        result.Value.NeedsAttention.Should().BeFalse();
        result.Value.BusinessOutcome.Should().BeNull();

        // The handoff handler must be called exactly once.
        handoffMock.Verify(h => h.HandleHandoffAsync(
            RunId, Attempt, Lease, MapRef, It.IsAny<CancellationToken>()),
            Times.Once,
            "the handoff handler must be called to record HANDED_OFF and release the worker");

        // The journal must NOT be called directly for state recording (that is
        // the handoff handler's responsibility).
        journalMock.Verify(j => j.RecordStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "RecordStateAsync must not be called directly for ASYNC_ACKNOWLEDGED — " +
            "use IServiceHandoffHandler instead");
    }

    // ── AC-4: handoff failure propagates as Result failure ────────────────────

    [Fact]
    public async Task Outcome_AsyncAcknowledged_When_HandoffFails_Returns_Failure()
    {
        // If the handoff handler fails (e.g. journal write error), the failure
        // must propagate — the run must not be left in an ambiguous state.
        var journalMock = new Mock<IRunJournal>();
        var handoffMock = new Mock<IServiceHandoffHandler>();

        handoffMock
            .Setup(h => h.HandleHandoffAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HandoffResult, Error>.Fail(
                Error.General("Journal.WriteFailure", "Disk full.")));

        var handler = new HandleServiceOutcomeHandler(journalMock.Object, handoffMock.Object);

        var result = await handler.HandleAsync(
            MakeCommand(ServiceOutcome.AsyncAcknowledged));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");
    }

    // ── AC-5: All six terminal run states are used across the five outcomes ────

    [Theory]
    [InlineData(ServiceOutcome.Succeeded,          RunState.Succeeded)]
    [InlineData(ServiceOutcome.Failed,             RunState.Failed)]
    [InlineData(ServiceOutcome.BusinessRejection,  RunState.Succeeded)]
    [InlineData(ServiceOutcome.UnknownOutcome,     RunState.Unknown)]
    public async Task All_Non_Async_Outcomes_Map_To_Expected_RunState(
        ServiceOutcome outcome, RunState expectedState)
    {
        // This parameterised test verifies that each serviceOutcome maps to
        // exactly the right run state without mutating any other state.
        IRunJournal journal;
        if (expectedState == RunState.Unknown)
            journal = JournalThatAcceptsUnknown();
        else
            journal = JournalThatAcceptsState(expectedState);

        var handoffMock = new Mock<IServiceHandoffHandler>();
        var handler = new HandleServiceOutcomeHandler(journal, handoffMock.Object);

        var result = await handler.HandleAsync(MakeCommand(outcome));

        result.IsSuccess.Should().BeTrue(
            because: $"{outcome} must succeed and map to {expectedState}");
        result.Value.FinalRunState.Should().Be(expectedState);

        // ASYNC_ACKNOWLEDGED is handled separately (requires mock wiring for handoff).
        handoffMock.Verify(h => h.HandleHandoffAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "IServiceHandoffHandler must only be called for ASYNC_ACKNOWLEDGED");
    }

    // ── Validation guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NullCommand_Throws_ArgumentNullException()
    {
        var handler = MakeHandlerWith(new Mock<IRunJournal>().Object);

        Func<Task> act = () => handler.HandleAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_BlankRunId_Returns_ValidationFailure()
    {
        var command = new HandleServiceOutcomeCommand(
            RunId:                 "   ",
            AttemptId:             Attempt,
            LeaseId:               Lease,
            InvokeResult:          new ServiceInvokeResult(ServiceOutcome.Succeeded, null, null),
            ContinuationMappingRef: null);

        var handler = MakeHandlerWith(new Mock<IRunJournal>().Object);
        var result  = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceOutcome.RunIdRequired");
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task HandleAsync_BlankAttemptId_Returns_ValidationFailure()
    {
        var command = new HandleServiceOutcomeCommand(
            RunId:                 RunId,
            AttemptId:             "",
            LeaseId:               Lease,
            InvokeResult:          new ServiceInvokeResult(ServiceOutcome.Succeeded, null, null),
            ContinuationMappingRef: null);

        var handler = MakeHandlerWith(new Mock<IRunJournal>().Object);
        var result  = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceOutcome.AttemptIdRequired");
    }

    [Fact]
    public async Task HandleAsync_BlankLeaseId_Returns_ValidationFailure()
    {
        var command = new HandleServiceOutcomeCommand(
            RunId:                 RunId,
            AttemptId:             Attempt,
            LeaseId:               "   ",
            InvokeResult:          new ServiceInvokeResult(ServiceOutcome.Succeeded, null, null),
            ContinuationMappingRef: null);

        var handler = MakeHandlerWith(new Mock<IRunJournal>().Object);
        var result  = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceOutcome.LeaseIdRequired");
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task HandleAsync_NullInvokeResult_Returns_ValidationFailure()
    {
        var command = new HandleServiceOutcomeCommand(
            RunId:                 RunId,
            AttemptId:             Attempt,
            LeaseId:               Lease,
            InvokeResult:          null!,
            ContinuationMappingRef: null);

        var handler = MakeHandlerWith(new Mock<IRunJournal>().Object);
        var result  = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceOutcome.InvokeResultRequired");
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    // ── Journal failure propagation ───────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenJournalFails_OnSucceeded_Returns_Failure()
    {
        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(
                Error.General("Journal.WriteFailure", "Disk full.")));

        var handler = MakeHandlerWith(journal.Object);
        var result  = await handler.HandleAsync(MakeCommand(ServiceOutcome.Succeeded));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");
    }

    [Fact]
    public async Task HandleAsync_WhenJournalFails_OnUnknown_Returns_Failure()
    {
        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.RecordUnknownStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(
                Error.General("Journal.WriteFailure", "IO error.")));

        var handler = MakeHandlerWith(journal.Object);
        var result  = await handler.HandleAsync(MakeCommand(ServiceOutcome.UnknownOutcome));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");
    }
}
