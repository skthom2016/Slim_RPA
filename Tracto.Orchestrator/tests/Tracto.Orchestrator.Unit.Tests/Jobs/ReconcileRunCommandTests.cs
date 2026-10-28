#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="ReconcileRunCommandHandler"/> — story OC-10.
///
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; all dependencies are Moq-backed loose mocks.
///
/// Scenarios covered:
///   1. Journal has RUN_SUCCEEDED + correct attemptId → State=SUCCEEDED, FinalResultRecord written.
///   2. Empty journal → UNKNOWN + NeedsAttention + FinalResultRecord written.
///   3. Uncertain journal (entries present, none are SUCCEEDED) → UNKNOWN + NeedsAttention.
///   4. Stale attemptId → 409 WRONG_ATTEMPT, no DB writes.
///   5. Auto-replay invariant: given ANY journal, reconcile NEVER sets State=QUEUED or CREATED.
///   6. Attempt count unchanged after reconcile (AttemptId is same before and after).
///   7. Already-terminal run → idempotent 200, no DB writes.
/// </summary>
public sealed class ReconcileRunCommandTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    private const string WorkerId = "worker-reconcile";

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a RunRecord in LEASED state: Created → Queued → AdoptLease.
    /// Returns the run and the lease AttemptId (which becomes the run-level fence).
    /// This simulates a run that was leased but the worker disconnected before RUNNING.
    /// </summary>
    private static (RunRecord run, Guid attemptId) MakeLeasedRun()
    {
        var run = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       "wf-reconcile",
            createdAt:        FixedNow.AddMinutes(-10),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: "artifact-001",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    "biz-job-01");

        run.Enqueue(FixedNow.AddMinutes(-9));

        var leaseAttemptId = Guid.NewGuid();
        run.AdoptLease(leaseAttemptId, WorkerId, FixedNow.AddMinutes(-8));

        return (run, leaseAttemptId);
    }

    /// <summary>
    /// Builds a RunRecord in RUNNING state: Created → Queued → AdoptLease → Start.
    /// Simulates a run where the worker started but disconnected mid-execution.
    /// </summary>
    private static (RunRecord run, Guid attemptId) MakeRunningRun()
    {
        var (run, leaseAttemptId) = MakeLeasedRun();
        run.Start(leaseAttemptId, FixedNow.AddMinutes(-7));
        return (run, leaseAttemptId);
    }

    /// <summary>
    /// Builds a fully wired <see cref="ReconcileRunCommandHandler"/> with the supplied mocks.
    /// </summary>
    private static ReconcileRunCommandHandler BuildHandler(
        Mock<IRunRepository>         runRepo,
        Mock<IFinalResultRepository> finalResultRepo,
        Mock<IJournalRepository>     journalRepo,
        Mock<IUnitOfWork>            uow,
        FakeClock?                   clock = null)
    {
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new ReconcileRunCommandHandler(
            runRepo:         runRepo.Object,
            finalResultRepo: finalResultRepo.Object,
            journalRepo:     journalRepo.Object,
            unitOfWork:      uow.Object,
            clock:           clock ?? new FakeClock(FixedNow),
            telemetry:       telemetry.Object,
            logger:          NullLogger<ReconcileRunCommandHandler>.Instance);
    }

    // ── Test 1: RUN_SUCCEEDED in journal → State=SUCCEEDED ────────────────────

    [Fact]
    public async Task JournalHasSucceeded_WithMatchingAttemptId_ReconcilesToSucceeded()
    {
        // Arrange
        var (run, attemptId) = MakeRunningRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var capturedResults = new List<FinalResultRecord>();
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((fr, _) => capturedResults.Add(fr))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var journalEntries = new[]
        {
            new ReconciliationJournalEntry("RUN_STARTED",   attemptId, FixedNow.AddMinutes(-6)),
            new ReconciliationJournalEntry("RUN_SUCCEEDED", attemptId, FixedNow.AddMinutes(-5)),
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: journalEntries);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("SUCCEEDED");
        result.Value.WasAlreadyTerminal.Should().BeFalse();

        run.State.Should().Be(RunState.Succeeded,
            "handler must transition run to SUCCEEDED when journal confirms it");

        capturedResults.Should().HaveCount(1);
        capturedResults[0].TerminalState.Should().Be(RunState.Succeeded);

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.RollbackAsync(), Times.Never);
    }

    // ── Test 2: Empty journal → UNKNOWN + NeedsAttention ─────────────────────

    [Fact]
    public async Task EmptyJournal_ReconcilesToUnknownWithNeedsAttention()
    {
        // Arrange
        var (run, attemptId) = MakeLeasedRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var capturedResults = new List<FinalResultRecord>();
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((fr, _) => capturedResults.Add(fr))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: []   // ← empty
        );

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("UNKNOWN");
        result.Value.WasAlreadyTerminal.Should().BeFalse();

        run.State.Should().Be(RunState.Unknown,
            "empty journal must yield UNKNOWN state");
        run.NeedsAttention.Should().BeTrue("UNKNOWN always requires NeedsAttention (invariant 2)");
        run.UnknownReason.Should().NotBeNullOrWhiteSpace();

        capturedResults.Should().HaveCount(1);
        capturedResults[0].TerminalState.Should().Be(RunState.Unknown);
        capturedResults[0].NeedsAttention.Should().BeTrue();

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Test 3: Uncertain journal (entries present, none RUN_SUCCEEDED) ───────

    [Fact]
    public async Task UncertainJournal_ReconcilesToUnknownWithNeedsAttention()
    {
        // Arrange — journal has entries but no RUN_SUCCEEDED
        var (run, attemptId) = MakeRunningRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        // Journal has entries but not RUN_SUCCEEDED — outcome is uncertain.
        var journalEntries = new[]
        {
            new ReconciliationJournalEntry("RUN_STARTED",     attemptId, FixedNow.AddMinutes(-6)),
            new ReconciliationJournalEntry("STEP_COMPLETED",  attemptId, FixedNow.AddMinutes(-5)),
            new ReconciliationJournalEntry("WORKER_CRASHED",  attemptId, FixedNow.AddMinutes(-4)),
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: journalEntries);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("UNKNOWN",
            "journal without RUN_SUCCEEDED must yield UNKNOWN");

        run.State.Should().Be(RunState.Unknown);
        run.NeedsAttention.Should().BeTrue();

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Test 4: Stale AttemptId → 409 WRONG_ATTEMPT ───────────────────────────

    [Fact]
    public async Task StaleAttemptId_Returns409WrongAttempt_WithNoDbWrites()
    {
        // Arrange
        var (run, _) = MakeLeasedRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      Guid.NewGuid(),   // ← stale / wrong AttemptId
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WRONG_ATTEMPT");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        finalResultRepo.Verify(
            r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 5: Auto-replay invariant ─────────────────────────────────────────

    /// <summary>
    /// Given ANY journal input (empty, uncertain, or confirmed SUCCEEDED),
    /// reconcile MUST NOT set State=QUEUED or CREATED.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnyJournalInputCases))]
    public async Task ReconcileNeverSetsQueuedOrCreated(
        ReconciliationJournalEntry[] journalEntries,
        string caseDescription)
    {
        _ = caseDescription;  // used only for test display

        // Arrange
        var (run, attemptId) = MakeRunningRun();

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: journalEntries);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — the reconciled state is NEVER QUEUED or CREATED.
        result.IsSuccess.Should().BeTrue();
        run.State.Should().NotBe(RunState.Queued,
            $"reconcile must NEVER auto-replay to QUEUED (case: {caseDescription})");
        run.State.Should().NotBe(RunState.Created,
            $"reconcile must NEVER auto-replay to CREATED (case: {caseDescription})");
    }

    public static TheoryData<ReconciliationJournalEntry[], string> AnyJournalInputCases()
    {
        var arbitraryAttemptId = Guid.NewGuid();  // We'll use a per-run attemptId in actual test runs
        return new TheoryData<ReconciliationJournalEntry[], string>
        {
            // Case 1: empty journal
            { [], "empty journal" },

            // Case 2: uncertain entries only
            {
                [
                    new ReconciliationJournalEntry("RUN_STARTED", arbitraryAttemptId, DateTimeOffset.UtcNow),
                ],
                "uncertain journal"
            },
        };
    }

    // ── Test 6: Attempt count unchanged after reconcile ───────────────────────

    [Fact]
    public async Task AttemptCountUnchanged_AfterReconcile_OriginalAttemptIdRetained()
    {
        // Arrange
        var (run, attemptId) = MakeRunningRun();
        var originalAttemptId = run.AttemptId;

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var journalEntries = new[]
        {
            new ReconciliationJournalEntry("RUN_SUCCEEDED", attemptId, FixedNow.AddMinutes(-1)),
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: journalEntries);

        // Act
        await handler.HandleAsync(command);

        // Assert — AttemptId must be the same value it was before reconciliation.
        run.AttemptId.Should().Be(originalAttemptId,
            "reconcile must NOT generate a new AttemptId; original is retained.");
    }

    // ── Test 7: Already-terminal run → idempotent 200 ────────────────────────

    [Fact]
    public async Task AlreadyTerminalRun_ReturnsIdempotent200_WithNoDbWrites()
    {
        // Arrange — run is already SUCCEEDED
        var (run, attemptId) = MakeRunningRun();
        run.Succeed(attemptId, FixedNow.AddMinutes(-3));   // manually move to terminal

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: [new ReconciliationJournalEntry("RUN_SUCCEEDED", attemptId, FixedNow)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — idempotent 200, no additional DB writes.
        result.IsSuccess.Should().BeTrue();
        result.Value.WasAlreadyTerminal.Should().BeTrue();

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no commit should occur when run is already terminal");
        finalResultRepo.Verify(
            r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no FinalResultRecord should be written for an already-terminal run");
    }

    // ── Test 8: RUN_SUCCEEDED in journal but with WRONG attempt id ────────────

    [Fact]
    public async Task JournalHasSucceeded_ButWrongAttemptId_ReconcilesToUnknown()
    {
        // Arrange — journal has RUN_SUCCEEDED but with a DIFFERENT attemptId
        var (run, attemptId) = MakeRunningRun();
        var otherAttemptId   = Guid.NewGuid();  // ← different from run's current AttemptId

        var runRepo         = new Mock<IRunRepository>(MockBehavior.Loose);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Loose);
        var journalRepo     = new Mock<IJournalRepository>(MockBehavior.Loose);
        var uow             = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = BuildHandler(runRepo, finalResultRepo, journalRepo, uow);

        // Journal entry has a DIFFERENT attemptId — must not be treated as confirmed.
        var journalEntries = new[]
        {
            new ReconciliationJournalEntry("RUN_SUCCEEDED", otherAttemptId, FixedNow.AddMinutes(-1)),
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attemptId,
            JournalEntries: journalEntries);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — SUCCEEDED entry with wrong attemptId must yield UNKNOWN.
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("UNKNOWN",
            "RUN_SUCCEEDED with a non-matching AttemptId must be treated as uncertain");
        run.State.Should().Be(RunState.Unknown);
        run.NeedsAttention.Should().BeTrue();
    }
}
