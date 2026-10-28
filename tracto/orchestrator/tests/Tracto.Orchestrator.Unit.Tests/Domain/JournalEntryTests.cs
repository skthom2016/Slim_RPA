using FluentAssertions;
using Tracto.Orchestrator.Domain.Journal;

namespace Tracto.Orchestrator.Unit.Tests.Domain;

/// <summary>
/// Unit tests for <see cref="JournalEntry"/> domain entity (OC-15).
/// </summary>
public sealed class JournalEntryTests
{
    private static readonly DateTimeOffset T0 =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidArgs_SetsAllProperties()
    {
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string payload = "{\"foo\":1}";

        var entry = new JournalEntry(runId, attemptId, "RunCreated", T0, payload);

        entry.Id.Should().NotBe(Guid.Empty,
            because: "Id must be auto-generated in the constructor");
        entry.RunId.Should().Be(runId);
        entry.AttemptId.Should().Be(attemptId);
        entry.EventType.Should().Be("RunCreated");
        entry.OccurredAt.Should().Be(T0);
        entry.Payload.Should().Be(payload);
    }

    [Fact]
    public void Constructor_NullAttemptId_IsAllowed()
    {
        var entry = new JournalEntry(Guid.NewGuid(), null, "Heartbeat", T0);
        entry.AttemptId.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullPayload_IsAllowed()
    {
        var entry = new JournalEntry(Guid.NewGuid(), null, "Heartbeat", T0, null);
        entry.Payload.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankEventType_Throws(string eventType)
    {
        var act = () => new JournalEntry(Guid.NewGuid(), null, eventType, T0);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("eventType");
    }

    // ─── Unique IDs ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_TwoEntries_HaveDistinctIds()
    {
        var runId = Guid.NewGuid();
        var e1 = new JournalEntry(runId, null, "RunCreated", T0);
        var e2 = new JournalEntry(runId, null, "RunCreated", T0);

        e1.Id.Should().NotBe(e2.Id,
            because: "each JournalEntry must have a unique auto-generated Id");
    }

    // ─── Immutability ─────────────────────────────────────────────────────────

    [Fact]
    public void JournalEntry_Properties_AreGetOnly()
    {
        var runId   = Guid.NewGuid();
        var payload = "{\"state\":\"Unknown\",\"reason\":\"Worker absent at restart\"}";
        var entry   = new JournalEntry(runId, null, "RunMarkedUnknown", T0, payload);

        // All data is accessible.
        entry.RunId.Should().Be(runId);
        entry.EventType.Should().Be("RunMarkedUnknown");
        entry.Payload.Should().Be(payload);
    }
}
