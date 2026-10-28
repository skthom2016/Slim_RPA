using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Unit.Tests;

/// <summary>
/// Unit tests for TER-US-022: Lease Fencing, Heartbeat, and Attempt Reconciliation.
///
/// Coverage:
///   AC-1  One-lease-at-a-time guard enforced by AcquireLeaseCommandHandler.
///   AC-2  Journal persistence: WriteEntryAsync is called on successful acquisition.
///   AC-3  Reconciliation: irreversible commands are NOT replayed after reconnect.
///   AC-4  Reconciliation: idempotent commands CAN be replayed.
/// </summary>
public sealed class LeaseAndReconciliationTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RunJournalEntry MakeEntry(
        string runId,
        string workerId,
        IdempotencyClass idempotencyClass,
        string? finalResult = null,
        RunState state = RunState.Running)
        => new(
            RunId:            runId,
            AttemptId:        Guid.NewGuid().ToString("D"),
            LeaseId:          Guid.NewGuid().ToString("D"),
            WorkerId:         workerId,
            HeartbeatSeq:     1L,
            CheckpointAt:     DateTimeOffset.UtcNow,
            FinalResult:      finalResult,
            IdempotencyClass: idempotencyClass,
            State:            state);

    private static IClock FixedClock(DateTimeOffset at)
    {
        var mock = new Mock<IClock>();
        mock.SetupGet(c => c.UtcNow).Returns(at);
        return mock.Object;
    }

    // -----------------------------------------------------------------------
    // AC-1: One-lease-at-a-time guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireLease_WhenWorkerAlreadyHoldsActiveLease_ReturnsFailure()
    {
        // Arrange
        const string workerId = "worker-A";
        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.HasActiveLeaseAsync(workerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));  // worker already holds a lease

        var handler = new AcquireLeaseCommandHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = new AcquireLeaseCommand(
            RunId:           Guid.NewGuid().ToString(),
            AttemptId:       Guid.NewGuid().ToString(),
            WorkerId:        workerId,
            IdempotencyClass: IdempotencyClass.Idempotent);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Lease.AlreadyHeld");

        // WriteEntryAsync must NOT have been called.
        journal.Verify(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcquireLease_WhenWorkerHasNoActiveLease_WritesEntryAndReturnsLeaseId()
    {
        // Arrange
        const string workerId = "worker-B";
        const string runId    = "run-001";
        var          journal  = new Mock<IRunJournal>();
        journal
            .Setup(j => j.HasActiveLeaseAsync(workerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(false));
        journal
            .Setup(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var fixedTime = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var handler = new AcquireLeaseCommandHandler(journal.Object, FixedClock(fixedTime));
        var command = new AcquireLeaseCommand(runId, Guid.NewGuid().ToString(), workerId, IdempotencyClass.Idempotent);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.LeaseId.Should().NotBeNullOrEmpty();
        result.Value.AcquiredAt.Should().Be(fixedTime);

        // Journal must have been persisted exactly once.
        journal.Verify(
            j => j.WriteEntryAsync(
                It.Is<RunJournalEntry>(e =>
                    e.RunId   == runId &&
                    e.WorkerId == workerId &&
                    e.State   == RunState.Leased &&
                    !e.IsCompleted),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AcquireLease_WhenJournalWriteFails_PropagatesFailure()
    {
        // Arrange
        var journal = new Mock<IRunJournal>();
        journal
            .Setup(j => j.HasActiveLeaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(false));
        journal
            .Setup(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(Error.General("Journal.WriteFailure", "Disk full.")));

        var handler = new AcquireLeaseCommandHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = new AcquireLeaseCommand("run-x", "attempt-x", "worker-C", IdempotencyClass.Irreversible);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Journal.WriteFailure");
    }

    // -----------------------------------------------------------------------
    // AC-2: Journal persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireLease_PersistsCorrectJournalFields()
    {
        // Arrange
        const string workerId  = "worker-D";
        const string runId     = "run-persist-001";
        const string attemptId = "attempt-001";
        RunJournalEntry? capturedEntry = null;

        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.HasActiveLeaseAsync(workerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<bool, Error>.Ok(false));
        journal.Setup(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()))
               .Callback<RunJournalEntry, CancellationToken>((e, _) => capturedEntry = e)
               .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new AcquireLeaseCommandHandler(journal.Object, FixedClock(DateTimeOffset.UtcNow));
        var command = new AcquireLeaseCommand(runId, attemptId, workerId, IdempotencyClass.Irreversible);

        // Act
        await handler.HandleAsync(command);

        // Assert persisted fields
        capturedEntry.Should().NotBeNull();
        capturedEntry!.RunId.Should().Be(runId);
        capturedEntry.AttemptId.Should().Be(attemptId);
        capturedEntry.WorkerId.Should().Be(workerId);
        capturedEntry.HeartbeatSeq.Should().Be(0L);
        capturedEntry.FinalResult.Should().BeNull("run is not yet complete");
        capturedEntry.IdempotencyClass.Should().Be(IdempotencyClass.Irreversible);
        capturedEntry.State.Should().Be(RunState.Leased);
        capturedEntry.IsCompleted.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // AC-3: Reconciliation — irreversible commands must NOT be replayed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_WhenIrreversibleCommandAndRunNotComplete_ShouldNotReplay()
    {
        // Arrange
        const string runId  = "run-irrev-001";
        var          entry  = MakeEntry(runId, "worker-E", IdempotencyClass.Irreversible, finalResult: null);

        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.ReadEntryAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));

        var handler = new ReconcileRunHandler(journal.Object);

        // Act
        var result = await handler.HandleAsync(runId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse("irreversible commands must never be auto-replayed");
        result.Value.Entry.IdempotencyClass.Should().Be(IdempotencyClass.Irreversible);
    }

    [Fact]
    public async Task Reconcile_WhenIrreversibleCommandAndRunComplete_ShouldNotReplay()
    {
        // Arrange
        const string runId = "run-irrev-complete-001";
        var entry = MakeEntry(runId, "worker-F", IdempotencyClass.Irreversible, finalResult: "SUCCEEDED");

        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.ReadEntryAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));

        var handler = new ReconcileRunHandler(journal.Object);

        // Act
        var result = await handler.HandleAsync(runId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse("completed runs must never be replayed regardless of idempotency class");
        result.Value.Entry.IsCompleted.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-4: Reconciliation — idempotent commands CAN be replayed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_WhenIdempotentCommandAndRunNotComplete_ShouldReplay()
    {
        // Arrange
        const string runId = "run-idem-001";
        var entry = MakeEntry(runId, "worker-G", IdempotencyClass.Idempotent, finalResult: null);

        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.ReadEntryAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));

        var handler = new ReconcileRunHandler(journal.Object);

        // Act
        var result = await handler.HandleAsync(runId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeTrue("idempotent in-flight runs are safe to replay after reconnect");
    }

    [Fact]
    public async Task Reconcile_WhenIdempotentCommandAndStateIsUnknown_ShouldNotReplay()
    {
        // Arrange — idempotent class but state is UNKNOWN.
        // CEC v1.0.1 invariant #2: UNKNOWN runs must never be auto-replayed,
        // regardless of idempotency class.
        const string runId = "run-unknown-001";
        var entry = MakeEntry(runId, "worker-H", IdempotencyClass.Idempotent,
            finalResult: null, state: RunState.Unknown);
        var journal = new Mock<IRunJournal>();
        journal.Setup(j => j.ReadEntryAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));
        var handler = new ReconcileRunHandler(journal.Object);

        // Act
        var result = await handler.HandleAsync(runId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse(
            "UNKNOWN state runs must never be auto-replayed regardless of idempotency class (CEC invariant #2)");
    }

    [Fact]
    public async Task Reconcile_WhenNoJournalEntryExists_ReturnsNotFoundFailure()
    {
        // Arrange
        const string runId  = "run-ghost";
        var          journal = new Mock<IRunJournal>();
        journal.Setup(j => j.ReadEntryAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(null));

        var handler = new ReconcileRunHandler(journal.Object);

        // Act
        var result = await handler.HandleAsync(runId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reconcile.NotFound");
    }
}
