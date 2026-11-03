namespace Tracto.Designer.Core.Browser;

using Tracto.Designer.Core.Recording;

/// <summary>
/// Turns browser capture events into <see cref="RawEvent"/>s and, when a recording session
/// is active, appends them to its transcript.
/// </summary>
public sealed class BrowserCaptureAdapter : IDisposable
{
    private readonly IBrowserCaptureEngine _engine;
    private readonly IRecordingSessionService? _session;
    private int _sequence;

    public BrowserCaptureAdapter(IBrowserCaptureEngine engine, IRecordingSessionService? session = null)
    {
        _engine = engine;
        _session = session;
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
            ControlName: e.EventType == BrowserEventType.KeyPress ? e.Target?.Describe() : e.ElementText,
            AutomationId: e.ElementSelector,
            ClassName: null,
            Value: e.IsPasswordInput ? null : e.InputValue,
            IsPasswordField: e.IsPasswordInput,
            KeyValue: e.EventType == BrowserEventType.KeyPress ? e.ElementText : null,
            ScreenX: 0,
            ScreenY: 0,
            Attributes: e.Target?.ToAttributes());

        RawEventCaptured?.Invoke(this, raw);

        if (_session is { CurrentState: SessionState.Recording })
        {
            var step = _session.Transcript.Count + 1;
            _session.AppendTranscriptEntry(new TranscriptEntry(step, raw.Timestamp, Describe(e), raw, null, null));
        }
    }

    private static string Describe(BrowserRawEvent e)
    {
        var what = e.Target?.Describe() ?? e.ElementText ?? e.ElementSelector ?? "element";
        return e.EventType switch
        {
            BrowserEventType.Click     => $"Clicked {what}",
            BrowserEventType.FormInput => e.IsPasswordInput ? $"Entered password in {what}" : $"Entered \"{e.InputValue}\" in {what}",
            BrowserEventType.KeyPress  => $"Pressed {e.ElementText} in {what}",
            BrowserEventType.Navigate  => $"Navigated to {e.Url}",
            BrowserEventType.PageLoad  => "Page loaded",
            _                          => e.EventType.ToString()
        };
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
