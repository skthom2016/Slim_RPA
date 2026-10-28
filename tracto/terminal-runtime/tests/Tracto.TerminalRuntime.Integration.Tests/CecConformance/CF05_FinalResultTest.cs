using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure.Cleanup;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-05 — Final Result Emission
///
/// CEC v1.0.1 defines six legal terminal states for a run:
///   SUCCEEDED | FAILED | CANCELLED | TIMED_OUT | UNKNOWN | HANDED_OFF
///
/// Each state must be emittable and correctly persisted in the journal.
///
/// UNKNOWN invariants (CEC v1.0.1 invariant #2):
///   - NeedsAttention = true
///   - UnknownReason is non-null and non-empty
///
/// HANDED_OFF invariants:
///   - Persisted via ServiceHandoffHandler (no dynamic scriptRef allowed per CF-08)
///   - Terminal worker is released; Run B creation is deferred to callback
///
/// All tests run against <see cref="MockOrchestratorFixture"/>; no real Orchestrator required.
/// </summary>
public sealed class CF05_FinalResultTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF05_FinalResultTest(MockOrchestratorFixture fx) => _fx = fx;

    [Theory(DisplayName = "CF-05-A: Terminal states can all be persisted to the journal")]
    [InlineData(RunState.Succeeded)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.TimedOut)]
    [InlineData(RunState.HandedOff)]
    public async Task TerminalState_IsPersisted(RunState state)
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);
        var result = await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, state);

        result.IsSuccess.Should().BeTrue();
        var persisted = (await _fx.Journal.GetStateAsync(runId)).Value;
        persisted.Should().Be(state, $"{state} must be stored correctly in the journal");
    }

    [Fact(DisplayName = "CF-05-B: UNKNOWN state always sets NeedsAttention=true and UnknownReason")]
    public async Task UnknownState_SetsNeedsAttentionAndReason()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");
        const string reason = "WSL distro crashed after irreversible side effect committed";

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);
        var result = await _fx.Journal.RecordUnknownStateAsync(runId, attemptId, leaseId, reason);

        result.IsSuccess.Should().BeTrue();
        var entry = (await _fx.Journal.ReadEntryAsync(runId)).Value!;
        entry.State.Should().Be(RunState.Unknown, "CEC invariant #2: UNKNOWN must be set");
        entry.NeedsAttention.Should().BeTrue(
            "CEC invariant #2: NeedsAttention must be true for every UNKNOWN run");
        entry.UnknownReason.Should().Be(reason,
            "CEC invariant #2: UnknownReason must be persisted");
    }

    [Fact(DisplayName = "CF-05-C: HANDED_OFF emitted by ServiceHandoffHandler")]
    public async Task ServiceHandoffHandler_EmitsHandedOff()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);

        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var handler = new ServiceHandoffHandler(
            _fx.Journal,
            clockMock.Object,
            NullLogger<ServiceHandoffHandler>.Instance);

        var handoffResult = await handler.HandleHandoffAsync(
            runId, attemptId, leaseId,
            continuationMappingRef: "static-mapping-ref-1");

        handoffResult.IsSuccess.Should().BeTrue();
        handoffResult.Value.RunId.Should().Be(runId);

        var state = (await _fx.Journal.GetStateAsync(runId)).Value;
        state.Should().Be(RunState.HandedOff, "HANDED_OFF must be persisted to the journal");
    }

    [Fact(DisplayName = "CF-05-D: RecordStateAsync with SUCCEEDED marks run as completed")]
    public async Task RecordSucceeded_MarksRunAsCompleted()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        // Persist initial entry via AcquireLeaseCommandHandler flow
        var entry = new RunJournalEntry(
            RunId:           runId,
            AttemptId:       attemptId,
            LeaseId:         leaseId,
            WorkerId:        "worker-cf05d",
            HeartbeatSeq:    0L,
            CheckpointAt:    DateTimeOffset.UtcNow,
            FinalResult:     null,
            IdempotencyClass: IdempotencyClass.Idempotent,
            State:           RunState.Running);
        await _fx.Journal.WriteEntryAsync(entry);

        // Write final result manually (the runner would do this via the outcome classifier)
        var completed = entry with { FinalResult = "SUCCEEDED", State = RunState.Succeeded };
        await _fx.Journal.WriteEntryAsync(completed);

        var read = (await _fx.Journal.ReadEntryAsync(runId)).Value!;
        read.IsCompleted.Should().BeTrue(
            "a run with a non-null FinalResult must be considered completed");
    }
}
