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

    private static TranscriptEntry At(DateTimeOffset t, RawEvent raw) =>
        new(1, t, $"Step: {raw.EventType}", raw with { Timestamp = t }, null, null);

    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consecutive_Character_Keys_Merge_Into_One_Type_Step()
    {
        var keys = "Notepad".Select((c, i) => At(T0.AddMilliseconds(i * 50), MakeRaw(RawEventType.KeyPress, null, key: c.ToString()))).ToList();
        var script = Gen.GenerateDraft(keys, CaptureEngineType.Desktop, "t");
        script.Steps.Should().ContainSingle();
        script.Steps[0].Action.Should().Be("type");
        script.Steps[0].Parameters["value"].Should().Be("Notepad");
    }

    [Fact]
    public void Legacy_Virtual_Key_Codes_For_Letters_Merge_Into_Lowercase_Text()
    {
        var keys = new[] { "VK_4E", "VK_4F", "VK_54" }
            .Select((k, i) => At(T0.AddMilliseconds(i * 50), MakeRaw(RawEventType.KeyPress, null, key: k))).ToList();
        var script = Gen.GenerateDraft(keys, CaptureEngineType.Desktop, "t");
        script.Steps.Should().ContainSingle();
        script.Steps[0].Parameters["value"].Should().Be("not");
    }

    [Fact]
    public void Named_Keys_And_Shortcuts_Stay_As_Key_Steps()
    {
        var entries = new[] { "Enter", "Ctrl+S" }
            .Select((k, i) => At(T0.AddMilliseconds(i * 50), MakeRaw(RawEventType.KeyPress, null, key: k))).ToList();
        var script = Gen.GenerateDraft(entries, CaptureEngineType.Desktop, "t");
        script.Steps.Select(s => s.Parameters["key"]).Should().Equal("Enter", "Ctrl+S");
    }

    [Fact]
    public void Events_On_The_Designer_Window_Are_Dropped()
    {
        var designer = new RawEvent(1, T0, RawEventType.Focus, "Tracto.Designer.App", "Tracto Designer", "Tracto Designer", null, null, null, false, null, 0, 0);
        var empty = new RawEvent(2, T0, RawEventType.Focus, "x", "", null, null, null, null, false, null, 0, 0);
        var script = Gen.GenerateDraft([At(T0, designer), At(T0, empty)], CaptureEngineType.Desktop, "t");
        script.Steps.Should().BeEmpty();
    }

    [Fact]
    public void Repeated_Identical_Focus_Events_Collapse()
    {
        var focus = MakeRaw(RawEventType.Focus, "Text editor");
        var script = Gen.GenerateDraft([At(T0, focus), At(T0.AddMilliseconds(10), focus)], CaptureEngineType.Desktop, "t");
        script.Steps.Should().ContainSingle();
    }

    [Fact]
    public void Click_Records_Window_And_Offset_Alongside_Coordinates()
    {
        var click = new RawEvent(1, T0, RawEventType.Click, "", "Save as", "Save", null, null, null, false, null, 1112, 624, 340, 215);
        var script = Gen.GenerateDraft([At(T0, click)], CaptureEngineType.Desktop, "t");
        var p = script.Steps[0].Parameters;
        p["window"].Should().Be("Save as");
        p["offset"].Should().Be("340,215");
        p["coordinates"].Should().Be("1112,624");
    }

    [Fact]
    public void Click_On_The_Designer_Window_Is_Dropped()
    {
        var click = new RawEvent(1, T0, RawEventType.Click, "", "Tracto Designer", null, null, null, null, false, null, 397, 46, 10, 10);
        Gen.GenerateDraft([At(T0, click)], CaptureEngineType.Desktop, "t").Steps.Should().BeEmpty();
    }

    [Fact]
    public void Recorded_Pause_Becomes_A_Delay_Step()
    {
        var script = Gen.GenerateDraft(
            [At(T0, MakeRaw(RawEventType.Click)), At(T0.AddMilliseconds(2040), MakeRaw(RawEventType.Click))],
            CaptureEngineType.Desktop, "t");
        script.Steps.Select(s => s.Action).Should().Equal("click", "delay", "click");
        script.Steps[1].Parameters["ms"].Should().Be("2000");
        script.Steps.Select(s => s.StepNumber).Should().Equal(1, 2, 3);
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
