namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

public class BrowserDraftGeneratorTests
{
    private static readonly ScriptDraftGenerator Gen = new();
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static RawEvent Raw(RawEventType type, BrowserTarget? target, string? value = null, string? key = null,
        bool pwd = false, string url = "", int secondsAfterStart = 0) =>
        new(1, T0.AddSeconds(secondsAfterStart), type, "browser", url,
            target?.Describe(), target?.Css, null, pwd ? null : value, pwd, key, 0, 0,
            Attributes: target?.ToAttributes());

    private static TranscriptEntry Entry(int n, RawEvent raw) => new(n, raw.Timestamp, $"Step {n}", raw, null, null);

    private static readonly BrowserTarget Username = new(Role: "textbox", Name: "Username", Label: "Username", Css: "#username", Tag: "input", InputType: "text");
    private static readonly BrowserTarget Password = new(Role: "textbox", Name: "Password", Label: "Password", Css: "#password", Tag: "input", InputType: "password");
    private static readonly BrowserTarget LoginButton = new(Role: "button", Name: "Login", Text: "Login", Css: "button[type=\"submit\"]", Tag: "button", InputType: "submit");

    [Fact]
    public void Login_Recording_Becomes_Navigate_Fill_Fill_Click()
    {
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.WindowActivate, null, url: "https://example.test/login")),
            Entry(2, Raw(RawEventType.Click, Username, secondsAfterStart: 1)),
            Entry(3, Raw(RawEventType.TextInput, Username, value: "tomsmith", secondsAfterStart: 1)),
            Entry(4, Raw(RawEventType.TextInput, Password, value: "secret", pwd: true, secondsAfterStart: 1)),
            Entry(5, Raw(RawEventType.Click, LoginButton, secondsAfterStart: 1)),
            Entry(6, Raw(RawEventType.WindowActivate, null, url: "https://example.test/secure", secondsAfterStart: 2))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "login");

        script.TargetRuntime.Should().Be("browser");
        script.Steps.Select(s => s.Action).Should().Equal("navigate", "delay", "fill", "fill", "click");
        script.Steps[0].Parameters["url"].Should().Be("https://example.test/login");
        script.Steps[2].Parameters["label"].Should().Be("Username");
        script.Steps[2].Parameters["value"].Should().Be("tomsmith");
        script.Steps[3].Parameters["value"].Should().Be("${secret:PASSWORD}");
        script.Steps[3].Parameters.Values.Should().NotContain("secret");
        script.Steps[4].Parameters["role"].Should().Be("button");
        script.Steps[4].Parameters["name"].Should().Be("Login");
        script.Steps.Select(s => s.StepNumber).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Navigation_Caused_By_A_Click_Is_Not_A_Step()
    {
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.WindowActivate, null, url: "https://example.test/")),
            Entry(2, Raw(RawEventType.Click, LoginButton, secondsAfterStart: 1)),
            Entry(3, Raw(RawEventType.WindowActivate, null, url: "https://example.test/next", secondsAfterStart: 2))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Select(s => s.Action).Should().Equal("navigate", "delay", "click");
    }

    [Fact]
    public void Typed_Url_After_A_Pause_Is_A_Navigate_Step()
    {
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.WindowActivate, null, url: "https://example.test/")),
            Entry(2, Raw(RawEventType.Click, LoginButton, secondsAfterStart: 1)),
            Entry(3, Raw(RawEventType.WindowActivate, null, url: "https://other.test/", secondsAfterStart: 30))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Select(s => s.Action).Should().Equal("navigate", "delay", "click", "navigate");
        script.Steps[3].Parameters["url"].Should().Be("https://other.test/");
    }

    [Fact]
    public void Enter_In_A_Field_Becomes_Press_With_The_Field_As_Target()
    {
        var search = new BrowserTarget(Role: "searchbox", Name: "Search", Css: "#q", Tag: "input", InputType: "search");
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.TextInput, search, value: "tracto")),
            Entry(2, Raw(RawEventType.KeyPress, search, key: "Enter"))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Select(s => s.Action).Should().Equal("fill", "press");
        script.Steps[1].Parameters["key"].Should().Be("Enter");
        script.Steps[1].Parameters["role"].Should().Be("searchbox");
    }

    [Fact]
    public void Retyping_The_Same_Field_Keeps_Only_The_Last_Value()
    {
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.TextInput, Username, value: "tom")),
            Entry(2, Raw(RawEventType.TextInput, Username, value: "tomsmith"))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Should().HaveCount(1);
        script.Steps[0].Parameters["value"].Should().Be("tomsmith");
    }

    [Fact]
    public void Checkbox_Change_Becomes_Check_Or_Uncheck()
    {
        var box = new BrowserTarget(Role: "checkbox", Name: "Remember me", Label: "Remember me", Css: "#remember", Tag: "input", InputType: "checkbox");
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.TextInput, box, value: "true")),
            Entry(2, Raw(RawEventType.TextInput, box, value: "false", secondsAfterStart: 1))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Select(s => s.Action).Should().Equal("check", "delay", "uncheck");
    }

    [Fact]
    public void Pause_Between_Steps_Becomes_A_Delay()
    {
        var entries = new[]
        {
            Entry(1, Raw(RawEventType.Click, LoginButton)),
            Entry(2, Raw(RawEventType.Click, LoginButton, secondsAfterStart: 2))
        };

        var script = Gen.GenerateDraft(entries, CaptureEngineType.Browser, "t");
        script.Steps.Select(s => s.Action).Should().Equal("click", "delay", "click");
        script.Steps[1].Parameters["ms"].Should().Be("2000");
    }

    [Fact]
    public void Adapter_Appends_To_An_Active_Recording_Session()
    {
        var session = new RecordingSessionService();
        session.Start(new RecordingSessionOptions(CaptureEngineType.Browser, System.IO.Path.GetTempPath()));
        var engine = new StubEngine();
        using var adapter = new BrowserCaptureAdapter(engine, session);

        engine.Fire(new BrowserRawEvent(T0, BrowserEventType.Click, "", "#b", "Go", null, false, LoginButton));

        session.Transcript.Should().HaveCount(1);
        session.Transcript[0].StepDescription.Should().Be("Clicked button \"Login\"");
        session.Transcript[0].SourceEvent.Attributes!["role"].Should().Be("button");
    }

    private sealed class StubEngine : IBrowserCaptureEngine
    {
        public bool IsRunning => false;
        public Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler<BrowserRawEvent>? EventCaptured;
        public void Fire(BrowserRawEvent e) => EventCaptured?.Invoke(this, e);
        public void Dispose() { }
    }
}
