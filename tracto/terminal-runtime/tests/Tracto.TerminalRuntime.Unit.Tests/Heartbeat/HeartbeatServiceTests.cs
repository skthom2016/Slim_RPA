using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Heartbeat;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Unit.Tests.Heartbeat;

/// <summary>
/// Unit tests for TER-US-018: Heartbeat During Long-Running Commands and Control-Plane Restart.
///
/// Coverage:
///   AC-1  Heartbeats are sent at the configured interval.
///   AC-2  Journal prevents duplicate irreversible command after Orchestrator restart.
///   AC-3a Heartbeat failure does NOT kill the running command while the lease is valid.
///   AC-3b When MaxFailures accumulate AND lease has expired → TIMED_OUT (no irreversible side effect).
///   AC-3c When MaxFailures accumulate AND lease has expired AND irreversible side effect committed → UNKNOWN.
///   AC-3d Max-deadline reached → TIMED_OUT.
/// </summary>
public sealed class HeartbeatServiceTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static HeartbeatContext MakeContext(
        string          runId          = "run-001",
        string          attemptId      = "att-001",
        string          leaseId        = "lease-001",
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? maxDeadline    = null,
        HeartbeatPolicy? policy        = null,
        bool            irreversible   = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new HeartbeatContext(
            runId:           runId,
            attemptId:       attemptId,
            leaseId:         leaseId,
            leaseExpiresAt:  leaseExpiresAt ?? now.AddMinutes(30),
            maxDeadline:     maxDeadline    ?? now.AddHours(1),
            policy:          policy         ?? new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 3),
            hasCommittedIrreversibleSideEffect: irreversible);
    }

    private static Mock<IRunJournal> HappyJournal()
    {
        var m = new Mock<IRunJournal>();
        m.Setup(j => j.RecordHeartbeatAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(Result.Ok<bool>(true));
        m.Setup(j => j.RecordStateAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<RunState>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(Result.Ok<bool>(true));
        m.Setup(j => j.RecordUnknownStateAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(Result.Ok<bool>(true));
        return m;
    }

    // ── AC-1: heartbeat sent at configured interval ───────────────────────────

    [Fact]
    public async Task HeartbeatService_SendsHeartbeats_AtConfiguredInterval()
    {
        // Arrange
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var journalMock = HappyJournal();
        var clockMock   = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // A very short interval so multiple heartbeats occur in the test window.
        var ctx = MakeContext(policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 100));
        using var commandCts = new CancellationTokenSource();

        // Act
        service.StartSession(ctx, commandCts.Token);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(350); // ~8 intervals of 40 ms → at least 5 deliveries

        commandCts.Cancel();          // stop the command
        service.StopSession(ctx.RunId);
        await service.StopAsync(CancellationToken.None);

        // Assert: at least 5 successful heartbeats delivered.
        senderMock.Verify(
            s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(5),
            "Expected at least 5 heartbeats within ~350 ms at 40 ms interval.");

        // Journal should have been updated for each successful delivery.
        journalMock.Verify(
            j => j.RecordHeartbeatAsync(ctx.RunId, ctx.AttemptId, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(5));
    }

    // ── AC-2: journal prevents duplicate irreversible command after reconnect ──

    [Fact]
    public async Task ReconcileRunHandler_PreventsIrreversibleCommandReplay_AfterOrchestratorRestart()
    {
        // Arrange: journal shows an in-flight irreversible run.
        var journalMock = new Mock<IRunJournal>();
        var entry = new RunJournalEntry(
            RunId:            "run-irrev",
            AttemptId:        "att-001",
            LeaseId:          "lease-001",
            WorkerId:         "worker-1",
            HeartbeatSeq:     3L,
            CheckpointAt:     DateTimeOffset.UtcNow.AddMinutes(-1),
            FinalResult:      null,
            IdempotencyClass: IdempotencyClass.Irreversible,
            State:            RunState.Running);

        journalMock
            .Setup(j => j.ReadEntryAsync("run-irrev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));

        var handler = new ReconcileRunHandler(journalMock.Object);

        // Act: Orchestrator restarts and calls reconcile.
        var result = await handler.HandleAsync("run-irrev");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse(
            "an irreversible command MUST NOT be auto-replayed after Orchestrator restart " +
            "(CEC v1.0.1 invariant #3)");
        result.Value.Entry.IdempotencyClass.Should().Be(IdempotencyClass.Irreversible);
    }

    [Fact]
    public async Task ReconcileRunHandler_AllowsIdempotentCommandReplay_AfterOrchestratorRestart()
    {
        var journalMock = new Mock<IRunJournal>();
        var entry = new RunJournalEntry(
            RunId:            "run-idem",
            AttemptId:        "att-001",
            LeaseId:          "lease-001",
            WorkerId:         "worker-1",
            HeartbeatSeq:     2L,
            CheckpointAt:     DateTimeOffset.UtcNow.AddSeconds(-30),
            FinalResult:      null,
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:            RunState.Running);

        journalMock
            .Setup(j => j.ReadEntryAsync("run-idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RunJournalEntry?, Error>.Ok(entry));

        var handler = new ReconcileRunHandler(journalMock.Object);
        var result  = await handler.HandleAsync("run-idem");

        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeTrue(
            "an idempotent in-flight run may be safely replayed after an Orchestrator restart");
    }

    // ── AC-3a: heartbeat failure does NOT kill the command while lease is valid ─

    [Fact]
    public async Task HeartbeatService_HeartbeatFailures_DoNotKillCommand_WhileLeaseIsValid()
    {
        // Arrange: every heartbeat fails, but the lease is far in the future.
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<bool>(Error.General("HB.Fail", "Orchestrator temporarily down.")));

        var now       = DateTimeOffset.UtcNow;
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(now); // lease is far in the future

        var journalMock = HappyJournal();

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // Lease expires in 1 hour → failures alone must not terminate the run.
        var ctx = MakeContext(
            leaseExpiresAt: now.AddHours(1),
            maxDeadline:    now.AddHours(2),
            policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 2));

        using var commandCts = new CancellationTokenSource();
        service.StartSession(ctx, commandCts.Token);
        await service.StartAsync(CancellationToken.None);

        // Allow 3+ heartbeat intervals — each will fail.
        await Task.Delay(250);

        // Assert: the termination token must NOT be cancelled — command is still alive.
        ctx.TerminationToken.IsCancellationRequested.Should().BeFalse(
            "heartbeat failures alone (lease still valid) must never terminate the command");

        commandCts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    // ── AC-3b: MaxFailures + lease expired → TIMED_OUT ───────────────────────

    [Fact]
    public async Task HeartbeatService_MaxFailuresAndLeaseExpired_SetsTimedOut()
    {
        // Arrange
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<bool>(Error.General("HB.Fail", "Unreachable.")));

        // Clock always returns a time AFTER the lease expiry.
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(expiredAt);

        var journalMock = HappyJournal();

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // MaxFailures = 2; lease already expired.
        var ctx = MakeContext(
            leaseExpiresAt: expiredAt.AddMinutes(-30), // expired 30 min ago
            maxDeadline:    DateTimeOffset.UtcNow.AddHours(1),
            policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 2),
            irreversible: false);

        service.StartSession(ctx);
        await service.StartAsync(CancellationToken.None);

        // Wait: 2 failures × 40 ms + generous buffer = ~300 ms.
        await Task.Delay(500);

        // Check termination BEFORE StopAsync so the context is still alive.
        bool wasTerminated = ctx.TerminationToken.IsCancellationRequested;
        await service.StopAsync(CancellationToken.None);
        ctx.Dispose();

        // Termination token must have been cancelled by the service.
        wasTerminated.Should().BeTrue(
            "after MaxFailures with lease expired, the service must terminate the run");

        // Journal must record TimedOut (no irreversible side effect).
        journalMock.Verify(
            j => j.RecordStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId, RunState.TimedOut,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "journal must be updated to TimedOut when no irreversible side effect was committed");
    }

    // ── AC-3c: MaxFailures + lease expired + irreversible side effect → UNKNOWN ─

    [Fact]
    public async Task HeartbeatService_MaxFailuresAndLeaseExpiredWithIrreversibleSideEffect_SetsUnknown()
    {
        // Arrange
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<bool>(Error.General("HB.Fail", "Unreachable.")));

        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(expiredAt);

        var journalMock = HappyJournal();

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // MaxFailures = 2; lease expired; irreversible side effect committed.
        var ctx = MakeContext(
            leaseExpiresAt: expiredAt.AddMinutes(-30),
            maxDeadline:    DateTimeOffset.UtcNow.AddHours(1),
            policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 2),
            irreversible: true);

        service.StartSession(ctx);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        bool wasTerminated = ctx.TerminationToken.IsCancellationRequested;
        await service.StopAsync(CancellationToken.None);
        ctx.Dispose();

        wasTerminated.Should().BeTrue("the service must terminate the run after MaxFailures + lease expiry");

        // CEC invariant #2: UNKNOWN must be written via RecordUnknownStateAsync so that
        // NeedsAttention=true and UnknownReason are persisted — RecordStateAsync never sets them.
        journalMock.Verify(
            j => j.RecordUnknownStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId,
                It.IsAny<string>(),          // unknownReason must be non-null/non-empty
                It.IsAny<CancellationToken>()),
            Times.Once,
            "when an irreversible side effect was committed, RecordUnknownStateAsync " +
            "must be called (CEC v1.0.1 invariant #2 — needsAttention=true + unknownReason required)");

        // RecordStateAsync must NOT be called with RunState.Unknown — that path omits NeedsAttention.
        journalMock.Verify(
            j => j.RecordStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId, RunState.Unknown,
                It.IsAny<CancellationToken>()),
            Times.Never,
            "RecordStateAsync must never be called with RunState.Unknown (CEC v1.0.1 invariant #2)");
    }

    // ── AC-3d: max-deadline exceeded → TIMED_OUT ──────────────────────────────

    [Fact]
    public async Task HeartbeatService_MaxDeadlineExceeded_SetsTimedOut()
    {
        // Arrange: heartbeats succeed but the deadline is in the past.
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var pastDeadline = DateTimeOffset.UtcNow.AddHours(-1); // deadline already passed
        var clockMock    = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(pastDeadline);

        var journalMock = HappyJournal();
        journalMock
            .Setup(j => j.RecordHeartbeatAsync(
                        It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<bool>(true));

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // MaxDeadline is in the past as seen by the (mocked) clock.
        var ctx = MakeContext(
            leaseExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            maxDeadline:    pastDeadline.AddMinutes(-5), // even more in the past
            policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 10),
            irreversible: false);

        service.StartSession(ctx);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        bool wasTerminated = ctx.TerminationToken.IsCancellationRequested;
        await service.StopAsync(CancellationToken.None);
        ctx.Dispose();

        wasTerminated.Should().BeTrue(
            "when the max-deadline is exceeded the service must terminate the run");

        journalMock.Verify(
            j => j.RecordStateAsync(
                ctx.RunId, ctx.AttemptId, ctx.LeaseId, RunState.TimedOut,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Failure counter resets on successful delivery ─────────────────────────

    [Fact]
    public async Task HeartbeatService_SuccessAfterFailures_ResetsFailureCounter()
    {
        // Arrange: fail once, then succeed.
        var callCount  = 0;
        var senderMock = new Mock<IHeartbeatSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<HeartbeatRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? Result.Fail<bool>(Error.General("HB.Fail", "First call fails."))
                    : Result.Ok<bool>(true);
            });

        var now       = DateTimeOffset.UtcNow;
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(now);

        var journalMock = HappyJournal();

        var service = new HeartbeatService(
            senderMock.Object, journalMock.Object, clockMock.Object,
            NullLogger<HeartbeatService>.Instance);

        // MaxFailures = 5, lease far in future.
        var ctx = MakeContext(
            leaseExpiresAt: now.AddHours(1),
            maxDeadline:    now.AddHours(2),
            policy: new HeartbeatPolicy(IntervalMs: 40, MaxFailures: 5));

        using var commandCts = new CancellationTokenSource();
        service.StartSession(ctx, commandCts.Token);
        await service.StartAsync(CancellationToken.None);

        // Allow at least 2 heartbeat intervals (1 fail + 1 succeed).
        await Task.Delay(250);

        ctx.TerminationToken.IsCancellationRequested.Should().BeFalse(
            "failure counter should have been reset on the successful second delivery");
        ctx.ConsecutiveFailures.Should().Be(0,
            "a successful heartbeat delivery must reset the consecutive-failure counter");

        commandCts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }
}
