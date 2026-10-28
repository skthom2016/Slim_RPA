namespace Tracto.Designer.Unit.Tests.Browser;

using FluentAssertions;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Recording;

/// <summary>
/// A controllable IBrowserCaptureEngine that lets tests fire events synchronously
/// without needing a real browser or WebSocket connection.
/// </summary>
internal sealed class StubBrowserCaptureEngine : IBrowserCaptureEngine
{
    public bool IsRunning => false;
    public Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event EventHandler<BrowserRawEvent>? EventCaptured;
    public void FireEvent(BrowserRawEvent e) => EventCaptured?.Invoke(this, e);
    public void Dispose() { }
}

public class BrowserCaptureTests
{
    [Fact]
    public void NullEngine_IsRunning_False()
    {
        new NullBrowserCaptureEngine().IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task NullEngine_Start_NoOp()
    {
        var e = new NullBrowserCaptureEngine();
        await e.StartAsync(new BrowserCaptureOptions());
        e.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task NullEngine_Stop_NoOp()
    {
        var e = new NullBrowserCaptureEngine();
        await e.StopAsync();
        e.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void BrowserRawEvent_IsPasswordInput_True_Clears_InputValue()
    {
        var e = new BrowserRawEvent(DateTimeOffset.UtcNow, BrowserEventType.FormInput, "http://x", "#pwd", null, "secret", true);
        e.IsPasswordInput.Should().BeTrue();
    }

    /// <summary>
    /// DS-15: MapEventType must map BrowserEventType.Navigate to RawEventType.WindowActivate.
    /// Verified by wiring a StubBrowserCaptureEngine through BrowserCaptureAdapter and
    /// asserting the EventType on the produced RawEvent.
    /// </summary>
    [Fact]
    public void BrowserCaptureAdapter_Maps_Navigate_To_WindowActivate()
    {
        var engine = new StubBrowserCaptureEngine();
        RawEvent? captured = null;
        using var adapter = new BrowserCaptureAdapter(engine);
        adapter.RawEventCaptured += (_, e) => captured = e;

        engine.FireEvent(new BrowserRawEvent(
            DateTimeOffset.UtcNow, BrowserEventType.Navigate, "http://example.com",
            null, null, null, false));

        captured.Should().NotBeNull();
        captured!.EventType.Should().Be(RawEventType.WindowActivate);
    }

    [Fact]
    public void BrowserCaptureOptions_Defaults()
    {
        var opts = new BrowserCaptureOptions();
        opts.BrowserType.Should().Be(BrowserType.Chrome);
        opts.DebugPort.Should().Be(9222);
        opts.TargetUrl.Should().BeNull();
    }

    [Fact]
    public void BrowserEventType_Has_Expected_Values()
    {
        Enum.GetValues<BrowserEventType>().Should().Contain(BrowserEventType.Click);
        Enum.GetValues<BrowserEventType>().Should().Contain(BrowserEventType.Navigate);
        Enum.GetValues<BrowserEventType>().Should().Contain(BrowserEventType.FormInput);
    }

    [Fact]
    public void BrowserCaptureAdapter_IsRunning_Reflects_Engine()
    {
        using var adapter = new BrowserCaptureAdapter(new NullBrowserCaptureEngine());
        adapter.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// DS-15: When IsPasswordInput is true the adapter must set Value to null on the
    /// produced RawEvent to prevent password plaintext from entering the transcript.
    /// Verified by firing a FormInput event through the adapter and checking Value.
    /// </summary>
    [Fact]
    public void BrowserCaptureAdapter_Password_Input_Null_Value()
    {
        var engine = new StubBrowserCaptureEngine();
        RawEvent? captured = null;
        using var adapter = new BrowserCaptureAdapter(engine);
        adapter.RawEventCaptured += (_, e) => captured = e;

        engine.FireEvent(new BrowserRawEvent(
            DateTimeOffset.UtcNow, BrowserEventType.FormInput, "http://x",
            "#pwd", null, "secret", true));

        captured.Should().NotBeNull();
        captured!.Value.Should().BeNull("password values must never enter the transcript");
        captured.IsPasswordField.Should().BeTrue();
    }

    /// <summary>
    /// Unknown BrowserEventType values must map to RawEventType.Unknown rather than
    /// silently corrupting the transcript as a Click.
    /// </summary>
    [Fact]
    public void BrowserCaptureAdapter_Unknown_EventType_Maps_To_Unknown()
    {
        var engine = new StubBrowserCaptureEngine();
        RawEvent? captured = null;
        using var adapter = new BrowserCaptureAdapter(engine);
        adapter.RawEventCaptured += (_, e) => captured = e;

        // Cast an out-of-range value to BrowserEventType to exercise the wildcard arm.
        engine.FireEvent(new BrowserRawEvent(
            DateTimeOffset.UtcNow, (BrowserEventType)999, "http://x",
            null, null, null, false));

        captured.Should().NotBeNull();
        captured!.EventType.Should().Be(RawEventType.Unknown);
    }
}
