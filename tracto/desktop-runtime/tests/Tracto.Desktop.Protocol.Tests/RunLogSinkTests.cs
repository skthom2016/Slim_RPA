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
    [InlineData(LogLevel.Information, "Info")]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Error")]
    public void Add_KeepsInformationAndAbove(LogLevel level, string expected)
    {
        var sink = Started();

        sink.Add(level, "something happened").Should().BeTrue();

        sink.EndRunAndDrain().Should().ContainSingle().Which.Level.Should().Be(expected);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    public void Add_DropsDebugAndTrace(LogLevel level)
    {
        var sink = Started();

        sink.Add(level, "just so you know").Should().BeFalse();

        sink.EndRunAndDrain().Should().BeEmpty();
    }

    // Info is wanted from the step engine only: what a step did. The HTTP client, the host and the
    // polling loop log Info all the time, and none of it says anything about the job.
    [Theory]
    [InlineData(LogLevel.Information, "ScriptExecutionEngine", true)]
    [InlineData(LogLevel.Information, "Tracto.Desktop.Host.ScriptExecutionEngine", true)]
    [InlineData(LogLevel.Information, "System.Net.Http.HttpClient.Default.LogicalHandler", false)]
    [InlineData(LogLevel.Information, "Tracto.Desktop.Host.JobWorker", false)]
    [InlineData(LogLevel.Information, "MyScriptExecutionEngine", false)]   // a different class that merely ends the same way
    [InlineData(LogLevel.Warning, "System.Net.Http.HttpClient.Default.LogicalHandler", true)]
    [InlineData(LogLevel.Error, "Anything.At.All", true)]
    [InlineData(LogLevel.Critical, "Anything.At.All", true)]
    [InlineData(LogLevel.Debug, "ScriptExecutionEngine", false)]
    [InlineData(LogLevel.Trace, "ScriptExecutionEngine", false)]
    [InlineData(LogLevel.None, "ScriptExecutionEngine", false)]
    public void IsKept_WantsInfoFromTheStepEngineOnly(LogLevel level, string category, bool expected)
    {
        RunLogSink.IsKept(level, category).Should().Be(expected);
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
        RunLogSink.LevelName(LogLevel.Information).Should().Be("Info");
        RunLogSink.LevelName(LogLevel.Debug).Should().BeNull();
        RunLogSink.LevelName(LogLevel.Trace).Should().BeNull();
    }
}
