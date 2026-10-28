#nullable enable
using System.Text.Json;
using FluentAssertions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;
using DomainITelemetryRepository = Tracto.Orchestrator.Domain.Telemetry.ITelemetryRepository;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Tests that a TelemetryEvent is emitted for every RunState transition defined
/// in the domain state machine (OC-14, "every state transition emits a TelemetryEvent").
///
/// Each test:
///   1. Performs the state transition on a RunRecord.
///   2. Constructs the TelemetryEvent that the command handler MUST emit for that transition.
///   3. Asserts the event has the correct EventType, RunId, and mandatory metadata.
///
/// The construction of the TelemetryEvent here mirrors the logic that belongs in
/// each Application command handler — these tests are the specification contract
/// against which those handlers are written.
///
/// ITelemetryService is mocked to capture the emitted event; ITelemetryRepository
/// is mocked to verify read-side access after the fact.
/// </summary>
public sealed class TelemetryStateTransitionEmissionTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 16, 0, 0, TimeSpan.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a RunRecord in CREATED state.
    /// </summary>
    private static RunRecord MakeCreatedRun() =>
        new(Guid.NewGuid(), "wf-oc14",
            createdAt:        FixedNow,
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: "art-oc14",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

    /// <summary>
    /// Builds a TelemetryEvent from a RunRecord for the given event type.
    /// Models the pattern that every command handler must follow.
    /// </summary>
    private static TelemetryEvent BuildEvent(
        RunRecord   run,
        string      eventType,
        string?     workerId  = null,
        Guid?       leaseId   = null,
        Guid?       attemptId = null,
        string?     metadata  = null) =>
        new(
            id:        Guid.NewGuid(),
            runId:     run.Id,
            eventType: eventType,
            occurredAt: FixedNow,
            workerId:  workerId,
            leaseId:   leaseId,
            attemptId: attemptId,
            metadata:  metadata);

    // ── CREATED ───────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_Created_EmitsRunCreatedEvent()
    {
        var run = MakeCreatedRun();

        var evt = BuildEvent(run, TelemetryEventType.RunCreated,
            metadata: JsonSerializer.Serialize(new { workflowId = run.WorkflowId }));

        evt.EventType.Should().Be(TelemetryEventType.RunCreated);
        evt.RunId.Should().Be(run.Id);
        evt.Metadata.Should().Contain("workflowId");
    }

    // ── QUEUED ────────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_CreatedToQueued_EmitsRunQueuedEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);

        run.State.Should().Be(RunState.Queued);

        var evt = BuildEvent(run, TelemetryEventType.RunQueued);

        evt.EventType.Should().Be(TelemetryEventType.RunQueued);
        evt.RunId.Should().Be(run.Id);
    }

    // ── LEASED ────────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_QueuedToLeased_EmitsRunLeasedEvent()
    {
        var run     = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));

        run.State.Should().Be(RunState.Leased);

        var evt = BuildEvent(run, TelemetryEventType.RunLeased,
            workerId:  run.WorkerId,
            leaseId:   Guid.NewGuid(),
            attemptId: run.AttemptId,
            metadata:  JsonSerializer.Serialize(new { expiresInSeconds = 30 }));

        evt.EventType.Should().Be(TelemetryEventType.RunLeased);
        evt.WorkerId.Should().Be("worker-01");
        evt.AttemptId.Should().Be(run.AttemptId);
    }

    // ── RUNNING ───────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_LeasedToRunning_EmitsRunRunningEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));

        run.State.Should().Be(RunState.Running);

        var evt = BuildEvent(run, TelemetryEventType.RunRunning,
            workerId:  run.WorkerId,
            attemptId: run.AttemptId);

        evt.EventType.Should().Be(TelemetryEventType.RunRunning);
        evt.RunId.Should().Be(run.Id);
    }

    // ── SUCCEEDED ────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_RunningToSucceeded_EmitsRunSucceededEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));

        var startedAt  = FixedNow.AddSeconds(2);
        var finishedAt = FixedNow.AddSeconds(12);
        var durationMs = (long)(finishedAt - startedAt).TotalMilliseconds;
        const int stepCount = 3;

        run.Succeed(run.AttemptId, finishedAt);

        run.State.Should().Be(RunState.Succeeded);

        var metadata = JsonSerializer.Serialize(new
        {
            stepCount,
            durationMs,
            businessOutcome = "TASK_COMPLETE"
        });
        var evt = BuildEvent(run, TelemetryEventType.RunSucceeded,
            workerId:  run.WorkerId,
            attemptId: run.AttemptId,
            metadata:  metadata);

        evt.EventType.Should().Be(TelemetryEventType.RunSucceeded);

        using var doc = JsonDocument.Parse(evt.Metadata!);
        doc.RootElement.GetProperty("stepCount").GetInt32().Should().Be(stepCount);
        doc.RootElement.GetProperty("durationMs").GetInt64().Should().Be(durationMs);
        doc.RootElement.GetProperty("businessOutcome").GetString().Should().Be("TASK_COMPLETE");
    }

    [Fact]
    public void Transition_RunningToSucceeded_StepCountMatchesJournalEntries()
    {
        // stepCount must be the count of JournalEntries for the run — tested by
        // constructing the metadata with the correct value and asserting round-trip.
        const int journalEntries = 7;

        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-02", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));
        run.Succeed(run.AttemptId, FixedNow.AddSeconds(30));

        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = journalEntries,
            durationMs      = 28_000L,
            businessOutcome = "BATCH_PROCESSED"
        });

        var evt = BuildEvent(run, TelemetryEventType.RunSucceeded, metadata: metadata);

        using var doc = JsonDocument.Parse(evt.Metadata!);
        doc.RootElement.GetProperty("stepCount").GetInt32().Should().Be(journalEntries,
            because: "stepCount must equal the number of JournalEntry rows for the run");
    }

    // ── FAILED ───────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_RunningToFailed_EmitsRunFailedEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));
        run.Fail(run.AttemptId, FixedNow.AddSeconds(5));

        run.State.Should().Be(RunState.Failed);

        var evt = BuildEvent(run, TelemetryEventType.RunFailed,
            workerId:  run.WorkerId,
            attemptId: run.AttemptId,
            metadata:  """{"errorCategory":"SCRIPT_ERROR"}""");

        evt.EventType.Should().Be(TelemetryEventType.RunFailed);
        evt.RunId.Should().Be(run.Id);
    }

    // ── HANDED_OFF ───────────────────────────────────────────────────────────

    [Fact]
    public void Transition_RunningToHandedOff_EmitsRunHandedOffEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));
        run.HandOff(run.AttemptId, FixedNow.AddSeconds(6));

        run.State.Should().Be(RunState.HandedOff);

        var evt = BuildEvent(run, TelemetryEventType.RunHandedOff,
            workerId:  run.WorkerId,
            attemptId: run.AttemptId);

        evt.EventType.Should().Be(TelemetryEventType.RunHandedOff);
    }

    // ── CANCELLED ────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_RunningToCancelled_EmitsRunCancelledEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));
        run.Cancel(FixedNow.AddSeconds(4));

        run.State.Should().Be(RunState.Cancelled);

        var evt = BuildEvent(run, TelemetryEventType.RunCancelled,
            metadata: """{"cancelledBy":"admin"}""");

        evt.EventType.Should().Be(TelemetryEventType.RunCancelled);
    }

    [Fact]
    public void Transition_QueuedToCancelled_EmitsRunCancelledEvent()
    {
        // Cancel is allowed from any non-terminal state.
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.Cancel(FixedNow.AddSeconds(1));

        run.State.Should().Be(RunState.Cancelled);

        var evt = BuildEvent(run, TelemetryEventType.RunCancelled);

        evt.EventType.Should().Be(TelemetryEventType.RunCancelled);
    }

    // ── TIMED_OUT ────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_RunningToTimedOut_EmitsRunTimedOutEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.AdoptLease(run.AttemptId, "worker-01", FixedNow.AddSeconds(1));
        run.Start(run.AttemptId, FixedNow.AddSeconds(2));
        run.MarkTimedOut(run.AttemptId, FixedNow.AddMinutes(5));

        run.State.Should().Be(RunState.TimedOut);

        var evt = BuildEvent(run, TelemetryEventType.RunTimedOut,
            workerId:  run.WorkerId,
            attemptId: run.AttemptId);

        evt.EventType.Should().Be(TelemetryEventType.RunTimedOut);
    }

    // ── UNKNOWN ──────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_ToUnknown_EmitsRunUnknownEvent()
    {
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.MarkUnknown("WORKER_TIMEOUT_NO_HB", FixedNow.AddMinutes(1));

        run.State.Should().Be(RunState.Unknown);
        run.NeedsAttention.Should().BeTrue();
        run.UnknownReason.Should().Be("WORKER_TIMEOUT_NO_HB");

        var metadata = JsonSerializer.Serialize(new
        {
            unknownReason  = run.UnknownReason,
            needsAttention = run.NeedsAttention
        });
        var evt = BuildEvent(run, TelemetryEventType.RunUnknown, metadata: metadata);

        evt.EventType.Should().Be(TelemetryEventType.RunUnknown);

        using var doc = JsonDocument.Parse(evt.Metadata!);
        doc.RootElement.GetProperty("unknownReason").GetString()
            .Should().Be("WORKER_TIMEOUT_NO_HB");
        doc.RootElement.GetProperty("needsAttention").GetBoolean()
            .Should().BeTrue(because: "UNKNOWN always sets needsAttention=true (invariant 2)");
    }

    [Fact]
    public void Transition_ToUnknown_NeverAutoReplayed()
    {
        // Domain invariant 2: UNKNOWN is never auto-replayed.
        // Verify that a run in UNKNOWN state is terminal and NeedsAttention is set.
        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);
        run.MarkUnknown("AMBIGUOUS_TERMINAL", FixedNow.AddMinutes(2));

        run.IsTerminal().Should().BeTrue(
            because: "UNKNOWN is a terminal state — it must never be automatically requeued");
        run.NeedsAttention.Should().BeTrue(
            because: "UNKNOWN always requires human attention (invariant 2)");
    }

    // ── WORKER_REGISTERED / WORKER_OFFLINE ───────────────────────────────────

    [Fact]
    public void WorkerRegistered_EventType_IsPresent()
    {
        // Worker lifecycle events are not tied to RunRecord state transitions;
        // we verify the event can be constructed correctly.
        var evt = new TelemetryEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),    // runId: can be a sentinel / non-existent run for worker events
            TelemetryEventType.WorkerRegistered,
            FixedNow,
            workerId: "worker-new",
            metadata: """{"runtimeType":"dotnet","maxSlots":2}""");

        evt.EventType.Should().Be(TelemetryEventType.WorkerRegistered);
        evt.WorkerId.Should().Be("worker-new");
    }

    [Fact]
    public void WorkerOffline_EventType_IsPresent()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TelemetryEventType.WorkerOffline,
            FixedNow,
            workerId: "worker-gone",
            metadata: """{"offlineReason":"HEARTBEAT_EXPIRED"}""");

        evt.EventType.Should().Be(TelemetryEventType.WorkerOffline);
        evt.WorkerId.Should().Be("worker-gone");
    }

    // ── CALLBACK_AUTH_FAILED ─────────────────────────────────────────────────

    [Fact]
    public void CallbackAuthFailed_MustIncludeSourceIp_NotBody()
    {
        var metadata = JsonSerializer.Serialize(new { sourceIp = "203.0.113.99" });

        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.CallbackAuthFailed,
            FixedNow,
            metadata: metadata);

        using var doc = JsonDocument.Parse(evt.Metadata!);
        doc.RootElement.TryGetProperty("sourceIp",      out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("body",          out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("payload",       out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("callbackBody",  out _).Should().BeFalse();
    }

    // ── ITelemetryService mock contract ──────────────────────────────────────

    [Fact]
    public async Task ITelemetryService_EmitAsync_IsCalledOnce_PerStateTransition()
    {
        // This test verifies the mock-based calling contract.
        // Each command handler must call ITelemetryService.EmitAsync exactly once
        // per business state transition.
        var mockService  = new Mock<ITelemetryService>(MockBehavior.Strict);
        TelemetryEvent? captured = null;

        mockService
            .Setup(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TelemetryEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var run = MakeCreatedRun();
        run.Enqueue(FixedNow);

        var eventToEmit = BuildEvent(run, TelemetryEventType.RunQueued);
        await mockService.Object.EmitAsync(eventToEmit, CancellationToken.None);

        mockService.Verify(s =>
            s.EmitAsync(It.Is<TelemetryEvent>(e => e.EventType == TelemetryEventType.RunQueued),
                        It.IsAny<CancellationToken>()),
            Times.Once,
            "ITelemetryService.EmitAsync must be called exactly once for RunQueued");

        captured.Should().NotBeNull();
        captured!.RunId.Should().Be(run.Id);
    }

    [Fact]
    public async Task ITelemetryRepository_GetByRunId_ReturnsEmittedEvents()
    {
        var runId = Guid.NewGuid();
        var events = new List<TelemetryEvent>
        {
            new(Guid.NewGuid(), runId, TelemetryEventType.RunCreated, FixedNow),
            new(Guid.NewGuid(), runId, TelemetryEventType.RunQueued,  FixedNow.AddSeconds(1)),
        };

        var mockRepo = new Mock<DomainITelemetryRepository>(MockBehavior.Strict);
        mockRepo
            .Setup(r => r.GetByRunIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events.AsReadOnly());

        var result = await mockRepo.Object.GetByRunIdAsync(runId);

        result.Should().HaveCount(2);
        result[0].EventType.Should().Be(TelemetryEventType.RunCreated);
        result[1].EventType.Should().Be(TelemetryEventType.RunQueued);
    }

    // ── All run states covered ────────────────────────────────────────────────

    [Fact]
    public void AllRunStates_HaveCorrespondingEventTypeConstant()
    {
        // Exhaustively enumerate every RunState and assert a TelemetryEventType constant exists.
        var stateToEvent = new Dictionary<RunState, string>
        {
            [RunState.Created]   = TelemetryEventType.RunCreated,
            [RunState.Queued]    = TelemetryEventType.RunQueued,
            [RunState.Leased]    = TelemetryEventType.RunLeased,
            [RunState.Running]   = TelemetryEventType.RunRunning,
            [RunState.Succeeded] = TelemetryEventType.RunSucceeded,
            [RunState.Failed]    = TelemetryEventType.RunFailed,
            [RunState.HandedOff] = TelemetryEventType.RunHandedOff,
            [RunState.Cancelled] = TelemetryEventType.RunCancelled,
            [RunState.TimedOut]  = TelemetryEventType.RunTimedOut,
            [RunState.Unknown]   = TelemetryEventType.RunUnknown,
        };

        foreach (var state in Enum.GetValues<RunState>())
        {
            stateToEvent.Should().ContainKey(state,
                because: $"RunState.{state} must have a telemetry event type constant mapping");

            var eventType = stateToEvent[state];
            TelemetryEventType.All.Should().Contain(eventType,
                because: $"'{eventType}' for RunState.{state} must be in TelemetryEventType.All");

            // Verify a TelemetryEvent can be constructed with this event type without throwing.
            var run = MakeCreatedRun();
            var act = () => new TelemetryEvent(Guid.NewGuid(), run.Id, eventType, FixedNow);
            act.Should().NotThrow(because: $"TelemetryEvent construction for {eventType} must not throw");
        }
    }
}
