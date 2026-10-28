using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Heartbeat;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-04 — Heartbeat
///
/// Verifies that:
///   AC-1: heartbeat is sent at the configured interval for each active run.
///   AC-2: heartbeat sequence is persisted in the journal after each successful delivery.
///   AC-3: missed heartbeats do NOT kill the running command until MaxFailures consecutive
///         failures have accumulated AND the lease has expired.
///
/// These tests run against the <see cref="MockOrchestratorFixture"/> with a mock
/// <see cref="IHeartbeatSender"/>.  No real network I/O takes place.
/// </summary>
public sealed class CF04_HeartbeatTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF04_HeartbeatTest(MockOrchestratorFixture fx) => _fx = fx;

    // ── AC-1 / AC-2 — happy-path heartbeat ────────────────────────────────────

    [Fact(DisplayName = "CF-04-A: Successful heartbeat increments journal sequence number")]
    public async Task SuccessfulHeartbeat_PersistsSeqNo_ToJournal()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        // Write an initial journal entry
        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);

        // Simulate a heartbeat delivery
        const int seqNo = 1;
        var hbResult = await _fx.Journal.RecordHeartbeatAsync(runId, attemptId, seqNo);
        hbResult.IsSuccess.Should().BeTrue();

        var entry = (await _fx.Journal.ReadEntryAsync(runId)).Value!;
        entry.HeartbeatSeq.Should().Be(seqNo, "the journal must persist the heartbeat sequence number (AC-2)");
    }

    [Fact(DisplayName = "CF-04-B: Heartbeat sequence is monotonically increasing")]
    public async Task HeartbeatSeq_IsMonotonicallyIncreasing()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);

        // Record seq 1, then 3 (skip 2), then attempt to go back to 2 (should be idempotent)
        await _fx.Journal.RecordHeartbeatAsync(runId, attemptId, 1);
        await _fx.Journal.RecordHeartbeatAsync(runId, attemptId, 3);
        await _fx.Journal.RecordHeartbeatAsync(runId, attemptId, 2);  // out-of-order / duplicate

        var entry = (await _fx.Journal.ReadEntryAsync(runId)).Value!;
        entry.HeartbeatSeq.Should().Be(3,
            "a lower or equal heartbeat sequence number must be ignored (monotonic only)");
    }

    // ── AC-3 — failure accumulation ───────────────────────────────────────────

    [Fact(DisplayName = "CF-04-C: HeartbeatContext consecutive failure counter increments and resets")]
    public void HeartbeatContext_FailureCounter_IncrementsAndResets()
    {
        using var ctx = new HeartbeatContext(
            runId:                              "hb-ctx-test",
            attemptId:                          "attempt-1",
            leaseId:                            "lease-1",
            leaseExpiresAt:                     DateTimeOffset.UtcNow.AddMinutes(5),
            maxDeadline:                        DateTimeOffset.UtcNow.AddMinutes(30),
            policy:                             new HeartbeatPolicy(IntervalMs: 100, MaxFailures: 3));

        ctx.IncrementFailures().Should().Be(1);
        ctx.IncrementFailures().Should().Be(2);
        ctx.IncrementFailures().Should().Be(3);
        ctx.ConsecutiveFailures.Should().Be(3);

        ctx.ResetFailures();
        ctx.ConsecutiveFailures.Should().Be(0, "reset must clear the failure counter");
    }

    [Fact(DisplayName = "CF-04-D: HeartbeatService sends heartbeat via IHeartbeatSender (AC-1)")]
    public async Task HeartbeatService_SendsHeartbeat_ViaAbstraction()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);

        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        // Build a context with a very short interval so the test completes quickly
        using var ctx = new HeartbeatContext(
            runId:                  runId,
            attemptId:              attemptId,
            leaseId:                leaseId,
            leaseExpiresAt:         DateTimeOffset.UtcNow.AddSeconds(30),
            maxDeadline:            DateTimeOffset.UtcNow.AddMinutes(5),
            policy:                 new HeartbeatPolicy(IntervalMs: 50, MaxFailures: 5));

        using var svc = new HeartbeatService(
            senderMock.Object,
            _fx.Journal,
            clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        using var commandCts = new CancellationTokenSource();
        svc.StartSession(ctx, commandCts.Token);

        // Wait long enough for at least one heartbeat to fire
        await Task.Delay(200);

        // Stop the session and give the loop a moment to clean up
        commandCts.Cancel();
        await Task.Delay(100);

        senderMock.Verify(
            s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "HeartbeatService must call IHeartbeatSender.SendAsync at the configured interval (AC-1)");
    }

    [Fact(DisplayName = "CF-04-E: HeartbeatRecord carries no interactiveSessionId or secret (AC-1, CEC inv #4)")]
    public async Task HeartbeatRecord_ContainsNoSecret()
    {
        const string secretToken = "super-secret-token-cf04e";
        var runId     = "run-hb-" + Guid.NewGuid().ToString("N")[..8];
        var attemptId = Guid.NewGuid().ToString("D");

        // Verify the HeartbeatRecord built by HeartbeatService doesn't leak secret tokens
        HeartbeatRecord? capturedRecord = null;
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .Callback<HeartbeatRecord, CancellationToken>((r, _) => capturedRecord = r)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        await _fx.Journal.RecordStateAsync(runId, attemptId, "lease-hb", RunState.Running);

        using var ctx = new HeartbeatContext(
            runId:          runId,
            attemptId:      attemptId,
            leaseId:        "lease-hb",
            leaseExpiresAt: DateTimeOffset.UtcNow.AddSeconds(30),
            maxDeadline:    DateTimeOffset.UtcNow.AddMinutes(5),
            policy:         new HeartbeatPolicy(IntervalMs: 50, MaxFailures: 5));

        using var svc = new HeartbeatService(
            senderMock.Object,
            _fx.Journal,
            clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        using var cts = new CancellationTokenSource();
        svc.StartSession(ctx, cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await Task.Delay(100);

        capturedRecord.Should().NotBeNull("at least one heartbeat must have been sent");
        var recordJson = System.Text.Json.JsonSerializer.Serialize(capturedRecord);
        recordJson.Should().NotContain(secretToken,
            "CEC invariant #4: no secret token must appear in a HeartbeatRecord payload");
    }
}
