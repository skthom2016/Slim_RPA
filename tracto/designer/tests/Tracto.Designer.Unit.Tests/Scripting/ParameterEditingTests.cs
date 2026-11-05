namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// Typing into a parameter rebuilt the parameter rows on every key, which destroyed the box
/// being typed in: only the first character ever landed. The same rebuild handed the editable
/// action box a fresh list each time, which blanked the action it was showing.
/// </summary>
public class ParameterEditingTests
{
    private static ScriptEditorViewModel EditorWithBrowserStep(string action = "fill")
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var segment = new ScriptSegment { Name = "Search", Runtime = "browser" };
        segment.Steps.Add(new ScriptStep { StepNumber = 1, Action = action, Description = "A step" });
        script.Segments.Add(segment);
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        vm.StepRows[0].IsExpanded = true;
        return vm;
    }

    [Fact]
    public void Typing_A_Parameter_Value_Keeps_The_Very_Same_Row()
    {
        var vm = EditorWithBrowserStep();
        var row = vm.StepRows[0];
        row.AddParameter();
        var box = row.StepParameters.Single();

        // One key at a time, the way a person types.
        foreach (var text in new[] { "t", "ti", "tib", "tibin" })
            box.Value = text;

        row.StepParameters.Should().ContainSingle();
        row.StepParameters.Single().Should().BeSameAs(box,
            "replacing the row mid-edit is what threw away the caret after one key");
        row.Step.Parameters[box.Name].Should().Be("tibin");
    }

    [Fact]
    public void Typing_A_Parameter_Does_Not_Disturb_The_Action_List()
    {
        var vm = EditorWithBrowserStep();
        var row = vm.StepRows[0];
        var before = row.ActionOptions;
        row.AddParameter();

        row.StepParameters.Single().Value = "Tibin";

        row.ActionOptions.Should().BeSameAs(before,
            "a new list makes the editable action box rebind and lose what it shows");
        row.Action.Should().Be("fill", "the action itself must survive typing elsewhere");
    }

    [Fact]
    public void The_Action_List_Is_The_Same_Instance_Across_Reads()
    {
        var row = EditorWithBrowserStep().StepRows[0];

        row.ActionOptions.Should().BeSameAs(row.ActionOptions);
        row.ActionOptions.Should().Contain("fill");
    }

    [Fact]
    public void Changing_The_Action_Refreshes_The_List_And_The_Hint()
    {
        var vm = EditorWithBrowserStep();
        var row = vm.StepRows[0];
        _ = row.ActionOptions;

        row.Action = "getText";

        row.ActionOptions.Should().Contain("getText");
        row.StoreAsHint.Should().Contain("value", "getText publishes a value a later step can read");
    }

    [Fact]
    public void Naming_A_Result_Leaves_The_Parameter_Rows_Standing()
    {
        var vm = EditorWithBrowserStep("getText");
        var row = vm.StepRows[0];
        row.AddParameter();
        var box = row.StepParameters.Single();

        row.StoreAs = "topResult";

        row.StepParameters.Single().Should().BeSameAs(box);
        row.Step.Parameters["storeAs"].Should().Be("topResult");
    }

    [Fact]
    public void A_Later_Step_Still_Learns_What_An_Earlier_One_Published()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var segment = new ScriptSegment { Name = "Search", Runtime = "browser" };
        segment.Steps.Add(new ScriptStep { StepNumber = 1, Action = "getText", Description = "Read" });
        segment.Steps.Add(new ScriptStep { StepNumber = 2, Action = "fill", Description = "Type" });
        script.Segments.Add(segment);
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        vm.StepRows[0].IsExpanded = true;
        vm.StepRows[1].IsExpanded = true;

        vm.StepRows[0].StoreAs = "topResult";

        vm.StepRows[1].AvailableData.Select(d => d.Reference)
          .Should().Contain("${topResult.value}",
              "refreshing the data lists must still happen, just without rebuilding the rows");
    }
}
