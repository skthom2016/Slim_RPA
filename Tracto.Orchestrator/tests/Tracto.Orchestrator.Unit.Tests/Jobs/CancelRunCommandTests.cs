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

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="CancelRunCommandHandler"/> — story OC-09.
///
/// Handler behaviour (implementation in Application/Jobs/CancelRunCommand.cs):
///   QUEUED  → RunRecord.State = CANCELLED. Optional lease expired. Journal "RUN_CANCELLED". Commit. Return RunId.
///   LEASED  → RunRecord.State = CANCELLED. Active lease expired. Journal "RUN_CANCELLED". Commit. Return RunId.
///             Subsequent AcceptLease for this run → 409 (run is terminal, no active lease).
///   RUNNING → LeaseRecord.CancellationRequested = true. Journal "RUN_CANCEL_REQUESTED". Commit. Return RunId.
///             Run stays RUNNING; worker discovers cancellation on next heartbeat.
///   CANCELLED → 200 idempotent. No transaction begun.
///   HANDED_OFF | SUCCEEDED | FAILED | TIMED_OUT | UNKNOWN → Fail("TERMINAL_STATE"). No transaction.
///   Run not found → Fail("NotFound"). No transaction.
///   Missing RequestedBy → Fail("Validation"). No transaction.
///
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; repositories are Moq-backed (loose mocks).
/// </summary>
public sealed class CancelRunCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    private const string RequestedBy  = "user@example.com";
    private const string WorkerId     = "worker-xyz";
    private const string ArtifactId   = "art-001";
    private const string ArtifactVer  = "1.0.0";
    private const string ArtifactHash = "sha256-cafebabe";

    // ── Domain object factories ───────────────────────────────────────────────

    private static RunRecord MakeCreatedRun(Guid runId)
        => new(
            id:               runId,
            workflowId:       "wf-cancel-test",
            createdAt:        FixedNow.AddMinutes(-10),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ArtifactId,
            scriptVersion:    ArtifactVer,
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

    private static RunRecord MakeQueuedRun(Guid runId)
    {
        var run = MakeCreatedRun(runId);
        run.Enqueue(FixedNow.AddMinutes(-8));
        return run;
    }

    private static (RunRecord run, Guid leaseAttemptId) MakeLeasedRun(Guid runId)
    {
        var run            = MakeQueuedRun(runId);
        var leaseAttemptId = Guid.NewGuid();
        run.AdoptLease(leaseAttemptId, WorkerId, FixedNow.AddMinutes(-5));
        return (run, leaseAttemptId);
    }

    private static (RunRecord run, Guid leaseAttemptId) MakeRunningRun(Guid runId)
    {
        var (run, leaseAttemptId) = MakeLeasedRun(runId);
        run.Start(leaseAttemptId, FixedNow.AddMinutes(-3));
        return (run, leaseAttemptId);
    }

    private static RunRecord MakeHandedOffRun(Guid runId)
    {
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        run.HandOff(leaseAttemptId, FixedNow.AddMinutes(-1));
        return run;
    }

    private static RunRecord MakeSucceededRun(Guid runId)
    {
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        run.Succeed(leaseAttemptId, FixedNow.AddMinutes(-1));
        return run;
    }

    private static RunRecord MakeFailedRun(Guid runId)
    {
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        run.Fail(leaseAttemptId, FixedNow.AddMinutes(-1));
        return run;
    }

    private static RunRecord MakeTimedOutRun(Guid runId)
    {
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        run.MarkTimedOut(leaseAttemptId, FixedNow.AddMinutes(-1));
        return run;
    }

    private static RunRecord MakeUnknownRun(Guid runId)
    {
        var run = MakeQueuedRun(runId);
        run.MarkUnknown("Lost contact during restart", FixedNow.AddMinutes(-1));
        return run;
    }

    private static RunRecord MakeCancelledRun(Guid runId)
    {
        var run = MakeQueuedRun(runId);
        run.Cancel(FixedNow.AddMinutes(-1));
        return run;
    }

    /// <summary>Returns an Offered (non-Expired) lease tied to the given run and attempt.</summary>
    private static LeaseRecord MakeOfferedLease(Guid runId, Guid attemptId)
        => new(
            leaseId:         Guid.NewGuid(),
            runId:           runId,
            attemptId:       attemptId,
            workerId:        WorkerId,
            artifactId:      ArtifactId,
            artifactVersion: ArtifactVer,
            artifactHash:    ArtifactHash,
            offeredAt:       FixedNow.AddMinutes(-5),
            expiresAt:       FixedNow.AddMinutes(5));

    /// <summary>Returns an Accepted lease (for a RUNNING run).</summary>
    private static LeaseRecord MakeAcceptedLease(Guid runId, Guid attemptId)
    {
        var lease = MakeOfferedLease(runId, attemptId);
        lease.Accept(FixedNow.AddMinutes(-3));
        return lease;
    }

    // ── Handler builder ───────────────────────────────────────────────────────

    private static Mock<ITelemetryService> MakeNullTelemetry()
    {
        var t = new Mock<ITelemetryService>();
        t.Setup(x => x.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return t;
    }

    private static CancelRunCommandHandler BuildHandler(
        Mock<IRunRepository>     runRepo,
        Mock<ILeaseRepository>   leaseRepo,
        Mock<IJournalRepository> journalRepo,
        Mock<IUnitOfWork>        uow,
        FakeClock?               clock = null)
        => new(
            runRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            uow.Object,
            clock ?? new FakeClock(FixedNow),
            MakeNullTelemetry().Object,
            NullLogger<CancelRunCommandHandler>.Instance);

    // ── 1. Cancel QUEUED → CANCELLED immediately ──────────────────────────────

    [Fact]
    public async Task HandleAsync_QueuedRun_Succeeds_And_TransitionsToCANCELLED()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        // No active lease for a QUEUED run.
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "cancelling a QUEUED run must succeed");
        result.Value.Should().Be(runId,
            because: "the handler returns the RunId of the cancelled run");
        run.State.Should().Be(RunState.Cancelled,
            because: "QUEUED → CANCELLED must be an immediate state transition");
    }

    [Fact]
    public async Task HandleAsync_QueuedRun_WritesRUN_CANCELLED_JournalEntry()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert — journal entry and commit must both occur atomically (invariant 5).
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_CANCELLED" && j.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_CANCELLED journal entry must be written in the same transaction");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "CommitAsync must be called exactly once to satisfy domain invariant 5");
    }

    [Fact]
    public async Task HandleAsync_QueuedRun_CallsUpdateOnRunRepository()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        runRepo.Verify(r => r.Update(It.Is<RunRecord>(r => r.Id == runId)), Times.Once,
            "Update must be called to register the state change with EF Core change tracking");
    }

    // ── 2. Cancel LEASED → CANCELLED; active lease expired ────────────────────

    [Fact]
    public async Task HandleAsync_LeasedRun_TransitionsToCANCELLED_And_ExpiresActiveLease()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var (run, leaseAttemptId) = MakeLeasedRun(runId);
        var lease = MakeOfferedLease(runId, leaseAttemptId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "cancelling a LEASED run must succeed");
        result.Value.Should().Be(runId);
        run.State.Should().Be(RunState.Cancelled,
            because: "LEASED → CANCELLED prevents AcceptLease from succeeding");
        lease.State.Should().Be(LeaseState.Expired,
            because: "the active lease must be expired so AcceptLease returns 409 rather than 200");
    }

    [Fact]
    public async Task HandleAsync_LeasedRun_WritesRUN_CANCELLED_JournalEntry()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var (run, leaseAttemptId) = MakeLeasedRun(runId);
        var lease = MakeOfferedLease(runId, leaseAttemptId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_CANCELLED" && j.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "RUN_CANCELLED must be journalled for the LEASED → CANCELLED transition");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 3. Cancel RUNNING → CancellationRequested=true; run stays RUNNING ──────

    [Fact]
    public async Task HandleAsync_RunningRun_SetsCancellationRequestedOnLease_RunStaysRunning()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        var lease = MakeAcceptedLease(runId, leaseAttemptId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "cancellation request for a RUNNING run must succeed");
        result.Value.Should().Be(runId);
        lease.CancellationRequested.Should().BeTrue(
            because: "LeaseRecord.CancellationRequested must be set so the worker sees it on its next heartbeat");
        run.State.Should().Be(RunState.Running,
            because: "the run record must NOT be transitioned to CANCELLED — the worker does that after acknowledging cancellation");
    }

    [Fact]
    public async Task HandleAsync_RunningRun_WritesRUN_CANCEL_REQUESTED_JournalEntry()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        var lease = MakeAcceptedLease(runId, leaseAttemptId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_CANCEL_REQUESTED" && j.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_CANCEL_REQUESTED journal entry must be written for audit (domain invariant 5)");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RunningRun_DoesNotMutateRunState()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var (run, leaseAttemptId) = MakeRunningRun(runId);
        var lease = MakeAcceptedLease(runId, leaseAttemptId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert — run repo Update must NOT be called; the run record is not touched.
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "the run state must not change on a RUNNING cancellation request — only the lease flag is set");
    }

    // ── 4. Cancel RUNNING with no active lease → graceful degradation ──────────

    [Fact]
    public async Task HandleAsync_RunningRunWithNoActiveLease_SucceedsAndCommits()
    {
        // Arrange — run is RUNNING but the expiry monitor already expired the lease.
        var runId = Guid.NewGuid();
        var (run, _) = MakeRunningRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert — must not throw; journal entry still recorded.
        result.IsSuccess.Should().BeTrue(
            because: "an absent lease is a recoverable edge case — the run will time out naturally");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the journal entry must still be committed even without an active lease");
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_CANCEL_REQUESTED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 5. Cancel HANDED_OFF → 409 TERMINAL_STATE ─────────────────────────────

    [Fact]
    public async Task HandleAsync_HandedOffRun_ReturnsTerminalStateError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeHandedOffRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(because: "HANDED_OFF is a terminal state — cannot cancel");
        result.Error.Code.Should().Be("TERMINAL_STATE",
            because: "the error code for a terminal-state rejection must be TERMINAL_STATE");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must never begin when the run is already terminal");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 6. Cancel SUCCEEDED → 409 ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SucceededRun_ReturnsTerminalStateError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeSucceededRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(because: "SUCCEEDED is a terminal state — cannot cancel");
        result.Error.Code.Should().Be("TERMINAL_STATE");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 7. Cancel FAILED → 409 ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FailedRun_ReturnsTerminalStateError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeFailedRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(because: "FAILED is a terminal state — cannot cancel");
        result.Error.Code.Should().Be("TERMINAL_STATE");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 8. Cancel TIMED_OUT → 409 ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_TimedOutRun_ReturnsTerminalStateError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeTimedOutRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(because: "TIMED_OUT is a terminal state — cannot cancel");
        result.Error.Code.Should().Be("TERMINAL_STATE");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 9. Cancel UNKNOWN → 409 ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_UnknownRun_ReturnsTerminalStateError()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "UNKNOWN is a terminal state requiring human review — cannot auto-cancel (domain invariant 2)");
        result.Error.Code.Should().Be("TERMINAL_STATE");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 10. Double cancel → 200 idempotent ────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AlreadyCancelledRun_Returns200Idempotent()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeCancelledRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a run that is already CANCELLED must return 200 idempotently");
        result.Value.Should().Be(runId);
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledRun_PerformsNoMutations()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeCancelledRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert — idempotent path must not begin a transaction or write anything.
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no transaction must begin for an idempotent double-cancel");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "CommitAsync must not be called when there is nothing to persist");
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "the run record must not be modified on a double cancel");
        journalRepo.Verify(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no journal entry must be written for an idempotent cancel");
    }

    // ── 11. Run not found → 404 ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var runId = Guid.NewGuid();

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((RunRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue(because: "a missing run must return a failure");
        result.Error.Code.Should().Be("NotFound",
            because: "the error code for a missing run must be NotFound");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must not begin when the run does not exist");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 12. Missing RequestedBy → 400 Validation ──────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task HandleAsync_MissingRequestedBy_ReturnsValidationError(string? requestedBy)
    {
        // Arrange
        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);
        var cmd     = new CancelRunCommand(Guid.NewGuid(), requestedBy!);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "RequestedBy is mandatory for audit purposes");
        result.Error.Code.Should().Be("Validation",
            because: "a missing RequestedBy is a client input error (Validation), not a domain conflict");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must not begin when input validation fails");
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the run must not even be loaded when RequestedBy is missing");
    }

    // ── 13. All terminal states (other than CANCELLED) — never begin transaction

    [Theory]
    [InlineData("HandedOff")]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Unknown")]
    public async Task HandleAsync_AllNonCancelledTerminalStates_NeverBeginTransaction(string stateName)
    {
        // Arrange
        var runId = Guid.NewGuid();
        RunRecord run = stateName switch
        {
            "HandedOff" => MakeHandedOffRun(runId),
            "Succeeded" => MakeSucceededRun(runId),
            "Failed"    => MakeFailedRun(runId),
            "TimedOut"  => MakeTimedOutRun(runId),
            "Unknown"   => MakeUnknownRun(runId),
            _           => throw new InvalidOperationException($"Unknown state: {stateName}")
        };

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TERMINAL_STATE",
            $"state={stateName} must produce TERMINAL_STATE error");
        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            $"no transaction must begin for terminal state {stateName}");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            $"CommitAsync must never be called for terminal state {stateName}");
        journalRepo.Verify(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            $"no journal entry must be written for terminal state {stateName}");
    }

    // ── 14. RunId is correctly propagated in the success result ──────────────

    [Fact]
    public async Task HandleAsync_QueuedRun_ResultValueIsTheRunId()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);

        var runRepo     = new Mock<IRunRepository>();
        var leaseRepo   = new Mock<ILeaseRepository>();
        var journalRepo = new Mock<IJournalRepository>();
        var uow         = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var handler = BuildHandler(runRepo, leaseRepo, journalRepo, uow);

        // Act
        var result = await handler.HandleAsync(new CancelRunCommand(runId, RequestedBy));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(runId,
            because: "the handler must return the RunId so the API can echo it back to the caller");
    }
}
