#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;
using ContinuationMapping = Tracto.Orchestrator.Domain.Runs.ContinuationMapping;
using ContinuationRunRecord = Tracto.Orchestrator.Domain.Runs.ContinuationRunRecord;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="CommitFinalResultCommandHandler"/> — story OC-06.
///
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; all dependencies are Moq-backed loose mocks.
///
/// Scenarios covered:
///   1. SUCCEEDED commit → run reaches terminal state, worker slot released, FinalResultRecord written.
///   2. UNKNOWN without reason → 422 UNKNOWN_REASON_REQUIRED.
///   3. Stale AttemptId → 409 WRONG_ATTEMPT.
///   4. Secret in payload → 422 SENSITIVE_PAYLOAD (security event emitted via logger).
///   5. Idempotent retry → second call returns 200, no duplicate FinalResultRecord written.
///   6. Slot released exactly once (two calls with same AttemptId → slot-release count == 1).
/// </summary>
public sealed class CommitFinalResultCommandTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private const string WorkerId = "worker-abc";

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a RunRecord that has already been transitioned to RUNNING via the
    /// standard state-machine path: Created → Queued → AdoptLease (LEASED) → Start (RUNNING).
    /// Returns the run and the AttemptId that was adopted from the lease.
    /// </summary>
    private static (RunRecord run, Guid attemptId) MakeRunningRun()
    {
        var run = new RunRecord(
            id:                   Guid.NewGuid(),
            workflowId:           "wf-test",
            createdAt:            FixedNow.AddMinutes(-5),
            correlationId:        Guid.NewGuid(),
            scriptArtifactId:     "artifact-001",
            scriptVersion:        "1.0.0",
            schemaVersion:        "1",
            inputRefs:            [],
            secretRefs:           [],
            deadline:             null,
            eventId:              null,
            triggerType:          TriggerType.Manual,
            businessJobId:        null);

        run.Enqueue(FixedNow.AddMinutes(-4));

        // Adopt a fresh lease AttemptId as the run-level fence.
        var leaseAttemptId = Guid.NewGuid();
        run.AdoptLease(leaseAttemptId, WorkerId, FixedNow.AddMinutes(-3));

        // Transition LEASED → RUNNING.
        run.Start(leaseAttemptId, FixedNow.AddMinutes(-2));

        return (run, leaseAttemptId);
    }

    /// <summary>Builds a no-op CreateContinuationRunCommandHandler (returns NO_MAPPING for all calls).</summary>
    private static CreateContinuationRunCommandHandler BuildNullContinuationHandler()
    {
        var mappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        mappingRepo.Setup(m => m.FindBestMatchAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((ContinuationMapping?)null);

        var contRunRepo = new Mock<IContinuationRunRepository>(MockBehavior.Loose);
        contRunRepo.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((ContinuationRunRecord?)null);

        var stubRunRepo2     = new Mock<IRunRepository>(MockBehavior.Loose);
        var stubRunRepo3     = new Mock<IRunRepository>(MockBehavior.Loose);
        var stubArtifactRepo = new Mock<IArtifactRepository>(MockBehavior.Loose);
        var stubJournalRepo2 = new Mock<IJournalRepository>(MockBehavior.Loose);
        var stubUow2         = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var stubTelemetry2   = new Mock<ITelemetryService>(MockBehavior.Loose);
        // CreateJobCommandHandler won't be reached because FindBestMatchAsync returns null.
        var createJobHandler = new CreateJobCommandHandler(
            stubRunRepo2.Object,
            stubArtifactRepo.Object,
            stubJournalRepo2.Object,
            stubUow2.Object,
            new FakeClock(FixedNow),
            stubTelemetry2.Object,
            NullLogger<CreateJobCommandHandler>.Instance);

        return new CreateContinuationRunCommandHandler(
            stubRunRepo3.Object,
            mappingRepo.Object,
            contRunRepo.Object,
            createJobHandler,
            stubUow2.Object,
            new FakeClock(FixedNow),
            NullLogger<CreateContinuationRunCommandHandler>.Instance);
    }

    /// <summary>Builds a fully wired handler with the supplied mock implementations.</summary>
    private static CommitFinalResultCommandHandler BuildHandler(
        Mock<IRunRepository>         runRepo,
        Mock<IWorkerRepository>      workerRepo,
        Mock<IFinalResultRepository> finalResultRepo,
        Mock<IJournalRepository>     journalRepo,
        Mock<IUnitOfWork>            uow,
        FakeClock?                   clock = null)
    {
        // GetByRunIdAsync is called for SUCCEEDED events (step count telemetry).
        journalRepo.Setup(j => j.GetByRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<JournalEntry>().AsReadOnly());

        var leaseRepo = new Mock<ILeaseRepository>(MockBehavior.Loose);
        leaseRepo.Setup(l => l.FindActiveLeaseForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        return new CommitFinalResultCommandHandler(
            runRepo:              runRepo.Object,
            workerRepo:           workerRepo.Object,
            finalResultRepo:      finalResultRepo.Object,
            journalRepo:          journalRepo.Object,
            unitOfWork:           uow.Object,
            clock:                clock ?? new FakeClock(FixedNow),
            telemetry:            new Mock<ITelemetryService>().Object,
            continuationHandler:  BuildNullContinuationHandler(),
            leaseRepo:            leaseRepo.Object,
            logger:               NullLogger<CommitFinalResultCommandHandler>.Instance);
    }

    // ── Test 1: SUCCEEDED commit ───────────────────────────────────────────────

    [Fact]
    public async Task Succeeded_Commit_TransitionsRunAndReleasesWorkerSlot()
    {
        // Arrange
        var (run, attemptId) = MakeRunningRun();

        var worker = new WorkerRecord(WorkerId, "dotnet", 30, FixedNow.AddHours(-1));
        worker.MarkRunning();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        workerRepo.Setup(r => r.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);

        var finalResultsAdded = new List<FinalResultRecord>();
        finalResultRepo.Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
                       .Callback<FinalResultRecord, CancellationToken>((fr, _) => finalResultsAdded.Add(fr))
                       .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        var command = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  ["blob://output/report.pdf"]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SlotReleased.Should().BeTrue();
        result.Value.TerminalState.Should().Be("SUCCEEDED");

        run.State.Should().Be(RunState.Succeeded);

        finalResultsAdded.Should().HaveCount(1);
        finalResultsAdded[0].TerminalState.Should().Be(RunState.Succeeded);
        finalResultsAdded[0].EvidenceRefs.Should().ContainSingle(e => e == "blob://output/report.pdf");

        worker.State.Should().Be(WorkerState.Available);

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.RollbackAsync(), Times.Never);
    }

    // ── Test 2: UNKNOWN without reason ────────────────────────────────────────

    [Fact]
    public async Task Unknown_WithoutReason_Returns422UnknownReasonRequired()
    {
        // Arrange
        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        var command = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Unknown,
            UnknownReason: null);   // ← missing reason

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("UNKNOWN_REASON_REQUIRED");

        // No DB interaction should have occurred.
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 3: Stale AttemptId ────────────────────────────────────────────────

    [Fact]
    public async Task StaleAttemptId_Returns409WrongAttempt()
    {
        // Arrange
        var (run, _) = MakeRunningRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        var command = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     Guid.NewGuid(),    // ← wrong / stale AttemptId
            TerminalState: RunState.Succeeded);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WRONG_ATTEMPT");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 4: Secret in payload ──────────────────────────────────────────────

    [Fact]
    public async Task SecretInPayload_Returns422SensitivePayload()
    {
        // Arrange
        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        // Payload contains a plaintext password — must be rejected.
        // The handler regex covers both plain-text (password=val) and JSON ("password":"val") forms.
        var sensitivePayload = "result=ok;password=supersecret123";

        var command = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Succeeded,
            Payload:       sensitivePayload);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SENSITIVE_PAYLOAD");

        // No run should have been loaded.
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 5: Idempotent retry ───────────────────────────────────────────────

    [Fact]
    public async Task IdempotentRetry_AlreadyTerminalRun_Returns200WithoutDuplicateRecord()
    {
        // Arrange — run is already in a terminal state (Succeeded).
        var (run, attemptId) = MakeRunningRun();
        run.Succeed(attemptId, FixedNow.AddMinutes(-1));   // manually move it to Succeeded

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        var command = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — idempotent 200, no DB writes.
        result.IsSuccess.Should().BeTrue();
        result.Value.SlotReleased.Should().BeFalse();

        finalResultRepo.Verify(
            r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Second delivery must NOT create a duplicate FinalResultRecord.");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 6: Slot released exactly once ────────────────────────────────────

    [Fact]
    public async Task SlotReleasedExactlyOnce_TwoCallsToSameTerminalRun()
    {
        // Arrange
        var (run, attemptId) = MakeRunningRun();

        var worker = new WorkerRecord(WorkerId, "dotnet", 30, FixedNow.AddHours(-1));
        worker.MarkRunning();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        workerRepo.Setup(r => r.GetByIdAsync(WorkerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(worker);

        var handler = BuildHandler(runRepo, workerRepo, finalResultRepo, journalRepo, uow);

        var command = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded);

        // First call — should commit and release the slot.
        var first = await handler.HandleAsync(command);
        first.IsSuccess.Should().BeTrue();
        first.Value.SlotReleased.Should().BeTrue("first call releases the slot");

        // Second call — run is now terminal; idempotent path must not release slot again.
        var second = await handler.HandleAsync(command);
        second.IsSuccess.Should().BeTrue();
        second.Value.SlotReleased.Should().BeFalse("second call is idempotent — slot must NOT be released again");

        // Worker.MarkAvailable must have been called exactly once.
        workerRepo.Verify(
            r => r.Update(It.IsAny<WorkerRecord>()),
            Times.Once,
            "Worker slot must be released exactly once across two calls.");

        // Commit must have been called exactly once (first call only).
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
