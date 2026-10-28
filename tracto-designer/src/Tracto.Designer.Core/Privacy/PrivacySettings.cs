namespace Tracto.Designer.Core.Privacy;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class PrivacySettings : INotifyPropertyChanged
{
    private PrivacyPolicy _policy = PrivacyPolicy.Default;
    private bool _allowScreenshots = true;
    private bool _allowNarrationExport = true;

    public PrivacyPolicy Policy
    {
        get => _policy;
        set { _policy = value; OnPropertyChanged(); }
    }

    public bool AllowScreenshots
    {
        get => _allowScreenshots;
        set { _allowScreenshots = value; OnPropertyChanged(); }
    }

    public bool AllowNarrationExport
    {
        get => _allowNarrationExport;
        set { _allowNarrationExport = value; OnPropertyChanged(); }
    }

    public bool HasFlag(PrivacyPolicy flag) => (_policy & flag) != 0;

    public void Reset()
    {
        Policy = PrivacyPolicy.Default;
        AllowScreenshots = true;
        AllowNarrationExport = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
