namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// One row of the parameter grid. The step's parameter dictionary stays the single source of
/// truth: a row writes straight through to it, so renaming a row moves the value with it.
/// </summary>
public sealed class StepParameterRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string?> _parameters;
    private readonly Action _changed;
    private string _name;

    public StepParameterRow(Dictionary<string, string?> parameters, string name, Action changed)
    {
        _parameters = parameters;
        _name = name;
        _changed = changed;
    }

    public string Name
    {
        get => _name;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (next == _name) return;
            // Another row already has that name: keep this one as it was rather than overwrite
            // the other row, and tell the box so it shows the name that still stands.
            if (next.Length > 0 && _parameters.ContainsKey(next))
            {
                OnPropertyChanged();
                return;
            }
            // Carry the value over to the new name.
            _parameters.Remove(_name, out var carried);
            _name = next;
            if (next.Length > 0) _parameters[next] = carried;
            OnPropertyChanged();
            _changed();
        }
    }

    public string? Value
    {
        get => _parameters.TryGetValue(_name, out var v) ? v : null;
        set
        {
            if (_name.Length == 0) return;
            _parameters[_name] = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
