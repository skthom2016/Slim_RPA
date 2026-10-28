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
using AppClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Unit.Tests.Leases;

/// <summary>
/// Unit tests for <see cref="AcceptLeaseCommandHandler"/> — story OC-04.
///
/// Each test covers exactly one observable behaviour (AC).
/// FakeClock controls time; Moq stubs all external dependencies.
/// Domain objects (RunRecord, LeaseRecord) are real instances so state transitions
/// are exercised against the actual domain logic.
/// </summary>
public sealed class AcceptLeaseCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private const string ValidWorkerId   = "worker-abc";
    private const string ValidArtifactId = "art-001";
    private const string ValidVersion    = "1.0.0";
    private const string ValidHash       = "sha256:deadbeef";

    // ── Domain-object helpers ─────────────────────────────────────────────────

    /// <summary>Creates a <see cref="RunRecord"/> that has been enqueued (QUEUED state).</summary>
    private static RunRecord QueuedRun(Guid? runId = null)
    {
        var id  = runId ?? Guid.NewGuid();
        var run = new RunRecord(
            id:                  id,
            workflowId:          "wf-1",
            createdAt:           FixedNow.AddMinutes(-10),
            correlationId:       Guid.NewGuid(),
            scriptArtifactId:    ValidArtifactId,
            scriptVersion:       ValidVersion,
            schemaVersion:       "1",
            inputRefs:           ["input://blob/a"],
            secretRefs:          ["ref:vault/key-1"],
            deadline:            null,
            eventId:             null,
            triggerType:         TriggerType.Manual,
            businessJobId:       null);

        run.Enqueue(FixedNow.AddMinutes(-9));
        return run;
    }

    /// <summary>Creates a <see cref="RunRecord"/> in LEASED state (for the requeue path).</summary>
    private static RunRecord LeasedRun(Guid leaseAttemptId, Guid? runId = null)
    {
        var run = QueuedRun(runId);
        run.AdoptLease(leaseAttemptId, ValidWorkerId, FixedNow.AddMinutes(-5));
        return run;
    }

    /// <summary>Creates an Offered <see cref="LeaseRecord"/> that is still valid (not expired).</summary>
    private static LeaseRecord OfferedLease(Guid runId, Guid attemptId, string workerId = ValidWorkerId)
        => new LeaseRecord(
            leaseId:        Guid.NewGuid(),
            runId:          runId,
            attemptId:      attemptId,
            workerId:       workerId,
            artifactId:     ValidArtifactId,
            artifactVersion: ValidVersion,
            artifactHash:   ValidHash,
            offeredAt:      FixedNow.AddMinutes(-1),
            expiresAt:      FixedNow.AddSeconds(30));   // expires in the future

    /// <summary>Creates an Offered <see cref="LeaseRecord"/> whose ExpiresAt is in the past.</summary>
    private static LeaseRecord ExpiredLease(Guid runId, Guid attemptId, string workerId = ValidWorkerId)
        => new LeaseRecord(
            leaseId:        Guid.NewGuid(),
            runId:          runId,
            attemptId:      attemptId,
            workerId:       workerId,
            artifactId:     ValidArtifactId,
            artifactVersion: ValidVersion,
            artifactHash:   ValidHash,
            offeredAt:      FixedNow.AddMinutes(-2),
            expiresAt:      FixedNow.AddSeconds(-10));  // expired 10 s ago

    // ── Handler factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a fully wired handler with Moq-backed dependencies.
    /// The caller overrides individual mock setups per test.
    /// </summary>
    private static AcceptLeaseCommandHandler MakeHandler(
        out Mock<IRunRepository>     runRepo,
        out Mock<ILeaseRepository>   leaseRepo,
        out Mock<IJournalRepository> journalRepo,
        out Mock<IUnitOfWork>        unitOfWork,
        FakeClock?                   clock = null)
    {
        runRepo     = new Mock<IRunRepository>(MockBehavior.Strict);
        leaseRepo   = new Mock<ILeaseRepository>(MockBehavior.Strict);
        journalRepo = new Mock<IJournalRepository>(MockBehavior.Strict);
        unitOfWork  = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // Unit-of-work lifecycle — common to all happy paths.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new AcceptLeaseCommandHandler(
            runRepo.Object,
            leaseRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            telemetry.Object,
            NullLogger<AcceptLeaseCommandHandler>.Instance);
    }

    // ── AC 1: Valid accept → run RUNNING, AcceptedAt set ─────────────────────

    [Fact]
    public async Task HandleAsync_ValidAccept_RunTransitionsToRunning()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = OfferedLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid lease accept must succeed");
        run.State.Should().Be(RunState.Running,
            because: "accepting a lease must transition the run from QUEUED to RUNNING");
        run.WorkerId.Should().Be(ValidWorkerId,
            because: "the accepting worker must be recorded on the run");
    }

    [Fact]
    public async Task HandleAsync_ValidAccept_LeaseAcceptedAtIsSet()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = OfferedLease(runId, attemptId);
        var clock     = new FakeClock(FixedNow);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out _, clock: clock);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert
        lease.State.Should().Be(LeaseState.Accepted,
            because: "accepting the lease must transition LeaseRecord.State to Accepted");
        lease.AcceptedAt.Should().Be(FixedNow,
            because: "AcceptedAt must be stamped with the clock time at the moment of acceptance");
    }

    [Fact]
    public async Task HandleAsync_ValidAccept_RUN_RUNNING_JournalEntryWritten()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = OfferedLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert — RUN_RUNNING journal entry must be appended exactly once (invariant 5).
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_RUNNING" && e.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_RUNNING journal entry must be written atomically with the state transition");
    }

    [Fact]
    public async Task HandleAsync_ValidAccept_CommitsUnitOfWork()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = OfferedLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert — all writes must be committed in one atomic transaction (invariant 5).
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "CommitAsync must be called exactly once to satisfy atomicity invariant 5");
    }

    // ── AC 2: Wrong WorkerId → 409 WRONG_WORKER ──────────────────────────────

    [Fact]
    public async Task HandleAsync_WrongWorkerId_ReturnsWrongWorkerError()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = OfferedLease(runId, attemptId, workerId: "correct-worker");

        var handler = MakeHandler(out _, out var leaseRepo, out _, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new AcceptLeaseCommand(runId, attemptId, WorkerId: "wrong-worker");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a WorkerId mismatch must be rejected");
        result.Error.Code.Should().Be("WRONG_WORKER",
            because: "the error code for a worker mismatch is WRONG_WORKER per invariant 3");
    }

    [Fact]
    public async Task HandleAsync_WrongWorkerId_NoStateChangesMade()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = OfferedLease(runId, attemptId, workerId: "correct-worker");

        var handler = MakeHandler(out var runRepo, out var leaseRepo, out _, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new AcceptLeaseCommand(runId, attemptId, WorkerId: "wrong-worker");

        // Act
        await handler.HandleAsync(command);

        // Assert — no run must be loaded or committed.
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "GetByIdAsync must not be called before the WorkerId check passes");
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never, "CommitAsync must not be called on a rejected request");
    }

    // ── AC 3: Wrong AttemptId → 409 WRONG_ATTEMPT ────────────────────────────

    [Fact]
    public async Task HandleAsync_WrongAttemptId_ReturnsWrongAttemptError()
    {
        // Arrange
        var runId            = Guid.NewGuid();
        var correctAttemptId = Guid.NewGuid();
        var wrongAttemptId   = Guid.NewGuid();

        var lease = OfferedLease(runId, correctAttemptId);

        var handler = MakeHandler(out _, out var leaseRepo, out _, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new AcceptLeaseCommand(runId, AttemptId: wrongAttemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an AttemptId mismatch must be rejected");
        result.Error.Code.Should().Be("WRONG_ATTEMPT",
            because: "the error code for an AttemptId fence violation is WRONG_ATTEMPT per invariant 3");
    }

    [Fact]
    public async Task HandleAsync_WrongAttemptId_NoCommit()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var lease     = OfferedLease(runId, attemptId: Guid.NewGuid());

        var handler = MakeHandler(out _, out var leaseRepo, out _, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new AcceptLeaseCommand(runId, AttemptId: Guid.NewGuid(), ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never, "CommitAsync must not be called when AttemptId fencing rejects the request");
    }

    // ── AC 4a: Late accept (expired), no prior RUNNING evidence → UNKNOWN ─────

    [Fact]
    public async Task HandleAsync_ExpiredLease_NoRunningEntries_RunMarkedUnknown()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);                   // QUEUED — no prior start
        var lease     = ExpiredLease(runId, attemptId);     // ExpiresAt < FixedNow

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);                      // returns Offered (not background-expired)
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        journalRepo.Setup(j => j.HasEntriesOfTypeAsync(runId, "RUN_RUNNING", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);                    // no confirmed start
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an expired lease must be rejected");
        result.Error.Code.Should().Be("LEASE_EXPIRED",
            because: "the error code for a late accept is LEASE_EXPIRED");

        run.State.Should().Be(RunState.Unknown,
            because: "when start is uncertain the run must be marked UNKNOWN (invariant 2)");
        run.NeedsAttention.Should().BeTrue(
            because: "UNKNOWN state always implies NeedsAttention=true (invariant 2)");
        run.UnknownReason.Should().NotBeNullOrEmpty(
            because: "UNKNOWN state requires a non-null UnknownReason (invariant 2)");
    }

    [Fact]
    public async Task HandleAsync_ExpiredLease_NoRunningEntries_CommitsAfterMarkingUnknown()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = ExpiredLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        journalRepo.Setup(j => j.HasEntriesOfTypeAsync(runId, "RUN_RUNNING", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert — the UNKNOWN mutation must be persisted atomically (invariant 5).
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the UNKNOWN state update must be committed to persist the attentional flag");
    }

    // ── AC 4b: Late accept (expired), RUN_RUNNING exists → requeue run ────────

    [Fact]
    public async Task HandleAsync_ExpiredLease_RunningEntryExists_RunRequeuedAndErrorReturned()
    {
        // Arrange — run must be LEASED for Requeue() to succeed.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = LeasedRun(attemptId, runId);        // QUEUED→LEASED via AdoptLease
        var lease     = ExpiredLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        journalRepo.Setup(j => j.HasEntriesOfTypeAsync(runId, "RUN_RUNNING", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);                     // start was previously confirmed
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an expired lease must still be rejected");
        result.Error.Code.Should().Be("LEASE_EXPIRED");

        run.State.Should().Be(RunState.Queued,
            because: "when the prior start was confirmed the run must be re-queued for retry");
    }

    // ── AC 5: Idempotent — same WorkerId + AttemptId already accepted → 200 ──

    [Fact]
    public async Task HandleAsync_SameWorkerAndAttemptAlreadyAccepted_ReturnsIdempotent200()
    {
        // Arrange — lease is already in Accepted state from a prior AcceptLease call.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var lease = OfferedLease(runId, attemptId);
        lease.Accept(FixedNow.AddSeconds(-5));   // simulate previous accept

        // Run must be RUNNING (state after successful acceptance).
        var run = QueuedRun(runId);
        run.AdoptLease(attemptId, ValidWorkerId, FixedNow.AddSeconds(-5));
        run.Start(attemptId, FixedNow.AddSeconds(-5));

        var handler = MakeHandler(out var runRepo, out var leaseRepo, out _, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);   // Accepted lease is non-Expired, so it is returned here
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a repeated accept with the same ids is idempotent");
    }

    [Fact]
    public async Task HandleAsync_IdempotentAccept_NoCommitIssued()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var lease = OfferedLease(runId, attemptId);
        lease.Accept(FixedNow.AddSeconds(-5));

        var run = QueuedRun(runId);
        run.AdoptLease(attemptId, ValidWorkerId, FixedNow.AddSeconds(-5));
        run.Start(attemptId, FixedNow.AddSeconds(-5));

        var handler = MakeHandler(out var runRepo, out var leaseRepo, out _, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        await handler.HandleAsync(command);

        // Assert — an idempotent response must not write anything.
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "CommitAsync must not be called on an idempotent repeat of the same accept");
    }

    [Fact]
    public async Task HandleAsync_IdempotentAccept_ReturnsSameLeasePayloadFields()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var lease = OfferedLease(runId, attemptId);
        lease.Accept(FixedNow.AddSeconds(-5));

        var run = QueuedRun(runId);
        run.AdoptLease(attemptId, ValidWorkerId, FixedNow.AddSeconds(-5));
        run.Start(attemptId, FixedNow.AddSeconds(-5));

        var handler = MakeHandler(out var runRepo, out var leaseRepo, out _, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — the idempotent response must echo the same identifiers.
        result.Value.RunId.Should().Be(runId);
        result.Value.AttemptId.Should().Be(attemptId);
        result.Value.ArtifactId.Should().Be(ValidArtifactId);
    }

    // ── AC 6: Lease payload must not contain plaintext secret values ──────────

    [Fact]
    public async Task HandleAsync_SecretRefsAreOpaqueRefs_PayloadContainsNoPlaintextSecrets()
    {
        // Arrange — SecretRefs are all opaque "ref:" references (invariant 4).
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        // Build a run whose SecretRefs start with "ref:" so they pass the invariant check.
        var run = new RunRecord(
            id:               runId,
            workflowId:       "wf-2",
            createdAt:        FixedNow.AddMinutes(-10),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ValidArtifactId,
            scriptVersion:    ValidVersion,
            schemaVersion:    "1",
            inputRefs:        ["input://blob/b"],
            secretRefs:       ["ref:vault/db-password", "ref:vault/api-key"],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        run.Enqueue(FixedNow.AddMinutes(-9));

        var lease = OfferedLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "opaque secret refs must be accepted");

        var payload = result.Value;
        payload.SecretRefs.Should().AllSatisfy(
            r => r.Should().StartWith("ref:",
                because: "domain invariant 4: every SecretRef must be an opaque reference, never a resolved value"));

        payload.HasResolvedSecretValues().Should().BeFalse(
            because: "the payload must not expose any plaintext secret (invariant 4)");
    }

    [Fact]
    public void LeasePayload_Create_WithPlaintextSecret_ThrowsInvalidOperationException()
    {
        // Arrange — a plaintext secret (no "ref:" prefix) must be rejected by the factory.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        // Act — LeasePayload.Create enforces invariant 4 via AssertNoResolvedSecrets().
        var act = () => LeasePayload.Create(
            runId:           runId,
            attemptId:       attemptId,
            artifactId:      ValidArtifactId,
            artifactVersion: ValidVersion,
            artifactHash:    ValidHash,
            inputRefs:       [],
            secretRefs:      ["super-secret-password"]);   // no "ref:" prefix → violation

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "domain invariant 4 prohibits plaintext secret values in LeasePayload");
    }

    [Fact]
    public async Task HandleAsync_ValidAccept_LeasePayloadContainsRequiredFields()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var run       = QueuedRun(runId);
        var lease     = OfferedLease(runId, attemptId);

        var handler = MakeHandler(out var runRepo, out var leaseRepo,
                                  out var journalRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — payload must include all required fields per OC-04 spec.
        result.IsSuccess.Should().BeTrue();
        var payload = result.Value;

        payload.RunId.Should().Be(runId,
            because: "the payload must echo the RunId");
        payload.AttemptId.Should().Be(attemptId,
            because: "the payload must echo the AttemptId for fence checks by subsequent stage calls");
        payload.ArtifactId.Should().Be(ValidArtifactId,
            because: "the payload must include the artifact identifier");
        payload.ArtifactVersion.Should().Be(ValidVersion,
            because: "the payload must include the pinned artifact version");
        payload.ArtifactHash.Should().Be(ValidHash,
            because: "the payload must include the content hash for integrity verification (invariant 7)");
        payload.InputRefs.Should().NotBeNull(
            because: "InputRefs must always be present, even if empty");
        payload.SecretRefs.Should().NotBeNull(
            because: "SecretRefs must always be present, even if empty");
    }

    // ── AC 7: No active or historical lease → 404 NotFound ───────────────────

    [Fact]
    public async Task HandleAsync_NoLeaseEverIssued_ReturnsNotFound()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var handler = MakeHandler(out _, out var leaseRepo, out _, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        // FindByRunIdAsync also returns null → run never had a lease.
        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "no lease means the run does not exist or was never offered");
        result.Error.Code.Should().Be("NotFound",
            because: "when no lease record exists for the run the response is 404 NotFound");
    }

    // ── AC 8: Background-expired lease (state=Expired) → 409 LEASE_EXPIRED ───

    [Fact]
    public async Task HandleAsync_BackgroundExpiredLease_ReturnsLeaseExpiredError()
    {
        // Arrange — the background expiry job already set the lease to Expired before
        // the worker's AcceptLease arrived. FindActiveLeaseForRunAsync returns null;
        // FindByRunIdAsync returns the Expired lease.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var backgroundExpiredLease = OfferedLease(runId, attemptId);
        backgroundExpiredLease.Expire();   // background job called Expire()

        var handler = MakeHandler(out _, out var leaseRepo, out _, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);           // not returned by active query

        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(backgroundExpiredLease);       // found by any-state query

        var command = new AcceptLeaseCommand(runId, attemptId, ValidWorkerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LEASE_EXPIRED",
            because: "a lease that was already expired by the background job must return LEASE_EXPIRED");
    }
}
