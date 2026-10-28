using FluentAssertions;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-07 — Irreversible Command: No Auto-Replay
///
/// CEC v1.0.1 invariants #2 and #3:
///   - An UNKNOWN run must never be auto-replayed.
///   - An irreversible command step must never be replayed after any failure.
///   - A completed run is not replayed regardless of its idempotency class.
///
/// This is enforced by <see cref="ReconcileRunHandler"/>, which is the sole
/// entry point for reconciliation after lease expiry or Orchestrator restart.
/// </summary>
public sealed class CF07_IrreversibleNoReplayTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF07_IrreversibleNoReplayTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-07-A: Irreversible command in UNKNOWN state → ShouldReplay = false")]
    public async Task IrreversibleCommand_UnknownState_ShouldNotReplay()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");
        const string reason = "irreversible side effect committed before lease expired";

        // Seed the journal with an UNKNOWN entry for an irreversible command
        var entry = new RunJournalEntry(
            RunId:            runId,
            AttemptId:        attemptId,
            LeaseId:          leaseId,
            WorkerId:         "worker-cf07a",
            HeartbeatSeq:     3L,
            CheckpointAt:     DateTimeOffset.UtcNow,
            FinalResult:      null,             // run is not completed — still in UNKNOWN
            IdempotencyClass: IdempotencyClass.Irreversible,
            State:            RunState.Unknown)
        {
            NeedsAttention = true,
            UnknownReason  = reason
        };
        await _fx.Journal.WriteEntryAsync(entry);

        var handler = new ReconcileRunHandler(_fx.Journal);
        var result  = await handler.HandleAsync(runId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse(
            "CEC invariant #2: an UNKNOWN run must never be auto-replayed");
        result.Value.Entry.NeedsAttention.Should().BeTrue(
            "CEC invariant #2: NeedsAttention must be true on the UNKNOWN entry");
    }

    [Fact(DisplayName = "CF-07-B: Irreversible command in TIMED_OUT state → ShouldReplay = false")]
    public async Task IrreversibleCommand_TimedOut_ShouldNotReplay()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        var entry = new RunJournalEntry(
            RunId:            runId,
            AttemptId:        attemptId,
            LeaseId:          leaseId,
            WorkerId:         "worker-cf07b",
            HeartbeatSeq:     1L,
            CheckpointAt:     DateTimeOffset.UtcNow,
            FinalResult:      null,
            IdempotencyClass: IdempotencyClass.Irreversible,
            State:            RunState.TimedOut);
        await _fx.Journal.WriteEntryAsync(entry);

        var handler = new ReconcileRunHandler(_fx.Journal);
        var result  = await handler.HandleAsync(runId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse(
            "an irreversible command must never be replayed regardless of its terminal state");
    }

    [Fact(DisplayName = "CF-07-C: Idempotent command in non-terminal state → ShouldReplay = true")]
    public async Task IdempotentCommand_NonTerminalState_ShouldReplay()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        var entry = new RunJournalEntry(
            RunId:            runId,
            AttemptId:        attemptId,
            LeaseId:          leaseId,
            WorkerId:         "worker-cf07c",
            HeartbeatSeq:     0L,
            CheckpointAt:     DateTimeOffset.UtcNow,
            FinalResult:      null,
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:            RunState.Leased);    // non-terminal, not UNKNOWN
        await _fx.Journal.WriteEntryAsync(entry);

        var handler = new ReconcileRunHandler(_fx.Journal);
        var result  = await handler.HandleAsync(runId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeTrue(
            "an idempotent command in a non-terminal, non-UNKNOWN state may be replayed");
    }

    [Fact(DisplayName = "CF-07-D: Completed run → ShouldReplay = false regardless of class")]
    public async Task CompletedRun_ShouldNotReplay()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        var entry = new RunJournalEntry(
            RunId:            runId,
            AttemptId:        attemptId,
            LeaseId:          leaseId,
            WorkerId:         "worker-cf07d",
            HeartbeatSeq:     5L,
            CheckpointAt:     DateTimeOffset.UtcNow,
            FinalResult:      "SUCCEEDED",      // marks IsCompleted = true
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:            RunState.Succeeded);
        await _fx.Journal.WriteEntryAsync(entry);

        var handler = new ReconcileRunHandler(_fx.Journal);
        var result  = await handler.HandleAsync(runId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShouldReplay.Should().BeFalse(
            "a completed run must never be replayed regardless of its idempotency class");
    }

    [Fact(DisplayName = "CF-07-E: ReconcileRunHandler fails with clear error when runId is unknown")]
    public async Task ReconcileRunHandler_FailsGracefully_WhenRunIdNotFound()
    {
        var handler = new ReconcileRunHandler(_fx.Journal);
        var result  = await handler.HandleAsync("non-existent-run-id-cf07e");

        result.IsFailure.Should().BeTrue(
            "ReconcileRunHandler must return a failure when the runId is not found in the journal");
        result.Error.Code.Should().Be("Reconcile.NotFound");
    }
}
