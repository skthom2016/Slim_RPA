namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// Picking a parameter from the dropdown did nothing and the box stayed empty: the list was
/// rebuilt on every edit, so the box was handed a new one and reset whatever it was showing.
/// Walks one step through the actions an author would try.
/// </summary>
public class ParameterDropdownTests
{
    private static (ScriptEditorViewModel Editor, StepRowViewModel Row) OneBrowserStep(string action)
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var segment = new ScriptSegment { Name = "Browse", Runtime = "browser" };
        segment.Steps.Add(new ScriptStep { StepNumber = 1, Action = action, Description = "A step" });
        script.Segments.Add(segment);
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        vm.StepRows[0].IsExpanded = true;
        return (vm, vm.StepRows[0]);
    }

    [Fact]
    public void Picking_A_Parameter_Puts_It_On_The_Step_And_Keeps_It_Showing()
    {
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        var box = row.StepParameters.Single();

        // Picking from the dropdown sets the name, the way the list does.
        box.Name = "css";

        box.Name.Should().Be("css", "the box has to keep showing what was picked");
        box.IsUnnamed.Should().BeFalse();
        row.Step.Parameters.Should().ContainKey("css");
    }

    [Fact]
    public void The_List_Does_Not_Change_When_A_Name_Is_Picked()
    {
        // Dropping the picked name from the list rebinds the box in the middle of the pick,
        // which clears it and loses the choice. The list stays as it is; a name already in use
        // is refused by the row instead.
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        var before = row.ParameterNameOptions;

        row.StepParameters.Single().Name = "css";

        row.ParameterNameOptions.Should().BeSameAs(before);
        row.ParameterNameOptions.Should().Contain(["css", "role", "label", "value"]);
    }

    [Fact]
    public void Changing_The_Action_Changes_What_The_List_Offers()
    {
        var (_, row) = OneBrowserStep("fill");
        row.ParameterNameOptions.Should().Contain("value", "fill takes a value");

        row.Action = "find";

        row.ParameterNameOptions.Should().NotContain("value", "find takes no value");
        row.ParameterNameOptions.Should().Contain(["role", "css", "timeoutMs"]);

        row.Action = "getAttr";

        row.ParameterNameOptions.Should().Contain("attribute", "getAttr needs to know which one");
    }

    [Fact]
    public void The_List_Is_The_Same_Instance_Until_Something_Changes_It()
    {
        var (_, row) = OneBrowserStep("fill");
        var first = row.ParameterNameOptions;

        row.ParameterNameOptions.Should().BeSameAs(first,
            "handing the box a new list each read is what blanked it");

        row.AddParameter();
        row.StepParameters.Single().Value = "typed";

        row.ParameterNameOptions.Should().BeSameAs(first,
            "typing a value does not change which names are left");
    }

    [Fact]
    public void Typing_A_Name_One_Character_At_A_Time_Works()
    {
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        var box = row.StepParameters.Single();

        foreach (var text in new[] { "c", "cs", "css" })
            box.Name = text;

        box.Name.Should().Be("css");
        row.Step.Parameters.Should().ContainKey("css").And.HaveCount(1,
            "the half-typed names must not be left behind on the step");
    }

    [Fact]
    public void A_Name_Another_Row_Holds_Is_Flagged_Rather_Than_Swallowed()
    {
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        row.StepParameters[0].Name = "css";
        row.AddParameter();
        var second = row.StepParameters[1];

        second.Name = "css";

        second.IsNameTaken.Should().BeTrue();
        second.Name.Should().Be("css", "the box keeps what was typed rather than fighting the author");
        row.Step.Parameters["css"].Should().BeNull("the other row's value must not be overwritten");
        row.Step.Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void A_Value_Typed_Before_The_Name_Is_Kept()
    {
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        var box = row.StepParameters.Single();

        box.Value = "#search";
        box.Name = "css";

        row.Step.Parameters["css"].Should().Be("#search");
    }

    [Fact]
    public void Clearing_A_Name_Takes_The_Parameter_Off_The_Step()
    {
        var (_, row) = OneBrowserStep("fill");
        row.AddParameter();
        var box = row.StepParameters.Single();
        box.Name = "css";
        box.Value = "#x";

        box.Name = "";

        row.Step.Parameters.Should().NotContainKey("css");
        box.Value.Should().Be("#x", "it comes back if a name is picked again");
        row.ParameterNameOptions.Should().Contain("css");
    }
}
