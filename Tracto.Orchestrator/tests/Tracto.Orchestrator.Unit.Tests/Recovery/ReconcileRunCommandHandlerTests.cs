#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;
using IClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Unit.Tests.Recovery;

/// <summary>
/// Unit tests for <see cref="ReconcileRunCommandHandler"/> (OC-10).
///
/// Acceptance criteria verified:
///   AC-1: Journal contains RUN_SUCCEEDED with matching AttemptId → State=SUCCEEDED + FinalResultRecord written.
///   AC-2: Empty journal → State=UNKNOWN, NeedsAttention=true, UnknownReason set.
///   AC-3: Journal contains entries but none are RUN_SUCCEEDED → State=UNKNOWN.
///   AC-4: RUN_SUCCEEDED entry carries a foreign AttemptId → treated as uncertain → UNKNOWN.
///   AC-5: Stale (mismatched) command AttemptId → 409 WRONG_ATTEMPT; run state unchanged.
///   AC-6: Auto-replay invariant — for ANY journal input, reconcile NEVER sets State=QUEUED or CREATED.
///   AC-7: AttemptId (attempt fence) is unchanged after reconcile on both SUCCEEDED and UNKNOWN paths.
///   AC-8: CommitAsync called exactly once per successful reconcile.
///   AC-9: RollbackAsync called when CommitAsync throws; exception propagates.
///   AC-10: Run not found → NotFound error returned without touching the database.
///   AC-11: Already-terminal run → idempotent 200; no second commit.
///   AC-12: UNKNOWN path also writes a FinalResultRecord (with UNKNOWN terminal state).
/// </summary>
public sealed class ReconcileRunCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2025, 8, 12, 9, 0, 0, TimeSpan.Zero);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ReconcileRunCommandHandler BuildHandler(
        IRunRepository         runRepo,
        IFinalResultRepository finalResultRepo,
        IJournalRepository     journalRepo,
        IUnitOfWork            unitOfWork,
        IClock?                clock = null)
    {
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new ReconcileRunCommandHandler(
            runRepo,
            finalResultRepo,
            journalRepo,
            unitOfWork,
            clock ?? new FakeClock(FixedNow),
            telemetry.Object,
            NullLogger<ReconcileRunCommandHandler>.Instance);
    }

    /// <summary>Creates a RunRecord in RUNNING state.</summary>
    private static RunRecord MakeRunningRun()
    {
        var t   = FixedNow.AddMinutes(-30);
        var run = new RunRecord(Guid.NewGuid(), "wf-oc10", t);
        run.Enqueue(t);
        run.AdoptLease(Guid.NewGuid(), "worker-oc10", t);
        run.Start(run.AttemptId, t);
        return run;
    }

    /// <summary>Creates a RunRecord in LEASED state (accepted lease but not yet started).</summary>
    private static RunRecord MakeLeasedRun()
    {
        var t   = FixedNow.AddMinutes(-10);
        var run = new RunRecord(Guid.NewGuid(), "wf-oc10-leased", t);
        run.Enqueue(t);
        run.AdoptLease(Guid.NewGuid(), "worker-leased", t);
        return run;
    }

    /// <summary>Creates a ReconciliationJournalEntry signalling RUN_SUCCEEDED for the given AttemptId.</summary>
    private static ReconciliationJournalEntry SucceededEntry(Guid attemptId) =>
        new(ReconcileRunCommandHandler.SucceededEventType, attemptId, FixedNow);

    /// <summary>Creates a ReconciliationJournalEntry with an unrelated event type.</summary>
    private static ReconciliationJournalEntry OtherEntry(Guid attemptId) =>
        new("RUN_CHECKPOINT", attemptId, FixedNow.AddMinutes(-5));

    // ─── AC-1: RUN_SUCCEEDED + correct AttemptId → State=SUCCEEDED ───────────

    [Fact]
    public async Task HandleAsync_JournalHasSucceededEntry_WithMatchingAttemptId_SetsStateSucceeded()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a confirmed SUCCEEDED journal entry must yield a successful reconcile");
        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Succeeded,
            because: "AC-1: journal has RUN_SUCCEEDED + matching AttemptId → run must be SUCCEEDED");
    }

    [Fact]
    public async Task HandleAsync_JournalHasSucceededEntry_WritesFinalResultRecord()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        FinalResultRecord? capturedFinal = null;
        var mockFinal = new Mock<IFinalResultRepository>();
        mockFinal.Setup(f => f.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
                 .Callback<FinalResultRecord, CancellationToken>((r, _) => capturedFinal = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert
        capturedFinal.Should().NotBeNull(
            because: "AC-1: a FinalResultRecord must be written when reconciled to SUCCEEDED");
        capturedFinal!.TerminalState.Should().Be(RunState.Succeeded,
            because: "the FinalResultRecord terminal state must be SUCCEEDED");
        capturedFinal.RunId.Should().Be(run.Id);
        capturedFinal.AttemptId.Should().Be(attempt);
    }

    // ─── AC-2: Empty journal → UNKNOWN + NeedsAttention ──────────────────────

    [Fact]
    public async Task HandleAsync_EmptyJournal_SetsStateUnknown_WithNeedsAttention()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "uncertain outcome is a valid reconcile result — UNKNOWN is surfaced via NeedsAttention");
        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Unknown,
            because: "AC-2: empty journal → UNKNOWN");
        captured.NeedsAttention.Should().BeTrue(
            because: "domain invariant 2: UNKNOWN always requires NeedsAttention=true");
        captured.UnknownReason.Should().NotBeNullOrWhiteSpace(
            because: "domain invariant 2: UNKNOWN always requires a non-empty UnknownReason");
    }

    [Fact]
    public async Task HandleAsync_EmptyJournal_UnknownReasonContainsReconciliation()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        captured!.UnknownReason.Should().Contain(
            "Reconciliation",
            because: "the canonical UnknownReason must identify the reconcile path");
    }

    // ─── AC-3: Uncertain journal (entries exist but no RUN_SUCCEEDED) → UNKNOWN ─

    [Fact]
    public async Task HandleAsync_JournalHasOtherEventsButNoSucceeded_SetsStateUnknown()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        // Journal has checkpoint events but no RUN_SUCCEEDED.
        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [OtherEntry(attempt), OtherEntry(attempt)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        captured!.State.Should().Be(RunState.Unknown,
            because: "AC-3: uncertain journal (no RUN_SUCCEEDED) → UNKNOWN");
        captured.NeedsAttention.Should().BeTrue();
    }

    // ─── AC-4: RUN_SUCCEEDED with foreign AttemptId → treated as uncertain ──

    [Fact]
    public async Task HandleAsync_JournalHasSucceededForDifferentAttempt_SetsStateUnknown()
    {
        // Arrange — RUN_SUCCEEDED entry carries a DIFFERENT AttemptId; must be ignored.
        var run            = MakeRunningRun();
        var currentAttempt = run.AttemptId;
        var foreignAttempt = Guid.NewGuid();

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        // RUN_SUCCEEDED from a foreign attempt — must NOT trigger SUCCEEDED.
        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      currentAttempt,
            JournalEntries: [SucceededEntry(foreignAttempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert
        captured!.State.Should().Be(RunState.Unknown,
            because: "AC-4: a RUN_SUCCEEDED for a foreign AttemptId must not be treated as evidence");
    }

    // ─── AC-5: Stale command AttemptId → 409 WRONG_ATTEMPT ────────────────────

    [Fact]
    public async Task HandleAsync_StaleAttemptId_ReturnsWrongAttemptConflict()
    {
        // Arrange
        var run          = MakeRunningRun();
        var staleAttempt = Guid.NewGuid(); // differs from run.AttemptId

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      staleAttempt,
            JournalEntries: [SucceededEntry(staleAttempt)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "AC-5: stale AttemptId must yield a failure");
        result.Error.Code.Should().Be("WRONG_ATTEMPT",
            because: "error code must be WRONG_ATTEMPT for AttemptId mismatch (409)");
    }

    [Fact]
    public async Task HandleAsync_StaleAttemptId_RunStateIsUnchanged()
    {
        // Arrange
        var run           = MakeRunningRun();
        var originalState = run.State;
        var staleAttempt  = Guid.NewGuid();

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      staleAttempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        run.State.Should().Be(originalState,
            because: "stale AttemptId must leave the run state completely unchanged");
        mockRun.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "IRunRepository.Update must never be called when the AttemptId is stale");
    }

    [Fact]
    public async Task HandleAsync_StaleAttemptId_NeverCallsCommit()
    {
        // Arrange
        var run          = MakeRunningRun();
        var staleAttempt = Guid.NewGuid();

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      staleAttempt,
            JournalEntries: [SucceededEntry(staleAttempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "stale AttemptId short-circuits before any write — no commit must happen");
    }

    // ─── AC-6: Auto-replay invariant ─────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllJournalVariants))]
    public async Task HandleAsync_AutoReplayInvariant_NeverSetsStateQueuedOrCreated(
        string scenario, ReconciliationJournalEntry[] journalEntries)
    {
        // Arrange — use a LEASED run (crash between QUEUED and RUNNING).
        var run     = MakeLeasedRun();
        var attempt = run.AttemptId;

        // Rebuild entries with the actual run's AttemptId so they are plausible.
        var adjustedEntries = journalEntries
            .Select(e => new ReconciliationJournalEntry(e.EventType, attempt, e.OccurredAt))
            .ToArray();

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: adjustedEntries);

        // Act
        await handler.HandleAsync(command);

        // Assert — QUEUED and CREATED must never appear after reconcile.
        run.State.Should().NotBe(RunState.Queued,
            because: $"AC-6 ({scenario}): reconcile MUST NOT auto-replay (set State=QUEUED)");
        run.State.Should().NotBe(RunState.Created,
            because: $"AC-6 ({scenario}): reconcile MUST NOT transition to CREATED");
    }

    public static TheoryData<string, ReconciliationJournalEntry[]> AllJournalVariants()
    {
        var any = Guid.NewGuid();
        return new TheoryData<string, ReconciliationJournalEntry[]>
        {
            { "empty",           [] },
            { "checkpoint-only", [new("RUN_CHECKPOINT", any, DateTimeOffset.UtcNow)] },
            { "succeeded-entry", [new(ReconcileRunCommandHandler.SucceededEventType, any, DateTimeOffset.UtcNow)] },
            { "mixed-events",    [
                new("RUN_CHECKPOINT",                                       any, DateTimeOffset.UtcNow.AddMinutes(-2)),
                new(ReconcileRunCommandHandler.SucceededEventType,          any, DateTimeOffset.UtcNow),
            ]},
        };
    }

    // ─── AC-7: AttemptId unchanged after reconcile (no attempt-count increment) ─

    [Fact]
    public async Task HandleAsync_SucceededPath_AttemptIdUnchangedAfterReconcile()
    {
        // Arrange
        var run             = MakeRunningRun();
        var originalAttempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      originalAttempt,
            JournalEntries: [SucceededEntry(originalAttempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert
        run.AttemptId.Should().Be(originalAttempt,
            because: "AC-7: reconcile must NOT issue a new AttemptId (attempt count must not increment)");
    }

    [Fact]
    public async Task HandleAsync_UncertainPath_AttemptIdUnchangedAfterReconcile()
    {
        // Arrange
        var run             = MakeRunningRun();
        var originalAttempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      originalAttempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        run.AttemptId.Should().Be(originalAttempt,
            because: "AC-7: reconcile must NOT issue a new AttemptId on the UNKNOWN path either");
    }

    // ─── AC-8: CommitAsync called exactly once ────────────────────────────────

    [Fact]
    public async Task HandleAsync_SucceededPath_CommitsExactlyOnce()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "AC-8: exactly one CommitAsync per reconcile (SUCCEEDED path)");
    }

    [Fact]
    public async Task HandleAsync_UncertainPath_CommitsExactlyOnce()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "AC-8: exactly one CommitAsync per reconcile (UNKNOWN path)");
    }

    // ─── AC-9: Commit throws → rollback called, exception propagates ──────────

    [Fact]
    public async Task HandleAsync_CommitThrows_CallsRollbackAndRethrows()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("Simulated DB failure"));
        mockUow.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        var act = async () => await handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "AC-9: a commit failure must propagate so callers can abort");

        mockUow.Verify(u => u.RollbackAsync(), Times.Once,
            "AC-9: rollback must be attempted when commit throws");
    }

    // ─── AC-10: Run not found → NotFound error ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((RunRecord?)null);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          Guid.NewGuid(),
            AttemptId:      Guid.NewGuid(),
            JournalEntries: []);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "AC-10: a missing run must yield a failure");
        result.Error.Code.Should().Be("NotFound",
            because: "error code must be NotFound");
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "nothing must be committed when the run does not exist");
    }

    // ─── AC-11: Already-terminal run → idempotent 200 ─────────────────────────

    [Fact]
    public async Task HandleAsync_AlreadySucceededRun_ReturnsIdempotent200()
    {
        // Arrange — build a run that is already in SUCCEEDED state.
        var t   = FixedNow.AddMinutes(-60);
        var run = new RunRecord(Guid.NewGuid(), "wf-idempotent", t);
        run.Enqueue(t);
        run.AdoptLease(Guid.NewGuid(), "worker-idempotent", t);
        run.Start(run.AttemptId, t);
        run.Succeed(run.AttemptId, t);   // already terminal
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "AC-11: reconciling an already-terminal run must return idempotent 200");
        result.Value.WasAlreadyTerminal.Should().BeTrue(
            because: "WasAlreadyTerminal must be set to indicate the idempotent path");
        result.Value.ReconciledState.Should().Be("SUCCEEDED");
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no commit must happen for an already-terminal run (no state change)");
        mockRun.Verify(r => r.Update(It.IsAny<RunRecord>()), Times.Never,
            "Update must not be called when the run is already terminal");
    }

    // ─── AC-12: UNKNOWN path also writes FinalResultRecord ────────────────────

    [Fact]
    public async Task HandleAsync_UncertainPath_WritesFinalResultRecordWithUnknownState()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        FinalResultRecord? capturedFinal = null;
        var mockFinal = new Mock<IFinalResultRepository>();
        mockFinal.Setup(f => f.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
                 .Callback<FinalResultRecord, CancellationToken>((r, _) => capturedFinal = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        capturedFinal.Should().NotBeNull(
            because: "AC-12: the UNKNOWN path must also write a FinalResultRecord");
        capturedFinal!.TerminalState.Should().Be(RunState.Unknown,
            because: "the FinalResultRecord terminal state must be UNKNOWN on the uncertain path");
        capturedFinal.NeedsAttention.Should().BeTrue(
            because: "invariant 2: UNKNOWN FinalResultRecord must have NeedsAttention=true");
    }

    // ─── Journal entry appended in both paths ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_SucceededPath_AppendsJournalEntry()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        JournalEntry? capturedEntry = null;
        var mockJournal = new Mock<IJournalRepository>();
        mockJournal.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Callback<JournalEntry, CancellationToken>((e, _) => capturedEntry = e);

        var mockFinal = new Mock<IFinalResultRepository>();
        var mockUow   = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        await handler.HandleAsync(command);

        // Assert — invariant 5: journal entry must be part of the atomic write.
        capturedEntry.Should().NotBeNull(
            because: "a JournalEntry must be appended in the same transaction (domain invariant 5)");
        capturedEntry!.RunId.Should().Be(run.Id);
        capturedEntry.AttemptId.Should().Be(attempt);
        capturedEntry.OccurredAt.Should().Be(FixedNow,
            because: "journal timestamp must come from the injected IClock");
    }

    [Fact]
    public async Task HandleAsync_UncertainPath_AppendsJournalEntry()
    {
        // Arrange
        var run     = MakeRunningRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()));

        JournalEntry? capturedEntry = null;
        var mockJournal = new Mock<IJournalRepository>();
        mockJournal.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Callback<JournalEntry, CancellationToken>((e, _) => capturedEntry = e);

        var mockFinal = new Mock<IFinalResultRepository>();
        var mockUow   = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: []);

        // Act
        await handler.HandleAsync(command);

        // Assert
        capturedEntry.Should().NotBeNull(
            because: "a JournalEntry must also be appended on the UNKNOWN path");
        capturedEntry!.RunId.Should().Be(run.Id);
    }

    // ─── LEASED run progresses through RUNNING → SUCCEEDED ────────────────────

    [Fact]
    public async Task HandleAsync_LeasedRun_WithSucceededJournal_SetsStateSucceeded()
    {
        // Arrange — worker crashed between LEASED and RUNNING but submitted evidence.
        var run     = MakeLeasedRun();
        var attempt = run.AttemptId;

        var mockRun = new Mock<IRunRepository>();
        mockRun.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        RunRecord? captured = null;
        mockRun.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var mockJournal = new Mock<IJournalRepository>();
        var mockFinal   = new Mock<IFinalResultRepository>();
        var mockUow     = new Mock<IUnitOfWork>();

        var handler = BuildHandler(mockRun.Object, mockFinal.Object, mockJournal.Object, mockUow.Object);

        var command = new ReconcileRunCommand(
            RunId:          run.Id,
            AttemptId:      attempt,
            JournalEntries: [SucceededEntry(attempt)]);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        captured!.State.Should().Be(RunState.Succeeded,
            because: "a LEASED run must reach SUCCEEDED when journal confirms the work completed");
    }
}
