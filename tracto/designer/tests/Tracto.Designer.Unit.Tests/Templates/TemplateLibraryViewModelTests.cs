namespace Tracto.Designer.Unit.Tests.Templates;

using FluentAssertions;
using Tracto.Designer.App.Services;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Templates;

/// <summary>
/// Stub IDialogService for unit tests — returns a pre-configured answer
/// and records whether it was called.
/// </summary>
internal sealed class StubDialogService : IDialogService
{
    private readonly bool _answer;
    public bool WasCalled { get; private set; }

    public StubDialogService(bool answer) => _answer = answer;

    public bool Confirm(string message, string title)
    {
        WasCalled = true;
        return _answer;
    }
}

public class TemplateLibraryViewModelTests
{
    private static Script MakeScript(int stepCount = 2)
    {
        var s = new Script { Name = "Test" };
        for (int i = 1; i <= stepCount; i++)
            s.Steps.Add(new ScriptStep { StepNumber = i, Description = $"Step {i}", Action = "click" });
        return s;
    }

    [Fact]
    public void UseTemplate_EmptyEditor_DoesNotShowDialog()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        var dialog = new StubDialogService(true);
        var vm = new TemplateLibraryViewModel(library, editor, dialog);

        vm.SelectedTemplate = library.GetAll().First();
        vm.UseTemplateCommand.Execute(null);

        dialog.WasCalled.Should().BeFalse("no confirmation is needed when the editor has no steps");
        editor.Script.Should().NotBeNull();
        editor.Script!.Id.Should().Be(library.GetAll().First().Id);
    }

    [Fact]
    public void UseTemplate_WithExistingSteps_Confirmed_ReplacesScript()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        editor.LoadScript(MakeScript(2));

        var dialog = new StubDialogService(answer: true);
        var vm = new TemplateLibraryViewModel(library, editor, dialog);

        var target = library.GetAll().Skip(1).First();
        vm.SelectedTemplate = target;
        vm.UseTemplateCommand.Execute(null);

        dialog.WasCalled.Should().BeTrue();
        editor.Script!.Id.Should().Be(target.Id);
    }

    [Fact]
    public void UseTemplate_WithExistingSteps_Cancelled_DoesNotReplace()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        var original = MakeScript(2);
        editor.LoadScript(original);

        var dialog = new StubDialogService(answer: false);
        var vm = new TemplateLibraryViewModel(library, editor, dialog);

        var target = library.GetAll().Skip(1).First();
        vm.SelectedTemplate = target;
        vm.UseTemplateCommand.Execute(null);

        dialog.WasCalled.Should().BeTrue();
        editor.Script.Should().Be(original, "script must not change when the user cancels");
    }

    [Fact]
    public void SearchText_Filters_Templates()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        var vm = new TemplateLibraryViewModel(library, editor, new StubDialogService(true));

        vm.SearchText = "login";

        vm.Templates.Should().NotBeEmpty();
        vm.Templates.All(t =>
            t.Name.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            t.Description.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            t.Tags.Any(tag => tag.Contains("login", StringComparison.OrdinalIgnoreCase)))
            .Should().BeTrue();
    }

    [Fact]
    public void CategoryFilter_Restricts_Templates_To_Category()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        var vm = new TemplateLibraryViewModel(library, editor, new StubDialogService(true));

        vm.CategoryFilter = TemplateCategory.Login;

        vm.Templates.Should().NotBeEmpty();
        vm.Templates.All(t => t.Category == TemplateCategory.Login).Should().BeTrue();
    }

    [Fact]
    public void UseTemplateCommand_CanExecute_False_When_NoSelection()
    {
        var library = new BuiltInTemplateLibrary();
        var editor = new ScriptEditorViewModel();
        var vm = new TemplateLibraryViewModel(library, editor, new StubDialogService(true));

        vm.SelectedTemplate = null;

        vm.UseTemplateCommand.CanExecute(null).Should().BeFalse();
    }
}
