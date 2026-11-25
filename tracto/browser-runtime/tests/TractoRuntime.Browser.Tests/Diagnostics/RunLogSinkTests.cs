using FluentAssertions;
using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Diagnostics;

namespace TractoRuntime.Browser.Tests.Diagnostics;

/// <summary>
/// Tests for the buffer that keeps what a worker logged while running a job, so the
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

    // ── Which levels are kept ────────────────────────────────────────────────

    [Theory]
    [InlineData(LogLevel.Information, "Info")]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Error")]
    public void Add_KeepsInformationAndAbove(LogLevel level, string expected)
    {
        var sink = Started();

        sink.Add(level, "something happened").Should().BeTrue();

        sink.EndRunAndDrain().Should().ContainSingle()
            .Which.Level.Should().Be(expected);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.None)]
    public void Add_DropsDebugAndTrace(LogLevel level)
    {
        var sink = Started();

        sink.Add(level, "just so you know").Should().BeFalse();

        sink.EndRunAndDrain().Should().BeEmpty();
    }

    // Info is wanted from the step engine only: which step ran. The host, the polling loop and the
    // browser launcher log Info all the time, and none of it says anything about the job.
    [Theory]
    [InlineData(LogLevel.Information, "BrowserRunEngine", true)]
    [InlineData(LogLevel.Information, "TractoRuntime.Browser.Host.BrowserRunEngine", true)]
    [InlineData(LogLevel.Information, "TractoRuntime.Browser.Host.BrowserWorker", false)]
    [InlineData(LogLevel.Information, "Microsoft.Hosting.Lifetime", false)]
    [InlineData(LogLevel.Information, "MyBrowserRunEngine", false)]   // another class that merely ends the same way
    [InlineData(LogLevel.Warning, "Microsoft.Hosting.Lifetime", true)]
    [InlineData(LogLevel.Error, "Anything.At.All", true)]
    [InlineData(LogLevel.Critical, "Anything.At.All", true)]
    [InlineData(LogLevel.Debug, "BrowserRunEngine", false)]
    [InlineData(LogLevel.Trace, "BrowserRunEngine", false)]
    [InlineData(LogLevel.None, "BrowserRunEngine", false)]
    public void IsKept_WantsInfoFromTheStepEngineOnly(LogLevel level, string category, bool expected)
    {
        RunLogSink.IsKept(level, category).Should().Be(expected);
    }

    [Fact]
    public void Add_IgnoresAnEmptyMessage()
    {
        var sink = Started();

        sink.Add(LogLevel.Error, "   ").Should().BeFalse();

        sink.EndRunAndDrain().Should().BeEmpty();
    }

    // ── Only while a run is active ───────────────────────────────────────────

    [Fact]
    public void Add_IgnoresLinesLoggedWhileNoRunIsActive()
    {
        // A worker warns about its own business between jobs — that belongs to nobody's run.
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

    // ── What is recorded ─────────────────────────────────────────────────────

    [Fact]
    public void Add_RecordsTheMessageStepAndTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var sink = Started();

        sink.Add(LogLevel.Error, "  locator not found  ", "step.9");

        var entry = sink.EndRunAndDrain().Should().ContainSingle().Subject;
        entry.Message.Should().Be("locator not found");
        entry.StepId.Should().Be("step.9");
        entry.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Drain_ReturnsEntriesInTheOrderTheyWereLogged()
    {
        var sink = Started();
        sink.Add(LogLevel.Warning, "first");
        sink.Add(LogLevel.Error, "second");
        sink.Add(LogLevel.Warning, "third");

        sink.EndRunAndDrain().Select(e => e.Message).Should().Equal("first", "second", "third");
    }

    // ── The cap ──────────────────────────────────────────────────────────────

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
        entries[3].Level.Should().Be("Warning");
        entries[3].Message.Should().Contain("7 further log entries were dropped");
    }

    [Fact]
    public void Drain_AddsNoDroppedNoteWhenNothingWasDropped()
    {
        var sink = Started(capacity: 3);
        sink.Add(LogLevel.Error, "only one");

        sink.EndRunAndDrain().Should().ContainSingle()
            .Which.Message.Should().Be("only one");
    }

    [Fact]
    public void BeginRun_ResetsTheDroppedCountToo()
    {
        var sink = Started(capacity: 1);
        sink.Add(LogLevel.Error, "kept");
        sink.Add(LogLevel.Error, "dropped");

        sink.BeginRun("run-2", null);
        sink.Add(LogLevel.Error, "new run");

        sink.EndRunAndDrain().Should().ContainSingle()
            .Which.Message.Should().Be("new run");
    }

    // ── Level naming, used by the forwarder to decide what to even format ────

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
