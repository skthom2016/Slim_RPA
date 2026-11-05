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

    /// <summary>
    /// The one signed-in browser profile on a machine. Every segment that asks for a signed-in
    /// browser shares it, so a person signs in once and all such segments benefit.
    /// </summary>
    public const string SharedProfile = "default";

    public const string FreshBrowser    = "Empty browser (no sign-in)";
    public const string SignedInBrowser = "Signed-in browser";

    /// <summary>The two choices offered for a browser segment.</summary>
    public static readonly IReadOnlyList<string> BrowserSessions = [FreshBrowser, SignedInBrowser];

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
            OnPropertyChanged(nameof(BrowserSession));
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
            OnPropertyChanged(nameof(BrowserSession));
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
            Profile = value ? SharedProfile : string.Empty;
        }
    }

    /// <summary>The choice as shown in the Designer's dropdown.</summary>
    public string BrowserSession
    {
        get => UseSignedInProfile ? SignedInBrowser : FreshBrowser;
        set => UseSignedInProfile = string.Equals(value, SignedInBrowser, StringComparison.Ordinal);
    }

    /// <summary>Plain words for the current choice, shown under the dropdown.</summary>
    public string BrowserSessionSummary => UseSignedInProfile
        ? "Uses the signed-in browser on the machine that runs it. Sign in once from the toolbar; " +
          "every segment set this way shares that one sign-in. Only one such segment runs at a time."
        : "Opens an empty browser with nothing signed in. Right for a search or any public page, " +
          "and several of these can run at the same time.";

    public bool IsBrowser => Runtime.Equals("browser", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<ScriptStep> Steps { get; } = [];

    /// <summary>Text shown in the segment list.</summary>
    public string Title
    {
        get
        {
            var tags = Runtime;
            if (!string.IsNullOrEmpty(Mode)) tags += $", {Mode}";
            if (UseSignedInProfile) tags += ", signed in";
            return $"{Name}  [{tags}]";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
