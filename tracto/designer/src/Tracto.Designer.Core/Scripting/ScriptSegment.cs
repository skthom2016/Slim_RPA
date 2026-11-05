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
    private string _profile = string.Empty;

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
            // Mode and profile mean nothing outside a browser segment.
            if (!IsBrowser) { _mode = string.Empty; _profile = string.Empty; }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBrowser));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(Profile));
            OnPropertyChanged(nameof(UseSignedInProfile));
            OnPropertyChanged(nameof(BrowserSessionSummary));
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

    /// <summary>
    /// Browser segments only: a named browser profile on the worker, so sites signed into
    /// earlier stay signed in. Empty means a throwaway browser with nothing signed in.
    /// </summary>
    public string Profile
    {
        get => _profile;
        set
        {
            _profile = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseSignedInProfile));
            OnPropertyChanged(nameof(BrowserSessionSummary));
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>
    /// Whether this segment runs in a signed-in profile or in a fresh browser. Turning it off
    /// clears the profile name, which is what makes the run use a fresh browser.
    ///
    /// <para>A fresh browser is the default and is right for anything that needs no sign-in,
    /// such as a search or a public page. It also lets several runs go at once, where runs
    /// sharing one profile have to wait for each other.</para>
    /// </summary>
    public bool UseSignedInProfile
    {
        get => !string.IsNullOrWhiteSpace(_profile);
        set
        {
            if (value == UseSignedInProfile) return;
            Profile = value ? SuggestProfileName() : string.Empty;
        }
    }

    /// <summary>Plain words for the current choice, shown next to the switch.</summary>
    public string BrowserSessionSummary => UseSignedInProfile
        ? $"Runs in the signed-in profile \"{_profile.Trim()}\". One run at a time on this profile."
        : "Runs in a fresh browser with nothing signed in. Several runs can go at once.";

    /// <summary>A safe starting name derived from the segment name, for the person to adjust.</summary>
    private string SuggestProfileName()
    {
        var chars = (Name ?? string.Empty)
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        var name = new string(chars).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-", StringComparison.Ordinal);
        if (name.Length > 64) name = name[..64].Trim('-');
        return name.Length > 0 && char.IsLetterOrDigit(name[0]) ? name : "signed-in";
    }

    public bool IsBrowser => Runtime.Equals("browser", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<ScriptStep> Steps { get; } = [];

    /// <summary>Text shown in the segment list.</summary>
    public string Title
    {
        get
        {
            var tags = Runtime;
            if (!string.IsNullOrEmpty(Mode)) tags += $", {Mode}";
            if (UseSignedInProfile) tags += $", {_profile.Trim()}";
            return $"{Name}  [{tags}]";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
