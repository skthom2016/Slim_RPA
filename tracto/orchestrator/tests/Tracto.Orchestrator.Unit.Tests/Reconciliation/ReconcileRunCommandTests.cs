#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Reconciliation;

/// <summary>
/// Unit tests for <see cref="ReconcileRunCommandHandler"/> and <see cref="ReconciliationService"/>
/// — story OC-10: Reconciliation After Disconnect or Restart.
///
/// Each test method covers exactly one observable behaviour (acceptance criterion).
/// FakeClock controls time; Moq stubs all external dependencies.
/// </summary>
public sealed class ReconcileRunCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    // ── Builder helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a RunRecord in LEASED state (the typical state for a run that needs
    /// reconciliation after a worker disconnect).
    /// </summary>
    private static RunRecord BuildLeasedRun(Guid? attemptIdOverride = null)
    {
        var run = new RunRecord(
            id:          Guid.NewGuid(),
            workflowId:  "wf-001",
            createdAt:   FixedNow.AddMinutes(-5),
            correlationId:        Guid.NewGuid(),
            scriptArtifactId:     "script-001",
            scriptVersion:        "1.0.0",
            schemaVersion:        "1",
            inputRefs:            [],
            secretRefs:           [],
            deadline:             null,
            eventId:              null,
            triggerType:          TriggerType.Manual,
            businessJobId:        "biz-job-42");

        // Enqueue then adopt a lease so we get a specific AttemptId
        run.Enqueue(FixedNow.AddMinutes(-4));

        if (attemptIdOverride.HasValue)
        {
            run.AdoptLease(attemptIdOverride.Value, "worker-1", FixedNow.AddMinutes(-3));
        }
        else
        {
            // Use whatever AttemptId AdoptLease assigns — we read it back via run.AttemptId
            run.AdoptLease(Guid.NewGuid(), "worker-1", FixedNow.AddMinutes(-3));
        }

        return run;
    }

    /// <summary>
    /// Fully wires a <see cref="ReconcileRunCommandHandler"/> with Moq-backed dependencies.
    /// Out-params expose mocks for fine-grained assertions per test.
    /// </summary>
    private static ReconcileRunCommandHandler MakeHandler(
        out Mock<IRunRepository>         runRepo,
        out Mock<IFinalResultRepository> finalResultRepo,
        out Mock<IJournalRepository>     journalRepo,
        out Mock<IUnitOfWork>            unitOfWork,
        RunRecord?   run   = null,
        FakeClock?   clock = null)
    {
        runRepo         = new Mock<IRunRepository>(MockBehavior.Strict);
        finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Strict);
        journalRepo     = new Mock<IJournalRepository>(MockBehavior.Strict);
        unitOfWork      = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // Run lookup default: return the supplied run (or null for 404 tests).
        runRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Update: track calls, do not throw.
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));

        // FinalResult write never fails.
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Journal append never fails.
        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // UnitOfWork lifecycle.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync())
                  .Returns(ValueTask.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new ReconcileRunCommandHandler(
            runRepo.Object,
            finalResultRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            telemetry.Object,
            NullLogger<ReconcileRunCommandHandler>.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 1: Journal has RUN_SUCCEEDED + correct AttemptId → State=SUCCEEDED
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_JournalContainsSucceededWithMatchingAttemptId_SetsStateToSucceeded()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(
            out var runRepo, out var finalResultRepo, out _, out _,
            run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        var journal = new[]
        {
            new ReconciliationJournalEntry(
                EventType:  ReconcileRunCommandHandler.SucceededEventType,
                AttemptId:  run.AttemptId,
                OccurredAt: FixedNow.AddSeconds(-30))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a conclusive SUCCEEDED journal entry with the correct AttemptId must reconcile to SUCCEEDED");
        result.Value.ReconciledState.Should().Be("SUCCEEDED");
        result.Value.WasAlreadyTerminal.Should().BeFalse();

        captured.Should().NotBeNull(because: "RunRecord.Update must have been called");
        captured!.State.Should().Be(RunState.Succeeded,
            because: "the run must be in SUCCEEDED state after reconciliation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 2: Empty journal → UNKNOWN + NeedsAttention = true
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_EmptyJournal_SetsStateToUnknownWithNeedsAttentionTrue()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(
            out var runRepo, out _, out _, out _,
            run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "reconciliation itself succeeds; the run state becomes UNKNOWN");
        result.Value.ReconciledState.Should().Be("UNKNOWN");

        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Unknown,
            because: "empty journal is uncertain — invariant 2 requires UNKNOWN");
        captured.NeedsAttention.Should().BeTrue(
            because: "domain invariant 2: UNKNOWN always requires NeedsAttention=true");
        captured.UnknownReason.Should().NotBeNullOrWhiteSpace(
            because: "domain invariant 2: UNKNOWN requires a non-null reason");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 3: Uncertain journal (non-SUCCEEDED events) → UNKNOWN + NeedsAttention
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_UncertainJournalWithoutSucceededEvent_SetsStateToUnknownWithNeedsAttentionTrue()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(
            out var runRepo, out _, out _, out _,
            run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        // Journal has entries, but none is RUN_SUCCEEDED
        var journal = new[]
        {
            new ReconciliationJournalEntry(
                EventType:  "RUN_RUNNING",
                AttemptId:  run.AttemptId,
                OccurredAt: FixedNow.AddSeconds(-60)),
            new ReconciliationJournalEntry(
                EventType:  "HEARTBEAT_RECEIVED",
                AttemptId:  run.AttemptId,
                OccurredAt: FixedNow.AddSeconds(-45))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("UNKNOWN");

        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Unknown,
            because: "journal with no RUN_SUCCEEDED event is uncertain — must produce UNKNOWN");
        captured.NeedsAttention.Should().BeTrue(
            because: "domain invariant 2: UNKNOWN always requires NeedsAttention=true");
        captured.UnknownReason.Should().NotBeNullOrWhiteSpace();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 4: Stale AttemptId → 409 Conflict (WRONG_ATTEMPT)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_StaleAttemptId_ReturnsConflictError()
    {
        // Arrange
        var run = BuildLeasedRun();
        var staleAttemptId = Guid.NewGuid(); // different from run.AttemptId

        var handler = MakeHandler(out _, out _, out _, out _, run: run);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      staleAttemptId,     // does not match run.AttemptId
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "an AttemptId mismatch must be rejected with a conflict error");
        result.Error.Code.Should().Be("WRONG_ATTEMPT",
            because: "domain invariant 3: stale AttemptId must return the WRONG_ATTEMPT code");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 5: Auto-replay invariant — reconcile NEVER sets State=QUEUED or CREATED
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]   // empty journal
    [InlineData(1)]   // journal with unrelated events
    [InlineData(2)]   // journal with SUCCEEDED but wrong AttemptId
    public async Task HandleAsync_AnyJournalInput_NeverSetsStateToQueuedOrCreated(int journalScenario)
    {
        // Arrange
        var run = BuildLeasedRun();
        var wrongAttemptId = Guid.NewGuid(); // different from run.AttemptId

        var journal = journalScenario switch
        {
            0 => Array.Empty<ReconciliationJournalEntry>(),
            1 => new[]
            {
                new ReconciliationJournalEntry("RUN_RUNNING", run.AttemptId, FixedNow.AddSeconds(-30))
            },
            _ => new[]
            {
                // SUCCEEDED with the WRONG AttemptId — must not be treated as conclusive
                new ReconciliationJournalEntry(
                    ReconcileRunCommandHandler.SucceededEventType,
                    wrongAttemptId,
                    FixedNow.AddSeconds(-30))
            }
        };

        var handler = MakeHandler(out var runRepo, out _, out _, out _, run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — the handler must succeed (not a 409 — AttemptId is correct)
        result.IsSuccess.Should().BeTrue(
            because: "the AttemptId is correct so the command should be processed");

        captured.Should().NotBeNull();
        captured!.State.Should().NotBe(RunState.Queued,
            because: "reconciliation MUST NEVER auto-replay a run by setting State=QUEUED");
        captured.State.Should().NotBe(RunState.Created,
            because: "reconciliation MUST NEVER set State=CREATED");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10 AC 6: Attempt count unchanged after reconcile (AttemptId NOT incremented)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_ReconcileSucceeded_DoesNotChangeAttemptId()
    {
        // Arrange
        var run = BuildLeasedRun();
        var originalAttemptId = run.AttemptId;

        var handler = MakeHandler(out var runRepo, out _, out _, out _, run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        var journal = new[]
        {
            new ReconciliationJournalEntry(
                ReconcileRunCommandHandler.SucceededEventType,
                run.AttemptId,
                FixedNow.AddSeconds(-10))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        await handler.HandleAsync(command);

        // Assert
        captured.Should().NotBeNull();
        captured!.AttemptId.Should().Be(originalAttemptId,
            because: "OC-10: reconcile must NOT increment AttemptId; original fence is retained");
    }

    [Fact]
    public async Task HandleAsync_ReconcileUnknown_DoesNotChangeAttemptId()
    {
        // Arrange
        var run = BuildLeasedRun();
        var originalAttemptId = run.AttemptId;

        var handler = MakeHandler(out var runRepo, out _, out _, out _, run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: []);           // empty → UNKNOWN path

        // Act
        await handler.HandleAsync(command);

        // Assert
        captured.Should().NotBeNull();
        captured!.AttemptId.Should().Be(originalAttemptId,
            because: "OC-10: UNKNOWN reconciliation must NOT increment AttemptId");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: Run not found → 404 NotFound
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange — pass null run so GetByIdAsync returns null
        var handler = MakeHandler(out _, out _, out _, out _, run: null);

        var command = new ReconcileRunCommand(
            RunId:          Guid.NewGuid(),
            AttemptId:      Guid.NewGuid(),
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a non-existent run must return an error");
        result.Error.Code.Should().Be("NotFound");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: Already-terminal run → idempotent 200 (WasAlreadyTerminal = true), no write
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_RunAlreadyTerminal_ReturnsIdempotent200WithoutWriting()
    {
        // Arrange — build a run in SUCCEEDED (terminal) state
        var run = BuildLeasedRun();
        run.ForceSucceed(run.AttemptId, FixedNow.AddMinutes(-1)); // already terminal

        var handler = MakeHandler(
            out var runRepo, out var finalResultRepo, out var journalRepo, out var unitOfWork,
            run: run);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.WasAlreadyTerminal.Should().BeTrue(
            because: "an already-terminal run must be handled idempotently");

        // No writes should have occurred
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "an already-terminal run must not trigger a RunRecord update");
        finalResultRepo.Verify(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no FinalResultRecord should be written on an idempotent reconcile");
        journalRepo.Verify(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no journal entry should be appended on an idempotent reconcile");
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no transaction commit should happen on an idempotent reconcile");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: SUCCEEDED journal entry with wrong AttemptId → treated as uncertain → UNKNOWN
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_SucceededEventWithWrongAttemptId_ProducesUnknown()
    {
        // Arrange
        var run = BuildLeasedRun();
        var wrongAttemptId = Guid.NewGuid();

        var handler = MakeHandler(out var runRepo, out _, out _, out _, run: run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        // The SUCCEEDED event carries a different (wrong) AttemptId
        var journal = new[]
        {
            new ReconciliationJournalEntry(
                ReconcileRunCommandHandler.SucceededEventType,
                wrongAttemptId,     // mismatch — must NOT count as conclusive
                FixedNow.AddSeconds(-20))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,   // correct fence on the command itself
            JournalEntries: journal);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReconciledState.Should().Be("UNKNOWN",
            because: "a SUCCEEDED event with the wrong AttemptId is not conclusive — outcome is UNKNOWN");

        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Unknown);
        captured.NeedsAttention.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: ReconciliationService.ScanInterval == 15 seconds
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReconciliationService_ScanInterval_Is15Seconds()
    {
        ReconciliationService.ScanInterval.Should().Be(TimeSpan.FromSeconds(15),
            because: "OC-10 specifies a 15-second reconciliation polling interval");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: ReconciliationService.ScanOnceAsync — no expired leases → no reconcile
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReconciliationService_ScanOnceAsync_NoExpiredLeases_DoesNotCallReconcileHandler()
    {
        // Arrange
        var leaseRepo    = new Mock<ILeaseRepository>(MockBehavior.Strict);
        var runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        var unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Strict);

        leaseRepo
            .Setup(r => r.FindExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaseRecord>());

        // handler that would be called if leases were found (should never be called)
        var reconcileHandler = new ReconcileRunCommandHandler(
            runRepo.Object, finalResultRepo.Object, journalRepo.Object,
            unitOfWork.Object, new FakeClock(FixedNow),
            Mock.Of<ITelemetryService>(),
            NullLogger<ReconcileRunCommandHandler>.Instance);

        var service = new ReconciliationService(
            leaseRepo.Object,
            runRepo.Object,
            journalRepo.Object,
            reconcileHandler,
            NullLogger<ReconciliationService>.Instance);

        // Act
        await service.ScanOnceAsync();

        // Assert — no run lookups should have occurred
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "with no expired leases, no run should be loaded");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: ReconciliationService.ScanOnceAsync — expired lease + empty journal → UNKNOWN
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReconciliationService_ScanOnceAsync_ExpiredLeaseWithEmptyJournal_ReconcilesToUnknown()
    {
        // Arrange
        var run = BuildLeasedRun();

        var lease = new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           run.Id,
            attemptId:       run.AttemptId,
            workerId:        "worker-1",
            artifactId:      "script-001",
            artifactVersion: "1.0.0",
            artifactHash:    "sha256:abc",
            offeredAt:       FixedNow.AddMinutes(-5),
            expiresAt:       FixedNow.AddMinutes(-1));
        lease.Expire();

        var leaseRepo    = new Mock<ILeaseRepository>(MockBehavior.Strict);
        var runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        var unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Strict);

        leaseRepo
            .Setup(r => r.FindExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lease });

        runRepo
            .Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        RunRecord? captured = null;
        runRepo
            .Setup(r => r.Update(It.IsAny<RunRecord>()))
            .Callback<RunRecord>(r => captured = r);

        // Empty journal for this run+attempt
        journalRepo
            .Setup(j => j.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<JournalEntry>());

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync())
                  .Returns(ValueTask.CompletedTask);

        var reconcileHandler = new ReconcileRunCommandHandler(
            runRepo.Object, finalResultRepo.Object, journalRepo.Object,
            unitOfWork.Object, new FakeClock(FixedNow),
            Mock.Of<ITelemetryService>(),
            NullLogger<ReconcileRunCommandHandler>.Instance);

        var service = new ReconciliationService(
            leaseRepo.Object,
            runRepo.Object,
            journalRepo.Object,
            reconcileHandler,
            NullLogger<ReconciliationService>.Instance);

        // Act
        await service.ScanOnceAsync();

        // Assert
        captured.Should().NotBeNull(
            because: "the expired lease must trigger reconciliation of its run");
        captured!.State.Should().Be(RunState.Unknown,
            because: "an empty journal is uncertain — the run must become UNKNOWN");
        captured.NeedsAttention.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: ReconciliationService skips runs whose AttemptId has changed (stale lease)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReconciliationService_ScanOnceAsync_StaleLeaseAttemptId_SkipsReconciliation()
    {
        // Arrange — the run now has a different AttemptId than the expired lease
        var run = BuildLeasedRun();
        var staleAttemptId = Guid.NewGuid(); // different from run.AttemptId

        var lease = new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           run.Id,
            attemptId:       staleAttemptId,   // stale
            workerId:        "worker-1",
            artifactId:      "script-001",
            artifactVersion: "1.0.0",
            artifactHash:    "sha256:abc",
            offeredAt:       FixedNow.AddMinutes(-10),
            expiresAt:       FixedNow.AddMinutes(-5));
        lease.Expire();

        var leaseRepo    = new Mock<ILeaseRepository>(MockBehavior.Strict);
        var runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        var unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Strict);

        leaseRepo
            .Setup(r => r.FindExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lease });

        runRepo
            .Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var reconcileHandler = new ReconcileRunCommandHandler(
            runRepo.Object, finalResultRepo.Object, journalRepo.Object,
            unitOfWork.Object, new FakeClock(FixedNow),
            Mock.Of<ITelemetryService>(),
            NullLogger<ReconcileRunCommandHandler>.Instance);

        var service = new ReconciliationService(
            leaseRepo.Object,
            runRepo.Object,
            journalRepo.Object,
            reconcileHandler,
            NullLogger<ReconciliationService>.Instance);

        // Act
        await service.ScanOnceAsync();

        // Assert — stale lease must be silently skipped; no journal load, no update
        journalRepo.Verify(j => j.GetByRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a stale lease must not trigger journal loading or reconciliation");
        runRepo.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "a stale lease must not update the RunRecord");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: Reconcile response carries the correct RunId and AttemptId echo
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_SuccessfulReconcile_ResponseEchosRunIdAndAttemptId()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(out _, out _, out _, out _, run: run);

        var journal = new[]
        {
            new ReconciliationJournalEntry(
                ReconcileRunCommandHandler.SucceededEventType,
                run.AttemptId,
                FixedNow.AddSeconds(-5))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RunId.Should().Be(run.Id,
            because: "the response must echo the RunId from the command");
        result.Value.AttemptId.Should().Be(run.AttemptId,
            because: "the response must echo the original AttemptId unchanged");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: FinalResultRecord written on SUCCEEDED reconcile
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_SucceededReconcile_WritesFinalResultRecord()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _,
            run: run);

        FinalResultRecord? capturedFinalResult = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((r, _) => capturedFinalResult = r)
            .Returns(Task.CompletedTask);

        var journal = new[]
        {
            new ReconciliationJournalEntry(
                ReconcileRunCommandHandler.SucceededEventType,
                run.AttemptId,
                FixedNow.AddSeconds(-10))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        await handler.HandleAsync(command);

        // Assert
        capturedFinalResult.Should().NotBeNull(
            because: "a FinalResultRecord must be written atomically with the SUCCEEDED state transition");
        capturedFinalResult!.TerminalState.Should().Be(RunState.Succeeded);
        capturedFinalResult.AttemptId.Should().Be(run.AttemptId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: FinalResultRecord written on UNKNOWN reconcile with unknownReason
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_UnknownReconcile_WritesFinalResultRecordWithUnknownReason()
    {
        // Arrange
        var run = BuildLeasedRun();

        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _,
            run: run);

        FinalResultRecord? capturedFinalResult = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((r, _) => capturedFinalResult = r)
            .Returns(Task.CompletedTask);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: []);   // empty → UNKNOWN

        // Act
        await handler.HandleAsync(command);

        // Assert
        capturedFinalResult.Should().NotBeNull(
            because: "a FinalResultRecord must be written for the UNKNOWN outcome");
        capturedFinalResult!.TerminalState.Should().Be(RunState.Unknown);
        capturedFinalResult.UnknownReason.Should().NotBeNullOrWhiteSpace(
            because: "domain invariant 2: FinalResultRecord for UNKNOWN must carry an UnknownReason");
        capturedFinalResult.NeedsAttention.Should().BeTrue(
            because: "domain invariant 2: FinalResultRecord for UNKNOWN must have NeedsAttention=true");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OC-10: All writes committed atomically
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleAsync_SuccessfulReconcile_CommitsTransactionExactlyOnce()
    {
        // Arrange
        var run = BuildLeasedRun();
        var handler = MakeHandler(out _, out _, out _, out var unitOfWork, run: run);

        var journal = new[]
        {
            new ReconciliationJournalEntry(
                ReconcileRunCommandHandler.SucceededEventType,
                run.AttemptId,
                FixedNow.AddSeconds(-5))
        };

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      run.AttemptId,
            JournalEntries: journal);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "domain invariant 5: all writes must be committed atomically in a single transaction");
    }
}
