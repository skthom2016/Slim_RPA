namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// One row of the parameter grid. The step's parameter dictionary stays the single source of
/// truth: a row writes straight through to it, so renaming a row moves the value with it.
///
/// <para>A row added from the UI starts with no name, so the author picks one rather than being
/// handed a guess. Until it has a name it holds its value here, because a dictionary has
/// nowhere to put a value with no key.</para>
/// </summary>
public sealed class StepParameterRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string?> _parameters;
    private readonly Action _changed;
    private string _name;
    private string? _pendingValue;

    public StepParameterRow(Dictionary<string, string?> parameters, string name, Action changed)
    {
        _parameters = parameters;
        _name = name ?? string.Empty;
        _changed = changed;
    }

    /// <summary>True while the row has no name, so nothing of it has reached the step yet.</summary>
    public bool IsUnnamed => _name.Length == 0;

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

            // Carry whatever the row holds over to the new name.
            string? carried;
            if (_name.Length == 0) carried = _pendingValue;
            else _parameters.Remove(_name, out carried);

            _name = next;
            if (next.Length > 0)
            {
                _parameters[next] = carried;
                _pendingValue = null;
            }
            else
            {
                _pendingValue = carried;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnnamed));
            OnPropertyChanged(nameof(Value));
            _changed();
        }
    }

    public string? Value
    {
        get => _name.Length == 0
            ? _pendingValue
            : _parameters.TryGetValue(_name, out var v) ? v : null;
        set
        {
            if (_name.Length == 0) _pendingValue = value;
            else _parameters[_name] = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
