namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;

public class ScriptDraftGeneratorTests
{
    private static RawEvent MakeRaw(RawEventType t, string? control = "btn", string? val = null, string? key = null, bool isPwd = false) =>
        new(1, DateTimeOffset.Now, t, "proc", "Window", control, "auto_id", "Button", isPwd ? null : val, isPwd, key, 100, 200);

    private static TranscriptEntry MakeEntry(int n, RawEvent raw, string? narr = null) =>
        new(n, DateTimeOffset.Now, $"Step {n}: {raw.EventType}", raw, narr, null);

    private static readonly ScriptDraftGenerator Gen = new();

    [Fact]
    public void EmptyTranscript_Produces_EmptyScript()
    {
        Gen.GenerateDraft([], CaptureEngineType.Desktop, "test").Steps.Should().BeEmpty();
    }

    [Fact]
    public void Click_Event_Maps_To_Click_Action()
    {
        var entry = MakeEntry(1, MakeRaw(RawEventType.Click, "OkBtn"));
        var script = Gen.GenerateDraft([entry], CaptureEngineType.Desktop, "t");
        script.Steps[0].Action.Should().Be("click");
        script.Steps[0].Parameters["target"].Should().Be("OkBtn");
    }

    [Fact]
    public void KeyPress_Event_Maps_To_Key_Action()
    {
        var entry = MakeEntry(1, MakeRaw(RawEventType.KeyPress, key: "Return"));
        var script = Gen.GenerateDraft([entry], CaptureEngineType.Desktop, "t");
        script.Steps[0].Action.Should().Be("key");
        script.Steps[0].Parameters["key"].Should().Be("Return");
    }

    [Fact]
    public void TextInput_Maps_To_Type_Action_With_Value()
    {
        var entry = MakeEntry(1, MakeRaw(RawEventType.TextInput, val: "hello"));
        var script = Gen.GenerateDraft([entry], CaptureEngineType.Desktop, "t");
        script.Steps[0].Action.Should().Be("type");
        script.Steps[0].Parameters["value"].Should().Be("hello");
    }

    [Fact]
    public void NarrationText_Appended_To_Description()
    {
        var entry = MakeEntry(1, MakeRaw(RawEventType.Click), "Click submit to save");
        var script = Gen.GenerateDraft([entry], CaptureEngineType.Desktop, "t");
        script.Steps[0].Description.Should().Contain("[narrator: Click submit to save]");
    }

    [Fact]
    public void BrowserEngine_Sets_TargetRuntime_Browser()
    {
        var script = Gen.GenerateDraft([], CaptureEngineType.Browser, "t");
        script.TargetRuntime.Should().Be("browser");
    }

    [Fact]
    public void StepNumbers_Sequential_And_Correct()
    {
        var entries = Enumerable.Range(1, 5).Select(i => MakeEntry(i, MakeRaw(RawEventType.Click))).ToList();
        var script = Gen.GenerateDraft(entries, CaptureEngineType.Desktop, "t");
        script.Steps.Select(s => s.StepNumber).Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void Password_Field_Value_Not_In_Parameters()
    {
        var raw = MakeRaw(RawEventType.TextInput, val: null, isPwd: true);
        var entry = MakeEntry(1, raw);
        var script = Gen.GenerateDraft([entry], CaptureEngineType.Desktop, "t");
        script.Steps[0].Parameters.ContainsKey("value").Should().BeFalse();
    }
}
