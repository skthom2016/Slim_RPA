namespace Tracto.Designer.Core.Browser;

public interface IBrowserCaptureEngine : IDisposable
{
    bool IsRunning { get; }
    Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    event EventHandler<BrowserRawEvent>? EventCaptured;
}
