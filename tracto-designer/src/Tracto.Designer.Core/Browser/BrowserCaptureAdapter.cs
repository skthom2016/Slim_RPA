namespace Tracto.Designer.Core.Browser;

using Tracto.Designer.Core.Recording;

public sealed class BrowserCaptureAdapter : IDisposable
{
    private readonly IBrowserCaptureEngine _engine;
    private int _sequence;

    public BrowserCaptureAdapter(IBrowserCaptureEngine engine)
    {
        _engine = engine;
        _engine.EventCaptured += OnBrowserEvent;
    }

    public event EventHandler<RawEvent>? RawEventCaptured;
    public bool IsRunning => _engine.IsRunning;

    public Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default) =>
        _engine.StartAsync(options, ct);

    public Task StopAsync(CancellationToken ct = default) => _engine.StopAsync(ct);

    private void OnBrowserEvent(object? sender, BrowserRawEvent e)
    {
        var raw = new RawEvent(
            Sequence: Interlocked.Increment(ref _sequence),
            Timestamp: e.Timestamp,
            EventType: MapEventType(e.EventType),
            ProcessName: "browser",
            WindowTitle: e.Url,
            ControlName: e.ElementText,
            AutomationId: e.ElementSelector,
            ClassName: null,
            Value: e.IsPasswordInput ? null : e.InputValue,
            IsPasswordField: e.IsPasswordInput,
            KeyValue: null,
            ScreenX: 0,
            ScreenY: 0);
        RawEventCaptured?.Invoke(this, raw);
    }

    private static RawEventType MapEventType(BrowserEventType t) => t switch
    {
        BrowserEventType.Click       => RawEventType.Click,
        BrowserEventType.Navigate    => RawEventType.WindowActivate,
        BrowserEventType.FormInput   => RawEventType.TextInput,
        BrowserEventType.KeyPress    => RawEventType.KeyPress,
        BrowserEventType.PageLoad    => RawEventType.WindowActivate,
        BrowserEventType.AjaxRequest => RawEventType.WindowActivate,
        _                            => RawEventType.Unknown
    };

    public void Dispose()
    {
        _engine.EventCaptured -= OnBrowserEvent;
        _engine.Dispose();
    }
}
