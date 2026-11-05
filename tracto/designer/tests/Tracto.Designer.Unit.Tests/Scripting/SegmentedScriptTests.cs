namespace Tracto.Designer.Unit.Tests.Scripting;

using System.IO;
using System.Text.Json;
using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.IsolatedMode;
using Tracto.Designer.Core.Privacy;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Validation;
using Tracto.Designer.Infrastructure.Serialization;

public class SegmentedScriptTests
{
    private static ScriptStep Step(int n, string action, string desc = "step") =>
        new() { StepNumber = n, Action = action, Description = desc };

    private static Script TwoSegments()
    {
        var s = new Script { Id = "notepad-to-bing", Name = "Notepad To Bing", Version = "1.0.0" };
        s.Params["notePath"] = @"C:\notes\a.txt";
        var read = new ScriptSegment { Name = "Read the note", Runtime = "desktop" };
        read.Steps.Add(Step(1, "app.launch", "Open Notepad"));
        read.Steps.Add(Step(2, "clipboard.get", "Read clipboard"));
        read.Steps[1].Parameters["storeAs"] = "note";
        var search = new ScriptSegment { Name = "Search Bing", Runtime = "browser" };
        search.Steps.Add(Step(1, "navigate", "Open Bing"));
        search.Steps[0].Parameters["url"] = "https://www.bing.com/";
        s.Segments.Add(read);
        s.Segments.Add(search);
        return s;
    }

    // ── serialization ───────────────────────────────────────────────────────

    [Fact]
    public void Json_Roundtrip_Keeps_Segments_Params_And_Steps()
    {
        var ser = new JsonScriptSerializer();
        var back = ser.Deserialize(ser.Serialize(TwoSegments()));

        back.IsSegmented.Should().BeTrue();
        back.Params.Should().ContainKey("notePath").WhoseValue.Should().Be(@"C:\notes\a.txt");
        back.Segments.Select(g => (g.Name, g.Runtime)).Should()
            .Equal(("Read the note", "desktop"), ("Search Bing", "browser"));
        back.Segments[0].Steps.Select(x => x.Action).Should().Equal("app.launch", "clipboard.get");
        back.Segments[0].Steps[1].Parameters["storeAs"].Should().Be("note");
        back.Segments[1].Steps[0].Parameters["url"].Should().Be("https://www.bing.com/");
    }

    [Fact]
    public void Json_Of_A_Segmented_Script_Has_No_TopLevel_TargetRuntime_Or_Steps()
    {
        using var doc = JsonDocument.Parse(new JsonScriptSerializer().Serialize(TwoSegments()));
        var root = doc.RootElement;

        root.TryGetProperty("targetRuntime", out _).Should().BeFalse();
        root.TryGetProperty("steps", out _).Should().BeFalse();
        root.GetProperty("segments").GetArrayLength().Should().Be(2);
        root.GetProperty("params").GetProperty("notePath").GetString().Should().Be(@"C:\notes\a.txt");
    }

    [Fact]
    public void Json_Of_A_Single_Runtime_Script_Is_Unchanged()
    {
        var s = new Script { Id = "flat", Name = "Flat", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(Step(1, "delay"));

        using var doc = JsonDocument.Parse(new JsonScriptSerializer().Serialize(s));

        doc.RootElement.GetProperty("targetRuntime").GetString().Should().Be("desktop");
        doc.RootElement.GetProperty("steps").GetArrayLength().Should().Be(1);
        doc.RootElement.TryGetProperty("segments", out _).Should().BeFalse();
    }

    [Fact]
    public void Json_Import_Reads_The_Envelope_Form()
    {
        var inner = new JsonScriptSerializer().Serialize(TwoSegments());
        var wrapped = "{\"schemaVersion\":\"1\",\"script\":" + inner + "}";

        var back = new JsonScriptSerializer().Deserialize(wrapped);

        back.Id.Should().Be("notepad-to-bing");
        back.Segments.Should().HaveCount(2);
    }

    [Fact]
    public void Json_Import_Reads_Number_And_Bool_Params_As_Text()
    {
        const string json = """
            { "id": "p", "name": "P", "version": "1.0.0",
              "params": { "count": 3, "on": true, "none": null },
              "segments": [ { "name": "a", "runtime": "Desktop",
                              "steps": [ { "stepNumber": 1, "action": "delay", "parameters": { "ms": "1" } } ] } ] }
            """;

        var s = new JsonScriptSerializer().Deserialize(json);

        s.Params["count"].Should().Be("3");
        s.Params["on"].Should().Be("true");
        s.Params["none"].Should().Be("");
        s.Segments[0].Runtime.Should().Be("desktop");
    }

    [Fact]
    public void Json_Import_Of_A_File_With_Several_Scripts_Explains_What_To_Do()
    {
        var act = () => new JsonScriptSerializer().Deserialize("{\"name\":\"f\",\"scripts\":[{},{}]}");

        act.Should().Throw<InvalidDataException>().WithMessage("*segments*");
    }

    [Fact]
    public void Yaml_Cannot_Save_A_Segmented_Script()
    {
        var act = () => new YamlScriptSerializer().Serialize(TwoSegments());

        act.Should().Throw<NotSupportedException>().WithMessage("*JSON*");
    }

    // ── validation ──────────────────────────────────────────────────────────

    [Fact]
    public void Validator_Accepts_A_Valid_Segmented_Script_Without_TargetRuntime()
    {
        var result = new ScriptValidator().Validate(TwoSegments());

        result.Errors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Validator_Rejects_An_Unknown_Segment_Runtime()
    {
        var s = TwoSegments();
        s.Segments[1].Runtime = "mainframe";

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-020");
    }

    [Fact]
    public void Validator_Rejects_A_Segment_With_No_Steps()
    {
        var s = TwoSegments();
        s.Segments[1].Steps.Clear();

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-021");
    }

    [Fact]
    public void Validator_Numbers_Steps_Per_Segment()
    {
        var s = TwoSegments();
        s.Segments[1].Steps[0].StepNumber = 5;

        var errors = new ScriptValidator().Validate(s).Errors;

        errors.Should().Contain(e => e.RuleId == "VAL-010" && e.Message.Contains("Search Bing"));
    }

    [Fact]
    public void Validator_Rejects_TopLevel_Steps_Beside_Segments()
    {
        var s = TwoSegments();
        s.Steps.Add(Step(1, "delay"));

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-024");
    }

    [Fact]
    public void Validator_Still_Requires_TargetRuntime_On_A_Single_Runtime_Script()
    {
        var s = new Script { Id = "x", Name = "X", Version = "1.0.0", TargetRuntime = "" };
        s.Steps.Add(Step(1, "delay"));

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-004");
    }

    // ── signing, privacy, isolated mode ─────────────────────────────────────

    [Fact]
    public void Canonical_Form_Changes_When_A_Segment_Step_Changes()
    {
        var a = TwoSegments();
        var b = TwoSegments();
        b.Segments[1].Steps[0].Parameters["url"] = "https://example.org/";

        ScriptCanonicalizer.Canonicalize(a).Should().NotBe(ScriptCanonicalizer.Canonicalize(b));
        ScriptCanonicalizer.Canonicalize(a).Should().Be(ScriptCanonicalizer.Canonicalize(TwoSegments()));
    }

    [Fact]
    public void Canonical_Form_Changes_When_A_Segment_Runtime_Changes()
    {
        var a = TwoSegments();
        var b = TwoSegments();
        b.Segments[1].Runtime = "desktop";

        ScriptCanonicalizer.Canonicalize(a).Should().NotBe(ScriptCanonicalizer.Canonicalize(b));
    }

    [Fact]
    public void Privacy_Filter_Keeps_Segments_And_Params()
    {
        var filtered = new PrivacyFilter(new PrivacySettings()).FilterScript(TwoSegments());

        filtered.IsSegmented.Should().BeTrue();
        filtered.Segments.Select(g => g.Runtime).Should().Equal("desktop", "browser");
        filtered.Segments[1].Steps[0].Parameters["url"].Should().Contain("bing.com");
        filtered.Params.Should().ContainKey("notePath");
    }

    [Fact]
    public void Isolated_Mode_Rejects_A_Browser_Segment()
    {
        var settings = new IsolatedModeSettings { IsEnabled = true };

        var result = new IsolatedModeValidator().Validate(TwoSegments(), settings);

        result.Violations.Should().Contain(v => v.Code == "ISO-001");
    }

    [Fact]
    public void Isolated_Mode_Accepts_A_Desktop_Only_Segmented_Script()
    {
        var s = TwoSegments();
        s.Segments.RemoveAt(1);

        new IsolatedModeValidator().Validate(s, new IsolatedModeSettings { IsEnabled = true })
            .Violations.Should().NotContain(v => v.Code == "ISO-001");
    }

    // ── editor view model ───────────────────────────────────────────────────

    private static ScriptEditorViewModel EditorWith(Script script)
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        return vm;
    }

    [Fact]
    public void Editor_Shows_The_Selected_Segments_Steps()
    {
        var vm = EditorWith(TwoSegments());

        vm.IsSegmented.Should().BeTrue();
        vm.SelectedSegment!.Name.Should().Be("Read the note");
        vm.ActiveSteps.Should().BeSameAs(vm.Script!.Segments[0].Steps);

        vm.SelectedSegment = vm.Script.Segments[1];

        vm.ActiveSteps.Should().BeSameAs(vm.Script.Segments[1].Steps);
    }

    [Fact]
    public void Editor_Add_Step_Goes_Into_The_Selected_Segment()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[1];

        vm.AddStepCommand.Execute(null);

        vm.Script.Segments[1].Steps.Should().HaveCount(2);
        vm.Script.Segments[1].Steps[1].StepNumber.Should().Be(2);
        vm.Script.Segments[0].Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Editor_First_Add_Segment_Wraps_Existing_Steps_Into_Segment_One()
    {
        var flat = new Script { Id = "f", Name = "F", Version = "1.0.0", TargetRuntime = "browser" };
        flat.Steps.Add(Step(1, "navigate"));
        flat.Steps.Add(Step(2, "click"));
        var vm = EditorWith(flat);

        vm.AddSegmentCommand.Execute(null);

        vm.IsSegmented.Should().BeTrue();
        vm.Script!.Steps.Should().BeEmpty();
        vm.Script.Segments.Should().HaveCount(2);
        vm.Script.Segments[0].Runtime.Should().Be("browser");
        vm.Script.Segments[0].Steps.Should().HaveCount(2);
        vm.Script.Segments[1].Runtime.Should().Be("desktop");
        vm.SelectedSegment.Should().BeSameAs(vm.Script.Segments[1]);
    }

    [Fact]
    public void Editor_Delete_Segment_Selects_A_Neighbour()
    {
        var vm = EditorWith(TwoSegments());

        vm.DeleteSegmentCommand.Execute(null);

        vm.Script!.Segments.Should().HaveCount(1);
        vm.SelectedSegment.Should().BeSameAs(vm.Script.Segments[0]);
        vm.Script.Segments[0].Name.Should().Be("Search Bing");
    }

    [Fact]
    public void Editor_Move_Segment_Down_Reorders_And_Keeps_Selection()
    {
        var vm = EditorWith(TwoSegments());
        var first = vm.SelectedSegment!;

        vm.MoveSegmentDownCommand.Execute(null);

        vm.Script!.Segments.IndexOf(first).Should().Be(1);
        vm.SelectedSegment.Should().BeSameAs(first);
        vm.MoveSegmentDownCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Editor_Make_Single_Returns_A_One_Segment_Script_To_Single_Runtime()
    {
        var s = TwoSegments();
        s.Segments.RemoveAt(0);
        var vm = EditorWith(s);

        vm.MergeToSingleCommand.CanExecute(null).Should().BeTrue();
        vm.MergeToSingleCommand.Execute(null);

        vm.IsSegmented.Should().BeFalse();
        vm.Script!.TargetRuntime.Should().Be("browser");
        vm.Script.Steps.Should().HaveCount(1);
        vm.ActiveSteps.Should().BeSameAs(vm.Script.Steps);
    }

    [Fact]
    public void Editor_Params_Text_Round_Trips()
    {
        var vm = EditorWith(TwoSegments());

        vm.ParamsText = "query=Playwright\r\nlimit=5\r\nbad line without equals";

        vm.Script!.Params.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["query"] = "Playwright", ["limit"] = "5"
        });
        vm.ParamsText.Should().Contain("query=Playwright").And.Contain("limit=5");
    }

    [Fact]
    public void Editor_Draft_Fills_An_Empty_Selected_Segment_And_Takes_Its_Runtime()
    {
        var s = TwoSegments();
        s.Segments.Add(new ScriptSegment { Name = "Empty", Runtime = "desktop" });
        var vm = EditorWith(s);
        vm.SelectedSegment = s.Segments[2];
        var draft = new Script { Id = "d", Name = "D", Version = "1.0.0", TargetRuntime = "browser" };
        draft.Steps.Add(Step(1, "navigate"));
        draft.Steps.Add(Step(2, "click"));

        vm.ApplyDraft(draft);

        vm.Script!.Segments.Should().HaveCount(3);
        vm.Script.Segments[2].Runtime.Should().Be("browser");
        vm.Script.Segments[2].Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Editor_Draft_Never_Overwrites_A_Segment_That_Already_Has_Steps()
    {
        var vm = EditorWith(TwoSegments());
        var draft = new Script { Id = "d", Name = "D", Version = "1.0.0", TargetRuntime = "browser" };
        draft.Steps.Add(Step(1, "navigate"));

        vm.ApplyDraft(draft);

        vm.Script!.Segments.Should().HaveCount(3);
        vm.Script.Segments[0].Steps.Should().HaveCount(2);
        vm.Script.Segments[1].Runtime.Should().Be("browser");
        vm.Script.Segments[1].Steps.Should().HaveCount(1);
        vm.Script.Segments[2].Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Editor_Draft_Replaces_A_Single_Runtime_Script_As_Before()
    {
        var flat = new Script { Id = "f", Name = "F", Version = "1.0.0", TargetRuntime = "desktop" };
        flat.Steps.Add(Step(1, "delay"));
        var vm = EditorWith(flat);
        var draft = new Script { Id = "d", Name = "D", Version = "1.0.0", TargetRuntime = "browser" };
        draft.Steps.Add(Step(1, "navigate"));

        vm.ApplyDraft(draft);

        vm.Script.Should().BeSameAs(draft);
        vm.IsSegmented.Should().BeFalse();
    }

    [Fact]
    public void Editor_Does_Not_Delete_The_Last_Segment()
    {
        var s = TwoSegments();
        s.Segments.RemoveAt(1);
        var vm = EditorWith(s);

        vm.DeleteSegmentCommand.CanExecute(null).Should().BeFalse();
        vm.DeleteSegmentCommand.Execute(null);

        vm.Script!.Segments.Should().HaveCount(1);
        vm.Script.Segments[0].Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Editor_First_Segment_Of_An_Empty_Browser_Script_Is_A_Browser_Segment()
    {
        var vm = EditorWith(new Script { Id = "e", Name = "E", Version = "1.0.0", TargetRuntime = "browser" });

        vm.AddSegmentCommand.Execute(null);

        vm.Script!.Segments.Should().HaveCount(1);
        vm.Script.Segments[0].Runtime.Should().Be("browser");
        vm.SelectedSegment.Should().BeSameAs(vm.Script.Segments[0]);
    }

    [Fact]
    public void Json_Import_Of_Empty_Segments_Gives_A_Clear_Error()
    {
        var act = () => new JsonScriptSerializer().Deserialize(
            "{\"id\":\"x\",\"name\":\"X\",\"version\":\"1.0.0\",\"segments\":[]}");

        act.Should().Throw<InvalidDataException>().WithMessage("*at least one segment*");
    }

    [Fact]
    public void Published_Flat_Script_Keeps_The_Step_Property_Order_Of_Earlier_Releases()
    {
        var s = new Script { Id = "flat", Name = "Flat", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(Step(1, "delay"));

        var json = JsonSerializer.Serialize(ScriptDocument.Build(s));

        json.IndexOf("\"action\"").Should().BeLessThan(json.IndexOf("\"description\""));
    }
    [Fact]
    public void Editor_Add_Segment_Works_With_No_Script_Open()
    {
        var vm = new ScriptEditorViewModel();

        vm.AddSegmentCommand.CanExecute(null).Should().BeTrue();
        vm.AddSegmentCommand.Execute(null);

        vm.Script.Should().NotBeNull();
        vm.IsSegmented.Should().BeTrue();
        vm.Script!.Segments.Should().HaveCount(1);
        vm.SelectedSegment.Should().BeSameAs(vm.Script.Segments[0]);
        vm.ActiveSteps.Should().BeSameAs(vm.Script.Segments[0].Steps);
    }

    [Fact]
    public void Editor_New_Script_Clears_Everything()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedStep = vm.ActiveSteps![0];

        vm.NewScript();

        vm.Script.Should().NotBeNull();
        vm.IsSegmented.Should().BeFalse();
        vm.Script!.Segments.Should().BeEmpty();
        vm.Script.Steps.Should().BeEmpty();
        vm.Script.Params.Should().BeEmpty();
        vm.SelectedStep.Should().BeNull();
        vm.SelectedSegment.Should().BeNull();
    }
    [Fact]
    public void Json_Roundtrip_Keeps_The_Browser_Profile()
    {
        var s = TwoSegments();
        s.Segments[1].Profile = "outlook-tibin";
        var ser = new JsonScriptSerializer();

        var back = ser.Deserialize(ser.Serialize(s));

        back.Segments[1].Profile.Should().Be("outlook-tibin");
        back.Segments[0].Profile.Should().BeEmpty();
    }

    [Fact]
    public void Json_Omits_Profile_When_It_Is_Not_Set()
    {
        using var doc = JsonDocument.Parse(new JsonScriptSerializer().Serialize(TwoSegments()));

        doc.RootElement.GetProperty("segments")[1].TryGetProperty("profile", out _).Should().BeFalse();
    }

    [Fact]
    public void Changing_A_Segment_Away_From_Browser_Clears_Its_Profile()
    {
        var seg = new ScriptSegment { Runtime = "browser", Profile = "outlook-tibin" };

        seg.Runtime = "desktop";

        seg.Profile.Should().BeEmpty();
    }

    [Fact]
    public void Validator_Rejects_A_Profile_On_A_Desktop_Segment()
    {
        var s = TwoSegments();
        s.Segments[0].Profile = "outlook-tibin";

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-026");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("..\escape")]
    [InlineData("-leading-dash")]
    public void Validator_Rejects_An_Unsafe_Profile_Name(string name)
    {
        var s = TwoSegments();
        s.Segments[1].Profile = name;

        new ScriptValidator().Validate(s).Errors.Should().Contain(e => e.RuleId == "VAL-026");
    }

    [Fact]
    public void Validator_Accepts_A_Safe_Profile_Name_On_A_Browser_Segment()
    {
        var s = TwoSegments();
        s.Segments[1].Profile = "outlook-tibin.01";

        new ScriptValidator().Validate(s).Errors
            .Should().NotContain(e => e.RuleId == "VAL-026");
    }

    [Fact]
    public void Canonical_Form_Changes_When_The_Profile_Changes()
    {
        var a = TwoSegments();
        var b = TwoSegments();
        b.Segments[1].Profile = "outlook-tibin";

        ScriptCanonicalizer.Canonicalize(a).Should().NotBe(ScriptCanonicalizer.Canonicalize(b));
    }
    [Fact]
    public void A_Browser_Segment_Uses_A_Fresh_Browser_Until_A_Profile_Is_Chosen()
    {
        var seg = new ScriptSegment { Runtime = "browser" };

        seg.UseSignedInProfile.Should().BeFalse();
        seg.Profile.Should().BeEmpty();
        seg.BrowserSessionSummary.Should().Contain("fresh browser");
    }

    [Fact]
    public void Choosing_A_Signed_In_Profile_Suggests_A_Name_From_The_Segment_Name()
    {
        var seg = new ScriptSegment { Runtime = "browser", Name = "Read Outlook mail" };

        seg.UseSignedInProfile = true;

        seg.Profile.Should().Be("read-outlook-mail");
        seg.BrowserSessionSummary.Should().Contain("One run at a time");
    }

    [Fact]
    public void Turning_The_Signed_In_Profile_Off_Goes_Back_To_A_Fresh_Browser()
    {
        var seg = new ScriptSegment { Runtime = "browser", Name = "Mail" };
        seg.UseSignedInProfile = true;

        seg.UseSignedInProfile = false;

        seg.Profile.Should().BeEmpty();
        seg.UseSignedInProfile.Should().BeFalse();
    }

    [Fact]
    public void Setting_A_Profile_Name_Turns_The_Signed_In_Choice_On()
    {
        var seg = new ScriptSegment { Runtime = "browser" };

        seg.Profile = "client-demo";

        seg.UseSignedInProfile.Should().BeTrue();
        seg.Title.Should().Contain("client-demo");
    }

    [Fact]
    public void A_Fresh_Browser_Segment_Writes_No_Profile_To_Json()
    {
        var s = TwoSegments();
        s.Segments[1].UseSignedInProfile = false;

        using var doc = JsonDocument.Parse(new JsonScriptSerializer().Serialize(s));

        doc.RootElement.GetProperty("segments")[1].TryGetProperty("profile", out _).Should().BeFalse();
    }
}
