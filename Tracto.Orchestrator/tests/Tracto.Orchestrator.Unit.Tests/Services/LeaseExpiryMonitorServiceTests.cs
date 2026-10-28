#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LeaseExpiryMonitorService"/> — story OC-05.
///
/// Uses <c>ScanOnceAsync</c> (the internal scan method) rather than starting the real
/// background loop, so tests are deterministic and complete without Thread.Sleep.
/// FakeClock drives time; Moq stubs all repository and unit-of-work interactions.
/// </summary>
public sealed class LeaseExpiryMonitorServiceTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 20, 16, 0, 0, TimeSpan.Zero);

    // ── Domain object helpers ─────────────────────────────────────────────────

    private static LeaseRecord BuildAcceptedLease(
        Guid?          runId    = null,
        DateTimeOffset? lastHb  = null)
    {
        var lease = new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           runId ?? Guid.NewGuid(),
            attemptId:       Guid.NewGuid(),
            workerId:        "worker-monitor",
            artifactId:      "art-mon",
            artifactVersion: "1.0",
            artifactHash:    "sha256:deadbeef",
            offeredAt:       FixedNow.AddMinutes(-5),
            expiresAt:       FixedNow.AddMinutes(25));

        lease.Accept(FixedNow.AddMinutes(-4));

        if (lastHb.HasValue)
            lease.RecordHeartbeat(lastHb.Value, seqNo: 1);

        return lease;
    }

    // ── Service factory ───────────────────────────────────────────────────────

    private static LeaseExpiryMonitorService MakeService(
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

        return new LeaseExpiryMonitorService(
            leaseRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            NullLogger<LeaseExpiryMonitorService>.Instance);
    }

    // ── Scan: no stale leases — no writes ────────────────────────────────────

    [Fact]
    public async Task ScanOnceAsync_NoExpiredHeartbeats_NoLeaseUpdated()
    {
        // Arrange — repository returns an empty list.
        var service = MakeService(out var leaseRepo, out var unitOfWork);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaseRecord>());

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — nothing should be committed when there are no stale leases.
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no writes must occur when there are no leases with expired heartbeats");
    }

    // ── Scan: one stale lease — marked Expired ────────────────────────────────

    [Fact]
    public async Task ScanOnceAsync_OneStaleLease_MarksLeaseExpired()
    {
        // Arrange — one accepted lease with heartbeat 90 s ago (> 60 s expiry window).
        var staleHeartbeat = FixedNow.AddSeconds(-90);
        var staleLease     = BuildAcceptedLease(lastHb: staleHeartbeat);

        var service = MakeService(out var leaseRepo, out _);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { staleLease });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — the lease must be in Expired state after the scan.
        staleLease.State.Should().Be(LeaseState.Expired,
            because: "the monitor must call ExpireAccepted on leases with overdue heartbeats");
    }

    [Fact]
    public async Task ScanOnceAsync_OneStaleLease_CommitsOneTransaction()
    {
        // Arrange
        var staleLease = BuildAcceptedLease(lastHb: FixedNow.AddSeconds(-90));

        var service = MakeService(out var leaseRepo, out var unitOfWork);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { staleLease });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — each lease expiry is committed in its own transaction.
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "each expired lease must be committed in an atomic transaction");
    }

    // ── Scan: multiple stale leases — each expired independently ─────────────

    [Fact]
    public async Task ScanOnceAsync_MultipleStaleLeases_AllMarkedExpired()
    {
        // Arrange — three leases, all with heartbeats older than the expiry window.
        var staleTime  = FixedNow.AddSeconds(-120);
        var lease1     = BuildAcceptedLease(lastHb: staleTime);
        var lease2     = BuildAcceptedLease(lastHb: staleTime);
        var lease3     = BuildAcceptedLease(lastHb: staleTime);

        var service = MakeService(out var leaseRepo, out _);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lease1, lease2, lease3 });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — all three must be expired.
        lease1.State.Should().Be(LeaseState.Expired);
        lease2.State.Should().Be(LeaseState.Expired);
        lease3.State.Should().Be(LeaseState.Expired);
    }

    [Fact]
    public async Task ScanOnceAsync_MultipleStaleLeases_EachCommittedSeparately()
    {
        // Arrange — two stale leases.
        var staleTime = FixedNow.AddSeconds(-120);
        var lease1    = BuildAcceptedLease(lastHb: staleTime);
        var lease2    = BuildAcceptedLease(lastHb: staleTime);

        var service = MakeService(out var leaseRepo, out var unitOfWork);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lease1, lease2 });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — two separate commits (one per lease).
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2),
            "each lease expiry must be committed in a separate atomic transaction");
    }

    // ── Scan: threshold passed to repository is now - ExpiryWindow ───────────

    [Fact]
    public async Task ScanOnceAsync_ThresholdPassedToRepository_IsNowMinusExpiryWindow()
    {
        // Arrange
        var clock = new FakeClock(FixedNow);

        var service = MakeService(out var leaseRepo, out _, clock: clock);

        DateTimeOffset capturedThreshold = default;

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, CancellationToken>((threshold, _) => capturedThreshold = threshold)
            .ReturnsAsync(Array.Empty<LeaseRecord>());

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert
        var expectedThreshold = FixedNow - LeaseExpiryMonitorService.ExpiryWindow;
        capturedThreshold.Should().Be(expectedThreshold,
            because: "the repository must be queried with threshold = now - ExpiryWindow");
    }

    // ── Scan: one expired lease fails to commit — others still processed ──────

    [Fact]
    public async Task ScanOnceAsync_OneLeaseCommitFails_OtherLeasesStillProcessed()
    {
        // Arrange — two stale leases; committing the first throws; second should still succeed.
        var staleTime = FixedNow.AddSeconds(-120);
        var lease1    = BuildAcceptedLease(lastHb: staleTime);
        var lease2    = BuildAcceptedLease(lastHb: staleTime);

        var leaseRepo  = new Mock<ILeaseRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lease1, lease2 });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);

        var commitCallCount = 0;
        unitOfWork
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                commitCallCount++;
                if (commitCallCount == 1)
                    throw new Exception("Simulated DB fault on first commit.");
                return Task.CompletedTask;
            });

        var service = new LeaseExpiryMonitorService(
            leaseRepo.Object,
            unitOfWork.Object,
            new FakeClock(FixedNow),
            NullLogger<LeaseExpiryMonitorService>.Instance);

        // Act — must not throw even though the first lease's commit fails.
        var act = async () => await service.ScanOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            because: "a single lease-expiry failure must not abort processing of remaining leases");

        // Assert — second lease must still be expired.
        lease2.State.Should().Be(LeaseState.Expired,
            because: "a commit failure for one lease must not prevent expiring subsequent leases");
    }

    // ── Run stays RUNNING: no state change on RunRecord during scan ───────────

    [Fact]
    public async Task ScanOnceAsync_ExpiredLease_DoesNotModifyRunRecord()
    {
        // Arrange — monitor expires the lease but must NOT touch the RunRecord.
        var staleLease = BuildAcceptedLease(lastHb: FixedNow.AddSeconds(-120));

        var service = MakeService(out var leaseRepo, out _);

        leaseRepo
            .Setup(r => r.FindAcceptedWithExpiredHeartbeatAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { staleLease });

        leaseRepo.Setup(r => r.Update(It.IsAny<LeaseRecord>()));

        // The run repository must never be touched by the monitor.
        var runRepo = new Mock<IRunRepository>(MockBehavior.Strict);

        // Act
        await service.ScanOnceAsync(CancellationToken.None);

        // Assert — IRunRepository has no calls because mock is strict and no setups were registered.
        runRepo.VerifyNoOtherCalls();
    }
}
