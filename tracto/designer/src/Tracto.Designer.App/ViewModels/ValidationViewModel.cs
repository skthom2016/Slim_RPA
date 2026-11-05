namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Validation;

public sealed class ValidationViewModel : INotifyPropertyChanged
{
    private readonly IScriptValidator _validator;
    private readonly ScriptEditorViewModel _editor;
    private readonly OutputLogViewModel? _output;
    private ScriptValidationResult? _result;

    public ValidationViewModel(IScriptValidator validator, ScriptEditorViewModel editor, OutputLogViewModel? output = null)
    {
        _validator = validator;
        _editor = editor;
        _output = output;
        ValidateCommand = new RelayCommand(_ => RunValidation(), _ => _editor.Script is not null);
    }

    public ICommand ValidateCommand { get; }

    public ScriptValidationResult? Result
    {
        get => _result;
        private set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool IsValid => _result?.IsValid ?? false;
    public bool HasErrors => _result?.IsValid == false;
    public bool HasResult => _result is not null;

    private void RunValidation()
    {
        if (_editor.Script is null) return;
        var result = _validator.Validate(_editor.Script);
        Result = result;
        Report(result);
    }

    private void Report(ScriptValidationResult result)
    {
        if (_output is null) return;

        foreach (var error in result.Errors)
        {
            var where = error.StepNumber is { } n ? $" (step {n})" : "";
            _output.Write("Validation", Level(error.Severity), $"{error.RuleId}: {error.Message}{where}");
        }

        var errors   = result.Errors.Count(e => e.Severity == ValidationSeverity.Error);
        var warnings = result.Errors.Count(e => e.Severity == ValidationSeverity.Warning);
        if (result.IsValid && warnings == 0)
            _output.Success("Validation", "Valid — no problems found.");
        else if (result.IsValid)
            _output.Warning("Validation", $"Valid, with {Count(warnings, "warning")}.");
        else
            _output.Error("Validation", $"Invalid — {Count(errors, "error")}, {Count(warnings, "warning")}.");
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static OutputSeverity Level(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error   => OutputSeverity.Error,
        ValidationSeverity.Warning => OutputSeverity.Warning,
        _                          => OutputSeverity.Info
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
