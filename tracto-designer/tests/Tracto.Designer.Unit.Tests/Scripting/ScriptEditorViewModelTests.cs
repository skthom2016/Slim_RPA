namespace Tracto.Designer.Unit.Tests.Scripting;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;

public class ScriptEditorViewModelTests
{
    private static Script MakeScript(int stepCount = 3)
    {
        var s = new Script { Name = "Test" };
        for (int i = 1; i <= stepCount; i++)
            s.Steps.Add(new ScriptStep { StepNumber = i, Description = $"Step {i}", Action = "click" });
        return s;
    }

    [Fact]
    public void LoadScript_Sets_Script()
    {
        var vm = new ScriptEditorViewModel();
        var s = MakeScript();
        vm.LoadScript(s);
        vm.Script.Should().Be(s);
    }

    [Fact]
    public void AddStep_Increases_StepCount()
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(MakeScript(2));
        vm.AddStepCommand.Execute(null);
        vm.Script!.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void DeleteStep_Removes_Selected()
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(MakeScript(3));
        vm.SelectedStep = vm.Script!.Steps[1];
        vm.DeleteStepCommand.Execute(null);
        vm.Script!.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void MoveUp_Reorders_Steps()
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(MakeScript(3));
        var step2 = vm.Script!.Steps[1];
        vm.SelectedStep = step2;
        vm.MoveUpCommand.Execute(null);
        vm.Script!.Steps[0].Should().Be(step2);
    }

    [Fact]
    public void MoveDown_Reorders_Steps()
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(MakeScript(3));
        var step1 = vm.Script!.Steps[0];
        vm.SelectedStep = step1;
        vm.MoveDownCommand.Execute(null);
        vm.Script!.Steps[1].Should().Be(step1);
    }

    [Fact]
    public void RenumberSteps_After_Delete()
    {
        var vm = new ScriptEditorViewModel();
        vm.LoadScript(MakeScript(3));
        vm.SelectedStep = vm.Script!.Steps[0];
        vm.DeleteStepCommand.Execute(null);
        vm.Script!.Steps[0].StepNumber.Should().Be(1);
        vm.Script!.Steps[1].StepNumber.Should().Be(2);
    }
}
