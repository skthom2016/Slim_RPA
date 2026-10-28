namespace Tracto.Designer.Core.IsolatedMode;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class IsolatedModeSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _restrictNetworkAccess = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsActive)); }
    }

    public bool RestrictNetworkAccess
    {
        get => _restrictNetworkAccess;
        set { _restrictNetworkAccess = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> AllowedLocalPaths { get; } = [];

    public bool IsActive => _isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
