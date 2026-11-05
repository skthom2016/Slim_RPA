namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// One row of the parameter grid. The step's parameter dictionary stays the single source of
/// truth, so a row writes straight through to it and renaming a row moves the value with it.
///
/// <para>The box shows whatever has been typed or picked, while the dictionary only ever holds
/// a usable name. That lets a name be typed a character at a time, and lets a row sit with no
/// name at all until one is chosen, which is how a new row starts.</para>
/// </summary>
public sealed class StepParameterRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string?> _parameters;
    private readonly Action<bool> _changed;
    private string _key;          // the name under which the step holds this parameter
    private string _text;         // what the name box shows, which may not be usable yet
    private string? _pendingValue;

    public StepParameterRow(Dictionary<string, string?> parameters, string name, Action<bool> changed)
    {
        _parameters = parameters;
        _key = name ?? string.Empty;
        _text = _key;
        _changed = changed;
    }

    /// <summary>True while nothing of this row has reached the step.</summary>
    public bool IsUnnamed => _key.Length == 0;

    /// <summary>
    /// Another row already holds this name, so it has not been taken. Shown rather than
    /// silently reverted, because reverting fights whoever is still typing.
    /// </summary>
    public bool IsNameTaken { get; private set; }

    public string Name
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (next == _text && !IsNameTaken) return;
            _text = next;

            var trimmed = next.Trim();
            var wasTaken = IsNameTaken;
            IsNameTaken = false;

            if (trimmed != _key)
            {
                if (trimmed.Length > 0 && _parameters.ContainsKey(trimmed))
                {
                    // Leave the step as it is: taking the name would overwrite the other row.
                    IsNameTaken = true;
                }
                else
                {
                    string? carried = _key.Length == 0 ? _pendingValue : null;
                    if (_key.Length > 0) _parameters.Remove(_key, out carried);

                    _key = trimmed;
                    if (trimmed.Length > 0)
                    {
                        _parameters[trimmed] = carried;
                        _pendingValue = null;
                    }
                    else
                    {
                        _pendingValue = carried;
                    }
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnnamed));
            OnPropertyChanged(nameof(Value));
            if (IsNameTaken != wasTaken) OnPropertyChanged(nameof(IsNameTaken));
            _changed(true);
        }
    }

    public string? Value
    {
        get => _key.Length == 0 ? _pendingValue : _parameters.TryGetValue(_key, out var v) ? v : null;
        set
        {
            if (_key.Length == 0) _pendingValue = value;
            else _parameters[_key] = value;
            OnPropertyChanged();
            _changed(false);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
