namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Infrastructure.Serialization;

/// <summary>
/// The editor could only change a step's description, so a runnable step could not be authored
/// and a hand-off between segments could not be built or even seen. These cover editing a step
/// and the data it passes on, including a script that arrives by import already doing it.
/// </summary>
public class StepEditorTests
{
    private static ScriptEditorViewModel EditorWith(Script script)
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        return vm;
    }

    private static Script TwoSegments()
    {
        var script = new Script { Id = "flow", Name = "Flow", Version = "1.0.0" };
        var browser = new ScriptSegment { Name = "Search", Runtime = "browser" };
        var read = new ScriptStep { StepNumber = 1, Action = "getText", Description = "Read the heading" };
        read.Parameters["css"] = "h2";
        read.Parameters["storeAs"] = "headline";
        browser.Steps.Add(read);

        var desktop = new ScriptSegment { Name = "Write", Runtime = "desktop" };
        var write = new ScriptStep { StepNumber = 1, Action = "notepad.type", Description = "Type it" };
        write.Parameters["text"] = "Found: ${param.headline.value}";
        desktop.Steps.Add(write);

        script.Segments.Add(browser);
        script.Segments.Add(desktop);
        return script;
    }

    [Fact]
    public void Selecting_A_Step_Shows_Its_Parameters()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.StepParameters.Select(p => p.Name).Should().BeEquivalentTo(["css", "storeAs"]);
        vm.StepParameters.Single(p => p.Name == "css").Value.Should().Be("h2");
    }

    [Fact]
    public void Editing_A_Parameter_Writes_Through_To_The_Step()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.StepParameters.Single(p => p.Name == "css").Value = ".b_algo h2";

        vm.SelectedStep.Parameters["css"].Should().Be(".b_algo h2");
    }

    [Fact]
    public void Renaming_A_Parameter_Carries_Its_Value_Over()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.StepParameters.Single(p => p.Name == "css").Name = "xpath";

        vm.SelectedStep.Parameters.Should().NotContainKey("css");
        vm.SelectedStep.Parameters["xpath"].Should().Be("h2");
    }

    [Fact]
    public void Adding_A_Parameter_Offers_One_The_Action_Understands()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[1];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.AddParameterCommand.Execute(null);

        vm.SelectedStep.Parameters.Should().ContainKey("windowTitle",
            "notepad.type takes windowTitle and the step does not have it yet");
    }

    [Fact]
    public void Deleting_A_Parameter_Removes_It_From_The_Step()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];
        vm.SelectedParameter = vm.StepParameters.Single(p => p.Name == "css");

        vm.DeleteParameterCommand.Execute(null);

        vm.SelectedStep.Parameters.Should().NotContainKey("css");
        vm.StepParameters.Should().HaveCount(1);
    }

    [Fact]
    public void Store_Result_As_Is_The_Step_StoreAs_Parameter()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.StoreAs.Should().Be("headline");

        vm.StoreAs = "topResult";
        vm.SelectedStep.Parameters["storeAs"].Should().Be("topResult");

        vm.StoreAs = "";
        vm.SelectedStep.Parameters.Should().NotContainKey("storeAs", "an empty name publishes nothing");
    }

    [Fact]
    public void The_Hint_Spells_Out_How_A_Later_Segment_Reads_The_Result()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.StoreAsHint.Should().Contain("${param.headline.value}");
    }

    [Fact]
    public void The_Action_List_Follows_The_Segment_Runtime()
    {
        var vm = EditorWith(TwoSegments());

        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.ActionOptions.Should().Contain("getText").And.NotContain("notepad.type");

        vm.SelectedSegment = vm.Script.Segments[1];
        vm.ActionOptions.Should().Contain("notepad.type").And.NotContain("getText");
    }

    [Fact]
    public void A_Step_In_A_Later_Segment_Is_Offered_The_Earlier_Segments_Data()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[1];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.HasAvailableData.Should().BeTrue();
        vm.AvailableData.Select(d => d.Reference).Should().Contain("${param.headline.value}");
    }

    [Fact]
    public void Inserting_A_Reference_Appends_It_To_The_Selected_Parameter()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[1];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];
        vm.SelectedParameter = vm.StepParameters.Single(p => p.Name == "text");
        vm.SelectedParameter.Value = "Headline: ";

        var reference = vm.AvailableData.Single(d => d.Reference == "${param.headline.value}");
        vm.InsertReferenceCommand.Execute(reference);

        vm.SelectedStep.Parameters["text"].Should().Be("Headline: ${param.headline.value}");
    }

    [Fact]
    public void Insert_Does_Nothing_Until_A_Parameter_Is_Selected()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[1];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];

        vm.InsertReferenceCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Renaming_A_Parameter_To_An_Existing_Name_Leaves_Both_Alone()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];
        var css = vm.StepParameters.Single(p => p.Name == "css");

        css.Name = "storeAs";

        vm.SelectedStep.Parameters["storeAs"].Should().Be("headline", "the other row must not be overwritten");
        vm.SelectedStep.Parameters["css"].Should().Be("h2", "a refused rename keeps the row as it was");
        css.Name.Should().Be("css");
    }

    [Fact]
    public void A_Browser_Action_That_Takes_An_Element_Suggests_The_Element_First()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        var click = new ScriptStep { StepNumber = 2, Action = "click", Description = "Click it" };
        vm.SelectedSegment.Steps.Add(click);
        vm.SelectedStep = click;

        vm.SuggestedParameters.First().Should().Be("role",
            "choosing the element comes before tuning a timeout");
        vm.SuggestedParameters.Should().Contain(["css", "text", "timeoutMs"]);
    }

    [Fact]
    public void A_Browser_Delay_Is_Not_Offered_Element_Selectors()
    {
        var vm = EditorWith(TwoSegments());
        vm.SelectedSegment = vm.Script!.Segments[0];
        var delay = new ScriptStep { StepNumber = 2, Action = "delay", Description = "Wait" };
        vm.SelectedSegment.Steps.Add(delay);
        vm.SelectedStep = delay;

        vm.SuggestedParameters.Should().Contain("ms");
        vm.SuggestedParameters.Should().NotContain(["css", "role", "xpath"]);
    }

    [Fact]
    public void An_Imported_Script_Shows_The_Hand_Off_It_Already_Has()
    {
        // Round-trip through the serializer, the way Import reaches the editor.
        var json = new JsonScriptSerializer().Serialize(TwoSegments());
        var imported = new JsonScriptSerializer().Deserialize(json);

        var vm = EditorWith(imported);
        vm.SelectedSegment = vm.Script!.Segments[0];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];
        vm.StoreAs.Should().Be("headline", "the producer shows what it publishes");

        vm.SelectedSegment = vm.Script.Segments[1];
        vm.SelectedStep = vm.SelectedSegment.Steps[0];
        vm.StepParameters.Single(p => p.Name == "text").Value
          .Should().Be("Found: ${param.headline.value}", "the consumer shows what it reads");
        vm.AvailableData.Select(d => d.Reference).Should().Contain("${param.headline.value}");
    }
}
