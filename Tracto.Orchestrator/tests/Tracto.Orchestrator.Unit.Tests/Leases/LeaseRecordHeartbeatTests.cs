#nullable enable
using FluentAssertions;
using Tracto.Orchestrator.Domain.Leases;

namespace Tracto.Orchestrator.Unit.Tests.Leases;

/// <summary>
/// Unit tests for heartbeat-related domain methods on <see cref="LeaseRecord"/> — story OC-05.
///
/// Tests exercise:
///   - RecordHeartbeat: valid update, stale seqNo rejection, wrong state rejection.
///   - RequestCancellation: flag setting.
///   - ComputeExpiryState: Active / Suspect / Expired logic.
///   - ExpireAccepted: idempotency.
/// </summary>
public sealed class LeaseRecordHeartbeatTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 20, 14, 0, 0, TimeSpan.Zero);

    private const string WorkerId   = "worker-hb-domain";
    private const string ArtifactId = "art-domain";
    private const string Version    = "1.0.0";
    private const string Hash       = "sha256:cafebabe";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LeaseRecord BuildOfferedLease(Guid? runId = null, Guid? attemptId = null)
        => new LeaseRecord(
            leaseId:         Guid.NewGuid(),
            runId:           runId     ?? Guid.NewGuid(),
            attemptId:       attemptId ?? Guid.NewGuid(),
            workerId:        WorkerId,
            artifactId:      ArtifactId,
            artifactVersion: Version,
            artifactHash:    Hash,
            offeredAt:       FixedNow.AddSeconds(-5),
            expiresAt:       FixedNow.AddSeconds(25));

    private static LeaseRecord BuildAcceptedLease(Guid? runId = null, Guid? attemptId = null)
    {
        var lease = BuildOfferedLease(runId, attemptId);
        lease.Accept(FixedNow.AddSeconds(-4));
        return lease;
    }

    // ── RecordHeartbeat: valid update ─────────────────────────────────────────

    [Fact]
    public void RecordHeartbeat_ValidSeqNo_UpdatesLastHeartbeatAt()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        var heartbeatTime = FixedNow;

        // Act
        lease.RecordHeartbeat(heartbeatTime, seqNo: 1);

        // Assert
        lease.LastHeartbeatAt.Should().Be(heartbeatTime,
            because: "RecordHeartbeat must stamp LastHeartbeatAt with the supplied timestamp");
    }

    [Fact]
    public void RecordHeartbeat_ValidSeqNo_UpdatesLastSeqNo()
    {
        // Arrange
        var lease = BuildAcceptedLease();

        // Act
        lease.RecordHeartbeat(FixedNow, seqNo: 42);

        // Assert
        lease.LastSeqNo.Should().Be(42,
            because: "RecordHeartbeat must advance LastSeqNo to the new value");
    }

    [Fact]
    public void RecordHeartbeat_MultipleValidCalls_LastSeqNoAdvancesMonotonically()
    {
        // Arrange
        var lease = BuildAcceptedLease();

        // Act
        lease.RecordHeartbeat(FixedNow.AddSeconds(0), seqNo: 1);
        lease.RecordHeartbeat(FixedNow.AddSeconds(5), seqNo: 2);
        lease.RecordHeartbeat(FixedNow.AddSeconds(10), seqNo: 10);

        // Assert
        lease.LastSeqNo.Should().Be(10,
            because: "each successive valid heartbeat must advance LastSeqNo");
        lease.LastHeartbeatAt.Should().Be(FixedNow.AddSeconds(10),
            because: "LastHeartbeatAt reflects the most recent accepted heartbeat");
    }

    // ── RecordHeartbeat: stale SeqNo throws ───────────────────────────────────

    [Fact]
    public void RecordHeartbeat_StaleSeqNo_ThrowsInvalidOperationException()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow, seqNo: 5);

        // Act — seqNo=3 is less than current LastSeqNo=5.
        var act = () => lease.RecordHeartbeat(FixedNow.AddSeconds(1), seqNo: 3);

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "RecordHeartbeat must reject a SeqNo that is not greater than LastSeqNo");
    }

    [Fact]
    public void RecordHeartbeat_EqualSeqNo_ThrowsInvalidOperationException()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow, seqNo: 7);

        // Act — equal seqNo is not strictly greater.
        var act = () => lease.RecordHeartbeat(FixedNow.AddSeconds(5), seqNo: 7);

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "an equal SeqNo violates the strict monotonicity requirement");
    }

    [Fact]
    public void RecordHeartbeat_StaleSeqNo_LastHeartbeatAtUnchanged()
    {
        // Arrange — establish a known LastHeartbeatAt.
        var lease         = BuildAcceptedLease();
        var firstHeartbeat = FixedNow;
        lease.RecordHeartbeat(firstHeartbeat, seqNo: 5);

        // Act — stale seqNo; must throw but LastHeartbeatAt must stay as is.
        try { lease.RecordHeartbeat(FixedNow.AddSeconds(1), seqNo: 3); }
        catch (InvalidOperationException) { /* expected */ }

        // Assert
        lease.LastHeartbeatAt.Should().Be(firstHeartbeat,
            because: "a rejected stale heartbeat must not modify LastHeartbeatAt");
    }

    // ── RecordHeartbeat: wrong state ──────────────────────────────────────────

    [Fact]
    public void RecordHeartbeat_OnOfferedLease_ThrowsInvalidOperationException()
    {
        // Arrange — lease is still Offered (never accepted).
        var lease = BuildOfferedLease();

        // Act
        var act = () => lease.RecordHeartbeat(FixedNow, seqNo: 1);

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "heartbeats are only valid on Accepted leases; Offered leases cannot be heartbeated");
    }

    [Fact]
    public void RecordHeartbeat_OnExpiredLease_ThrowsInvalidOperationException()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.ExpireAccepted();

        // Act
        var act = () => lease.RecordHeartbeat(FixedNow, seqNo: 1);

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "a heartbeat on an Expired lease must be rejected at the domain level");
    }

    // ── RequestCancellation ───────────────────────────────────────────────────

    [Fact]
    public void RequestCancellation_SetsCancellationRequestedToTrue()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.CancellationRequested.Should().BeFalse("baseline: no cancellation requested");

        // Act
        lease.RequestCancellation();

        // Assert
        lease.CancellationRequested.Should().BeTrue(
            because: "RequestCancellation must set the flag to true");
    }

    [Fact]
    public void RequestCancellation_CalledTwice_RemainsTrue()
    {
        // Arrange
        var lease = BuildAcceptedLease();

        // Act
        lease.RequestCancellation();
        lease.RequestCancellation();

        // Assert
        lease.CancellationRequested.Should().BeTrue(
            because: "RequestCancellation is idempotent — calling it twice must not fault");
    }

    // ── ComputeExpiryState ────────────────────────────────────────────────────

    private static readonly TimeSpan Expiry  = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Suspect = TimeSpan.FromSeconds(30);

    [Fact]
    public void ComputeExpiryState_NoHeartbeatYet_ReturnsSuspect()
    {
        // Arrange — freshly accepted, LastHeartbeatAt is null.
        var lease = BuildAcceptedLease();

        // Act
        var state = lease.ComputeExpiryState(FixedNow, Expiry, Suspect);

        // Assert
        state.Should().Be(LeaseExpiryState.Suspect,
            because: "a lease that has never sent a heartbeat is Suspect");
    }

    [Fact]
    public void ComputeExpiryState_RecentHeartbeat_ReturnsActive()
    {
        // Arrange — heartbeat 5 s ago (well within 30 s suspect window).
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow.AddSeconds(-5), seqNo: 1);

        // Act
        var state = lease.ComputeExpiryState(FixedNow, Expiry, Suspect);

        // Assert
        state.Should().Be(LeaseExpiryState.Active,
            because: "a heartbeat received within the suspect window means the lease is Active");
    }

    [Fact]
    public void ComputeExpiryState_HeartbeatOlderThanSuspectWindow_ReturnsSuspect()
    {
        // Arrange — heartbeat 35 s ago (past 30 s suspect threshold, within 60 s expiry).
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow.AddSeconds(-35), seqNo: 1);

        // Act
        var state = lease.ComputeExpiryState(FixedNow, Expiry, Suspect);

        // Assert
        state.Should().Be(LeaseExpiryState.Suspect,
            because: "a heartbeat older than the suspect window but within the expiry window is Suspect");
    }

    [Fact]
    public void ComputeExpiryState_HeartbeatOlderThanExpiryWindow_ReturnsExpired()
    {
        // Arrange — heartbeat 65 s ago (past 60 s expiry threshold).
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow.AddSeconds(-65), seqNo: 1);

        // Act
        var state = lease.ComputeExpiryState(FixedNow, Expiry, Suspect);

        // Assert
        state.Should().Be(LeaseExpiryState.Expired,
            because: "a heartbeat older than the expiry window means the lease is Expired");
    }

    [Fact]
    public void ComputeExpiryState_LeaseAlreadyExpiredState_ReturnsExpired()
    {
        // Arrange — lease was marked Expired by the monitor service.
        var lease = BuildAcceptedLease();
        lease.RecordHeartbeat(FixedNow.AddSeconds(-2), seqNo: 1);  // recent heartbeat
        lease.ExpireAccepted();

        // Act — even with a recent heartbeat, the Expired state wins.
        var state = lease.ComputeExpiryState(FixedNow, Expiry, Suspect);

        // Assert
        state.Should().Be(LeaseExpiryState.Expired,
            because: "a lease whose State=Expired must always return LeaseExpiryState.Expired");
    }

    // ── ExpireAccepted: idempotent ────────────────────────────────────────────

    [Fact]
    public void ExpireAccepted_CalledOnAcceptedLease_SetsStateToExpired()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.State.Should().Be(LeaseState.Accepted, "baseline");

        // Act
        lease.ExpireAccepted();

        // Assert
        lease.State.Should().Be(LeaseState.Expired,
            because: "ExpireAccepted must transition Accepted → Expired");
    }

    [Fact]
    public void ExpireAccepted_CalledTwice_RemainsExpiredWithoutThrowing()
    {
        // Arrange
        var lease = BuildAcceptedLease();
        lease.ExpireAccepted();

        // Act
        var act = () => lease.ExpireAccepted();

        // Assert — idempotent: no exception.
        act.Should().NotThrow(because: "ExpireAccepted must be idempotent");
        lease.State.Should().Be(LeaseState.Expired);
    }

    // ── ExpireAccepted: guard against wrong state (CRITICAL fix) ─────────────

    [Fact]
    public void ExpireAccepted_CalledOnOfferedLease_ThrowsInvalidOperationException()
    {
        // Arrange — lease is still Offered (never accepted).
        var lease = BuildOfferedLease();
        lease.State.Should().Be(LeaseState.Offered, "baseline");

        // Act
        var act = () => lease.ExpireAccepted();

        // Assert — the monitor path must only expire Accepted leases;
        //          calling it on an Offered lease is a programming error.
        act.Should().Throw<InvalidOperationException>(
            because: "ExpireAccepted must not silently expire an Offered lease — use Expire() for that");
    }
}
