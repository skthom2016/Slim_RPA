#nullable enable
using FluentAssertions;
using Tracto.Orchestrator.Domain.Telemetry;

namespace Tracto.Orchestrator.Unit.Tests.Telemetry;

/// <summary>
/// Unit tests for the <see cref="TelemetryEvent"/> domain entity.
/// Covers construction invariants, field assignments, and rejection of invalid input.
/// No EF, no framework refs — pure domain logic.
/// </summary>
public sealed class TelemetryEventEntityTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // ── Happy-path construction ───────────────────────────────────────────────

    [Fact]
    public void Constructor_WithMinimalValidArgs_SetsAllProperties()
    {
        var id      = Guid.NewGuid();
        var runId   = Guid.NewGuid();
        const string eventType = TelemetryEventType.RunCreated;

        var evt = new TelemetryEvent(id, runId, eventType, FixedNow);

        evt.Id.Should().Be(id);
        evt.RunId.Should().Be(runId);
        evt.EventType.Should().Be(eventType);
        evt.OccurredAt.Should().Be(FixedNow);
        evt.WorkerId.Should().BeNull();
        evt.LeaseId.Should().BeNull();
        evt.AttemptId.Should().BeNull();
        evt.Metadata.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllOptionalArgs_SetsAllProperties()
    {
        var id        = Guid.NewGuid();
        var runId     = Guid.NewGuid();
        var leaseId   = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string workerId  = "worker-abc";
        const string metadata  = """{"stepCount":5,"durationMs":1234}""";

        var evt = new TelemetryEvent(
            id, runId, TelemetryEventType.RunSucceeded, FixedNow,
            workerId, leaseId, attemptId, metadata);

        evt.WorkerId.Should().Be(workerId);
        evt.LeaseId.Should().Be(leaseId);
        evt.AttemptId.Should().Be(attemptId);
        evt.Metadata.Should().Be(metadata);
    }

    // ── Guard clauses ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        var act = () => new TelemetryEvent(
            Guid.Empty, Guid.NewGuid(), TelemetryEventType.RunCreated, FixedNow);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Id must not be empty*");
    }

    [Fact]
    public void Constructor_EmptyRunId_ThrowsArgumentException()
    {
        var act = () => new TelemetryEvent(
            Guid.NewGuid(), Guid.Empty, TelemetryEventType.RunCreated, FixedNow);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RunId must not be empty*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_BlankEventType_ThrowsArgumentException(string? eventType)
    {
        var act = () => new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(), eventType!, FixedNow);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*EventType must be a non-empty string*");
    }

    // ── Immutability ──────────────────────────────────────────────────────────

    [Fact]
    public void TelemetryEvent_HasNoPublicSetters()
    {
        var properties = typeof(TelemetryEvent).GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name)
            .ToList();

        properties.Should().BeEmpty(
            because: "TelemetryEvent is append-only; no property should have a public setter");
    }

    // ── Metadata is opaque ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullMetadata_IsValidForNoContextEvents()
    {
        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.RunQueued, FixedNow,
            metadata: null);

        evt.Metadata.Should().BeNull();
    }

    [Fact]
    public void Constructor_ValidJsonMetadata_StoredVerbatim()
    {
        const string json = """{"sourceIp":"192.168.1.1"}""";

        var evt = new TelemetryEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            TelemetryEventType.CallbackAuthFailed, FixedNow,
            metadata: json);

        evt.Metadata.Should().Be(json);
    }
}
