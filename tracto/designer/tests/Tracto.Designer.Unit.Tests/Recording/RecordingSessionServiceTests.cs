namespace Tracto.Designer.Unit.Tests.Recording;

using FluentAssertions;
using Tracto.Designer.Core.Recording;

public class RecordingSessionServiceTests
{
    private static RecordingSessionOptions Opts() =>
        new(CaptureEngineType.Desktop, System.IO.Path.GetTempPath());

    private static RecordingSessionService Svc() => new();

    [Fact]
    public void Initial_State_Is_Idle()
    {
        Svc().CurrentState.Should().Be(SessionState.Idle);
    }

    [Fact]
    public void Start_From_Idle_Transitions_To_Recording()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.CurrentState.Should().Be(SessionState.Recording);
    }

    [Fact]
    public void Start_From_Recording_Throws()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Invoking(s => s.Start(Opts())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pause_From_Recording_Transitions_To_Paused()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Pause();
        svc.CurrentState.Should().Be(SessionState.Paused);
    }

    [Fact]
    public void Pause_From_Idle_Throws()
    {
        Svc().Invoking(s => s.Pause()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resume_From_Paused_Transitions_To_Recording()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Pause();
        svc.Resume();
        svc.CurrentState.Should().Be(SessionState.Recording);
    }

    [Fact]
    public void Resume_From_Idle_Throws()
    {
        Svc().Invoking(s => s.Resume()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Stop_From_Recording_Transitions_To_Stopped()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Stop();
        svc.CurrentState.Should().Be(SessionState.Stopped);
    }

    [Fact]
    public void Stop_From_Paused_Transitions_To_Stopped()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Pause();
        svc.Stop();
        svc.CurrentState.Should().Be(SessionState.Stopped);
    }

    [Fact]
    public void Stop_From_Idle_Throws()
    {
        Svc().Invoking(s => s.Stop()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Discard_From_Recording_Clears_Transcript_Returns_Idle()
    {
        var svc = Svc();
        svc.Start(Opts());
        var entry = new TranscriptEntry(
            1,
            DateTimeOffset.Now,
            "test",
            new RawEvent(1, DateTimeOffset.Now, RawEventType.Click, "p", "w", null, null, null, null, false, null, 0, 0),
            null,
            null);
        svc.AppendTranscriptEntry(entry);
        svc.Discard();
        svc.CurrentState.Should().Be(SessionState.Idle);
        svc.Transcript.Should().BeEmpty();
    }

    [Fact]
    public void Discard_From_Idle_Is_NoOp()
    {
        var svc = Svc();
        svc.Invoking(s => s.Discard()).Should().NotThrow();
        svc.CurrentState.Should().Be(SessionState.Idle);
    }

    [Fact]
    public void Discard_From_Stopped_Throws()
    {
        var svc = Svc();
        svc.Start(Opts());
        svc.Stop();
        svc.Invoking(s => s.Discard()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StateChanged_Fires_On_Valid_Transition()
    {
        var svc = Svc();
        var states = new List<SessionState>();
        svc.StateChanged += (_, s) => states.Add(s);
        svc.Start(Opts());
        svc.Pause();
        svc.Resume();
        svc.Stop();
        states.Should().ContainInOrder(
            SessionState.Recording,
            SessionState.Paused,
            SessionState.Recording,
            SessionState.Stopped);
    }

    [Fact]
    public void EngineType_Set_On_Start_Cleared_On_Discard()
    {
        var svc = Svc();
        svc.ActiveEngineType.Should().BeNull();
        svc.Start(Opts());
        svc.ActiveEngineType.Should().Be(CaptureEngineType.Desktop);
        svc.Discard();
        svc.ActiveEngineType.Should().BeNull();
    }
}
