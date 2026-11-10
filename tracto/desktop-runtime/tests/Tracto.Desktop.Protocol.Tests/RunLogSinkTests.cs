#nullable enable

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tracto.Desktop.Protocol;
using Xunit;

namespace Tracto.Desktop.Protocol.Tests;

/// <summary>
/// Tests for the buffer that keeps what the worker logged while running a job, so the
/// Orchestrator can store it and a failure can be explained afterwards.
/// </summary>
public sealed class RunLogSinkTests
{
    private static RunLogSink Started(int capacity = 500)
    {
        var sink = new RunLogSink(capacity);
        sink.BeginRun("run-1", "attempt-1");
        return sink;
    }

    [Theory]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Error")]
    public void Add_KeepsWarningAndAbove(LogLevel level, string expected)
    {
        var sink = Started();

        sink.Add(level, "something went wrong").Should().BeTrue();

        sink.EndRunAndDrain().Should().ContainSingle().Which.Level.Should().Be(expected);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    public void Add_DropsInformationAndBelow(LogLevel level)
    {
        var sink = Started();

        sink.Add(level, "just so you know").Should().BeFalse();

        sink.EndRunAndDrain().Should().BeEmpty();
    }

    [Fact]
    public void Add_IgnoresLinesLoggedWhileNoRunIsActive()
    {
        // The worker warns about its own business between jobs; that belongs to nobody's run.
        var sink = new RunLogSink();

        sink.Add(LogLevel.Error, "pull failed, retrying").Should().BeFalse();
        sink.CurrentRunId.Should().BeNull();
    }

    [Fact]
    public void BeginRun_DiscardsWhateverTheLastRunLeftBehind()
    {
        var sink = Started();
        sink.Add(LogLevel.Error, "from the first run");

        sink.BeginRun("run-2", "attempt-2");

        sink.CurrentRunId.Should().Be("run-2");
        sink.CurrentAttemptId.Should().Be("attempt-2");
        sink.EndRunAndDrain().Should().BeEmpty();
    }

    [Fact]
    public void EndRunAndDrain_StopsCollectingSoLinesAreNeverSentTwice()
    {
        var sink = Started();
        sink.Add(LogLevel.Error, "the failure");

        sink.EndRunAndDrain().Should().ContainSingle();

        sink.CurrentRunId.Should().BeNull();
        sink.Add(LogLevel.Error, "after the run").Should().BeFalse();
        sink.EndRunAndDrain().Should().BeEmpty();
    }

    [Fact]
    public void Add_RecordsTheMessageStepAndTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var sink = Started();

        sink.Add(LogLevel.Error, "  window not found  ", "step.4");

        var entry = sink.EndRunAndDrain().Should().ContainSingle().Subject;
        entry.Message.Should().Be("window not found");
        entry.StepId.Should().Be("step.4");
        entry.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Drain_ReturnsEntriesInTheOrderTheyWereLogged()
    {
        var sink = Started();
        sink.Add(LogLevel.Warning, "first");
        sink.Add(LogLevel.Error, "second");

        sink.EndRunAndDrain().Select(e => e.Message).Should().Equal("first", "second");
    }

    [Fact]
    public void Add_StopsAtCapacitySoALoopingFailureCannotFillMemory()
    {
        var sink = Started(capacity: 3);

        for (var i = 0; i < 10; i++)
            sink.Add(LogLevel.Error, $"line {i}");

        var entries = sink.EndRunAndDrain();

        // Three kept, plus one line saying what was lost rather than leaving a silent gap.
        entries.Should().HaveCount(4);
        entries.Take(3).Select(e => e.Message).Should().Equal("line 0", "line 1", "line 2");
        entries[3].Message.Should().Contain("7 further log entries were dropped");
    }

    [Fact]
    public void LevelName_NamesOnlyTheLevelsThatAreKept()
    {
        RunLogSink.LevelName(LogLevel.Warning).Should().Be("Warning");
        RunLogSink.LevelName(LogLevel.Error).Should().Be("Error");
        RunLogSink.LevelName(LogLevel.Critical).Should().Be("Error");
        RunLogSink.LevelName(LogLevel.Information).Should().BeNull();
    }
}
