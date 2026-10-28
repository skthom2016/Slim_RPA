namespace Tracto.Designer.Core.Desktop;

using Tracto.Designer.Core.Recording;

public interface IDesktopCaptureEngine : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    event EventHandler<RawEvent>? EventCaptured;
}
