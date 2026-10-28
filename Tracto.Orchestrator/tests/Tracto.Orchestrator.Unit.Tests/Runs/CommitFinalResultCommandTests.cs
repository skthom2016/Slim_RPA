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
using Tracto.Orchestrator.Domain.Workers;
using Tracto.Orchestrator.Infrastructure.Clock;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracto.Orchestrator.Unit.Tests.Runs;

/// <summary>
/// Unit tests for <see cref="CommitFinalResultCommandHandler"/> — story OC-06.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; Moq stubs all external dependencies.
/// </summary>
public sealed class CommitFinalResultCommandTests
{
    // ── Fixed baseline ────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    private const string WorkerIdValue = "worker-oc06-test";

    // ── Domain helpers ────────────────────────────────────────────────────────

    /// <summary>Creates a RunRecord in RUNNING state, with a known AttemptId.</summary>
    private static RunRecord RunningRun(out Guid attemptId)
    {
        var run = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       "wf-oc06",
            createdAt:        FixedNow.AddMinutes(-20),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: "artifact-oc06",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        run.Enqueue(FixedNow.AddMinutes(-15));
        run.AdoptLease(Guid.NewGuid(), WorkerIdValue, FixedNow.AddMinutes(-10));
        run.Start(run.AttemptId, FixedNow.AddMinutes(-5));
        attemptId = run.AttemptId;
        return run;
    }

    /// <summary>Creates a RunRecord already in SUCCEEDED terminal state.</summary>
    private static RunRecord SucceededRun(out Guid attemptId)
    {
        var run = RunningRun(out attemptId);
        run.Succeed(attemptId, FixedNow.AddMinutes(-1));
        return run;
    }

    /// <summary>Creates a WorkerRecord in Running state (simulates an active slot).</summary>
    private static WorkerRecord RunningWorker() =>
        new(WorkerIdValue, "dotnet", 60, FixedNow.AddHours(-1));

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a no-op continuation handler (no mappings configured).</summary>
    private static CreateContinuationRunCommandHandler NullContinuationHandler()
    {
        var mappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        mappingRepo.Setup(m => m.FindBestMatchAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((Tracto.Orchestrator.Domain.Runs.ContinuationMapping?)null);

        var contRunRepo = new Mock<IContinuationRunRepository>(MockBehavior.Loose);
        contRunRepo.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((Tracto.Orchestrator.Domain.Runs.ContinuationRunRecord?)null);

        var stubRunRepo2     = new Mock<IRunRepository>(MockBehavior.Loose);
        var stubArtifactRepo = new Mock<IArtifactRepository>(MockBehavior.Loose);
        var stubJournalRepo2 = new Mock<IJournalRepository>(MockBehavior.Loose);
        var stubUow2         = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var stubTelemetry2   = new Mock<ITelemetryService>(MockBehavior.Loose);

        var createJobHandler = new CreateJobCommandHandler(
            stubRunRepo2.Object,
            stubArtifactRepo.Object,
            stubJournalRepo2.Object,
            stubUow2.Object,
            new FakeClock(FixedNow),
            stubTelemetry2.Object,
            NullLogger<CreateJobCommandHandler>.Instance);

        return new CreateContinuationRunCommandHandler(
            new Mock<IRunRepository>(MockBehavior.Loose).Object,
            mappingRepo.Object,
            contRunRepo.Object,
            createJobHandler,
            stubUow2.Object,
            new FakeClock(FixedNow),
            NullLogger<CreateContinuationRunCommandHandler>.Instance);
    }

    // ── Handler factory ───────────────────────────────────────────────────────

    private static CommitFinalResultCommandHandler MakeHandler(
        out Mock<IRunRepository>         runRepo,
        out Mock<IFinalResultRepository> finalResultRepo,
        out Mock<IWorkerRepository>      workerRepo,
        out Mock<IJournalRepository>     journalRepo,
        out Mock<IUnitOfWork>            unitOfWork,
        RunRecord?     runToReturn    = null,
        WorkerRecord?  workerToReturn = null,
        FakeClock?     clock          = null)
    {
        runRepo         = new Mock<IRunRepository>(MockBehavior.Strict);
        finalResultRepo = new Mock<IFinalResultRepository>(MockBehavior.Strict);
        workerRepo      = new Mock<IWorkerRepository>(MockBehavior.Strict);
        journalRepo     = new Mock<IJournalRepository>(MockBehavior.Strict);
        unitOfWork      = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // Run lookup.
        runRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runToReturn);

        // Run update (called when not already terminal).
        if (runToReturn is not null && !runToReturn.IsTerminal())
            runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));

        // FinalResultRecord write — set up for all paths; idempotent tests verify Times.Never.
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Worker lookup.
        workerRepo
            .Setup(w => w.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workerToReturn);

        // Worker update.
        workerRepo.Setup(w => w.Update(It.IsAny<WorkerRecord>()));

        // Journal append.
        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Journal read (used by SUCCEEDED path to get stepCount).
        journalRepo
            .Setup(j => j.GetByRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<JournalEntry>)new List<JournalEntry>());

        // UnitOfWork lifecycle.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var leaseRepo = new Mock<ILeaseRepository>(MockBehavior.Loose);
        leaseRepo.Setup(l => l.FindActiveLeaseForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        return new CommitFinalResultCommandHandler(
            runRepo.Object,
            workerRepo.Object,
            finalResultRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            telemetry.Object,
            NullContinuationHandler(),
            leaseRepo.Object,
            NullLogger<CommitFinalResultCommandHandler>.Instance);
    }

    // ── 1. Happy path: SUCCEEDED commit ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SucceededCommit_RunTransitionsToTerminal()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        RunRecord? captured = null;
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid SUCCEEDED commit must succeed");
        result.Value.TerminalState.Should().Be("SUCCEEDED");
        captured.Should().NotBeNull(because: "Run.Update must be called");
        captured!.State.Should().Be(RunState.Succeeded,
            because: "the RunRecord must transition to SUCCEEDED");
        captured.IsTerminal().Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SucceededCommit_WorkerSlotReleased()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out var workerRepo, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        WorkerRecord? capturedWorker = null;
        workerRepo.Setup(w => w.Update(It.IsAny<WorkerRecord>()))
                  .Callback<WorkerRecord>(w => capturedWorker = w);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SlotReleased.Should().BeTrue(
            because: "the worker slot must be released on a successful commit");
        capturedWorker.Should().NotBeNull(because: "WorkerRecord.Update must be called");
        capturedWorker!.State.Should().Be(WorkerState.Available,
            because: "the worker state must be AVAILABLE after slot release");
    }

    [Fact]
    public async Task HandleAsync_SucceededCommit_FinalResultRecordWritten()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        FinalResultRecord? capturedFinal = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((f, _) => capturedFinal = f)
            .Returns(Task.CompletedTask);

        var cmd = new CommitFinalResultCommand(
            RunId:           run.Id,
            AttemptId:       attemptId,
            TerminalState:   RunState.Succeeded,
            BusinessOutcome: "ORDER_FULFILLED",
            EvidenceRefs:    ["evidence://log/1"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedFinal.Should().NotBeNull(because: "FinalResultRecord.AddAsync must be called");
        capturedFinal!.TerminalState.Should().Be(RunState.Succeeded);
        capturedFinal.BusinessOutcome.Should().Be("ORDER_FULFILLED");
        capturedFinal.EvidenceRefs.Should().Contain("evidence://log/1");
        capturedFinal.AttemptId.Should().Be(attemptId);
    }

    [Fact]
    public async Task HandleAsync_SucceededCommit_JournalEntryWritten()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out _, out var journalRepo, out _,
            runToReturn:    run,
            workerToReturn: worker);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_SUCCEEDED"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_SUCCEEDED journal entry must be written");
    }

    [Fact]
    public async Task HandleAsync_SucceededCommit_CommitsUnitOfWork()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out _, out _, out var uow,
            runToReturn:    run,
            workerToReturn: worker);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "CommitAsync must be called exactly once to satisfy invariant 5");
    }

    // ── 2. UNKNOWN without UnknownReason → 422 UNKNOWN_REASON_REQUIRED ────────

    [Fact]
    public async Task HandleAsync_UnknownWithoutReason_Returns422UnknownReasonRequired()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Unknown,
            UnknownReason: null,          // absent
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "UNKNOWN state without UnknownReason must be rejected (invariant 2)");
        result.Error.Code.Should().Be("UNKNOWN_REASON_REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_UnknownWithBlankReason_Returns422UnknownReasonRequired(string blankReason)
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Unknown,
            UnknownReason: blankReason,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("UNKNOWN_REASON_REQUIRED",
            because: $"blank UnknownReason '{blankReason}' must be treated as absent");
    }

    [Fact]
    public async Task HandleAsync_UnknownWithNullReason_NoWritesMadeBeforeRunLoad()
    {
        // Arrange — the rejection happens before the run is loaded.
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _, out var uow);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Unknown,
            UnknownReason: null,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — the run repo must not have been queried and nothing committed.
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the run must not be loaded when UNKNOWN_REASON_REQUIRED validation fails");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "CommitAsync must not be called on a validation failure");
    }

    // ── 3. Stale / wrong AttemptId → 409 WRONG_ATTEMPT ───────────────────────

    [Fact]
    public async Task HandleAsync_StaleAttemptId_Returns409WrongAttempt()
    {
        // Arrange
        var run     = RunningRun(out _);  // real AttemptId from AdoptLease
        var staleId = Guid.NewGuid();     // different — simulates a stale message

        var handler = MakeHandler(
            out _, out _, out _, out _, out _,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     staleId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "a stale AttemptId must be rejected (invariant 3)");
        result.Error.Code.Should().Be("WRONG_ATTEMPT");
    }

    [Fact]
    public async Task HandleAsync_StaleAttemptId_NoWritesMade()
    {
        // Arrange
        var run = RunningRun(out _);
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out var journalRepo, out var uow,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     Guid.NewGuid(),   // stale
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — nothing must be written.
        finalResultRepo.Verify(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FinalResultRecord must not be written on an AttemptId mismatch");
        journalRepo.Verify(j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType != "SECURITY_SENSITIVE_PAYLOAD_REJECTED"),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Journal entry must not be written on an AttemptId mismatch");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 4. Secret in payload → 422 SENSITIVE_PAYLOAD ─────────────────────────

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("secret=mysecretvalue")]
    [InlineData("apikey=AKIA1234567890ABCDEF")]
    [InlineData("token=eyJhbGciOiJIUzI1NiJ9.payload.sig")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig")]
    [InlineData("aws_secret_access_key=wJalrXUtnFEMI")]
    public async Task HandleAsync_PayloadWithSecret_Returns422SensitivePayload(string secretPayload)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _, out _);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Succeeded,
            Payload:       secretPayload,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: $"payload '{secretPayload}' matches a secret pattern and must be rejected");
        result.Error.Code.Should().Be("SENSITIVE_PAYLOAD");
    }

    [Fact]
    public async Task HandleAsync_SecretPayload_NoRunLoadedAndNoCommit()
    {
        // Arrange — the secret scan happens before the run is loaded.
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _, out var uow);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Succeeded,
            Payload:       "password=s3cr3t!",
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the run must not be loaded when the payload is rejected for containing a secret");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "no transaction must be committed when the payload is rejected");
    }

    [Fact]
    public async Task HandleAsync_SecretPayload_NoJournalEntryWritten()
    {
        // Arrange — use loose mocks; rejection happens before any DB interaction.
        var runRepo         = new Mock<IRunRepository>();
        var finalResultRepo = new Mock<IFinalResultRepository>();
        var workerRepo      = new Mock<IWorkerRepository>();
        var journalRepo     = new Mock<IJournalRepository>();
        var unitOfWork      = new Mock<IUnitOfWork>();

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var leaseRepo2 = new Mock<ILeaseRepository>(MockBehavior.Loose);
        leaseRepo2.Setup(l => l.FindActiveLeaseForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((LeaseRecord?)null);

        var handler = new CommitFinalResultCommandHandler(
            runRepo.Object,
            workerRepo.Object,
            finalResultRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            new FakeClock(FixedNow),
            Mock.Of<ITelemetryService>(),
            NullContinuationHandler(),
            leaseRepo2.Object,
            NullLogger<CommitFinalResultCommandHandler>.Instance);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Succeeded,
            Payload:       "secret=ABCDEF",
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — no journal entry of any kind must be written when the payload is rejected.
        // The security warning is emitted via the ILogger (verified at integration level).
        journalRepo.Verify(
            j => j.AppendAsync(
                It.IsAny<JournalEntry>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "No journal entry must be written when the payload is rejected for containing a secret");
    }

    [Fact]
    public async Task HandleAsync_SafePayload_IsAccepted()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            Payload:       """{"status":"ok","count":42}""",   // safe JSON payload
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a payload without secret patterns must be accepted");
    }

    // ── 5. Idempotent retry: run already terminal ─────────────────────────────

    [Fact]
    public async Task HandleAsync_RunAlreadyTerminal_ReturnsIdempotent200()
    {
        // Arrange
        var run = SucceededRun(out var attemptId);

        // After Succeed(), the run is SUCCEEDED. Re-use same attemptId.
        var handler = MakeHandler(
            out _, out _, out _, out _, out _,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a second call on an already-terminal run must return idempotent 200");
        result.Value.SlotReleased.Should().BeFalse(
            because: "the worker slot must not be released again on an idempotent retry");
    }

    [Fact]
    public async Task HandleAsync_IdempotentRetry_NoDuplicateFinalResultRecord()
    {
        // Arrange
        var run = SucceededRun(out var attemptId);
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _, out _,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        finalResultRepo.Verify(
            r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FinalResultRecord must not be written again on an idempotent retry");
    }

    [Fact]
    public async Task HandleAsync_IdempotentRetry_NoCommitIssuedToDatabase()
    {
        // Arrange
        var run = SucceededRun(out var attemptId);
        var handler = MakeHandler(
            out _, out _, out _, out _, out var uow,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "CommitAsync must not be called for an idempotent return (no DB writes were made)");
    }

    [Fact]
    public async Task HandleAsync_IdempotentRetry_ReturnsExistingTerminalState()
    {
        // Arrange — the run is already SUCCEEDED.
        var run = SucceededRun(out var attemptId);
        var handler = MakeHandler(
            out _, out _, out _, out _, out _,
            runToReturn: run);

        // Command says FAILED, but the run is already SUCCEEDED.
        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Failed,   // different from what is stored
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — must reflect the already-committed state, not the new request.
        result.IsSuccess.Should().BeTrue();
        result.Value.TerminalState.Should().Be("SUCCEEDED",
            because: "the idempotent path returns the run's actual current state");
    }

    // ── 6. Slot released exactly once ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FirstCall_SlotReleasedExactlyOnce()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out var workerRepo, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        int updateCallCount = 0;
        workerRepo.Setup(w => w.Update(It.IsAny<WorkerRecord>()))
                  .Callback<WorkerRecord>(_ => updateCallCount++);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        updateCallCount.Should().Be(1,
            because: "the worker slot must be released exactly once on the first (non-idempotent) call");
    }

    [Fact]
    public async Task HandleAsync_SecondCallOnTerminalRun_SlotNotReleasedAgain()
    {
        // Arrange — simulate the state after the first call: run is already SUCCEEDED.
        var run = SucceededRun(out var attemptId);
        var handler = MakeHandler(
            out _, out _, out var workerRepo, out _, out _,
            runToReturn: run);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — the idempotent path must not touch the worker record.
        workerRepo.Verify(w => w.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "WorkerRepository must not be queried on an idempotent retry");
        workerRepo.Verify(w => w.Update(It.IsAny<WorkerRecord>()),
            Times.Never,
            "WorkerRecord must not be updated on an idempotent retry (slot already released)");
    }

    [Fact]
    public async Task HandleAsync_TwoCallsTotal_SlotReleaseCountIsOne()
    {
        // Arrange — first call: run is RUNNING.
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var clock  = new FakeClock(FixedNow);

        int workerUpdateCount = 0;

        // We drive both calls through separate handler instances that share
        // the same "database state" via closure-tracked run state.
        // Call 1: non-idempotent → releases slot.
        var handler1 = MakeHandler(
            out _, out _, out var workerRepo1, out _, out _,
            runToReturn:    run,
            workerToReturn: worker,
            clock:          clock);
        workerRepo1.Setup(w => w.Update(It.IsAny<WorkerRecord>()))
                   .Callback<WorkerRecord>(_ => workerUpdateCount++);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        await handler1.HandleAsync(cmd);

        // After first call, the run is now terminal (SUCCEEDED in memory).
        // Call 2: idempotent → slot must NOT be released again.
        var handler2 = MakeHandler(
            out _, out _, out var workerRepo2, out _, out _,
            runToReturn: run,   // same run object — it is now SUCCEEDED
            clock:       clock);
        workerRepo2.Setup(w => w.Update(It.IsAny<WorkerRecord>()))
                   .Callback<WorkerRecord>(_ => workerUpdateCount++);

        await handler2.HandleAsync(cmd);

        // Assert
        workerUpdateCount.Should().Be(1,
            because: "across two calls the worker slot must be released exactly once");
    }

    // ── 7. CANCELLED commit → run transitions to CANCELLED, slot released ────────

    [Fact]
    public async Task HandleAsync_CancelledCommit_RunTransitionsToCancelled()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        RunRecord? captured = null;
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Cancelled,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid CANCELLED commit must succeed");
        result.Value.TerminalState.Should().Be("CANCELLED");
        captured.Should().NotBeNull(because: "Run.Update must be called");
        captured!.State.Should().Be(RunState.Cancelled,
            because: "the RunRecord must transition to CANCELLED");
        captured.IsTerminal().Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_CancelledCommit_WorkerSlotReleased()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out _, out var workerRepo, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        WorkerRecord? capturedWorker = null;
        workerRepo.Setup(w => w.Update(It.IsAny<WorkerRecord>()))
                  .Callback<WorkerRecord>(w => capturedWorker = w);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Cancelled,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SlotReleased.Should().BeTrue(
            because: "the worker slot must be released when a run is cancelled");
        capturedWorker!.State.Should().Be(WorkerState.Available,
            because: "the worker state must be AVAILABLE after slot release on cancel");
    }

    // ── 9. UNKNOWN with reason → run marked UNKNOWN + NeedsAttention ──────────

    [Fact]
    public async Task HandleAsync_UnknownWithReason_RunMarkedWithNeedsAttention()
    {
        // Arrange
        var run = RunningRun(out var attemptId);
        // Run starts as RUNNING; for UNKNOWN transition we use MarkUnknown which doesn't
        // require RUNNING state validation. We need to bypass the strict Succeed/Fail path.
        // MarkUnknown in RunRecord transitions from any state.
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _, out _,
            runToReturn: run);

        RunRecord? captured = null;
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()))
               .Callback<RunRecord>(r => captured = r);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Unknown,
            UnknownReason: "Worker vanished mid-run without reporting.",
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Unknown);
        captured.NeedsAttention.Should().BeTrue(
            because: "UNKNOWN state must always set NeedsAttention=true (invariant 2)");
        captured.UnknownReason.Should().Be("Worker vanished mid-run without reporting.");
    }

    [Fact]
    public async Task HandleAsync_UnknownWithReason_FinalResultHasNeedsAttentionTrue()
    {
        // Arrange
        var run = RunningRun(out var attemptId);
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _, out _,
            runToReturn: run);

        FinalResultRecord? capturedFinal = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((f, _) => capturedFinal = f)
            .Returns(Task.CompletedTask);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Unknown,
            UnknownReason: "No heartbeat after 60 s.",
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedFinal.Should().NotBeNull();
        capturedFinal!.NeedsAttention.Should().BeTrue(
            because: "FinalResultRecord for UNKNOWN must carry NeedsAttention=true (domain invariant 2)");
        capturedFinal.UnknownReason.Should().Be("No heartbeat after 60 s.");
    }

    // ── 10. Run not found → 404 ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange — run repo returns null (run does not exist).
        var handler = MakeHandler(
            out _, out _, out _, out _, out _,
            runToReturn: null);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound",
            because: "an unknown run must produce a not-found error");
    }

    // ── 11. Non-terminal TerminalState → validation error ────────────────────

    [Theory]
    [InlineData(RunState.Created)]
    [InlineData(RunState.Queued)]
    [InlineData(RunState.Leased)]
    [InlineData(RunState.Running)]
    public async Task HandleAsync_NonTerminalTargetState_ReturnsValidationError(RunState badState)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _, out _);

        var cmd = new CommitFinalResultCommand(
            RunId:         Guid.NewGuid(),
            AttemptId:     Guid.NewGuid(),
            TerminalState: badState,
            EvidenceRefs:  []);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: $"state '{badState}' is not a terminal state and must be rejected");
        result.Error.Code.Should().Be("Validation");
    }

    // ── 12. BusinessJobId and EvidenceRefs round-trip ────────────────────────

    [Fact]
    public async Task HandleAsync_BusinessJobIdAndEvidenceRefs_StoredOnFinalResultRecord()
    {
        // Arrange
        var run    = RunningRun(out var attemptId);
        var worker = RunningWorker();
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker);

        FinalResultRecord? capturedFinal = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((f, _) => capturedFinal = f)
            .Returns(Task.CompletedTask);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            BusinessJobId: "biz-job-oc06",
            EvidenceRefs:  ["evidence://log/a", "evidence://screenshot/b"]);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedFinal.Should().NotBeNull();
        capturedFinal!.BusinessJobId.Should().Be("biz-job-oc06");
        capturedFinal.EvidenceRefs.Should().BeEquivalentTo(
            ["evidence://log/a", "evidence://screenshot/b"],
            because: "EvidenceRefs must be stored verbatim");
    }

    // ── 13. FakeClock timestamp propagated ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_CompletedAt_UsesClockUtcNow()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2026, 10, 24, 16, 30, 0, TimeSpan.Zero);
        var clock        = new FakeClock(expectedTime);
        var run          = RunningRun(out var attemptId);
        var worker       = RunningWorker();
        var handler = MakeHandler(
            out _, out var finalResultRepo, out _, out _, out _,
            runToReturn:    run,
            workerToReturn: worker,
            clock:          clock);

        FinalResultRecord? capturedFinal = null;
        finalResultRepo
            .Setup(r => r.AddAsync(It.IsAny<FinalResultRecord>(), It.IsAny<CancellationToken>()))
            .Callback<FinalResultRecord, CancellationToken>((f, _) => capturedFinal = f)
            .Returns(Task.CompletedTask);

        var cmd = new CommitFinalResultCommand(
            RunId:         run.Id,
            AttemptId:     attemptId,
            TerminalState: RunState.Succeeded,
            EvidenceRefs:  []);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedFinal.Should().NotBeNull();
        capturedFinal!.CompletedAt.Should().Be(expectedTime,
            because: "CompletedAt must reflect the FakeClock's UtcNow (no Thread.Sleep)");
    }
}
