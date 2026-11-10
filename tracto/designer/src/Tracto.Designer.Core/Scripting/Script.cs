namespace Tracto.Designer.Core.Scripting;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class Script : INotifyPropertyChanged
{
    /// <summary>What a script is called before anyone names it. Code that decides whether an
    /// author chose a name compares against this rather than repeating the literal.</summary>
    public const string DefaultName = "Untitled Script";

    private string _id = Guid.NewGuid().ToString("N");
    private string _name = DefaultName;
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

    /// <summary>Steps of a single-runtime script. Empty when the script is segmented.</summary>
    public ObservableCollection<ScriptStep> Steps { get; } = [];

    /// <summary>
    /// Runtime-specific parts of a segmented script, run in order. When this has items, the
    /// script has no TargetRuntime of its own and <see cref="Steps"/> is not used.
    /// </summary>
    public ObservableCollection<ScriptSegment> Segments { get; } = [];

    /// <summary>Default parameters, read by steps as ${param.name}. Used by segmented scripts.</summary>
    public Dictionary<string, string> Params { get; set; } = [];

    public bool IsSegmented => Segments.Count > 0;

    /// <summary>"segmented" for a segmented script, otherwise the TargetRuntime.</summary>
    public string EffectiveRuntime => IsSegmented ? "segmented" : TargetRuntime;

    /// <summary>Every step in run order: the segments' steps for a segmented script.</summary>
    public IEnumerable<ScriptStep> AllSteps => IsSegmented ? Segments.SelectMany(s => s.Steps) : Steps;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
