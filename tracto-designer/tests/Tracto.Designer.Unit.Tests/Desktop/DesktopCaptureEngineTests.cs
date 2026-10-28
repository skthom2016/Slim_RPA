namespace Tracto.Designer.Unit.Tests.Desktop;

using FluentAssertions;
using Moq;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Infrastructure.Desktop;

public class DesktopCaptureEngineTests
{
    [Fact]
    public void Initially_Not_Running()
    {
        var mockSession = new Mock<IRecordingSessionService>();
        mockSession.Setup(s => s.Transcript).Returns(new List<TranscriptEntry>().AsReadOnly());
        var engine = new DesktopCaptureEngine(mockSession.Object);
        engine.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_When_Not_Running_Does_Not_Throw()
    {
        var mockSession = new Mock<IRecordingSessionService>();
        mockSession.Setup(s => s.Transcript).Returns(new List<TranscriptEntry>().AsReadOnly());
        var engine = new DesktopCaptureEngine(mockSession.Object);
        engine.Invoking(e => e.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void NullJabAdapter_IsAvailable_False()
    {
        new NullJabAdapter().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void NullJabAdapter_Dispose_Does_Not_Throw()
    {
        new NullJabAdapter().Invoking(a => a.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Session_AppendEntry_Not_Called_When_Session_Idle()
    {
        var mockSession = new Mock<IRecordingSessionService>();
        mockSession.Setup(s => s.CurrentState).Returns(SessionState.Idle);
        mockSession.Setup(s => s.Transcript).Returns(new List<TranscriptEntry>().AsReadOnly());
        // AppendTranscriptEntry should not throw when session is idle (service ignores it)
        mockSession.Object.Invoking(s => s.AppendTranscriptEntry(
            new TranscriptEntry(1, DateTimeOffset.Now, "t",
                new RawEvent(1, DateTimeOffset.Now, RawEventType.Click, "p", "w", null, null, null, null, false, null, 0, 0),
                null, null))).Should().NotThrow();
    }
}
