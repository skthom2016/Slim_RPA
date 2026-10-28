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
    private ScriptValidationResult? _result;

    public ValidationViewModel(IScriptValidator validator, ScriptEditorViewModel editor)
    {
        _validator = validator;
        _editor = editor;
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
        Result = _validator.Validate(_editor.Script);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
