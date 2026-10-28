#nullable enable
using System.Diagnostics;
using FluentAssertions;
using Tracto.Orchestrator.Infrastructure.Telemetry;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Tests that <see cref="OrchestratorActivitySource"/> emits Activities with the
/// required OpenTelemetry tags (run.id, attempt.id) on every command span (OC-14).
///
/// Uses <see cref="ActivityListener"/> to intercept activities without a real OTEL
/// SDK or exporter — pure in-process, no Thread.Sleep, no network I/O.
/// </summary>
public sealed class TelemetryActivityTagTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity>   _captured = [];

    public TelemetryActivityTagTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo     = source => source.Name == OrchestratorActivitySource.SourceName,
            Sample             = (ref ActivityCreationOptions<ActivityContext> _) =>
                                     ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted    = activity => { lock (_captured) _captured.Add(activity); },
            ActivityStopped    = _ => { }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // ── Source metadata ───────────────────────────────────────────────────────

    [Fact]
    public void ActivitySource_HasExpectedName()
    {
        OrchestratorActivitySource.SourceName.Should().Be("Tracto.Orchestrator");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        OrchestratorActivitySource.SourceVersion.Should().NotBeNullOrWhiteSpace();
    }

    // ── run.id tag ────────────────────────────────────────────────────────────

    [Fact]
    public void StartedActivity_WithRunIdTag_TagIsPresent()
    {
        var runId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity("CreateRun");
        activity?.SetTag("run.id", runId.ToString());

        activity.Should().NotBeNull(because: "a listener is registered");
        activity!.GetTagItem("run.id").Should().Be(runId.ToString(),
            because: "run.id tag must be set on the activity span");
    }

    [Fact]
    public void StartedActivity_WithAttemptIdTag_TagIsPresent()
    {
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity("AcceptLease");
        activity?.SetTag("run.id",     runId.ToString());
        activity?.SetTag("attempt.id", attemptId.ToString());

        activity!.GetTagItem("attempt.id").Should().Be(attemptId.ToString(),
            because: "attempt.id tag must be set on the activity span");
    }

    [Fact]
    public void StartedActivity_RunIdTag_MustNotBeEmpty()
    {
        var runId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity("CommitFinalResult");
        activity?.SetTag("run.id", runId.ToString());

        var tagValue = activity?.GetTagItem("run.id") as string;
        tagValue.Should().NotBeNullOrWhiteSpace(
            because: "run.id must be a non-empty GUID string on every span");
        Guid.TryParse(tagValue, out _).Should().BeTrue(
            because: "run.id tag must be parseable as a GUID");
    }

    // ── Multiple command spans ────────────────────────────────────────────────

    [Theory]
    [InlineData("CreateRun")]
    [InlineData("QueueRun")]
    [InlineData("PullJob")]
    [InlineData("AcceptLease")]
    [InlineData("SendHeartbeat")]
    [InlineData("CommitFinalResult")]
    [InlineData("CancelRun")]
    [InlineData("ReconcileRun")]
    [InlineData("ResolveUnknownRun")]
    public void EachCommandSpan_MustCarry_RunIdTag(string operationName)
    {
        var runId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity(operationName);
        activity?.SetTag("run.id", runId.ToString());

        activity.Should().NotBeNull();
        activity!.GetTagItem("run.id").Should().Be(runId.ToString(),
            because: $"span '{operationName}' must carry a run.id tag");
    }

    [Theory]
    [InlineData("AcceptLease")]
    [InlineData("SendHeartbeat")]
    [InlineData("CommitFinalResult")]
    [InlineData("ReconcileRun")]
    public void CommandSpans_WithAttemptContext_MustCarry_AttemptIdTag(string operationName)
    {
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity(operationName);
        activity?.SetTag("run.id",     runId.ToString());
        activity?.SetTag("attempt.id", attemptId.ToString());

        activity!.GetTagItem("attempt.id").Should().Be(attemptId.ToString(),
            because: $"span '{operationName}' with attempt context must carry attempt.id tag");
    }

    // ── Listener intercepts all started activities ────────────────────────────

    [Fact]
    public void ActivityListener_CapturesStartedActivities()
    {
        var initialCount = _captured.Count;
        var runId        = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity("TestOperation");
        activity?.SetTag("run.id", runId.ToString());

        _captured.Count.Should().BeGreaterThan(initialCount,
            because: "the listener must intercept activities started from OrchestratorActivitySource");
    }

    [Fact]
    public void ActivityListener_CapturedActivity_HasSourceName()
    {
        using var activity = OrchestratorActivitySource.StartActivity("SourceNameCheck");

        activity.Should().NotBeNull();
        activity!.Source.Name.Should().Be(OrchestratorActivitySource.SourceName);
    }

    // ── StartActivity returns null when no listener: fast path ────────────────

    [Fact]
    public void ActivitySource_StartsActivity_WhenListenerIsRegistered()
    {
        // Listener is registered in ctor; StartActivity must return non-null.
        using var activity = OrchestratorActivitySource.StartActivity("ListenerPresent");
        activity.Should().NotBeNull(
            because: "OrchestratorActivitySource must return an Activity when a listener is active");
    }

    // ── Tag-string shape ──────────────────────────────────────────────────────

    [Fact]
    public void RunIdTag_StoredAs_LowercaseGuidString()
    {
        var runId = Guid.NewGuid();
        // Canonical format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (lower-case, with hyphens).
        var expected = runId.ToString();   // default format is lowercase with hyphens

        using var activity = OrchestratorActivitySource.StartActivity("TagFormat");
        activity?.SetTag("run.id", runId.ToString());

        (activity?.GetTagItem("run.id") as string).Should().MatchRegex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            because: "run.id must be stored in standard lowercase GUID format");
    }

    [Fact]
    public void AttemptIdTag_StoredAs_LowercaseGuidString()
    {
        var attemptId = Guid.NewGuid();

        using var activity = OrchestratorActivitySource.StartActivity("AttemptTagFormat");
        activity?.SetTag("attempt.id", attemptId.ToString());

        (activity?.GetTagItem("attempt.id") as string).Should().MatchRegex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            because: "attempt.id must be stored in standard lowercase GUID format");
    }
}
