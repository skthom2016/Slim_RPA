using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure.Cleanup;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-08 — Handoff: HANDED_OFF + Static Continuation Only
///
/// CEC v1.0.1 / TER-US-019 invariants:
///   - A run may transition to HANDED_OFF via <c>ServiceHandoffHandler</c>.
///   - The ContinuationMappingRef stored on the handoff result is a static key
///     resolved from <c>IStaticContinuationMappingStore</c> — it is NEVER a
///     dynamic scriptRef supplied inline by the Orchestrator payload.
///   - The Terminal worker is released immediately; Run B is NOT created by the
///     handoff handler.
///   - HANDED_OFF must be persisted before the method returns.
/// </summary>
public sealed class CF08_HandoffTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF08_HandoffTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-08-A: HANDED_OFF is persisted to the journal")]
    public async Task HandledOff_IsPersistedToJournal()
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

        var result = await handler.HandleHandoffAsync(runId, attemptId, leaseId, "static-map-ref-A");

        result.IsSuccess.Should().BeTrue();

        var state = (await _fx.Journal.GetStateAsync(runId)).Value;
        state.Should().Be(RunState.HandedOff, "HANDED_OFF must be persisted before the method returns");
    }

    [Fact(DisplayName = "CF-08-B: ContinuationMappingRef is preserved as a static key (no dynamic scriptRef)")]
    public async Task HandledOff_PreservesStaticContinuationRef()
    {
        const string staticRef = "continuation-mapping-static-key-cf08b";
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

        var result = await handler.HandleHandoffAsync(runId, attemptId, leaseId, staticRef);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContinuationMappingRef.Should().Be(staticRef,
            "the handoff result must carry the same static continuation key that was passed in");
    }

    [Fact(DisplayName = "CF-08-C: Handoff with null ContinuationMappingRef is valid")]
    public async Task HandledOff_NullContinuationRef_IsValid()
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

        // Null continuation means no continuation is configured; still a valid handoff.
        var result = await handler.HandleHandoffAsync(runId, attemptId, leaseId, continuationMappingRef: null);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContinuationMappingRef.Should().BeNull(
            "a handoff without a continuation mapping is legal");
        (await _fx.Journal.GetStateAsync(runId)).Value.Should().Be(RunState.HandedOff);
    }

    [Fact(DisplayName = "CF-08-D: Handoff fails with validation error when runId is empty")]
    public async Task HandledOff_FailsGracefully_WhenRunIdIsEmpty()
    {
        var clockMock = new Mock<IClock>();
        var handler   = new ServiceHandoffHandler(
            _fx.Journal,
            clockMock.Object,
            NullLogger<ServiceHandoffHandler>.Instance);

        var result = await handler.HandleHandoffAsync(string.Empty, "attempt-1", "lease-1", null);

        result.IsFailure.Should().BeTrue("runId is a required parameter");
        result.Error.Kind.Should().Be(Application.Common.ErrorKind.Validation);
    }

    [Fact(DisplayName = "CF-08-E: HandedOffAt is set from IClock.UtcNow (not system time)")]
    public async Task HandledOff_UsesClockForTimestamp()
    {
        var frozen    = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var runId     = Guid.NewGuid().ToString("D");
        var attemptId = Guid.NewGuid().ToString("D");
        var leaseId   = Guid.NewGuid().ToString("D");

        await _fx.Journal.RecordStateAsync(runId, attemptId, leaseId, RunState.Running);

        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(frozen);

        var handler = new ServiceHandoffHandler(
            _fx.Journal,
            clockMock.Object,
            NullLogger<ServiceHandoffHandler>.Instance);

        var result = await handler.HandleHandoffAsync(runId, attemptId, leaseId, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.HandedOffAt.Should().Be(frozen,
            "HandedOffAt must be sourced from IClock.UtcNow to allow deterministic testing");
    }
}
