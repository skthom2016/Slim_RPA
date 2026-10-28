using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure.Cleanup;

namespace Tracto.TerminalRuntime.Unit.Tests.Cleanup;

/// <summary>
/// Unit tests for TER-US-019 AC-3: service.invoke returning confirmed async
/// acknowledgement transitions the run to HANDED_OFF and releases the Terminal worker.
///
/// AC-3 invariants:
///   - HANDED_OFF state is written to the journal.
///   - HandoffResult carries the correct RunId, HandedOffAt, and ContinuationMappingRef.
///   - No continuation run is created here (that is deferred to ReceiveCallbackHandler).
/// </summary>
public sealed class ServiceHandoffHandlerTests
{
    private static IClock FixedClock(DateTimeOffset at)
    {
        var mock = new Mock<IClock>();
        mock.SetupGet(c => c.UtcNow).Returns(at);
        return mock.Object;
    }

    [Fact]
    public async Task HandleHandoffAsync_RecordsHandedOffStateInJournal()
    {
        // Arrange
        const string runId    = "run-handoff-001";
        const string attempt  = "attempt-001";
        const string lease    = "lease-001";
        const string mapRef   = "map-001";

        var fixedTime = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordStateAsync(runId, attempt, lease, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ServiceHandoffHandler(
            journal.Object,
            FixedClock(fixedTime),
            NullLogger<ServiceHandoffHandler>.Instance);

        // Act
        var result = await handler.HandleHandoffAsync(runId, attempt, lease, mapRef);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RunId.Should().Be(runId);
        result.Value.HandedOffAt.Should().Be(fixedTime);
        result.Value.ContinuationMappingRef.Should().Be(mapRef);

        // Journal must have been called to record HANDED_OFF.
        journal.Verify(j => j.RecordStateAsync(
            runId, attempt, lease, RunState.HandedOff, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleHandoffAsync_WhenJournalFails_ReturnsFailure()
    {
        // Arrange
        const string runId   = "run-handoff-fail";
        const string attempt = "attempt-fail";
        const string lease   = "lease-fail";

        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(
                Error.General("Journal.WriteFailure", "Disk full.")));

        var handler = new ServiceHandoffHandler(
            journal.Object,
            FixedClock(DateTimeOffset.UtcNow),
            NullLogger<ServiceHandoffHandler>.Instance);

        // Act
        var result = await handler.HandleHandoffAsync(runId, attempt, lease, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");
    }

    [Fact]
    public async Task HandleHandoffAsync_WithNullContinuationRef_Succeeds()
    {
        // Arrange — no continuation configured
        const string runId   = "run-no-continuation";
        const string attempt = "attempt-nc";
        const string lease   = "lease-nc";

        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ServiceHandoffHandler(
            journal.Object,
            FixedClock(DateTimeOffset.UtcNow),
            NullLogger<ServiceHandoffHandler>.Instance);

        // Act
        var result = await handler.HandleHandoffAsync(runId, attempt, lease, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ContinuationMappingRef.Should().BeNull();
    }

    [Fact]
    public async Task HandleHandoffAsync_WithBlankRunId_ReturnsValidationFailure()
    {
        var journal = new Mock<IRunJournal>();
        var handler = new ServiceHandoffHandler(
            journal.Object,
            FixedClock(DateTimeOffset.UtcNow),
            NullLogger<ServiceHandoffHandler>.Instance);

        var result = await handler.HandleHandoffAsync(
            runId: "   ", attemptId: "a", leaseId: "l", continuationMappingRef: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Handoff.RunIdRequired");
        journal.Verify(
            j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
