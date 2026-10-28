#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="SendHeartbeatCommandHandler"/> — story OC-05.
///
/// Each test exercises exactly one observable behaviour (acceptance criterion).
/// FakeClock controls time; Moq stubs all external dependencies.
/// Domain objects (LeaseRecord) are real instances so the domain invariants are exercised.
/// </summary>
public sealed class SendHeartbeatCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private const string ValidWorkerId   = "worker-hb-01";
    private const string ValidArtifactId = "art-hb";
    private const string ValidVersion    = "2.0.0";
    private const string ValidHash       = "sha256:aabbccdd";

    // ── Domain-object helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="LeaseRecord"/> in the <see cref="LeaseState.Accepted"/> state
    /// with an initial LastSeqNo of 0 and no cancellation flag.
    /// </summary>
    private static LeaseRecord AcceptedLease(
        Guid   runId,
        Guid   attemptId,
        string workerId               = ValidWorkerId,
        bool   cancellationRequested  = false,
        long   lastSeqNo              = 0)
    {
        var lease = new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           runId,
            attemptId:       attemptId,
            workerId:        workerId,
            artifactId:      ValidArtifactId,
            artifactVersion: ValidVersion,
            artifactHash:    ValidHash,
            offeredAt:       FixedNow.AddMinutes(-2),
            expiresAt:       FixedNow.AddMinutes(28));

        // Transition: Offered → Accepted.
        lease.Accept(FixedNow.AddMinutes(-1));

        // Advance seqNo to match the requested baseline.
        if (lastSeqNo > 0)
            lease.RecordHeartbeat(FixedNow.AddSeconds(-30), lastSeqNo);

        if (cancellationRequested)
            lease.RequestCancellation();

        return lease;
    }

    /// <summary>
    /// Creates an <see cref="LeaseState.Offered"/> (not yet accepted) lease.
    /// </summary>
    private static LeaseRecord OfferedLease(Guid runId, Guid attemptId)
        => new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           runId,
            attemptId:       attemptId,
            workerId:        ValidWorkerId,
            artifactId:      ValidArtifactId,
            artifactVersion: ValidVersion,
            artifactHash:    ValidHash,
            offeredAt:       FixedNow.AddSeconds(-10),
            expiresAt:       FixedNow.AddSeconds(20));

    // ── Handler factory ───────────────────────────────────────────────────────

    private static SendHeartbeatCommandHandler MakeHandler(
        out Mock<ILeaseRepository> leaseRepo,
        out Mock<IUnitOfWork>      unitOfWork,
        FakeClock?                 clock = null)
    {
        leaseRepo  = new Mock<ILeaseRepository>(MockBehavior.Strict);
        unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);

        return new SendHeartbeatCommandHandler(
            leaseRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            NullLogger<SendHeartbeatCommandHandler>.Instance);
    }

    // ── AC 1: Valid heartbeat — LastHeartbeatAt updated, result returned ──────

    [Fact]
    public async Task HandleAsync_ValidHeartbeat_ReturnsSuccess()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid heartbeat must succeed");
    }

    [Fact]
    public async Task HandleAsync_ValidHeartbeat_LastHeartbeatAtUpdatedToClockNow()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var clock     = new FakeClock(FixedNow);
        var lease     = AcceptedLease(runId, attemptId);

        var handler = MakeHandler(out var leaseRepo, out _, clock: clock);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        await handler.HandleAsync(command);

        // Assert
        lease.LastHeartbeatAt.Should().Be(FixedNow,
            because: "LastHeartbeatAt must be stamped with the current clock time on a valid heartbeat");
    }

    [Fact]
    public async Task HandleAsync_ValidHeartbeat_LastSeqNoUpdated()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, lastSeqNo: 5);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 6);

        // Act
        await handler.HandleAsync(command);

        // Assert
        lease.LastSeqNo.Should().Be(6,
            because: "LastSeqNo must be advanced to the SeqNo from a valid heartbeat");
    }

    [Fact]
    public async Task HandleAsync_ValidHeartbeat_CommitsUnitOfWork()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId);

        var handler = MakeHandler(out var leaseRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "a valid heartbeat must commit atomically (domain invariant 5)");
    }

    // ── AC 2: Stale SeqNo — rejected, LastHeartbeatAt NOT updated ────────────

    [Fact]
    public async Task HandleAsync_StaleSeqNo_ReturnsStaleSeqNoError()
    {
        // Arrange — lease has LastSeqNo=10; incoming seqNo=5 is stale.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, lastSeqNo: 10);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 5);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a stale SeqNo must be rejected");
        result.Error.Code.Should().Be("STALE_SEQ_NO",
            because: "the error code for a monotonicity violation is STALE_SEQ_NO per OC-05");
    }

    [Fact]
    public async Task HandleAsync_StaleSeqNo_LastHeartbeatAtNotUpdated()
    {
        // Arrange — lease has a known last heartbeat timestamp.
        var runId              = Guid.NewGuid();
        var attemptId          = Guid.NewGuid();
        var expectedHeartbeat  = FixedNow.AddSeconds(-30);
        var lease              = AcceptedLease(runId, attemptId, lastSeqNo: 10);
        // Force a specific last heartbeat time by directly verifying the value was set during setup.
        var lastHeartbeatBefore = lease.LastHeartbeatAt;

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 5);

        // Act
        await handler.HandleAsync(command);

        // Assert — LastHeartbeatAt must remain exactly as it was before the stale heartbeat arrived.
        lease.LastHeartbeatAt.Should().Be(lastHeartbeatBefore,
            because: "a stale SeqNo must NOT update LastHeartbeatAt — spec OC-05 is explicit");
    }

    [Fact]
    public async Task HandleAsync_SeqNoEqualToLastSeqNo_ReturnsIdempotent200()
    {
        // Arrange — equal seqNo is an exact duplicate; spec invariant 3 requires idempotent 200.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, lastSeqNo: 7);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 7);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — exact duplicate must succeed idempotently, not be rejected.
        result.IsSuccess.Should().BeTrue(because: "an exact duplicate SeqNo must return idempotent 200 per spec invariant 3");
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_StaleSeqNo_NoCommitIssued()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, lastSeqNo: 10);

        var handler = MakeHandler(out var leaseRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 3);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a stale heartbeat must not commit — nothing should be written");
    }

    // ── AC 3: Expired lease → 409 LEASE_EXPIRED ──────────────────────────────

    [Fact]
    public async Task HandleAsync_ExpiredLease_ReturnsLeaseExpiredError()
    {
        // Arrange — the LeaseExpiryMonitorService already expired this lease.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var expiredLease = AcceptedLease(runId, attemptId);
        expiredLease.ExpireAccepted();

        var handler = MakeHandler(out var leaseRepo, out _);

        // FindActiveLeaseForRunAsync returns null because state=Expired.
        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        // FindByRunIdAsync returns the expired lease.
        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expiredLease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an expired lease must be rejected");
        result.Error.Code.Should().Be("LEASE_EXPIRED",
            because: "a heartbeat on an expired lease must return LEASE_EXPIRED (OC-05)");
    }

    [Fact]
    public async Task HandleAsync_ExpiredLease_NoCommitIssued()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var expiredLease = AcceptedLease(runId, attemptId);
        expiredLease.ExpireAccepted();

        var handler = MakeHandler(out var leaseRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expiredLease);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no writes must be committed for a rejected expired-lease heartbeat");
    }

    // ── AC 4: CancellationRequested=true delivered when set on lease ──────────

    [Fact]
    public async Task HandleAsync_CancellationRequestedTrue_ReturnedInHeartbeatResult()
    {
        // Arrange — orchestrator has flagged cancellation on this lease.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, cancellationRequested: true);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a cancellation-flagged heartbeat must still be accepted");
        result.Value.CancellationRequested.Should().BeTrue(
            because: "the handler must forward the CancellationRequested flag to the worker (OC-05)");
    }

    [Fact]
    public async Task HandleAsync_CancellationRequestedFalse_ReturnedAsFalseInHeartbeatResult()
    {
        // Arrange — no cancellation requested (normal case).
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, cancellationRequested: false);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);
        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.Value.CancellationRequested.Should().BeFalse(
            because: "when no cancellation is requested the flag must be false");
    }

    // ── AC 5: AttemptId mismatch → 409 WRONG_ATTEMPT ─────────────────────────

    [Fact]
    public async Task HandleAsync_WrongAttemptId_ReturnsWrongAttemptError()
    {
        // Arrange
        var runId            = Guid.NewGuid();
        var correctAttemptId = Guid.NewGuid();
        var wrongAttemptId   = Guid.NewGuid();
        var lease            = AcceptedLease(runId, correctAttemptId);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, wrongAttemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an AttemptId mismatch must be rejected");
        result.Error.Code.Should().Be("WRONG_ATTEMPT",
            because: "stale or foreign AttemptId → 409 (domain invariant 3)");
    }

    [Fact]
    public async Task HandleAsync_WrongAttemptId_NoCommitIssued()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var lease     = AcceptedLease(runId, Guid.NewGuid());

        var handler = MakeHandler(out var leaseRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, Guid.NewGuid(), ValidWorkerId, SeqNo: 1);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── AC 6: No lease at all → 404 NotFound ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_NoLeaseFound_ReturnsNotFound()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);
        leaseRepo.Setup(r => r.FindByRunIdAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((LeaseRecord?)null);

        var command = new SendHeartbeatCommand(runId, attemptId, ValidWorkerId, SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound",
            because: "when no lease exists for the run the response is 404 NotFound");
    }

    // ── AC 6b: WorkerId mismatch → 409 WRONG_WORKER ──────────────────────────

    [Fact]
    public async Task HandleAsync_WrongWorkerId_ReturnsWrongWorkerError()
    {
        // Arrange — lease belongs to ValidWorkerId; heartbeat comes from a different worker.
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, workerId: ValidWorkerId);

        var handler = MakeHandler(out var leaseRepo, out _);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, "rogue-worker-99", SeqNo: 1);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a WorkerId mismatch must be rejected");
        result.Error.Code.Should().Be("WRONG_WORKER",
            because: "a rogue worker's heartbeat must return WRONG_WORKER (MAJOR fix)");
    }

    [Fact]
    public async Task HandleAsync_WrongWorkerId_NoCommitIssued()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var lease     = AcceptedLease(runId, attemptId, workerId: ValidWorkerId);

        var handler = MakeHandler(out var leaseRepo, out var unitOfWork);

        leaseRepo.Setup(r => r.FindActiveLeaseForRunAsync(runId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(lease);

        var command = new SendHeartbeatCommand(runId, attemptId, "rogue-worker-99", SeqNo: 1);

        // Act
        await handler.HandleAsync(command);

        // Assert
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a WorkerId-rejected heartbeat must not commit anything");
    }

    // ── AC 7: No PAUSED state in RunState enum ────────────────────────────────

    [Fact]
    public void RunState_DoesNotContainPausedState()
    {
        // Arrange / Act
        var stateNames = Enum.GetNames<Tracto.Orchestrator.Domain.Runs.RunState>();

        // Assert
        stateNames.Should().NotContain("Paused",
            because: "OC-05 spec explicitly forbids introducing a PAUSED state — runs stay RUNNING during screen-lock wait");
        stateNames.Should().NotContainMatch("*pause*",
            because: "no variant of PAUSED is permitted in the RunState machine");
    }
}
