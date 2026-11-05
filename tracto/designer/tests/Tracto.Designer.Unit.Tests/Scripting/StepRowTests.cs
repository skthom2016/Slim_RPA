namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// Each step owns its own details. Selecting a step used to put the caret straight into its
/// description, and its action and parameters were shown in one panel below the whole list,
/// far from the step they belonged to.
/// </summary>
public class StepRowTests
{
    private static ScriptEditorViewModel EditorWith(params (string Action, string Description)[] steps)
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0", TargetRuntime = "desktop" };
        for (int i = 0; i < steps.Length; i++)
            script.Steps.Add(new ScriptStep
            {
                StepNumber = i + 1,
                Action = steps[i].Action,
                Description = steps[i].Description
            });
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        return vm;
    }

    // ── the description, behind a pencil ─────────────────────────────────────

    [Fact]
    public void A_Description_Is_Read_Only_Until_The_Pencil_Is_Pressed()
    {
        var row = EditorWith(("delay", "Wait a moment")).StepRows[0];

        row.IsEditingDescription.Should().BeFalse();
        row.IsShowingDescription.Should().BeTrue("the row shows plain text, not an open edit box");
        row.SaveDescriptionCommand.CanExecute(null).Should().BeFalse("there is nothing to save yet");

        row.EditDescriptionCommand.Execute(null);

        row.IsEditingDescription.Should().BeTrue();
        row.IsShowingDescription.Should().BeFalse();
        row.DescriptionDraft.Should().Be("Wait a moment", "editing starts from what is there");
    }

    [Fact]
    public void Typing_Changes_Nothing_Until_The_Tick_Is_Pressed()
    {
        var row = EditorWith(("delay", "Wait a moment")).StepRows[0];
        row.EditDescriptionCommand.Execute(null);

        row.DescriptionDraft = "Wait for the window";

        row.Step.Description.Should().Be("Wait a moment", "the step keeps its description until the tick");

        row.SaveDescriptionCommand.Execute(null);

        row.Step.Description.Should().Be("Wait for the window");
        row.Description.Should().Be("Wait for the window");
        row.IsEditingDescription.Should().BeFalse("the row goes back to plain text");
    }

    [Fact]
    public void Cancelling_Throws_The_Edit_Away()
    {
        var row = EditorWith(("delay", "Wait a moment")).StepRows[0];
        row.EditDescriptionCommand.Execute(null);
        row.DescriptionDraft = "something else";

        row.CancelDescriptionCommand.Execute(null);

        row.Step.Description.Should().Be("Wait a moment");
        row.IsEditingDescription.Should().BeFalse();
    }

    [Fact]
    public void A_Saved_Description_Is_Trimmed()
    {
        var row = EditorWith(("delay", "Wait")).StepRows[0];
        row.EditDescriptionCommand.Execute(null);
        row.DescriptionDraft = "   Wait for the window   ";

        row.SaveDescriptionCommand.Execute(null);

        row.Step.Description.Should().Be("Wait for the window");
    }

    // ── the details, under their own step ────────────────────────────────────

    [Fact]
    public void A_Step_Starts_Collapsed_And_Opens_On_Its_Own()
    {
        var vm = EditorWith(("app.launch", "Open Notepad"), ("delay", "Wait"));

        vm.StepRows.Should().OnlyContain(r => !r.IsExpanded);

        vm.StepRows[0].ToggleExpandCommand.Execute(null);

        vm.StepRows[0].IsExpanded.Should().BeTrue();
        vm.StepRows[1].IsExpanded.Should().BeFalse("opening one step does not open the rest");

        vm.StepRows[0].ToggleExpandCommand.Execute(null);
        vm.StepRows[0].IsExpanded.Should().BeFalse("the arrow closes it again");
    }

    [Fact]
    public void Opening_A_Step_Builds_That_Steps_Own_Parameters()
    {
        var vm = EditorWith(("app.launch", "Open Notepad"));
        vm.StepRows[0].Step.Parameters["executable"] = "notepad.exe";

        vm.StepRows[0].IsExpanded = true;

        vm.StepRows[0].StepParameters.Should().ContainSingle();
        vm.StepRows[0].StepParameters[0].Name.Should().Be("executable");
        vm.StepRows[0].StepParameters[0].Value.Should().Be("notepad.exe");
    }

    [Fact]
    public void Each_Row_Edits_Only_Its_Own_Step()
    {
        var vm = EditorWith(("app.launch", "Open Notepad"), ("delay", "Wait"));
        vm.StepRows[0].IsExpanded = true;
        vm.StepRows[1].IsExpanded = true;

        vm.StepRows[0].AddParameter();
        vm.StepRows[0].StepParameters.Single().Name = "executable";
        vm.StepRows[0].StepParameters.Single().Value = "notepad.exe";

        vm.StepRows[0].Step.Parameters["executable"].Should().Be("notepad.exe");
        vm.StepRows[1].Step.Parameters.Should().BeEmpty("the other step is untouched");
    }

    [Fact]
    public void A_Rows_Action_List_Follows_Its_Segment_Runtime()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var browser = new ScriptSegment { Name = "Search", Runtime = "browser" };
        browser.Steps.Add(new ScriptStep { StepNumber = 1, Action = "getText", Description = "Read" });
        script.Segments.Add(browser);
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);

        vm.StepRows[0].ActionOptions.Should().Contain("getText").And.NotContain("notepad.type");
    }

    [Fact]
    public void A_Row_Shows_What_An_Earlier_Segment_Published()
    {
        var script = new Script { Id = "s", Name = "S", Version = "1.0.0" };
        var browser = new ScriptSegment { Name = "Search", Runtime = "browser" };
        var read = new ScriptStep { StepNumber = 1, Action = "getText", Description = "Read" };
        read.Parameters["storeAs"] = "topResult";
        browser.Steps.Add(read);
        var desktop = new ScriptSegment { Name = "Write", Runtime = "desktop" };
        desktop.Steps.Add(new ScriptStep { StepNumber = 1, Action = "notepad.type", Description = "Type" });
        script.Segments.Add(browser);
        script.Segments.Add(desktop);

        var vm = new ScriptEditorViewModel();
        vm.LoadScript(script);
        vm.SelectedSegment = script.Segments[1];
        vm.StepRows[0].IsExpanded = true;

        vm.StepRows[0].HasAvailableData.Should().BeTrue();
        vm.StepRows[0].AvailableData.Select(d => d.Reference).Should().Contain("${param.topResult.value}");
    }

    [Fact]
    public void Naming_A_Result_Updates_What_Later_Open_Steps_Can_Read()
    {
        var vm = EditorWith(("file.read", "Read the file"), ("type", "Type it"));
        vm.StepRows[1].IsExpanded = true;
        vm.StepRows[1].HasAvailableData.Should().BeFalse("nothing publishes anything yet");

        vm.StepRows[0].IsExpanded = true;
        vm.StepRows[0].StoreAs = "note";

        vm.StepRows[1].AvailableData.Select(d => d.Reference)
          .Should().Contain("${note.firstLine}", "the later step sees it without being reopened");
    }

    [Fact]
    public void Rows_Follow_The_Steps_As_They_Are_Added_And_Removed()
    {
        var vm = EditorWith(("delay", "Wait"));

        vm.AddStepCommand.Execute(null);
        vm.StepRows.Should().HaveCount(2);

        vm.SelectedStep = vm.StepRows[1].Step;
        vm.DeleteStepCommand.Execute(null);

        vm.StepRows.Should().ContainSingle();
        vm.StepRows[0].Step.Description.Should().Be("Wait");
    }

    [Fact]
    public void A_Row_Keeps_Up_With_Its_Step_Being_Renumbered()
    {
        var vm = EditorWith(("delay", "First"), ("delay", "Second"));
        var secondRow = vm.StepRows[1];

        vm.SelectedStep = secondRow.Step;
        vm.MoveUpCommand.Execute(null);

        secondRow.StepNumber.Should().Be(1, "the row shows the number its step now has");
    }
}
