namespace Tracto.Designer.Infrastructure.Desktop;

using Tracto.Designer.Core.Desktop;

public sealed class NullJabAdapter : IJabCaptureAdapter
{
    public bool IsAvailable => false;
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
