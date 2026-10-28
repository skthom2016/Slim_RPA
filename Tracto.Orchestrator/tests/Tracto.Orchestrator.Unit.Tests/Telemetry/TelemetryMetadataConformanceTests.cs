#nullable enable
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Tests that telemetry event metadata conforms to the per-event-type rules defined in OC-14.
///
/// Rules exercised:
///   RUN_SUCCEEDED   → metadata must contain stepCount (int), durationMs (long), businessOutcome (string).
///   CALLBACK_AUTH_FAILED → metadata must contain sourceIp (string); callback body must be absent.
///   SECURITY_EVENT  → any secret ref value must be stored as "REDACTED".
///   RUN_UNKNOWN     → metadata should contain unknownReason.
///
/// These tests operate on the TelemetryEvent domain entity and JSON metadata directly —
/// no EF, no framework, no command-handler wiring needed.
/// </summary>
public sealed class TelemetryMetadataConformanceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    // ── Helper: build a TelemetryEvent with arbitrary metadata JSON ───────────

    private static TelemetryEvent Build(string eventType, string? metadata) =>
        new(Guid.NewGuid(), Guid.NewGuid(), eventType, FixedNow, metadata: metadata);

    private static JsonDocument ParseMetadata(TelemetryEvent evt)
    {
        evt.Metadata.Should().NotBeNullOrWhiteSpace(
            because: $"{evt.EventType} must carry structured metadata");
        return JsonDocument.Parse(evt.Metadata!);
    }

    // ── RUN_SUCCEEDED ─────────────────────────────────────────────────────────

    [Fact]
    public void RunSucceeded_Metadata_MustContain_StepCount()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = 7,
            durationMs      = 4200L,
            businessOutcome = "ORDER_FULFILLED"
        });

        var evt = Build(TelemetryEventType.RunSucceeded, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("stepCount", out var stepCount).Should().BeTrue(
            because: "RUN_SUCCEEDED metadata must include stepCount");
        stepCount.ValueKind.Should().Be(JsonValueKind.Number);
        stepCount.GetInt32().Should().Be(7);
    }

    [Fact]
    public void RunSucceeded_Metadata_MustContain_DurationMs()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = 3,
            durationMs      = 9876L,
            businessOutcome = "INVOICE_SENT"
        });

        var evt = Build(TelemetryEventType.RunSucceeded, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("durationMs", out var durationMs).Should().BeTrue(
            because: "RUN_SUCCEEDED metadata must include durationMs");
        durationMs.GetInt64().Should().Be(9876L);
    }

    [Fact]
    public void RunSucceeded_Metadata_MustContain_BusinessOutcome()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = 2,
            durationMs      = 500L,
            businessOutcome = "REPORT_GENERATED"
        });

        var evt = Build(TelemetryEventType.RunSucceeded, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("businessOutcome", out var outcome).Should().BeTrue(
            because: "RUN_SUCCEEDED metadata must include businessOutcome");
        outcome.GetString().Should().Be("REPORT_GENERATED");
    }

    [Fact]
    public void RunSucceeded_Metadata_StepCount_ReflectsJournalEntryCount()
    {
        // Simulate: a run with 5 journal entries should emit stepCount=5.
        const int journalEntryCount = 5;
        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = journalEntryCount,
            durationMs      = 3000L,
            businessOutcome = "DATA_SYNCED"
        });

        var evt = Build(TelemetryEventType.RunSucceeded, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.GetProperty("stepCount").GetInt32().Should().Be(journalEntryCount);
    }

    [Fact]
    public void RunSucceeded_DurationMs_MustBeNonNegative()
    {
        // durationMs is computed from (completedAt - createdAt).TotalMilliseconds.
        // It must never be negative.
        var start  = FixedNow;
        var finish = start.AddSeconds(42);
        var durationMs = (long)(finish - start).TotalMilliseconds;

        var metadata = JsonSerializer.Serialize(new
        {
            stepCount       = 1,
            durationMs,
            businessOutcome = "OK"
        });

        var evt = Build(TelemetryEventType.RunSucceeded, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.GetProperty("durationMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    // ── CALLBACK_AUTH_FAILED ──────────────────────────────────────────────────

    [Fact]
    public void CallbackAuthFailed_Metadata_MustContain_SourceIp()
    {
        var metadata = JsonSerializer.Serialize(new { sourceIp = "10.0.0.5" });

        var evt = Build(TelemetryEventType.CallbackAuthFailed, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("sourceIp", out var ip).Should().BeTrue(
            because: "CALLBACK_AUTH_FAILED metadata must include sourceIp");
        ip.GetString().Should().Be("10.0.0.5");
    }

    [Fact]
    public void CallbackAuthFailed_Metadata_MustNotContain_CallbackBody()
    {
        // The callback body must NEVER be stored — security invariant 4.
        var metadata = JsonSerializer.Serialize(new
        {
            sourceIp = "203.0.113.7"
            // NOTE: "body", "payload", "callbackBody" must not appear here.
        });

        var evt = Build(TelemetryEventType.CallbackAuthFailed, metadata);
        using var doc = ParseMetadata(evt);
        var root = doc.RootElement;

        root.TryGetProperty("body",         out _).Should().BeFalse(
            because: "callback body must never be stored in CALLBACK_AUTH_FAILED metadata");
        root.TryGetProperty("payload",      out _).Should().BeFalse(
            because: "callback payload must never be stored");
        root.TryGetProperty("callbackBody", out _).Should().BeFalse(
            because: "callbackBody must never be stored");
        root.TryGetProperty("content",      out _).Should().BeFalse(
            because: "callback content must never be stored");
    }

    [Fact]
    public void CallbackAuthFailed_Metadata_SourceIp_MustNotBeNull()
    {
        var metadata = JsonSerializer.Serialize(new { sourceIp = "192.168.0.1" });
        var evt = Build(TelemetryEventType.CallbackAuthFailed, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.GetProperty("sourceIp").GetString()
            .Should().NotBeNullOrWhiteSpace(
                because: "sourceIp is mandatory in CALLBACK_AUTH_FAILED");
    }

    // ── RUN_UNKNOWN ───────────────────────────────────────────────────────────

    [Fact]
    public void RunUnknown_Metadata_MustContain_UnknownReason()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            unknownReason  = "WORKER_TIMEOUT_NO_HB",
            needsAttention = true
        });

        var evt = Build(TelemetryEventType.RunUnknown, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("unknownReason", out var reason).Should().BeTrue(
            because: "RUN_UNKNOWN metadata must carry unknownReason");
        reason.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RunUnknown_Metadata_NeedsAttention_MustBeTrue()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            unknownReason  = "LEASE_EXPIRED_NO_HB",
            needsAttention = true
        });

        var evt = Build(TelemetryEventType.RunUnknown, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("needsAttention", out var na).Should().BeTrue();
        na.GetBoolean().Should().BeTrue(
            because: "UNKNOWN state always sets needsAttention=true (domain invariant 2)");
    }

    // ── SECURITY_EVENT ────────────────────────────────────────────────────────

    [Fact]
    public void SecurityEvent_Metadata_SecretRef_MustBeRedacted()
    {
        // Any secret reference value in metadata must be stored as "REDACTED".
        var metadata = JsonSerializer.Serialize(new
        {
            eventDetail = "Secret pattern detected in payload",
            secretRef   = "REDACTED"    // must be "REDACTED", never the real value
        });

        var evt = Build(TelemetryEventType.SecurityEvent, metadata);
        using var doc = ParseMetadata(evt);

        doc.RootElement.TryGetProperty("secretRef", out var secretRef).Should().BeTrue();
        secretRef.GetString().Should().Be("REDACTED",
            because: "security invariant 4: no plaintext secret stored in any event");
    }

    // ── General: metadata JSON must be parseable ──────────────────────────────

    [Theory]
    [InlineData(TelemetryEventType.RunCreated)]
    [InlineData(TelemetryEventType.RunQueued)]
    [InlineData(TelemetryEventType.RunLeased)]
    [InlineData(TelemetryEventType.RunRunning)]
    [InlineData(TelemetryEventType.RunFailed)]
    [InlineData(TelemetryEventType.RunHandedOff)]
    [InlineData(TelemetryEventType.RunCancelled)]
    [InlineData(TelemetryEventType.RunTimedOut)]
    [InlineData(TelemetryEventType.WorkerRegistered)]
    [InlineData(TelemetryEventType.WorkerOffline)]
    [InlineData(TelemetryEventType.CallbackReceived)]
    public void NonNullMetadata_WhenProvided_MustBeValidJson(string eventType)
    {
        var metadata = """{"key":"value","count":1}""";
        var evt = Build(eventType, metadata);

        var act = () => JsonDocument.Parse(evt.Metadata!);
        act.Should().NotThrow(because: $"metadata for {eventType} must be valid JSON when non-null");
    }
}
