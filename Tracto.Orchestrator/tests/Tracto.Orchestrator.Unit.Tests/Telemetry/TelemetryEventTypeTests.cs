#nullable enable
using FluentAssertions;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Tests that <see cref="TelemetryEventType"/> provides complete, non-empty constants
/// and that every <see cref="RunState"/> value has a corresponding telemetry event type.
/// </summary>
public sealed class TelemetryEventTypeTests
{
    // ── All list completeness ─────────────────────────────────────────────────

    [Fact]
    public void All_ContainsAtLeast15EventTypes()
    {
        TelemetryEventType.All.Should().HaveCountGreaterThanOrEqualTo(15,
            because: "the spec defines 15 event type constants");
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        TelemetryEventType.All
            .GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty(because: "every event type must be unique");
    }

    [Fact]
    public void All_ContainsNoNullOrWhitespaceEntries()
    {
        TelemetryEventType.All
            .Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s));
    }

    // ── Individual constant presence ──────────────────────────────────────────

    [Theory]
    [InlineData(TelemetryEventType.RunCreated,        "RUN_CREATED")]
    [InlineData(TelemetryEventType.RunQueued,         "RUN_QUEUED")]
    [InlineData(TelemetryEventType.RunLeased,         "RUN_LEASED")]
    [InlineData(TelemetryEventType.RunRunning,        "RUN_RUNNING")]
    [InlineData(TelemetryEventType.RunSucceeded,      "RUN_SUCCEEDED")]
    [InlineData(TelemetryEventType.RunFailed,         "RUN_FAILED")]
    [InlineData(TelemetryEventType.RunHandedOff,      "RUN_HANDED_OFF")]
    [InlineData(TelemetryEventType.RunCancelled,      "RUN_CANCELLED")]
    [InlineData(TelemetryEventType.RunTimedOut,       "RUN_TIMED_OUT")]
    [InlineData(TelemetryEventType.RunUnknown,        "RUN_UNKNOWN")]
    [InlineData(TelemetryEventType.WorkerRegistered,  "WORKER_REGISTERED")]
    [InlineData(TelemetryEventType.WorkerOffline,     "WORKER_OFFLINE")]
    [InlineData(TelemetryEventType.CallbackReceived,  "CALLBACK_RECEIVED")]
    [InlineData(TelemetryEventType.CallbackAuthFailed,"CALLBACK_AUTH_FAILED")]
    [InlineData(TelemetryEventType.SecurityEvent,     "SECURITY_EVENT")]
    public void Constant_HasExpectedStringValue(string actual, string expected)
    {
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(TelemetryEventType.RunCreated)]
    [InlineData(TelemetryEventType.RunQueued)]
    [InlineData(TelemetryEventType.RunLeased)]
    [InlineData(TelemetryEventType.RunRunning)]
    [InlineData(TelemetryEventType.RunSucceeded)]
    [InlineData(TelemetryEventType.RunFailed)]
    [InlineData(TelemetryEventType.RunHandedOff)]
    [InlineData(TelemetryEventType.RunCancelled)]
    [InlineData(TelemetryEventType.RunTimedOut)]
    [InlineData(TelemetryEventType.RunUnknown)]
    [InlineData(TelemetryEventType.WorkerRegistered)]
    [InlineData(TelemetryEventType.WorkerOffline)]
    [InlineData(TelemetryEventType.CallbackReceived)]
    [InlineData(TelemetryEventType.CallbackAuthFailed)]
    [InlineData(TelemetryEventType.SecurityEvent)]
    public void All_ContainsEachKnownConstant(string eventType)
    {
        TelemetryEventType.All.Should().Contain(eventType);
    }

    // ── RunState ↔ TelemetryEventType mapping ─────────────────────────────────

    /// <summary>
    /// Every RunState value must map to a corresponding TelemetryEventType string.
    /// This test enumerates ALL RunState members and asserts a 1-to-1 mapping exists
    /// in the <see cref="TelemetryEventType.All"/> set.
    /// </summary>
    [Theory]
    [InlineData(RunState.Created,   TelemetryEventType.RunCreated)]
    [InlineData(RunState.Queued,    TelemetryEventType.RunQueued)]
    [InlineData(RunState.Leased,    TelemetryEventType.RunLeased)]
    [InlineData(RunState.Running,   TelemetryEventType.RunRunning)]
    [InlineData(RunState.Succeeded, TelemetryEventType.RunSucceeded)]
    [InlineData(RunState.Failed,    TelemetryEventType.RunFailed)]
    [InlineData(RunState.HandedOff, TelemetryEventType.RunHandedOff)]
    [InlineData(RunState.Cancelled, TelemetryEventType.RunCancelled)]
    [InlineData(RunState.TimedOut,  TelemetryEventType.RunTimedOut)]
    [InlineData(RunState.Unknown,   TelemetryEventType.RunUnknown)]
    public void EveryRunState_HasMappedEventTypeInAll(RunState state, string expectedEventType)
    {
        TelemetryEventType.All.Should().Contain(expectedEventType,
            because: $"RunState.{state} must have a corresponding telemetry event type");
    }

    [Fact]
    public void EveryRunStateValue_HasMappedEventType_ExhaustiveEnumeration()
    {
        // Guard: if someone adds a new RunState enum member, this test catches the gap.
        var allRunStates = Enum.GetValues<RunState>();
        var runStateEventTypeMap = new Dictionary<RunState, string>
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

        foreach (var state in allRunStates)
        {
            runStateEventTypeMap.Should().ContainKey(state,
                because: $"RunState.{state} must have a TelemetryEventType entry; " +
                         "update the map when new RunState values are added");

            TelemetryEventType.All.Should().Contain(runStateEventTypeMap[state],
                because: $"RunState.{state} → '{runStateEventTypeMap[state]}' must be in TelemetryEventType.All");
        }
    }
}
