using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-03 — AttemptId Fencing
///
/// CEC v1.0.1 invariant #3: duplicate identical message → idempotent 200.
/// A duplicate leaseAccepted carrying the same (RunId, AttemptId) pair must be
/// processed identically to the first delivery (idempotent write).
///
/// A message with a different AttemptId for the same RunId must be treated as a
/// new attempt and must update the journal accordingly.
///
/// All tests run against the <see cref="MockOrchestratorFixture"/>; no real Orchestrator required.
/// </summary>
public sealed class CF03_AttemptIdFencingTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF03_AttemptIdFencingTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-03-A: Duplicate leaseAccepted with same attemptId → idempotent")]
    public async Task DuplicateLeaseAccepted_SameAttemptId_IsIdempotent()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");

        // First write
        var first = await _fx.Journal.RecordStateAsync(runId, attemptId, "lease-1", RunState.Leased);
        first.IsSuccess.Should().BeTrue();

        // Second write — same attemptId + same state → idempotent (no error)
        var second = await _fx.Journal.RecordStateAsync(runId, attemptId, "lease-1", RunState.Leased);
        second.IsSuccess.Should().BeTrue(
            "a duplicate RecordStateAsync with the same (runId, attemptId, state) must be idempotent");

        // State after both writes must still be Leased
        var state = await _fx.Journal.GetStateAsync(runId);
        state.IsSuccess.Should().BeTrue();
        state.Value.Should().Be(RunState.Leased);
    }

    [Fact(DisplayName = "CF-03-B: Different attemptId for same runId advances the journal")]
    public async Task DifferentAttemptId_ForSameRunId_AdvancesJournal()
    {
        var runId      = Guid.NewGuid().ToString("D");
        var attemptId1 = Guid.NewGuid().ToString("D");
        var attemptId2 = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId1, "lease-1", RunState.Leased);
        await _fx.Journal.RecordStateAsync(runId, attemptId2, "lease-2", RunState.Running);

        var entry = await _fx.Journal.ReadEntryAsync(runId);
        entry.IsSuccess.Should().BeTrue();
        entry.Value.Should().NotBeNull();
        entry.Value!.AttemptId.Should().Be(attemptId2,
            "the journal must reflect the most-recently written attemptId");
        entry.Value.State.Should().Be(RunState.Running);
    }

    [Fact(DisplayName = "CF-03-C: AcquireLeaseCommandHandler enforces one-lease-per-worker")]
    public async Task AcquireLeaseCommandHandler_PreventsDoubleAcquisition()
    {
        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var handler  = new AcquireLeaseCommandHandler(_fx.Journal, clockMock.Object);

        var workerId  = "worker-fence-" + Guid.NewGuid().ToString("N")[..8];
        var cmd1      = new AcquireLeaseCommand(
            RunId:           Guid.NewGuid().ToString("D"),
            AttemptId:       Guid.NewGuid().ToString("D"),
            WorkerId:        workerId,
            IdempotencyClass: IdempotencyClass.Idempotent);

        var first = await handler.HandleAsync(cmd1);
        first.IsSuccess.Should().BeTrue("the first lease acquisition must succeed");

        // Attempt a second lease for the SAME worker
        var cmd2 = cmd1 with { RunId = Guid.NewGuid().ToString("D"), AttemptId = Guid.NewGuid().ToString("D") };
        var second = await handler.HandleAsync(cmd2);
        second.IsSuccess.Should().BeFalse(
            "a worker that already holds an active lease must not be granted a second one");
        second.Error.Code.Should().Be("Lease.AlreadyHeld");
    }

    [Fact(DisplayName = "CF-03-D: RecordUnknownState is idempotent for same (runId, attemptId, reason)")]
    public async Task RecordUnknownState_SameAttemptId_IsIdempotent()
    {
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");
        const string reason = "test reason CF-03-D";

        var first  = await _fx.Journal.RecordUnknownStateAsync(runId, attemptId, leaseId, reason);
        var second = await _fx.Journal.RecordUnknownStateAsync(runId, attemptId, leaseId, reason);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue(
            "RecordUnknownStateAsync must be idempotent for the same (runId, attemptId, reason)");

        var entry = (await _fx.Journal.ReadEntryAsync(runId)).Value!;
        entry.State.Should().Be(RunState.Unknown);
        entry.NeedsAttention.Should().BeTrue();
        entry.UnknownReason.Should().Be(reason);
    }
}
