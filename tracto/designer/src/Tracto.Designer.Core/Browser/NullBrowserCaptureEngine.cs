namespace Tracto.Designer.Core.Browser;

public sealed class NullBrowserCaptureEngine : IBrowserCaptureEngine
{
    public bool IsRunning => false;
    public Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event EventHandler<BrowserRawEvent>? EventCaptured { add { } remove { } }
    public void Dispose() { }
}
