namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Scripting;

public sealed class ScriptEditorViewModel : INotifyPropertyChanged
{
    private Script? _script;
    private ScriptStep? _selectedStep;

    public Script? Script
    {
        get => _script;
        set { _script = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasScript)); }
    }

    public ScriptStep? SelectedStep
    {
        get => _selectedStep;
        set { _selectedStep = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public bool HasScript => _script is not null;

    public ICommand AddStepCommand    { get; }
    public ICommand DeleteStepCommand { get; }
    public ICommand MoveUpCommand     { get; }
    public ICommand MoveDownCommand   { get; }

    public ScriptEditorViewModel()
    {
        AddStepCommand    = new RelayCommand(_ => AddStep());
        DeleteStepCommand = new RelayCommand(_ => DeleteStep(), _ => SelectedStep is not null);
        MoveUpCommand     = new RelayCommand(_ => MoveUp(),     _ => CanMoveUp());
        MoveDownCommand   = new RelayCommand(_ => MoveDown(),   _ => CanMoveDown());
    }

    public void LoadScript(Script script)
    {
        Script = script;
        RenumberSteps();
    }

    private void AddStep()
    {
        if (_script is null) return;
        var step = new ScriptStep { StepNumber = _script.Steps.Count + 1, Description = "New step", Action = "action" };
        _script.Steps.Add(step);
        SelectedStep = step;
    }

    private void DeleteStep()
    {
        if (_script is null || _selectedStep is null) return;
        _script.Steps.Remove(_selectedStep);
        SelectedStep = null;
        RenumberSteps();
    }

    private bool CanMoveUp() => _script is not null && _selectedStep is not null && _script.Steps.IndexOf(_selectedStep) > 0;
    private bool CanMoveDown() => _script is not null && _selectedStep is not null && _script.Steps.IndexOf(_selectedStep) < _script.Steps.Count - 1;

    private void MoveUp()
    {
        if (_script is null || _selectedStep is null) return;
        int i = _script.Steps.IndexOf(_selectedStep);
        if (i <= 0) return;
        _script.Steps.Move(i, i - 1);
        RenumberSteps();
    }

    private void MoveDown()
    {
        if (_script is null || _selectedStep is null) return;
        int i = _script.Steps.IndexOf(_selectedStep);
        if (i < 0 || i >= _script.Steps.Count - 1) return;
        _script.Steps.Move(i, i + 1);
        RenumberSteps();
    }

    private void RenumberSteps()
    {
        if (_script is null) return;
        for (int i = 0; i < _script.Steps.Count; i++)
            _script.Steps[i].StepNumber = i + 1;
    }

    private void UpdateCommandStates() => ((RelayCommand)DeleteStepCommand).RaiseCanExecuteChanged();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
