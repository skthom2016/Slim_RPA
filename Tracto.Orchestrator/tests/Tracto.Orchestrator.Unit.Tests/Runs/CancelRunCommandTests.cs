#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Runs;

/// <summary>
/// Unit tests for <see cref="CancelRunCommandHandler"/> — story OC-09.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; Moq stubs all external dependencies.
/// </summary>
public sealed class CancelRunCommandTests
{
    // ── Fixed baseline ────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private const string RequestedBy = "operator-alice";
    private const string WorkerId    = "worker-beta";

    // ── Builders ──────────────────────────────────────────────────────────────

    private static RunRecord QueuedRun()
    {
        var r = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       "wf-001",
            createdAt:        FixedNow.AddMinutes(-10),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: "artifact-1",
            scriptVersion:    "1.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        r.Enqueue(FixedNow.AddMinutes(-5));
        return r;
    }

    private static RunRecord LeasedRun()
    {
        var r = QueuedRun();
        r.AdoptLease(Guid.NewGuid(), WorkerId, FixedNow.AddMinutes(-3));
        return r;
    }

    private static RunRecord RunningRun()
    {
        var r = LeasedRun();
        r.Start(r.AttemptId, FixedNow.AddMinutes(-1));
        return r;
    }

    private static LeaseRecord ActiveLease(Guid runId, Guid attemptId)
        => new(
            leaseId:              Guid.NewGuid(),
            runId:                runId,
            attemptId:            attemptId,
            workerId:             WorkerId,
            artifactId:           "artifact-1",
            artifactVersion:      "1.0",
            artifactHash:         "sha256-abc",
            offeredAt:            FixedNow.AddMinutes(-4),
            expiresAt:            FixedNow.AddMinutes(26),
            interactiveSessionId: null);

    // ── Handler factory ───────────────────────────────────────────────────────

    private static (
        CancelRunCommandHandler  handler,
        Mock<IRunRepository>     runRepo,
        Mock<ILeaseRepository>   leaseRepo,
        Mock<IJournalRepository> journalRepo,
        Mock<IUnitOfWork>        uow)
    MakeHandler(FakeClock? clock = null)
    {
        clock ??= new FakeClock(FixedNow);

        var runRepo     = new Mock<IRunRepository>(MockBehavior.Strict);
        var leaseRepo   = new Mock<ILeaseRepository>(MockBehavior.Strict);
        var journalRepo = new Mock<IJournalRepository>(MockBehavior.Strict);
        var uow         = new Mock<IUnitOfWork>(MockBehavior.Strict);

        uow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        var handler = new CancelRunCommandHandler(
            runRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            uow.Object,
            clock,
            telemetry.Object,
            NullLogger<CancelRunCommandHandler>.Instance);

        return (handler, runRepo, leaseRepo, journalRepo, uow);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancel QUEUED → run transitions to CANCELLED immediately.
    /// </summary>
    [Fact]
    public async Task Cancel_QueuedRun_TransitionsToCancelled()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var run = QueuedRun();

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(l => l.FindActiveLeaseForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        runRepo.Setup(r => r.Update(run));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(run.Id, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(run.Id);
        run.State.Should().Be(RunState.Cancelled);

        runRepo.Verify(r => r.Update(run), Times.Once);
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_CANCELLED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Cancel RUNNING → CancellationRequested=true on the active lease; run stays RUNNING.
    /// </summary>
    [Fact]
    public async Task Cancel_RunningRun_SetsCancellationRequestedOnLease_RunStaysRunning()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var run   = RunningRun();
        var lease = ActiveLease(run.Id, run.AttemptId);
        // Accept the lease to put it into Accepted state.
        lease.Accept(FixedNow.AddMinutes(-1));

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(l => l.FindActiveLeaseForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(l => l.Update(lease));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(run.Id, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(run.Id);

        // Run must still be RUNNING — the worker transitions it on the next heartbeat.
        run.State.Should().Be(RunState.Running);

        // CancellationRequested must be set on the lease.
        lease.CancellationRequested.Should().BeTrue();

        leaseRepo.Verify(l => l.Update(lease), Times.Once);
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_CANCEL_REQUESTED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Cancel LEASED → run transitions to CANCELLED;
    /// the outstanding lease is expired so a subsequent AcceptLease returns 409.
    /// </summary>
    [Fact]
    public async Task Cancel_LeasedRun_TransitionsToCancelled_LeaseIsExpired()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var run   = LeasedRun();
        var lease = ActiveLease(run.Id, run.AttemptId);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        runRepo.Setup(r => r.Update(run));
        leaseRepo.Setup(l => l.FindActiveLeaseForRunAsync(run.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(l => l.Update(lease));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(run.Id, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(run.Id);
        run.State.Should().Be(RunState.Cancelled);
        lease.State.Should().Be(LeaseState.Expired);

        runRepo.Verify(r => r.Update(run), Times.Once);
        leaseRepo.Verify(l => l.Update(lease), Times.Once);
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_CANCELLED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Cancel HANDED_OFF → 409 TERMINAL_STATE.
    /// </summary>
    [Fact]
    public async Task Cancel_HandedOffRun_Returns409TerminalState()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var run = RunningRun();
        run.HandOff(run.AttemptId, FixedNow);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(run.Id, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TERMINAL_STATE");

        // No state mutation and no commit issued.
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Double-cancel → 200 idempotent with no second write.
    /// </summary>
    [Fact]
    public async Task Cancel_AlreadyCancelledRun_Returns200Idempotent_NoWrites()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var run = QueuedRun();
        run.Cancel(FixedNow.AddMinutes(-1)); // already cancelled

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(run.Id, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(run.Id);
        run.State.Should().Be(RunState.Cancelled);

        // No state mutation, no journal write, no commit.
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never);
        journalRepo.Verify(
            j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Run not found → Result.Fail with NotFound error code.
    /// </summary>
    [Fact]
    public async Task Cancel_NonExistentRun_ReturnsNotFound()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();
        var runId = Guid.NewGuid();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((RunRecord?)null);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    /// <summary>
    /// Empty RequestedBy → validation error before any DB access.
    /// </summary>
    [Fact]
    public async Task Cancel_EmptyRequestedBy_ReturnsValidationError()
    {
        // Arrange
        var (handler, runRepo, leaseRepo, journalRepo, uow) = MakeHandler();

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(Guid.NewGuid(), ""));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");

        // No DB access should have occurred.
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
