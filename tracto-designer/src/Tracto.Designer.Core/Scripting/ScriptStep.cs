namespace Tracto.Designer.Core.Scripting;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class ScriptStep : INotifyPropertyChanged
{
    private int _stepNumber;
    private string _description = string.Empty;
    private string _action = string.Empty;
    private string? _condition;
    private Dictionary<string, string?> _parameters = [];

    public int StepNumber
    {
        get => _stepNumber;
        set { _stepNumber = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string Action
    {
        get => _action;
        set { _action = value; OnPropertyChanged(); }
    }

    public string? Condition
    {
        get => _condition;
        set { _condition = value; OnPropertyChanged(); }
    }

    public Dictionary<string, string?> Parameters
    {
        get => _parameters;
        set { _parameters = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
