namespace Tracto.Designer.Core.Scripting;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// One runtime-specific part of a segmented script. Segments run in order, each on a worker of
/// its own runtime, and later segments receive the outputs of earlier ones.
/// </summary>
public sealed class ScriptSegment : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<string> Runtimes = ["desktop", "browser", "terminal"];

    /// <summary>Browser modes, plus the empty entry meaning "whatever the host is set to".</summary>
    public static readonly IReadOnlyList<string> Modes = ["", "headed", "headless"];

    private string _name = "Segment";
    private string _runtime = "desktop";
    private string _mode = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); }
    }

    public string Runtime
    {
        get => _runtime;
        set
        {
            _runtime = value;
            // Mode means nothing outside a browser segment.
            if (!IsBrowser) _mode = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBrowser));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>
    /// Browser segments only: <c>headed</c> or <c>headless</c> for this segment, whatever the
    /// browser host itself is configured for. Empty leaves the host's setting in charge.
    /// </summary>
    public string Mode
    {
        get => _mode;
        set { _mode = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); }
    }

    public bool IsBrowser => Runtime.Equals("browser", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<ScriptStep> Steps { get; } = [];

    /// <summary>Text shown in the segment list.</summary>
    public string Title =>
        string.IsNullOrEmpty(Mode) ? $"{Name}  [{Runtime}]" : $"{Name}  [{Runtime}, {Mode}]";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
