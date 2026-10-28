namespace Tracto.Designer.Core.Scripting;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class Script : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Untitled Script";
    private string _version = "1.0.0";
    private string _targetRuntime = "desktop";

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Version
    {
        get => _version;
        set { _version = value; OnPropertyChanged(); }
    }

    public string TargetRuntime
    {
        get => _targetRuntime;
        set { _targetRuntime = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ScriptStep> Steps { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
