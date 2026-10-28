namespace Tracto.Designer.Core.Desktop;

public interface IJabCaptureAdapter : IDisposable
{
    bool IsAvailable { get; }
    void Start();
    void Stop();
}
