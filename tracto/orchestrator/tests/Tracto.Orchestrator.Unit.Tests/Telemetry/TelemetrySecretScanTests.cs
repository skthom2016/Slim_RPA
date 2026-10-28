#nullable enable
using System.Text.RegularExpressions;
using FluentAssertions;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Secret-scan tests for telemetry events (OC-14, domain invariant 4).
///
/// Rules:
///   - No plaintext secret or domain payload must appear in any TelemetryEvent field.
///   - Any secret reference must be stored as the literal string "REDACTED".
///   - CALLBACK_AUTH_FAILED: sourceIp only; callback body absent.
///
/// The tests simulate the "write a batch of events to an in-memory store, then
/// scan every event for secret patterns" workflow described in the spec.
/// </summary>
public sealed class TelemetrySecretScanTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    // ── Patterns that must NEVER appear in any event field ────────────────────

    private static readonly Regex[] SecretPatterns =
    [
        new(@"(?i)(password|passwd|pwd)[""'\s]*[:=][""'\s]*\S+",             RegexOptions.Compiled),
        new(@"(?i)(secret|api[_-]?key|apikey|token)[""'\s]*[:=][""'\s]*\S+", RegexOptions.Compiled),
        new(@"(?i)(auth[_-]?token|bearer)\s+[A-Za-z0-9\-._~+/]+=*",         RegexOptions.Compiled),
        new(@"(?i)(private[_-]?key|-----BEGIN)",                              RegexOptions.Compiled),
        new(@"(?i)aws[_-]?(secret[_-]?access[_-]?key|secret|access[_-]?key)",RegexOptions.Compiled),
    ];

    private static bool ContainsSecret(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return SecretPatterns.Any(p => p.IsMatch(text));
    }

    private static void AssertNoSecretIn(TelemetryEvent evt)
    {
        ContainsSecret(evt.WorkerId).Should().BeFalse(
            because: $"WorkerId of event {evt.EventType} must not contain secret material");
        ContainsSecret(evt.Metadata).Should().BeFalse(
            because: $"Metadata of event {evt.EventType} must not contain plaintext secrets");
        ContainsSecret(evt.EventType).Should().BeFalse(
            because: $"EventType itself must not contain secret material");
    }

    // ── Individual event secret scans ─────────────────────────────────────────

    [Fact]
    public void RunCreated_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunCreated, FixedNow,
            metadata: """{"workflowId":"wf-001","triggerType":"Manual"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunQueued_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunQueued, FixedNow,
            metadata: """{"queuedAt":"2026-10-24T15:00:00Z"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunLeased_Event_ContainsNoSecret()
    {
        var attemptId = Guid.NewGuid();
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunLeased, FixedNow,
            workerId: "worker-prod-01",
            leaseId:  Guid.NewGuid(),
            attemptId: attemptId,
            metadata: $"""{"{"}"attemptId":"{attemptId}","expiresInSeconds":30{"}"} """);

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunRunning_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunRunning, FixedNow,
            workerId: "worker-prod-02",
            metadata: """{"startedAt":"2026-10-24T15:00:01Z"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunSucceeded_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunSucceeded, FixedNow,
            metadata: """{"stepCount":4,"durationMs":8000,"businessOutcome":"ORDER_CONFIRMED"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunFailed_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunFailed, FixedNow,
            metadata: """{"errorCategory":"SCRIPT_ERROR","errorMessage":"Index out of range"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunHandedOff_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunHandedOff, FixedNow,
            metadata: """{"handoffTarget":"downstream-system"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunCancelled_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunCancelled, FixedNow,
            metadata: """{"cancelledBy":"admin","reason":"Manual cancellation"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunTimedOut_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunTimedOut, FixedNow,
            metadata: """{"deadlineUtc":"2026-10-24T14:59:00Z"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void RunUnknown_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunUnknown, FixedNow,
            metadata: """{"unknownReason":"WORKER_TIMEOUT_NO_HB","needsAttention":true}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void WorkerRegistered_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.WorkerRegistered, FixedNow,
            workerId: "worker-new-01",
            metadata: """{"runtimeType":"dotnet","capabilities":["screen","keyboard"]}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void WorkerOffline_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.WorkerOffline, FixedNow,
            workerId: "worker-offline-01",
            metadata: """{"offlineReason":"HEARTBEAT_EXPIRED"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void CallbackReceived_Event_ContainsNoSecret()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.CallbackReceived, FixedNow,
            metadata: """{"callbackId":"cb-001","authenticated":true}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void CallbackAuthFailed_Event_ContainsNoSecret()
    {
        // Only sourceIp in metadata — body explicitly absent.
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.CallbackAuthFailed, FixedNow,
            metadata: """{"sourceIp":"198.51.100.42"}""");

        AssertNoSecretIn(evt);
    }

    [Fact]
    public void SecurityEvent_WithRedactedSecretRef_ContainsNoSecret()
    {
        // Secret value replaced with "REDACTED" — scanner must not flag it.
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.SecurityEvent, FixedNow,
            metadata: """{"eventDetail":"Secret scan violation","secretRef":"REDACTED"}""");

        AssertNoSecretIn(evt);
    }

    // ── Batch scan: entire event store must be clean ──────────────────────────

    [Fact]
    public void AllEventsInBatch_HaveNoSecretMaterial()
    {
        // Simulate the full set of events that would appear in the DB for a single run.
        var runId    = Guid.NewGuid();
        var workerId = "worker-batch-01";
        var leaseId  = Guid.NewGuid();
        var attempt  = Guid.NewGuid();

        var events = new List<TelemetryEvent>
        {
            new(Guid.NewGuid(), runId, TelemetryEventType.RunCreated,   FixedNow,
                metadata: """{"workflowId":"wf-batch","triggerType":"Scheduled"}"""),

            new(Guid.NewGuid(), runId, TelemetryEventType.RunQueued,    FixedNow.AddSeconds(1),
                metadata: null),

            new(Guid.NewGuid(), runId, TelemetryEventType.RunLeased,    FixedNow.AddSeconds(2),
                workerId: workerId, leaseId: leaseId, attemptId: attempt,
                metadata: """{"expiresInSeconds":30}"""),

            new(Guid.NewGuid(), runId, TelemetryEventType.RunRunning,   FixedNow.AddSeconds(3),
                workerId: workerId, leaseId: leaseId, attemptId: attempt,
                metadata: null),

            new(Guid.NewGuid(), runId, TelemetryEventType.RunSucceeded, FixedNow.AddSeconds(10),
                workerId: workerId, leaseId: leaseId, attemptId: attempt,
                metadata: """{"stepCount":2,"durationMs":7000,"businessOutcome":"BATCH_DONE"}"""),
        };

        foreach (var evt in events)
            AssertNoSecretIn(evt);
    }

    // ── Negative tests: secret-containing metadata must be detectable ─────────

    [Theory]
    [InlineData("""{"password":"s3cr3t!"}""")]
    [InlineData("""{"api_key":"AKIAIOSFODNN7EXAMPLE"}""")]
    [InlineData("""{"token":"eyJhbGciOiJIUzI1NiJ9.payload.sig"}""")]
    [InlineData("""{"secret":"topsecretvalue"}""")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    public void SecretScanner_DetectsKnownSecretPatterns(string maliciousMetadata)
    {
        // This is a negative test: we verify the scanner itself catches secret patterns.
        // In production, the handler must reject such payloads before constructing the event.
        ContainsSecret(maliciousMetadata).Should().BeTrue(
            because: $"'{maliciousMetadata}' must be flagged as containing secret material");
    }

    [Theory]
    [InlineData("""{"stepCount":5,"durationMs":1234,"businessOutcome":"OK"}""")]
    [InlineData("""{"sourceIp":"10.0.0.1"}""")]
    [InlineData("""{"unknownReason":"TIMEOUT","needsAttention":true}""")]
    [InlineData("""{"secretRef":"REDACTED"}""")]
    [InlineData(null)]
    public void SecretScanner_DoesNotFlagCleanMetadata(string? metadata)
    {
        ContainsSecret(metadata).Should().BeFalse(
            because: "clean metadata must not produce false positives");
    }
}
